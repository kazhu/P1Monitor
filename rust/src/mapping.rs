//! The list of OBIS mappings of a device, loaded from `obismappings.json`.

use std::collections::HashMap;
use std::ops::Index;
use std::path::{Path, PathBuf};
use std::sync::Arc;

use indexmap::IndexMap;
use serde::Deserialize;

use crate::model::{DsmrType, DsmrUnit, ObisMapping};

#[derive(Debug, thiserror::Error)]
pub enum MappingError {
    #[error("duplicate id mapping `{0}`")]
    DuplicateId(String),
    #[error("device `{0}` is not found in the mappings file")]
    UnknownDevice(String),
    #[error("cannot read mappings file {path}")]
    Io {
        path: PathBuf,
        #[source]
        source: std::io::Error,
    },
    #[error("invalid mappings file")]
    Json(#[from] serde_json::Error),
}

/// Number mappings sharing a unit; written as one InfluxDB line.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UnitNumberMappings {
    pub unit: DsmrUnit,
    /// Ordered by field name.
    pub mappings: Vec<Arc<ObisMapping>>,
}

/// Immutable, indexed list of mappings with the derived views the reader and the writer need.
#[derive(Debug)]
pub struct ObisMappingList {
    mappings: Vec<Arc<ObisMapping>>,
    by_id: HashMap<Box<[u8]>, usize>,
    tags: Vec<Arc<ObisMapping>>,
    number_mappings_by_unit: Vec<UnitNumberMappings>,
    time_field: Option<Arc<ObisMapping>>,
}

impl ObisMappingList {
    /// Builds the list; each mapping's `index` is set to its position.
    pub fn new(mappings: impl IntoIterator<Item = ObisMapping>) -> Result<Self, MappingError> {
        let mappings: Vec<Arc<ObisMapping>> = mappings
            .into_iter()
            .enumerate()
            .map(|(index, mapping)| Arc::new(ObisMapping { index, ..mapping }))
            .collect();

        let mut by_id = HashMap::with_capacity(mappings.len());
        for mapping in &mappings {
            if by_id
                .insert(mapping.id.as_bytes().into(), mapping.index)
                .is_some()
            {
                return Err(MappingError::DuplicateId(mapping.id.clone()));
            }
        }

        let mut tags: Vec<_> = mappings
            .iter()
            .filter(|m| matches!(m.dsmr_type, DsmrType::String | DsmrType::OnOff))
            .cloned()
            .collect();
        tags.sort_by(|a, b| a.field_name.cmp(&b.field_name));

        // Groups keep the order in which their unit first appears.
        let mut number_mappings_by_unit: Vec<UnitNumberMappings> = Vec::new();
        for mapping in mappings.iter().filter(|m| m.dsmr_type == DsmrType::Number) {
            match number_mappings_by_unit
                .iter_mut()
                .find(|g| g.unit == mapping.unit)
            {
                Some(group) => group.mappings.push(Arc::clone(mapping)),
                None => number_mappings_by_unit.push(UnitNumberMappings {
                    unit: mapping.unit,
                    mappings: vec![Arc::clone(mapping)],
                }),
            }
        }
        for group in &mut number_mappings_by_unit {
            group
                .mappings
                .sort_by(|a, b| a.field_name.cmp(&b.field_name));
        }

        let time_field = mappings
            .iter()
            .find(|m| m.dsmr_type == DsmrType::Time)
            .cloned();

        Ok(Self {
            mappings,
            by_id,
            tags,
            number_mappings_by_unit,
            time_field,
        })
    }

    /// Parses the mappings of `device_name` from the JSON content of a mappings file.
    pub fn from_json(json: &str, device_name: &str) -> Result<Self, MappingError> {
        let json = json.strip_prefix('\u{feff}').unwrap_or(json);
        let mut devices: HashMap<String, DeviceMappingDescriptor> = serde_json::from_str(json)?;
        let device = devices
            .remove(device_name)
            .ok_or_else(|| MappingError::UnknownDevice(device_name.to_owned()))?;
        Self::new(
            device
                .mapping
                .into_iter()
                .map(|(id, d)| ObisMapping::new(id, d.field_name, d.dsmr_type, d.unit)),
        )
    }

