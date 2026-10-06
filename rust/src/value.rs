//! Values parsed from the OBIS lines of a telegram.

use std::fmt;
use std::str::FromStr;
use std::sync::Arc;

use chrono::{DateTime, FixedOffset, Local, LocalResult, NaiveDate, TimeZone};
use rust_decimal::Decimal;

use crate::intern::StringInternCache;
use crate::model::{DsmrType, DsmrUnit, ObisMapping};

/// Longest accepted string value.
pub const MAX_STRING_LENGTH: usize = 32;
/// Longest accepted number, without its unit.
pub const MAX_NUMBER_LENGTH: usize = 30;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum OnOff {
    Off,
    On,
}

impl OnOff {
    #[must_use]
    pub const fn as_str(self) -> &'static str {
        match self {
            Self::Off => "OFF",
            Self::On => "ON",
        }
    }
}

impl fmt::Display for OnOff {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.as_str())
    }
}

/// A parsed value. The variant always matches the [`DsmrType`] of the mapping it belongs to.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub enum Value {
    Ignored,
    String(Arc<str>),
    Number(Decimal),
    Time(DateTime<FixedOffset>),
    OnOff(OnOff),
}

/// Why a raw value was rejected.
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error)]
pub enum ValueError {
    #[error("value is too long")]
    TooLong,
    #[error("unit does not match the mapping")]
    UnitMismatch,
    #[error("not a number")]
    InvalidNumber,
    #[error("not a valid timestamp")]
    InvalidTime,
    #[error("not ON or OFF")]
    InvalidOnOff,
}

/// The slot of one mapping in a telegram: empty until its line is parsed.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct DsmrValue {
    mapping: Arc<ObisMapping>,
    value: Option<Value>,
}

impl DsmrValue {
    /// An empty value for `mapping`.
    #[must_use]
    pub fn new(mapping: Arc<ObisMapping>) -> Self {
        Self {
            mapping,
            value: None,
        }
    }

    /// A value for `mapping` that is already set.
    #[must_use]
    pub fn with_value(mapping: Arc<ObisMapping>, value: Value) -> Self {
        Self {
            mapping,
            value: Some(value),
        }
    }

    #[must_use]
    pub fn mapping(&self) -> &Arc<ObisMapping> {
        &self.mapping
    }

    #[must_use]
    pub fn value(&self) -> Option<&Value> {
        self.value.as_ref()
    }

    #[must_use]
    pub fn is_empty(&self) -> bool {
        self.value.is_none()
    }

    pub fn clear(&mut self) {
        self.value = None;
    }

    /// Parses `raw` (the text between the parentheses) according to the mapping's type.
    /// On failure the value is left empty.
    pub fn try_set(
        &mut self,
        raw: &[u8],
        strings: &mut StringInternCache,
    ) -> Result<(), ValueError> {
        let parsed = match self.mapping.dsmr_type {
            DsmrType::Ignored => Ok(Value::Ignored),
            DsmrType::String => parse_string(raw, strings).map(Value::String),
            DsmrType::Number => parse_number(raw, self.mapping.unit).map(Value::Number),
            DsmrType::Time => parse_time(raw, &Local).map(Value::Time),
            DsmrType::OnOff => parse_on_off(raw).map(Value::OnOff),
        };
        self.value = parsed.as_ref().ok().cloned();
        parsed.map(|_| ())
    }
}

impl fmt::Display for DsmrValue {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let ObisMapping {
            id,
            field_name,
            unit,
            ..
        } = self.mapping.as_ref();
        write!(f, "{id} {field_name}: ")?;
        match &self.value {
            None => f.write_str("empty"),
            Some(Value::Ignored) => f.write_str("ignored"),
            Some(Value::String(s)) => write!(f, "\"{s}\""),
            Some(Value::Number(n)) if *unit == DsmrUnit::None => write!(f, "{n}"),
            Some(Value::Number(n)) => write!(f, "{n} {unit}"),
            Some(Value::Time(t)) => write!(f, "{}", RoundTrip(t)),
            Some(Value::OnOff(v)) => write!(f, "{v}"),
        }
    }
}

