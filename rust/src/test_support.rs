//! Shared helpers for unit tests.

use std::sync::{Arc, Mutex};

use tracing::field::{Field, Visit};
use tracing::{Event, Level, Subscriber};
use tracing_subscriber::layer::{Context, Layer, SubscriberExt};

use crate::mapping::ObisMappingList;
use crate::model::{DsmrType, DsmrUnit, ObisMapping};
use crate::value::DsmrValue;

/// Captured log events: level and formatted message.
pub type Logs = Vec<(Level, String)>;

/// The mappings used by the C# tests (the E.ON SX631 meter).
pub fn test_mappings() -> Vec<ObisMapping> {
    use DsmrType::{Ignored, Number, OnOff, String, Time};
    use DsmrUnit::{A, Hz, KW, KWh, Kvar, Kvarh, None, V};
    let mappings = [
        ("0-0:1.0.0", "time", Time, None),
        ("0-0:42.0.0", "name", String, None),
        ("0-0:96.1.0", "serial", String, None),
        ("0-0:96.14.0", "tariff", Number, None),
        ("0-0:96.50.68", "state", OnOff, None),
        ("1-0:1.8.0", "import_energy", Number, KWh),
        ("1-0:1.8.1", "import_energy_tariff_1", Number, KWh),
        ("1-0:1.8.2", "import_energy_tariff_2", Number, KWh),
        ("1-0:1.8.3", "import_energy_tariff_3", Number, KWh),
        ("1-0:1.8.4", "import_energy_tariff_4", Number, KWh),
        ("1-0:2.8.0", "export_energy", Number, KWh),
        ("1-0:2.8.1", "export_energy_tariff_1", Number, KWh),
        ("1-0:2.8.2", "export_energy_tariff_2", Number, KWh),
        ("1-0:2.8.3", "export_energy_tariff_3", Number, KWh),
        ("1-0:2.8.4", "export_energy_tariff_4", Number, KWh),
        ("1-0:3.8.0", "import_reactive_energy", Number, Kvarh),
        ("1-0:4.8.0", "export_reactive_energy", Number, Kvarh),
        ("1-0:5.8.0", "reactive_energy_q1", Number, Kvarh),
        ("1-0:6.8.0", "reactive_energy_q2", Number, Kvarh),
        ("1-0:7.8.0", "reactive_energy_q3", Number, Kvarh),
        ("1-0:8.8.0", "reactive_energy_q4", Number, Kvarh),
        ("1-0:32.7.0", "voltage_l1", Number, V),
        ("1-0:52.7.0", "voltage_l2", Number, V),
        ("1-0:72.7.0", "voltage_l3", Number, V),
        ("1-0:31.7.0", "current_l1", Number, A),
        ("1-0:51.7.0", "current_l2", Number, A),
        ("1-0:71.7.0", "current_l3", Number, A),
        ("1-0:13.7.0", "power_factor", Number, None),
        ("1-0:33.7.0", "power_factor_l1", Number, None),
        ("1-0:53.7.0", "power_factor_l2", Number, None),
        ("1-0:73.7.0", "power_factor_l3", Number, None),
        ("1-0:14.7.0", "frequency", Number, Hz),
        ("1-0:1.7.0", "import_power", Number, KW),
        ("1-0:2.7.0", "export_power", Number, KW),
        ("1-0:5.7.0", "reactive_power_q1", Number, Kvar),
        ("1-0:6.7.0", "reactive_power_q2", Number, Kvar),
        ("1-0:7.7.0", "reactive_power_q3", Number, Kvar),
        ("1-0:8.7.0", "reactive_power_q4", Number, Kvar),
        ("0-0:17.0.0", "limiter_limit", Ignored, None),
        ("1-0:15.8.0", "energy_combined", Ignored, None),
        ("1-0:31.4.0", "current_limit_l1", Ignored, None),
        ("1-0:51.4.0", "current_limit_l2", Ignored, None),
        ("1-0:71.4.0", "current_limit_l3", Ignored, None),
        ("0-0:98.1.0", "previous_month", Ignored, None),
        ("0-0:96.13.0", "message", Ignored, None),
    ];
    mappings
        .into_iter()
        .enumerate()
        .map(|(index, (id, field_name, dsmr_type, unit))| ObisMapping {
            index,
            ..ObisMapping::new(id, field_name, dsmr_type, unit)
        })
        .collect()
}

pub fn test_mapping_list() -> Arc<ObisMappingList> {
    Arc::new(ObisMappingList::new(test_mappings()).expect("valid test mappings"))
}

pub fn new_values(mappings: &ObisMappingList) -> Vec<DsmrValue> {
    mappings
        .iter()
        .map(|m| DsmrValue::new(Arc::clone(m)))
        .collect()
}

/// Runs `f` with a subscriber that records every event on this thread.
pub fn capture_logs<R>(f: impl FnOnce() -> R) -> (R, Logs) {
    let logs = Arc::new(Mutex::new(Vec::new()));
    let subscriber = tracing_subscriber::registry().with(Capture(Arc::clone(&logs)));
    let result = tracing::subscriber::with_default(subscriber, f);
    let logs = std::mem::take(&mut *logs.lock().unwrap());
    (result, logs)
}

struct Capture(Arc<Mutex<Logs>>);

impl<S: Subscriber> Layer<S> for Capture {
    fn on_event(&self, event: &Event<'_>, _: Context<'_, S>) {
        let mut message = MessageVisitor(String::new());
        event.record(&mut message);
        self.0
            .lock()
            .unwrap()
            .push((*event.metadata().level(), message.0));
    }
}

struct MessageVisitor(String);

impl Visit for MessageVisitor {
    fn record_debug(&mut self, field: &Field, value: &dyn std::fmt::Debug) {
        if field.name() == "message" {
            self.0 = format!("{value:?}");
        }
    }
}