    /// Loads the mappings of `device_name` from a mappings file.
    pub fn load(path: &Path, device_name: &str) -> Result<Self, MappingError> {
        let json = std::fs::read_to_string(path).map_err(|source| MappingError::Io {
            path: path.to_owned(),
            source,
        })?;
        Self::from_json(&json, device_name)
    }

    #[must_use]
    pub fn len(&self) -> usize {
        self.mappings.len()
    }

    #[must_use]
    pub fn is_empty(&self) -> bool {
        self.mappings.is_empty()
    }

    #[must_use]
    pub fn get(&self, index: usize) -> Option<&Arc<ObisMapping>> {
        self.mappings.get(index)
    }

    pub fn iter(&self) -> std::slice::Iter<'_, Arc<ObisMapping>> {
        self.mappings.iter()
    }

    /// Looks up a mapping by its OBIS id, without allocating.
    #[must_use]
    pub fn find_by_id(&self, id: &[u8]) -> Option<&Arc<ObisMapping>> {
        self.by_id.get(id).map(|&index| &self.mappings[index])
    }

    /// String and on/off mappings (written as tags), ordered by field name.
    #[must_use]
    pub fn tags(&self) -> &[Arc<ObisMapping>] {
        &self.tags
    }

    #[must_use]
    pub fn number_mappings_by_unit(&self) -> &[UnitNumberMappings] {
        &self.number_mappings_by_unit
    }

    /// The first mapping of type [`DsmrType::Time`], if any.
    #[must_use]
    pub fn time_field(&self) -> Option<&Arc<ObisMapping>> {
        self.time_field.as_ref()
    }
}

impl Index<usize> for ObisMappingList {
    type Output = ObisMapping;

    fn index(&self, index: usize) -> &ObisMapping {
        &self.mappings[index]
    }
}

impl<'a> IntoIterator for &'a ObisMappingList {
    type Item = &'a Arc<ObisMapping>;
    type IntoIter = std::slice::Iter<'a, Arc<ObisMapping>>;

    fn into_iter(self) -> Self::IntoIter {
        self.iter()
    }
}