/// Formats a timestamp like .NET's round-trip format `O`: `2023-08-21T11:24:30.0000000+02:00`.
struct RoundTrip<'a>(&'a DateTime<FixedOffset>);

impl fmt::Display for RoundTrip<'_> {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let t = self.0;
        let ticks = t.timestamp_subsec_nanos() / 100;
        write!(
            f,
            "{}.{ticks:07}{}",
            t.format("%Y-%m-%dT%H:%M:%S"),
            t.format("%:z")
        )
    }
}

fn parse_string(raw: &[u8], strings: &mut StringInternCache) -> Result<Arc<str>, ValueError> {
    if raw.len() > MAX_STRING_LENGTH {
        return Err(ValueError::TooLong);
    }
    Ok(strings.get(raw))
}

/// Parses `<number>` or `<number>*<unit>`; the unit must match `unit`.
/// Trailing zeros after the decimal point are dropped (`0042.4200` is `42.42`).
pub fn parse_number(raw: &[u8], unit: DsmrUnit) -> Result<Decimal, ValueError> {
    let number = match memchr::memchr(b'*', raw) {
        Some(star) if unit != DsmrUnit::None && &raw[star + 1..] == unit.as_str().as_bytes() => {
            &raw[..star]
        }
        Some(_) => return Err(ValueError::UnitMismatch),
        None if unit != DsmrUnit::None => return Err(ValueError::UnitMismatch),
        None => raw,
    };
    if number.len() > MAX_NUMBER_LENGTH {
        return Err(ValueError::TooLong);
    }

    let digits = number
        .strip_prefix(b"-")
        .or_else(|| number.strip_prefix(b"+"))
        .unwrap_or(number);
    let (integer, fraction) = match memchr::memchr(b'.', digits) {
        Some(dot) => (&digits[..dot], &digits[dot + 1..]),
        None => (digits, &b""[..]),
    };
    let all_digits = |s: &[u8]| s.iter().all(u8::is_ascii_digit);
    if integer.len() + fraction.len() == 0 || !all_digits(integer) || !all_digits(fraction) {
        return Err(ValueError::InvalidNumber);
    }

    // Only ASCII digits, sign and dot are left, so this is valid UTF-8.
    let text = std::str::from_utf8(number).map_err(|_| ValueError::InvalidNumber)?;
    Decimal::from_str(text)
        .map(|d| d.normalize())
        .map_err(|_| ValueError::InvalidNumber)
}

/// Parses `YYMMDDhhmmssX`, where `X` is `S` (summer) or `W` (winter) time, in the time zone `tz`.
///
/// When a local time occurs twice (the hour when daylight saving time ends), `S` selects the
/// first (summer time) and `W` the second (winter time) occurrence.
pub fn parse_time<Tz: TimeZone>(raw: &[u8], tz: &Tz) -> Result<DateTime<FixedOffset>, ValueError> {
    let [digits @ .., season] = raw else {
        return Err(ValueError::InvalidTime);
    };
    if digits.len() != 12
        || !matches!(season, b'S' | b'W')
        || !digits.iter().all(u8::is_ascii_digit)
    {
        return Err(ValueError::InvalidTime);
    }
    let field = |i: usize| u32::from(digits[i] - b'0') * 10 + u32::from(digits[i + 1] - b'0');

    // Two-digit years follow .NET: 00-49 are 20xx, 50-99 are 19xx.
    let yy = field(0);
    let year = i32::try_from(if yy < 50 { 2000 + yy } else { 1900 + yy })
        .map_err(|_| ValueError::InvalidTime)?;
    let naive = NaiveDate::from_ymd_opt(year, field(2), field(4))
        .and_then(|date| date.and_hms_opt(field(6), field(8), field(10)))
        .ok_or(ValueError::InvalidTime)?;

    let local = match tz.from_local_datetime(&naive) {
        LocalResult::Single(t) => t,
        LocalResult::Ambiguous(summer, winter) => {
            if *season == b'S' {
                summer
            } else {
                winter
            }
        }
        LocalResult::None => return Err(ValueError::InvalidTime),
    };
    Ok(local.fixed_offset())
}

