//! Reads telegrams from the P1 port over TCP and hands complete sets of values to a sink.

use std::io;
use std::sync::Arc;
use std::time::Duration;

use chrono::{DateTime, FixedOffset, Local};
use tokio::io::AsyncReadExt;
use tokio::net::TcpStream;
use tokio::time::timeout;
use tracing::{Level, debug, enabled, error, info};

use crate::config::DsmrReaderOptions;
use crate::latin1::Latin1;
use crate::mapping::ObisMappingList;
use crate::parser::DsmrParser;
use crate::value::{DsmrValue, Value};

/// How long the connection may stay silent before it is re-established.
const RECEIVE_TIMEOUT: Duration = Duration::from_secs(30);
/// Pause before reconnecting, so an unreachable device does not cause a busy loop.
const RECONNECT_DELAY: Duration = Duration::from_secs(5);
/// Minimum free space in the receive buffer before it is considered full.
const MIN_FREE_BUFFER: usize = 16;

/// Receives the values of every complete, valid telegram.
pub trait TelegramSink {
    /// Called with one value per mapping, in mapping order; none of them is empty.
    fn insert(&mut self, values: &[DsmrValue]);
}

/// The timestamp of a telegram: its time field, or the current time if the mappings have none.
#[must_use]
pub fn telegram_time(mappings: &ObisMappingList, values: &[DsmrValue]) -> DateTime<FixedOffset> {
    mappings
        .time_field()
        .and_then(|field| match values[field.index].value() {
            Some(Value::Time(time)) => Some(*time),
            _ => None,
        })
        .unwrap_or_else(|| Local::now().fixed_offset())
}

#[derive(Debug)]
pub struct DsmrReader<S> {
    mappings: Arc<ObisMappingList>,
    parser: DsmrParser,
    values: Vec<DsmrValue>,
    sink: S,
}

impl<S: TelegramSink> DsmrReader<S> {
    #[must_use]
    pub fn new(mappings: Arc<ObisMappingList>, sink: S) -> Self {
        let values = mappings
            .iter()
            .map(|m| DsmrValue::new(Arc::clone(m)))
            .collect();
        Self {
            parser: DsmrParser::new(Arc::clone(&mappings)),
            mappings,
            values,
            sink,
        }
    }

    #[must_use]
    pub fn sink(&self) -> &S {
        &self.sink
    }

    /// Connects to the device and processes its data until the task is cancelled,
    /// reconnecting whenever the connection fails.
    pub async fn run(&mut self, options: &DsmrReaderOptions) {
        let mut buffer = vec![0u8; usize::from(options.buffer_size)];
        loop {
            if let Err(e) = self.read_from_device(options, &mut buffer).await {
                error!("Error reading from Dsmr, retrying: {e}");
            }
            tokio::time::sleep(RECONNECT_DELAY).await;
        }
    }

    async fn read_from_device(
        &mut self,
        options: &DsmrReaderOptions,
        buffer: &mut [u8],
    ) -> io::Result<()> {
        let address = (options.host.as_str(), options.port);
        let mut stream = timeout(RECEIVE_TIMEOUT, TcpStream::connect(address))
            .await
            .map_err(|_| io::Error::new(io::ErrorKind::TimedOut, "connection timed out"))??;
        info!("Connected to {}:{}", options.host, options.port);

        let mut count = 0;
        loop {
            if buffer.len() - count < MIN_FREE_BUFFER {
                error!(
                    "Buffer full, dropping {count} bytes of data\n{}",
                    Latin1(&buffer[..count])
                );
                count = 0;
            }
            let read = timeout(RECEIVE_TIMEOUT, stream.read(&mut buffer[count..]))
                .await
                .map_err(|_| io::Error::new(io::ErrorKind::TimedOut, "no data received"))??;
            if read == 0 {
                info!("Connection closed by {}:{}", options.host, options.port);
                return Ok(());
            }
            count = self.process_buffer(&mut buffer[..count + read]);
        }
    }

    /// Processes every complete telegram in `buffer`, moves the unprocessed rest to the front
    /// of `buffer` and returns its length.
    pub fn process_buffer(&mut self, buffer: &mut [u8]) -> usize {
        let mut rest: &[u8] = buffer;
        while let Some(data_lines) = self.parser.try_find_data_lines(&mut rest) {
            self.process_data_lines(data_lines);
        }
        let remaining = rest.len();
        let consumed = buffer.len() - remaining;
        if remaining > 0 && consumed > 0 {
            buffer.copy_within(consumed.., 0);
        }
        remaining
    }

