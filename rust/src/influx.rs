//! Writes telegrams to InfluxDB (v2 HTTP API, line protocol).

use std::fmt::Write as _;
use std::sync::Arc;
use std::time::Duration;

use reqwest::header::{AUTHORIZATION, CONTENT_TYPE, HeaderValue};
use tokio::sync::mpsc;
use tracing::{debug, error, info};
use url::Url;

use crate::config::InfluxDbOptions;
use crate::mapping::ObisMappingList;
use crate::model::DsmrUnit;
use crate::reader::{TelegramSink, telegram_time};
use crate::value::{DsmrValue, Value};

const MEASUREMENT: &str = "p1value";
const REQUEST_TIMEOUT: Duration = Duration::from_secs(100);

#[derive(Debug, thiserror::Error)]
pub enum InfluxError {
    #[error("invalid InfluxDB base URL")]
    InvalidUrl(#[from] url::ParseError),
    #[error("invalid InfluxDB token")]
    InvalidToken(#[from] reqwest::header::InvalidHeaderValue),
    #[error("HTTP client error")]
    Http(#[from] reqwest::Error),
    #[error("InfluxDB returned {status}: {body}")]
    Status {
        status: reqwest::StatusCode,
        body: String,
    },
}

/// Formats the values of a telegram as InfluxDB line protocol: one `p1value` line per unit,
/// tagged with every string and on/off value, with the numbers of that unit as fields.
///
/// ```text
/// p1value,name=AUX1030303218166,serial=9903218166,state=ON,unit=kWh export_energy=1714.369,... 1692285270
/// ```
#[derive(Debug, Clone)]
pub struct LineProtocolFormatter {
    mappings: Arc<ObisMappingList>,
}

impl LineProtocolFormatter {
    #[must_use]
    pub fn new(mappings: Arc<ObisMappingList>) -> Self {
        Self { mappings }
    }

    /// Appends the lines for `values` (one per mapping, in mapping order) to `out`.
    pub fn format(&self, values: &[DsmrValue], out: &mut String) {
        let timestamp = telegram_time(&self.mappings, values).timestamp();
        for group in self.mappings.number_mappings_by_unit() {
            out.push_str(MEASUREMENT);
            for tag in self.mappings.tags() {
                let text = match values[tag.index].value() {
                    Some(Value::String(s)) => s,
                    Some(Value::OnOff(v)) => v.as_str(),
                    _ => continue,
                };
                out.push(',');
                push_escaped(out, &tag.field_name);
                out.push('=');
                push_escaped(out, text);
            }
            if group.unit != DsmrUnit::None {
                out.push_str(",unit=");
                out.push_str(group.unit.as_str());
            }
            out.push(' ');

            let mut first = true;
            for field in &group.mappings {
                if let Some(Value::Number(number)) = values[field.index].value() {
                    if !first {
                        out.push(',');
                    }
                    first = false;
                    push_escaped(out, &field.field_name);
                    let _ = write!(out, "={number}");
                }
            }

            let _ = writeln!(out, " {timestamp}");
        }
    }
}

/// Escapes the characters that are special in line protocol keys and tag values.
fn push_escaped(out: &mut String, text: &str) {
    for c in text.chars() {
        if matches!(c, ',' | '=' | ' ' | '\\') {
            out.push('\\');
        }
        out.push(c);
    }
}

/// [`TelegramSink`] that formats telegrams and queues them for the [`InfluxDbWriter`].
#[derive(Debug)]
pub struct InfluxDbSink {
    formatter: LineProtocolFormatter,
    queue: mpsc::UnboundedSender<String>,
}

impl InfluxDbSink {
    #[must_use]
    pub fn new(mappings: Arc<ObisMappingList>, queue: mpsc::UnboundedSender<String>) -> Self {
        Self {
            formatter: LineProtocolFormatter::new(mappings),
            queue,
        }
    }
}

impl TelegramSink for InfluxDbSink {
    fn insert(&mut self, values: &[DsmrValue]) {
        let mut lines = String::with_capacity(2048);
        self.formatter.format(values, &mut lines);
        if self.queue.send(lines).is_err() {
            error!("InfluxDB writer is not running, dropped values");
        }
    }
}

/// Posts line protocol batches to the InfluxDB `/api/v2/write` endpoint.
#[derive(Debug)]
pub struct InfluxDbWriter {
    client: reqwest::Client,
    url: Url,
}

impl InfluxDbWriter {
    pub fn new(options: &InfluxDbOptions) -> Result<Self, InfluxError> {
        let mut url = Url::parse(&options.base_url)?.join("api/v2/write")?;
        url.query_pairs_mut()
            .append_pair("org", &options.organization)
            .append_pair("bucket", &options.bucket)
            .append_pair("precision", "s");

        let mut token = HeaderValue::from_str(&format!("Token {}", options.token))?;
        token.set_sensitive(true);
        let headers = [
            (AUTHORIZATION, token),
            (
                CONTENT_TYPE,
                HeaderValue::from_static("text/plain; charset=utf-8"),
            ),
        ]
        .into_iter()
        .collect();

        let client = reqwest::Client::builder()
            .default_headers(headers)
            .timeout(REQUEST_TIMEOUT)
            .build()?;
        Ok(Self { client, url })
    }

    /// The write endpoint, including the query parameters.
    #[must_use]
    pub fn url(&self) -> &Url {
        &self.url
    }

    pub async fn write(&self, lines: String) -> Result<(), InfluxError> {
        let response = self
            .client
            .post(self.url.clone())
            .body(lines)
            .send()
            .await?;
        let status = response.status();
        if status.is_success() {
            return Ok(());
        }
        let body = response.text().await.unwrap_or_default();
        Err(InfluxError::Status { status, body })
    }

    /// Writes queued batches until the queue is closed and drained.
    pub async fn run(self, mut queue: mpsc::UnboundedReceiver<String>) {
        while let Some(lines) = queue.recv().await {
            let length = lines.len();
            match self.write(lines).await {
                Ok(()) => debug!("Wrote {length} long values to InfluxDB"),
                Err(e) => error!("Error writing to InfluxDB: {e}"),
            }
        }
        info!("InfluxDB writer stopped");
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::reader::DsmrReader;
    use crate::test_support::test_mapping_list;
    use chrono::{Local, TimeZone};

    #[derive(Default)]
    struct Collect(Vec<Vec<DsmrValue>>);

    impl TelegramSink for Collect {
        fn insert(&mut self, values: &[DsmrValue]) {
            self.0.push(values.to_vec());
        }
    }

    #[test]
    fn formats_sample_telegram() {
        let mappings = test_mapping_list();
        let mut reader = DsmrReader::new(Arc::clone(&mappings), Collect::default());
        reader.process_buffer(&mut include_bytes!("../testdata/sample.txt").to_vec());
        let values = &reader.sink().0[0];

        let mut lines = String::new();
        LineProtocolFormatter::new(mappings).format(values, &mut lines);

        let ts = Local
            .with_ymd_and_hms(2023, 8, 17, 17, 14, 30)
            .unwrap()
            .timestamp();
        let tags = "p1value,name=AUX1030303218166,serial=9903218166,state=ON";
        let expected = [
            format!(
                "{tags} power_factor=0.336,power_factor_l1=0.842,power_factor_l2=0.989,power_factor_l3=0.845,tariff=1 {ts}"
            ),
            format!(
                "{tags},unit=kWh export_energy=1714.369,export_energy_tariff_1=1233.413,export_energy_tariff_2=480.956,\
                 export_energy_tariff_3=0,export_energy_tariff_4=0,import_energy=812.421,import_energy_tariff_1=470.111,\
                 import_energy_tariff_2=342.31,import_energy_tariff_3=0,import_energy_tariff_4=0 {ts}"
            ),
            format!(
                "{tags},unit=kvarh export_reactive_energy=439.269,import_reactive_energy=18.858,reactive_energy_q1=11.481,\
                 reactive_energy_q2=7.377,reactive_energy_q3=186.705,reactive_energy_q4=252.564 {ts}"
            ),
            format!("{tags},unit=V voltage_l1=234,voltage_l2=232.7,voltage_l3=233.5 {ts}"),
            format!("{tags},unit=A current_l1=1,current_l2=0,current_l3=0 {ts}"),
            format!("{tags},unit=Hz frequency=49.99 {ts}"),
            format!("{tags},unit=kW export_power=0.168,import_power=0.25 {ts}"),
            format!(
                "{tags},unit=kvar reactive_power_q1=0,reactive_power_q2=0,reactive_power_q3=0.066,reactive_power_q4=0.159 {ts}"
            ),
        ];
        assert_eq!(lines.lines().collect::<Vec<_>>(), expected);
        assert!(lines.ends_with('\n'));
    }

    #[test]
    fn escapes_special_characters() {
        let mut out = String::new();
        push_escaped(&mut out, r"a b,c=d\e");
        assert_eq!(out, r"a\ b\,c\=d\\e");
    }

    #[test]
    fn builds_write_url() {
        let options = InfluxDbOptions {
            base_url: "http://192.168.34.6:8086/".into(),
            token: "t".into(),
            organization: "my org".into(),
            bucket: "b&c".into(),
        };
        let writer = InfluxDbWriter::new(&options).unwrap();
        assert_eq!(
            writer.url().as_str(),
            "http://192.168.34.6:8086/api/v2/write?org=my+org&bucket=b%26c&precision=s"
        );
    }

    #[tokio::test]
    async fn posts_lines() {
        use tokio::io::{AsyncReadExt, AsyncWriteExt};

        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let server = tokio::spawn(async move {
            let (mut socket, _) = listener.accept().await.unwrap();
            let mut request = Vec::new();
            let mut buffer = [0u8; 1024];
            while !String::from_utf8_lossy(&request).contains("line 1") {
                let n = socket.read(&mut buffer).await.unwrap();
                request.extend_from_slice(&buffer[..n]);
            }
            socket
                .write_all(b"HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n\r\n")
                .await
                .unwrap();
            String::from_utf8(request).unwrap()
        });

        let options = InfluxDbOptions {
            base_url: format!("http://127.0.0.1:{port}"),
            token: "secret".into(),
            organization: "o".into(),
            bucket: "b".into(),
        };
        InfluxDbWriter::new(&options)
            .unwrap()
            .write("line 1".into())
            .await
            .unwrap();

        let request = server.await.unwrap().to_lowercase();
        assert!(
            request.starts_with("post /api/v2/write?org=o&bucket=b&precision=s http/1.1\r\n"),
            "{request}"
        );
        assert!(
            request.contains("authorization: token secret\r\n"),
            "{request}"
        );
        assert!(
            request.contains("content-type: text/plain; charset=utf-8\r\n"),
            "{request}"
        );
    }
}