fn parse_on_off(raw: &[u8]) -> Result<OnOff, ValueError> {
    match raw {
        b"ON" => Ok(OnOff::On),
        b"OFF" => Ok(OnOff::Off),
        _ => Err(ValueError::InvalidOnOff),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use rust_decimal::dec;

    fn mapping(dsmr_type: DsmrType, unit: DsmrUnit) -> Arc<ObisMapping> {
        Arc::new(ObisMapping::new("id", "field", dsmr_type, unit))
    }

    fn set(value: &mut DsmrValue, raw: &[u8]) -> Result<(), ValueError> {
        value.try_set(raw, &mut StringInternCache::new(4))
    }

    #[test]
    fn new_value_is_empty() {
        for dsmr_type in [
            DsmrType::Ignored,
            DsmrType::String,
            DsmrType::Number,
            DsmrType::Time,
            DsmrType::OnOff,
        ] {
            let value = DsmrValue::new(mapping(dsmr_type, DsmrUnit::None));
            assert!(value.is_empty());
            assert_eq!(value.to_string(), "id field: empty");
        }
    }

    #[test]
    fn ignored_value() {
        let mut value = DsmrValue::new(mapping(DsmrType::Ignored, DsmrUnit::None));
        set(
            &mut value,
            b"12345678901234567890123456789012345678901234567890",
        )
        .unwrap();
        assert!(!value.is_empty());
        assert_eq!(value.to_string(), "id field: ignored");
        let empty = DsmrValue::new(mapping(DsmrType::Ignored, DsmrUnit::None));
        assert_ne!(value, empty);
        value.clear();
        assert!(value.is_empty());
        assert_eq!(value, empty);
    }

    #[test]
    fn string_value() {
        let mut value = DsmrValue::new(mapping(DsmrType::String, DsmrUnit::None));
        set(&mut value, b"12345678901234567890123456789012").unwrap();
        assert!(!value.is_empty());
        assert_eq!(
            value.to_string(),
            "id field: \"12345678901234567890123456789012\""
        );
        let expected = DsmrValue::with_value(
            mapping(DsmrType::String, DsmrUnit::None),
            Value::String("12345678901234567890123456789012".into()),
        );
        assert_eq!(value, expected);
        value.clear();
        assert!(value.is_empty());
        assert_ne!(value, expected);
    }

    #[test]
    fn string_value_too_long() {
        let mut value = DsmrValue::with_value(
            mapping(DsmrType::String, DsmrUnit::None),
            Value::String("x".into()),
        );
        assert_eq!(
            set(&mut value, b"123456789012345678901234567890123"),
            Err(ValueError::TooLong)
        );
        assert!(value.is_empty());
    }

    #[test]
    fn number_value() {
        let cases = [
            ("0", DsmrUnit::None, dec!(0)),
            ("0000", DsmrUnit::None, dec!(0)),
            ("4.2", DsmrUnit::None, dec!(4.2)),
            ("0042.4200", DsmrUnit::None, dec!(42.42)),
            ("0.42", DsmrUnit::None, dec!(0.42)),
            ("42.0", DsmrUnit::None, dec!(42)),
            ("42", DsmrUnit::None, dec!(42)),
            ("42*kWh", DsmrUnit::KWh, dec!(42)),
            ("42*kvarh", DsmrUnit::Kvarh, dec!(42)),
            ("42*kW", DsmrUnit::KW, dec!(42)),
            ("42*kvar", DsmrUnit::Kvar, dec!(42)),
            ("42*Hz", DsmrUnit::Hz, dec!(42)),
            ("42*V", DsmrUnit::V, dec!(42)),
            ("42*A", DsmrUnit::A, dec!(42)),
        ];
        for (input, unit, expected) in cases {
            let mut value = DsmrValue::new(mapping(DsmrType::Number, unit));
            set(&mut value, input.as_bytes()).unwrap();
            assert_eq!(value.value(), Some(&Value::Number(expected)), "{input}");
            let text = if unit == DsmrUnit::None {
                format!("id field: {expected}")
            } else {
                format!("id field: {expected} {unit}")
            };
            assert_eq!(value.to_string(), text);
            let expected =
                DsmrValue::with_value(mapping(DsmrType::Number, unit), Value::Number(expected));
            assert_eq!(value, expected);
            value.clear();
            assert!(value.is_empty());
            assert_ne!(value, expected);
        }
    }

    #[test]
    fn number_formatting_drops_trailing_zeros() {
        assert_eq!(
            parse_number(b"0042.4200", DsmrUnit::None)
                .unwrap()
                .to_string(),
            "42.42"
        );
        assert_eq!(
            parse_number(b"234.0", DsmrUnit::None).unwrap().to_string(),
            "234"
        );
        assert_eq!(
            parse_number(b"000000.000", DsmrUnit::None)
                .unwrap()
                .to_string(),
            "0"
        );
        assert_eq!(
            parse_number(b"0001", DsmrUnit::None).unwrap().to_string(),
            "1"
        );
        assert_eq!(
            parse_number(b"-1.50", DsmrUnit::None).unwrap().to_string(),
            "-1.5"
        );
    }

    #[test]
    fn number_value_wrong_unit() {
        let cases = [
            ("42", DsmrUnit::KW),
            ("42*kWh", DsmrUnit::KW),
            ("42*kvarh", DsmrUnit::Kvar),
            ("42*kW", DsmrUnit::KWh),
            ("42*kvar", DsmrUnit::Kvarh),
            ("42*Hz", DsmrUnit::A),
            ("42*V", DsmrUnit::A),
            ("42*A", DsmrUnit::V),
            ("42*A", DsmrUnit::None),
        ];
        for (input, unit) in cases {
            let mut value =
                DsmrValue::with_value(mapping(DsmrType::Number, unit), Value::Number(Decimal::ONE));
            assert_eq!(
                set(&mut value, input.as_bytes()),
                Err(ValueError::UnitMismatch),
                "{input}"
            );
            assert!(value.is_empty());
        }
    }

    #[test]
    fn number_value_invalid() {
        for input in [
            "",
            ".",
            "1.2.3",
            "abc",
            "1e5",
            " 1",
            "1_000",
            "1234567890123456789012345678901",
        ] {
            assert!(
                parse_number(input.as_bytes(), DsmrUnit::None).is_err(),
                "{input}"
            );
        }
    }

    #[test]
    fn time_value() {
        let cet = FixedOffset::east_opt(2 * 3600).unwrap();
        let expected = cet.with_ymd_and_hms(2023, 8, 21, 11, 24, 30).unwrap();
        assert_eq!(parse_time(b"230821112430W", &cet), Ok(expected));
        assert_eq!(parse_time(b"230821112430S", &cet), Ok(expected));

        let mut value = DsmrValue::new(mapping(DsmrType::Time, DsmrUnit::None));
        set(&mut value, b"230821112430S").unwrap();
        let expected = Local
            .with_ymd_and_hms(2023, 8, 21, 11, 24, 30)
            .unwrap()
            .fixed_offset();
        assert_eq!(value.value(), Some(&Value::Time(expected)));
        assert_eq!(
            value.to_string(),
            format!("id field: {}", RoundTrip(&expected))
        );
        let expected = DsmrValue::with_value(
            mapping(DsmrType::Time, DsmrUnit::None),
            Value::Time(expected),
        );
        assert_eq!(value, expected);
        value.clear();
        assert!(value.is_empty());
        assert_ne!(value, expected);
    }

    #[test]
    fn time_value_display() {
        let value = DsmrValue::with_value(
            mapping(DsmrType::Time, DsmrUnit::None),
            Value::Time(
                FixedOffset::east_opt(2 * 3600)
                    .unwrap()
                    .with_ymd_and_hms(2023, 8, 21, 11, 24, 30)
                    .unwrap(),
            ),
        );
        assert_eq!(
            value.to_string(),
            "id field: 2023-08-21T11:24:30.0000000+02:00"
        );
    }

    #[test]
    fn time_value_failures() {
        let cases = [
            "20230821112430S",
            "230821112430s",
            " 30821112430S",
            "A30821112430S",
            "230021112430S",
            "231321112430S",
            "230800112430S",
            "230832112430S",
            "230821242430S",
            "230821116030S",
            "230821112460S",
            "",
        ];
        for input in cases {
            let mut value = DsmrValue::with_value(
                mapping(DsmrType::Time, DsmrUnit::None),
                Value::Time(Local::now().fixed_offset()),
            );
            assert_eq!(
                set(&mut value, input.as_bytes()),
                Err(ValueError::InvalidTime),
                "{input}"
            );
            assert!(value.is_empty());
        }
    }

    /// Fixed time zone with a fall-back transition, for testing ambiguous local times.
    #[derive(Clone)]
    struct FallBack;

    impl TimeZone for FallBack {
        type Offset = FixedOffset;

        fn from_offset(_: &FixedOffset) -> Self {
            Self
        }

        fn offset_from_local_date(&self, _: &NaiveDate) -> LocalResult<FixedOffset> {
            unimplemented!()
        }

        fn offset_from_local_datetime(
            &self,
            _: &chrono::NaiveDateTime,
        ) -> LocalResult<FixedOffset> {
            LocalResult::Ambiguous(
                FixedOffset::east_opt(7200).unwrap(),
                FixedOffset::east_opt(3600).unwrap(),
            )
        }

        fn offset_from_utc_date(&self, _: &NaiveDate) -> FixedOffset {
            unimplemented!()
        }

        fn offset_from_utc_datetime(&self, _: &chrono::NaiveDateTime) -> FixedOffset {
            unimplemented!()
        }
    }

    #[test]
    fn ambiguous_time_uses_season_flag() {
        assert_eq!(
            parse_time(b"231029023000S", &FallBack)
                .unwrap()
                .to_rfc3339(),
            "2023-10-29T02:30:00+02:00"
        );
        assert_eq!(
            parse_time(b"231029023000W", &FallBack)
                .unwrap()
                .to_rfc3339(),
            "2023-10-29T02:30:00+01:00"
        );
    }

    #[test]
    fn on_off_value() {
        for (input, expected) in [("ON", OnOff::On), ("OFF", OnOff::Off)] {
            let mut value = DsmrValue::new(mapping(DsmrType::OnOff, DsmrUnit::None));
            set(&mut value, input.as_bytes()).unwrap();
            assert_eq!(value.value(), Some(&Value::OnOff(expected)));
            assert_eq!(value.to_string(), format!("id field: {expected}"));
            let expected = DsmrValue::with_value(
                mapping(DsmrType::OnOff, DsmrUnit::None),
                Value::OnOff(expected),
            );
            assert_eq!(value, expected);
            value.clear();
            assert!(value.is_empty());
            assert_ne!(value, expected);
        }
    }

    #[test]
    fn on_off_value_failure() {
        let mut value = DsmrValue::with_value(
            mapping(DsmrType::OnOff, DsmrUnit::None),
            Value::OnOff(OnOff::On),
        );
        assert_eq!(set(&mut value, b"off"), Err(ValueError::InvalidOnOff));
        assert!(value.is_empty());
    }
}