#[derive(Deserialize)]
struct DeviceMappingDescriptor {
    // `country`, `providerName` and `source` are documentation only.
    // IndexMap keeps the file order, which defines the mapping indices.
    mapping: IndexMap<String, MappingDescriptor>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct MappingDescriptor {
    field_name: String,
    #[serde(rename = "type")]
    dsmr_type: DsmrType,
    #[serde(default)]
    unit: DsmrUnit,
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::test_support::test_mappings;

    #[test]
    fn list() {
        let expected = test_mappings();
        let list = ObisMappingList::new(expected.clone()).unwrap();
        assert_eq!(list.len(), expected.len());
        for (i, mapping) in expected.iter().enumerate() {
            assert_eq!(&list[i], mapping);
            assert_eq!(list[i].index, i);
        }
    }

    #[test]
    fn tags() {
        let list = ObisMappingList::new(test_mappings()).unwrap();
        let names: Vec<_> = list.tags().iter().map(|m| m.field_name.as_str()).collect();
        assert_eq!(names, ["name", "serial", "state"]);
    }

    #[test]
    fn time_field() {
        let mappings = test_mappings();
        let list = ObisMappingList::new(mappings.clone()).unwrap();
        assert_eq!(list.time_field().map(Arc::as_ref), Some(&mappings[0]));
    }

    #[test]
    fn iterates_in_order() {
        let mappings = test_mappings();
        let list = ObisMappingList::new(mappings.clone()).unwrap();
        let mut count = 0;
        for (mapping, expected) in list.iter().zip(&mappings) {
            assert_eq!(mapping.as_ref(), expected);
            count += 1;
        }
        assert_eq!(count, mappings.len());
    }

    #[test]
    fn find_by_id() {
        let mappings = test_mappings();
        let list = ObisMappingList::new(mappings.clone()).unwrap();
        for expected in &mappings {
            assert_eq!(
                list.find_by_id(expected.id.as_bytes()).map(Arc::as_ref),
                Some(expected),
                "{}",
                expected.id
            );
        }
        assert!(list.find_by_id(b"").is_none());
        assert!(list.find_by_id(b" ").is_none());
        assert!(list.find_by_id(b"x").is_none());
        assert!(list.find_by_id(b"0-0:96.50.68999999").is_none());
    }

    #[test]
    fn number_mappings_by_unit() {
        let list = ObisMappingList::new(test_mappings()).unwrap();
        let groups: Vec<_> = list
            .number_mappings_by_unit()
            .iter()
            .map(|g| {
                (
                    g.unit,
                    g.mappings
                        .iter()
                        .map(|m| m.field_name.as_str())
                        .collect::<Vec<_>>(),
                )
            })
            .collect();
        assert_eq!(
            groups,
            [
                (
                    DsmrUnit::None,
                    vec![
                        "power_factor",
                        "power_factor_l1",
                        "power_factor_l2",
                        "power_factor_l3",
                        "tariff"
                    ]
                ),
                (
                    DsmrUnit::KWh,
                    vec![
                        "export_energy",
                        "export_energy_tariff_1",
                        "export_energy_tariff_2",
                        "export_energy_tariff_3",
                        "export_energy_tariff_4",
                        "import_energy",
                        "import_energy_tariff_1",
                        "import_energy_tariff_2",
                        "import_energy_tariff_3",
                        "import_energy_tariff_4",
                    ]
                ),
                (
                    DsmrUnit::Kvarh,
                    vec![
                        "export_reactive_energy",
                        "import_reactive_energy",
                        "reactive_energy_q1",
                        "reactive_energy_q2",
                        "reactive_energy_q3",
                        "reactive_energy_q4",
                    ]
                ),
                (DsmrUnit::V, vec!["voltage_l1", "voltage_l2", "voltage_l3"]),
                (DsmrUnit::A, vec!["current_l1", "current_l2", "current_l3"]),
                (DsmrUnit::Hz, vec!["frequency"]),
                (DsmrUnit::KW, vec!["export_power", "import_power"]),
                (
                    DsmrUnit::Kvar,
                    vec![
                        "reactive_power_q1",
                        "reactive_power_q2",
                        "reactive_power_q3",
                        "reactive_power_q4"
                    ]
                ),
            ]
        );
    }

    #[test]
    fn rejects_duplicate_ids() {
        let mapping = ObisMapping::new("1-0:1.8.0", "a", DsmrType::Number, DsmrUnit::KWh);
        assert!(matches!(
            ObisMappingList::new([mapping.clone(), mapping]),
            Err(MappingError::DuplicateId(id)) if id == "1-0:1.8.0"
        ));
    }

    const JSON: &str = r#"{
        "EON_HU_SX631": {
            "country": "Hungary",
            "vendor": "E.ON",
            "source": "https://www.eon.hu/",
            "mapping": {
                "0-0:1.0.0": { "fieldName": "time", "type": "Time", "unit": "A" }
            }
        }
    }"#;

    fn validate(list: &ObisMappingList) {
        assert_eq!(list.len(), 1);
        assert_eq!(list[0].id, "0-0:1.0.0");
        assert_eq!(list[0].field_name, "time");
        assert_eq!(list[0].dsmr_type, DsmrType::Time);
        assert_eq!(list[0].unit, DsmrUnit::A);
    }

    #[test]
    fn loads_from_json() {
        validate(&ObisMappingList::from_json(JSON, "EON_HU_SX631").unwrap());
        validate(&ObisMappingList::from_json(&format!("\u{feff}{JSON}"), "EON_HU_SX631").unwrap());
        assert!(matches!(
            ObisMappingList::from_json(JSON, "other"),
            Err(MappingError::UnknownDevice(_))
        ));
    }

    #[test]
    fn loads_from_file() {
        let path =
            std::env::temp_dir().join(format!("p1monitor-mappings-{}.json", std::process::id()));
        std::fs::write(&path, JSON).unwrap();
        let result = ObisMappingList::load(&path, "EON_HU_SX631");
        std::fs::remove_file(&path).unwrap();
        validate(&result.unwrap());
    }

    #[test]
    fn shipped_mappings_file_is_valid() {
        let list = ObisMappingList::load(
            Path::new(concat!(env!("CARGO_MANIFEST_DIR"), "/obismappings.json")),
            "EON_HU_SX631",
        )
        .unwrap();
        assert_eq!(list.len(), 45);
    }
}
