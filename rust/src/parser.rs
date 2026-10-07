//! Finds telegrams in the byte stream and parses their OBIS lines.

use std::sync::Arc;

use memchr::memmem;
use tracing::{Level, enabled, error, info, trace, warn};

use crate::crc;
use crate::intern::StringInternCache;
use crate::latin1::Latin1;
use crate::mapping::ObisMappingList;
use crate::value::{DsmrValue, ValueError};

const STRING_CACHE_SIZE: usize = 32;

/// Why a data line was not stored.
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error)]
pub enum LineError {
    #[error("line is not well formed")]
    Malformed,
    #[error("unknown OBIS id")]
    UnknownId,
    #[error("duplicated value")]
    Duplicate,
    #[error("invalid value: {0}")]
    InvalidValue(#[from] ValueError),
}

#[derive(Debug)]
pub struct DsmrParser {
    mappings: Arc<ObisMappingList>,
    strings: StringInternCache,
    is_first_datagram: bool,
}

impl DsmrParser {
    #[must_use]
    pub fn new(mappings: Arc<ObisMappingList>) -> Self {
        Self {
            mappings,
            strings: StringInternCache::new(STRING_CACHE_SIZE),
            is_first_datagram: true,
        }
    }

    /// Looks for a complete telegram at the start of `buffer`:
    /// `/XXX5<identification>\r\n\r\n<data lines>\r\n!<CRC>\r\n`.
    ///
    /// Returns the data lines (separated by `\r\n`) of a telegram with a valid CRC and advances
    /// `buffer` past it. Returns `None` when more data is needed, leaving `buffer` untouched, or
    /// when invalid data was dropped from the front of `buffer` (which is logged).
    pub fn try_find_data_lines<'a>(&mut self, buffer: &mut &'a [u8]) -> Option<&'a [u8]> {
        if buffer.is_empty() {
            return None;
        }

        // If we don't start with the identification line, drop everything before it.
        if buffer[0] != b'/' {
            let start = memmem::find(buffer, b"\r\n/")? + 2;
            let dropped = Latin1(&buffer[..start]);
            if self.is_first_datagram {
                info!("Dropped data before datagram start: {dropped}");
            } else {
                error!("Dropped data before datagram start: {dropped}");
            }
            *buffer = &buffer[start..];
        }

        // Check the identification line, which is followed by an empty line.
        let ident_end = memmem::find(buffer, b"\r\n")?;
        if ident_end + 3 >= buffer.len() {
            return None;
        }
        if buffer.get(4) != Some(&b'5') || &buffer[ident_end + 2..ident_end + 4] != b"\r\n" {
            error!(
                "Invalid identification line, dropped the line {}",
                Latin1(&buffer[..ident_end])
            );
            *buffer = &buffer[ident_end + 2..];
            return None;
        }
        let data_start = ident_end + 4;

        // Look for the CRC line.
        let data_end = ident_end + 2 + memmem::find(&buffer[ident_end + 2..], b"\r\n!")?;
        let crc_start = data_end + 3;
        if crc_start + 5 >= buffer.len() {
            return None;
        }
        self.is_first_datagram = false;

        let telegram_end = crc_start + 6;
        if &buffer[crc_start + 4..telegram_end] == b"\r\n"
            && crc::check_crc(&buffer[..crc_start], &buffer[crc_start..crc_start + 4])
        {
            let data = buffer.get(data_start..data_end).unwrap_or_default();
            *buffer = &buffer[telegram_end..];
            return Some(data);
        }

