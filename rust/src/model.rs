//! Mapping model: which OBIS id maps to which field, with which type and unit.

use std::fmt;
use std::str::FromStr;

use serde::{Deserialize, Deserializer, de};

/// How the value of an OBIS line is interpreted.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum DsmrType {
    /// The line must be present, but its value is not stored.
    Ignored,
    /// Stored as an InfluxDB tag.
    String,
    /// Stored as an InfluxDB field, grouped by unit.
    Number,
    /// Timestamp of the telegram.
    Time,
    /// `ON` / `OFF`, stored as an InfluxDB tag.
    OnOff,
}

/// Unit of a numeric value, written as `<value>*<unit>` in a telegram.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub enum DsmrUnit {
    #[default]
    None,
    KWh,
    Kvarh,
    KW,
    Kvar,
    Hz,
    V,
    A,
}

impl DsmrUnit {
    /// The unit as it appears in telegrams and in the `unit` tag (`None` for unitless values).
    #[must_use]
    pub const fn as_str(self) -> &'static str {
        match self {
            Self::None => "None",
            Self::KWh => "kWh",
            Self::Kvarh => "kvarh",
            Self::KW => "kW",
            Self::Kvar => "kvar",
            Self::Hz => "Hz",
            Self::V => "V",
            Self::A => "A",
        }
    }
}

impl fmt::Display for DsmrUnit {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.as_str())
    }
}

/// Error for an unknown [`DsmrType`] or [`DsmrUnit`] name.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[error("unknown {kind} `{value}`")]
pub struct UnknownVariant {
    kind: &'static str,
    value: String,
}

// Names are matched case-insensitively, like System.Text.Json's string enum converter.
impl FromStr for DsmrType {
    type Err = UnknownVariant;

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        [
            Self::Ignored,
            Self::String,
            Self::Number,
            Self::Time,
            Self::OnOff,
        ]
        .into_iter()
        .find(|t| format!("{t:?}").eq_ignore_ascii_case(s))
        .ok_or_else(|| UnknownVariant {
            kind: "type",
            value: s.to_owned(),
        })
    }
}

impl FromStr for DsmrUnit {
    type Err = UnknownVariant;

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        [
            Self::None,
            Self::KWh,
            Self::Kvarh,
            Self::KW,
            Self::Kvar,
            Self::Hz,
            Self::V,
            Self::A,
        ]
        .into_iter()
        .find(|u| u.as_str().eq_ignore_ascii_case(s))
        .ok_or_else(|| UnknownVariant {
            kind: "unit",
            value: s.to_owned(),
        })
    }
}

fn deserialize_from_str<'de, D, T>(deserializer: D) -> Result<T, D::Error>
where
    D: Deserializer<'de>,
    T: FromStr<Err = UnknownVariant>,
{
    let s = <std::borrow::Cow<'de, str>>::deserialize(deserializer)?;
    s.parse().map_err(de::Error::custom)
}

impl<'de> Deserialize<'de> for DsmrType {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        deserialize_from_str(deserializer)
    }
}

impl<'de> Deserialize<'de> for DsmrUnit {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        deserialize_from_str(deserializer)
    }
}

/// One OBIS id mapped to a field.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct ObisMapping {
    /// OBIS reference, e.g. `1-0:1.8.0`.
    pub id: String,
    /// Field (or tag) name written to InfluxDB.
    pub field_name: String,
    pub dsmr_type: DsmrType,
    pub unit: DsmrUnit,
    /// Position of the mapping in its [`ObisMappingList`](crate::mapping::ObisMappingList), set by the list.
    pub index: usize,
}

impl ObisMapping {
    #[must_use]
    pub fn new(
        id: impl Into<String>,
        field_name: impl Into<String>,
        dsmr_type: DsmrType,
        unit: DsmrUnit,
    ) -> Self {
        Self {
            id: id.into(),
            field_name: field_name.into(),
            dsmr_type,
            unit,
            index: 0,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_names_case_insensitively() {
        assert_eq!("OnOff".parse(), Ok(DsmrType::OnOff));
        assert_eq!("onoff".parse(), Ok(DsmrType::OnOff));
        assert_eq!("kWh".parse(), Ok(DsmrUnit::KWh));
        assert_eq!("KWH".parse(), Ok(DsmrUnit::KWh));
        assert!("bogus".parse::<DsmrUnit>().is_err());
    }
}