    fn process_data_lines(&mut self, mut data_lines: &[u8]) {
        self.values.iter_mut().for_each(DsmrValue::clear);

        while !data_lines.is_empty() {
            // Problems are logged by the parser; the telegram is still used if nothing is missing.
            let _ = self
                .parser
                .parse_data_line(&mut data_lines, &mut self.values);
        }

        let mut has_error = false;
        for value in self.values.iter().filter(|v| v.is_empty()) {
            error!("{} is missing, dropping all values", value.mapping().id);
            has_error = true;
        }

        if !has_error {
            if enabled!(Level::DEBUG) {
                debug!(
                    "Enqueuing values for {}",
                    telegram_time(&self.mappings, &self.values).to_rfc3339()
                );
            }
            self.sink.insert(&self.values);
        }

        self.values.iter_mut().for_each(DsmrValue::clear);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::model::DsmrType;
    use crate::test_support::{capture_logs, test_mapping_list};
    use crate::value::OnOff;
    use chrono::TimeZone;
    use rust_decimal::Decimal;
    use std::str::FromStr;

    const SAMPLE: &[u8] = include_bytes!("../testdata/sample.txt");
    const MISSING_LINE: &[u8] = include_bytes!("../testdata/missing_line.txt");
    const DUPLICATED_VALUE: &[u8] = include_bytes!("../testdata/duplicated_value.txt");

    #[derive(Default)]
    struct RecordingSink(Vec<Vec<DsmrValue>>);

    impl TelegramSink for RecordingSink {
        fn insert(&mut self, values: &[DsmrValue]) {
            self.0.push(values.to_vec());
        }
    }

    fn reader() -> DsmrReader<RecordingSink> {
        DsmrReader::new(test_mapping_list(), RecordingSink::default())
    }

    #[allow(clippy::match_same_arms)] // one arm per field reads like the C# test
    fn expected_value(field_name: &str) -> Value {
        let number = |s: &str| Value::Number(Decimal::from_str(s).unwrap());
        match field_name {
            "time" => Value::Time(
                Local
                    .with_ymd_and_hms(2023, 8, 17, 17, 14, 30)
                    .unwrap()
                    .fixed_offset(),
            ),
            "name" => Value::String("AUX1030303218166".into()),
            "serial" => Value::String("9903218166".into()),
            "tariff" => number("1"),
            "state" => Value::OnOff(OnOff::On),
            "import_energy" => number("812.421"),
            "import_energy_tariff_1" => number("470.111"),
            "import_energy_tariff_2" => number("342.31"),
            "import_energy_tariff_3" | "import_energy_tariff_4" => number("0"),
            "export_energy" => number("1714.369"),
            "export_energy_tariff_1" => number("1233.413"),
            "export_energy_tariff_2" => number("480.956"),
            "export_energy_tariff_3" | "export_energy_tariff_4" => number("0"),
            "import_reactive_energy" => number("18.858"),
            "export_reactive_energy" => number("439.269"),
            "reactive_energy_q1" => number("11.481"),
            "reactive_energy_q2" => number("7.377"),
            "reactive_energy_q3" => number("186.705"),
            "reactive_energy_q4" => number("252.564"),
            "voltage_l1" => number("234"),
            "voltage_l2" => number("232.7"),
            "voltage_l3" => number("233.5"),
            "current_l1" => number("1"),
            "current_l2" | "current_l3" => number("0"),
            "power_factor" => number("0.336"),
            "power_factor_l1" => number("0.842"),
            "power_factor_l2" => number("0.989"),
            "power_factor_l3" => number("0.845"),
            "frequency" => number("49.99"),
            "import_power" => number("0.25"),
            "export_power" => number("0.168"),
            "reactive_power_q1" | "reactive_power_q2" => number("0"),
            "reactive_power_q3" => number("0.066"),
            "reactive_power_q4" => number("0.159"),
            "limiter_limit" | "energy_combined" | "current_limit_l1" | "current_limit_l2"
            | "current_limit_l3" | "previous_month" | "message" => Value::Ignored,
            other => panic!("Unexpected field name {other}"),
        }
    }

    #[test]
    fn all_happy() {
        let mut reader = reader();
        let mut buffer = SAMPLE.to_vec();

        let (remaining, logs) = capture_logs(|| reader.process_buffer(&mut buffer));

        assert_eq!(remaining, 0);
        let inserted = &reader.sink().0;
        assert_eq!(inserted.len(), 1);
        let values = &inserted[0];
        assert_eq!(values.len(), test_mapping_list().len());
        for (i, value) in values.iter().enumerate() {
            assert_eq!(value.mapping().index, i);
            assert_eq!(
                value.value(),
                Some(&expected_value(&value.mapping().field_name)),
                "{}",
                value.mapping().field_name
            );
            if value.mapping().dsmr_type == DsmrType::Ignored {
                assert_eq!(value.value(), Some(&Value::Ignored));
            }
        }

        let logs: Vec<_> = logs
            .into_iter()
            .filter(|(level, _)| *level != Level::TRACE)
            .collect();
        let expected_time = Local
            .with_ymd_and_hms(2023, 8, 17, 17, 14, 30)
            .unwrap()
            .fixed_offset()
            .to_rfc3339();
        assert_eq!(
            logs,
            [(
                Level::DEBUG,
                format!("Enqueuing values for {expected_time}")
            )]
        );
    }

    #[test]
    fn two_datagrams() {
        let mut reader = reader();
        let mut buffer = [SAMPLE, SAMPLE].concat();

        let (remaining, logs) = capture_logs(|| reader.process_buffer(&mut buffer));

        assert_eq!(remaining, 0);
        assert_eq!(reader.sink().0.len(), 2);
        assert!(
            logs.iter().all(|(level, _)| *level >= Level::DEBUG),
            "{logs:?}"
        );
    }

    #[test]
    fn missing_data() {
        let mut reader = reader();
        let mut buffer = MISSING_LINE.to_vec();

        let (remaining, logs) = capture_logs(|| reader.process_buffer(&mut buffer));

        assert_eq!(remaining, 0);
        assert!(reader.sink().0.is_empty());
        let logs: Vec<_> = logs
            .into_iter()
            .filter(|(level, _)| *level != Level::TRACE)
            .collect();
        assert_eq!(
            logs,
            [(
                Level::ERROR,
                "1-0:32.7.0 is missing, dropping all values".to_owned()
            )]
        );
    }

    #[test]
    fn duplicated_value() {
        let mut reader = reader();
        let mut buffer = DUPLICATED_VALUE.to_vec();

        let (remaining, logs) = capture_logs(|| reader.process_buffer(&mut buffer));

        assert_eq!(remaining, 0);
        assert_eq!(reader.sink().0.len(), 1);
        let logs: Vec<_> = logs
            .into_iter()
            .filter(|(level, _)| *level < Level::DEBUG)
            .collect();
        assert_eq!(
            logs,
            [(
                Level::ERROR,
                "0-0:1.0.0(230817171430S): duplicated value".to_owned()
            )]
        );
    }

    #[test]
    fn keeps_partial_telegram_at_buffer_start() {
        let mut reader = reader();
        let split = 1000;
        let mut buffer = [SAMPLE, &SAMPLE[..split]].concat();

        let remaining = reader.process_buffer(&mut buffer);

        assert_eq!(remaining, split);
        assert_eq!(&buffer[..remaining], &SAMPLE[..split]);
        assert_eq!(reader.sink().0.len(), 1);

        let mut buffer = [&SAMPLE[..split], &SAMPLE[split..]].concat();
        assert_eq!(reader.process_buffer(&mut buffer), 0);
        assert_eq!(reader.sink().0.len(), 2);
    }

    #[tokio::test]
    async fn reads_from_tcp() {
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let server = tokio::spawn(async move {
            use tokio::io::AsyncWriteExt;
            let (mut socket, _) = listener.accept().await.unwrap();
            // Send the telegram in two chunks to exercise buffering across reads.
            socket.write_all(&SAMPLE[..700]).await.unwrap();
            socket.flush().await.unwrap();
            tokio::time::sleep(Duration::from_millis(50)).await;
            socket.write_all(&SAMPLE[700..]).await.unwrap();
        });

        let options = DsmrReaderOptions {
            host: "127.0.0.1".into(),
            port,
            buffer_size: 4096,
        };
        let mut reader = reader();
        let mut buffer = vec![0u8; 4096];
        reader
            .read_from_device(&options, &mut buffer)
            .await
            .unwrap();
        server.await.unwrap();

        assert_eq!(reader.sink().0.len(), 1);
    }
}