        error!(
            "Invalid CRC, dropped the datagram {}",
            Latin1(&buffer[..telegram_end])
        );
        *buffer = &buffer[telegram_end..];
        None
    }

    /// Parses the first line of `buffer` (`<OBIS id>(<value>)`) into its slot in `values`,
    /// and advances `buffer` to the next line. Problems are logged and returned.
    ///
    /// Returns the index of the value that was set.
    pub fn parse_data_line(
        &mut self,
        buffer: &mut &[u8],
        values: &mut [DsmrValue],
    ) -> Result<usize, LineError> {
        let line = match memmem::find(buffer, b"\r\n") {
            Some(end) => {
                let line = &buffer[..end];
                *buffer = &buffer[end + 2..];
                line
            }
            None => std::mem::take(buffer),
        };

        let Some(open) =
            memchr::memchr(b'(', line).filter(|&open| open >= 1 && line.last() == Some(&b')'))
        else {
            error!("{}: not well formed, dropped", Latin1(line));
            return Err(LineError::Malformed);
        };
        let raw_value = &line[open + 1..line.len() - 1];

        let Some(mapping) = self.mappings.find_by_id(&line[..open]) else {
            warn!("{}: unknown obis id, line dropped", Latin1(line));
            return Err(LineError::UnknownId);
        };

        let value = &mut values[mapping.index];
        if !value.is_empty() {
            error!("{}: duplicated value", Latin1(line));
            return Err(LineError::Duplicate);
        }

        if let Err(e) = value.try_set(raw_value, &mut self.strings) {
            error!("{}: parsing of value failed", Latin1(line));
            return Err(e.into());
        }

        if enabled!(Level::TRACE) {
            trace!("{value} parsed");
        }
        Ok(mapping.index)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::test_support::{Logs, capture_logs, new_values, test_mapping_list};
    use crate::value::{OnOff, Value};
    use chrono::{Local, TimeZone};
    use rust_decimal::Decimal;

    fn parser() -> DsmrParser {
        DsmrParser::new(test_mapping_list())
    }

    /// Runs `try_find_data_lines` once; returns (data lines, remaining buffer, logs).
    fn find(parser: &mut DsmrParser, input: &str) -> (Option<String>, String, Logs) {
        let bytes = input.as_bytes();
        let ((data, remaining), logs) = capture_logs(|| {
            let mut buffer = bytes;
            let data = parser.try_find_data_lines(&mut buffer);
            (
                data.map(crate::latin1::decode),
                crate::latin1::decode(buffer),
            )
        });
        (data, remaining, logs)
    }

    #[test]
    fn second_datagram_garbage_is_an_error() {
        let mut parser = parser();
        let (data, remaining, logs) = find(
            &mut parser,
            "/abc512\r\n\r\n\r\n!774B\r\ngarbage\r\n/abc512\r\n\r\n\r\n!774B\r\n",
        );
        assert_eq!(data.as_deref(), Some(""));
        assert_eq!(remaining, "garbage\r\n/abc512\r\n\r\n\r\n!774B\r\n");
        assert!(logs.is_empty());

        let (data, remaining, logs) = find(&mut parser, &remaining);
        assert_eq!(data.as_deref(), Some(""));
        assert_eq!(remaining, "");
        assert_eq!(
            logs,
            [(
                Level::ERROR,
                "Dropped data before datagram start: garbage\r\n".to_owned()
            )]
        );
    }

    #[test]
    fn finds_data_lines() {
        let cases = [
            ("/abc512\r\n\r\n\r\n!774B\r\n", "", "", None),
            (
                "garbage\r\n/abc512\r\n\r\n\r\n!774B\r\n",
                "",
                "",
                Some((
                    Level::INFO,
                    "Dropped data before datagram start: garbage\r\n",
                )),
            ),
            (
                "/abc512\r\n\r\ndataline1\r\ndataline2\r\n!12EC\r\n",
                "dataline1\r\ndataline2",
                "",
                None,
            ),
            (
                "/abc512\r\n\r\ndataline1\r\ndataline2\r\n!12EC\r\n/rem5ining\r\n\r\n\r\n!0000\r\n",
                "dataline1\r\ndataline2",
                "/rem5ining\r\n\r\n\r\n!0000\r\n",
                None,
            ),
        ];
        for (input, expected_data, expected_remaining, expected_log) in cases {
            let (data, remaining, logs) = find(&mut parser(), input);
            assert_eq!(data.as_deref(), Some(expected_data), "{input:?}");
            assert_eq!(remaining, expected_remaining, "{input:?}");
            let expected_logs: Logs = expected_log
                .into_iter()
                .map(|(l, m)| (l, m.to_owned()))
                .collect();
            assert_eq!(logs, expected_logs, "{input:?}");
        }
    }

    #[test]
    fn partial_data_is_kept() {
        let cases = [
            "",
            "garbage",
            "garbage\r\n",
            "/abc512",
            "/abc512\r\n",
            "/abc512\r\n\r\ndataline1\r\ndataline2\r\n",
            "/abc512\r\n\r\ndataline1\r\ndataline2\r\n!12EC",
        ];
        for input in cases {
            let (data, remaining, logs) = find(&mut parser(), input);
            assert_eq!(data, None, "{input:?}");
            assert_eq!(remaining, input);
            assert!(logs.is_empty(), "{input:?}: {logs:?}");
        }
    }

    #[test]
    fn invalid_data_is_dropped() {
        let cases = [
            (
                "garbage\r\n/",
                "/",
                Level::INFO,
                "Dropped data before datagram start: garbage\r\n",
            ),
            (
                "/abcx12\r\n\r\n",
                "\r\n",
                Level::ERROR,
                "Invalid identification line, dropped the line /abcx12",
            ),
            (
                "/abc512\r\nerror",
                "error",
                Level::ERROR,
                "Invalid identification line, dropped the line /abc512",
            ),
            (
                "/abc512\r\n\ra",
                "\ra",
                Level::ERROR,
                "Invalid identification line, dropped the line /abc512",
            ),
            (
                "/abc512\r\n\r\ndataline1\r\ndataline2\r\n!0000\r\n",
                "",
                Level::ERROR,
                "Invalid CRC, dropped the datagram /abc512\r\n\r\ndataline1\r\ndataline2\r\n!0000\r\n",
            ),
        ];
        for (input, expected_remaining, level, message) in cases {
            let (data, remaining, logs) = find(&mut parser(), input);
            assert_eq!(data, None, "{input:?}");
            assert_eq!(remaining, expected_remaining, "{input:?}");
            assert_eq!(logs, [(level, message.to_owned())], "{input:?}");
        }
    }

    #[test]
    fn empty_data_with_shared_line_break_does_not_panic() {
        let (_, remaining, _) = find(&mut parser(), "/abc5\r\n\r\n!0000\r\n");
        assert_eq!(remaining, "");
    }

    /// Runs `parse_data_line` once; returns (result, remaining buffer, values, logs without trace).
    fn parse(input: &[u8], values: &mut [DsmrValue]) -> (Result<usize, LineError>, String, Logs) {
        let mut parser = parser();
        let ((result, remaining), logs) = capture_logs(|| {
            let mut buffer = input;
            let result = parser.parse_data_line(&mut buffer, values);
            (result, crate::latin1::decode(buffer))
        });
        (
            result,
            remaining,
            logs.into_iter()
                .filter(|(level, _)| *level != Level::TRACE)
                .collect(),
        )
    }

    #[test]
    fn parses_data_line() {
        let time = Value::Time(
            Local
                .with_ymd_and_hms(2023, 8, 17, 17, 14, 30)
                .unwrap()
                .fixed_offset(),
        );
        let cases = [
            ("0-0:1.0.0(230817171430S)", "time", time.clone(), ""),
            ("0-0:1.0.0(230817171430S)\r\n", "time", time.clone(), ""),
            (
                "0-0:1.0.0(230817171430S)\r\nremaining",
                "time",
                time,
                "remaining",
            ),
            (
                "0-0:42.0.0(AUX1030303218166)",
                "name",
                Value::String("AUX1030303218166".into()),
                "",
            ),
            ("0-0:96.50.68(ON)", "state", Value::OnOff(OnOff::On), ""),
            (
                "0-0:96.14.0(0001)",
                "tariff",
                Value::Number(Decimal::ONE),
                "",
            ),
            (
                "1-0:13.7.0(0.336)",
                "power_factor",
                Value::Number(Decimal::new(336, 3)),
                "",
            ),
            (
                "0-0:98.1.0(230801000000S)(000663.924*kWh)",
                "previous_month",
                Value::Ignored,
                "",
            ),
        ];
        for (input, field_name, expected, expected_remaining) in cases {
            let mut values = new_values(&test_mapping_list());
            let (result, remaining, logs) = parse(input.as_bytes(), &mut values);
            let index = result.unwrap();
            assert_eq!(remaining, expected_remaining);
            assert_eq!(values[index].mapping().field_name, field_name);
            assert_eq!(values[index].value(), Some(&expected));
            assert!(logs.is_empty(), "{logs:?}");
        }
    }

    #[test]
    fn data_line_failures() {
        let cases = [
            (
                "0-0:96.50.68ON)\r\n0-0:96.14.0(0000)",
                "0-0:96.14.0(0000)",
                LineError::Malformed,
                (Level::ERROR, "0-0:96.50.68ON): not well formed, dropped"),
            ),
            (
                "0-0:96.50.68ON)",
                "",
                LineError::Malformed,
                (Level::ERROR, "0-0:96.50.68ON): not well formed, dropped"),
            ),
            (
                "(ON)\r\n0-0:96.14.0(0000)",
                "0-0:96.14.0(0000)",
                LineError::Malformed,
                (Level::ERROR, "(ON): not well formed, dropped"),
            ),
            (
                "",
                "",
                LineError::Malformed,
                (Level::ERROR, ": not well formed, dropped"),
            ),
            (
                "0-0:0.0.0(ON)\r\n0-0:96.14.0(0000)",
                "0-0:96.14.0(0000)",
                LineError::UnknownId,
                (Level::WARN, "0-0:0.0.0(ON): unknown obis id, line dropped"),
            ),
            (
                "0-0:96.50.68(on)\r\n0-0:96.14.0(0000)",
                "0-0:96.14.0(0000)",
                LineError::InvalidValue(ValueError::InvalidOnOff),
                (Level::ERROR, "0-0:96.50.68(on): parsing of value failed"),
            ),
        ];
        for (input, expected_remaining, expected_error, (level, message)) in cases {
            let mut values = new_values(&test_mapping_list());
            let (result, remaining, logs) = parse(input.as_bytes(), &mut values);
            assert_eq!(result, Err(expected_error), "{input:?}");
            assert_eq!(remaining, expected_remaining, "{input:?}");
            assert!(values.iter().all(DsmrValue::is_empty));
            assert_eq!(logs, [(level, message.to_owned())], "{input:?}");
        }
    }

    #[test]
    fn duplicated_value() {
        let mut parser = parser();
        let mut values = new_values(&test_mapping_list());
        let mut buffer: &[u8] = b"0-0:96.50.68(ON)\r\n0-0:96.50.68(ON)";

        let (index, logs) = capture_logs(|| parser.parse_data_line(&mut buffer, &mut values));
        let index = index.unwrap();
        assert!(!values[index].is_empty());
        assert!(logs.iter().all(|(level, _)| *level == Level::TRACE));

        let (result, logs) = capture_logs(|| parser.parse_data_line(&mut buffer, &mut values));
        assert_eq!(result, Err(LineError::Duplicate));
        assert_eq!(
            logs,
            [(
                Level::ERROR,
                "0-0:96.50.68(ON): duplicated value".to_owned()
            )]
        );
    }

    #[test]
    fn traces_parsed_values() {
        let mut values = new_values(&test_mapping_list());
        let mut parser = parser();
        let (_, logs) =
            capture_logs(|| parser.parse_data_line(&mut &b"0-0:96.50.68(ON)"[..], &mut values));
        assert_eq!(
            logs,
            [(Level::TRACE, "0-0:96.50.68 state: ON parsed".to_owned())]
        );
    }
}
