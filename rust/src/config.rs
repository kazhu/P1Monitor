//! Layered configuration, compatible with the .NET version's `appsettings.json`.
//!
//! Sources, later ones overriding earlier ones:
//! 1. `appsettings.json` in the configuration directory (JSON with comments),
//! 2. `appsettings.{Environment}.json`, where the environment comes from `--environment` or
//!    `DOTNET_ENVIRONMENT` and defaults to `Production`,
//! 3. environment variables prefixed with `P1Monitor_`, using `__` as the section separator
//!    (e.g. `P1Monitor_InfluxDb__Token`),
//! 4. command line arguments: `--Section:Key=value`, `--Section:Key value` or `Section:Key=value`.
//!
//! Keys are case-insensitive, as in .NET.

use std::collections::BTreeMap;
use std::path::{Path, PathBuf};
use std::str::FromStr;

use serde::{Deserialize, Deserializer, de};
use serde_json::{Map, Value};

const ENV_PREFIX: &str = "p1monitor_";
const DEFAULT_ENVIRONMENT: &str = "Production";

#[derive(Debug, thiserror::Error)]
pub enum ConfigError {
    #[error("cannot read {path}")]
    Io {
        path: PathBuf,
        #[source]
        source: std::io::Error,
    },
    #[error("invalid JSON in {path}")]
    Json {
        path: PathBuf,
        #[source]
        source: json5::Error,
    },
    #[error("missing value for command line argument `{0}`")]
    MissingArgumentValue(String),
    #[error("invalid configuration")]
    Invalid(#[from] serde_json::Error),
}

#[derive(Debug, Clone, Deserialize)]
pub struct Settings {
    #[serde(rename = "dsmrreader")]
    pub dsmr_reader: DsmrReaderOptions,
    #[serde(rename = "influxdb")]
    pub influx_db: InfluxDbOptions,
    #[serde(rename = "obismapping")]
    pub obis_mapping: ObisMappingsOptions,
    #[serde(default)]
    pub logging: LoggingOptions,
}

#[derive(Debug, Clone, Deserialize)]
pub struct DsmrReaderOptions {
    /// Name or address of the device.
    pub host: String,
    #[serde(default = "default_port", deserialize_with = "number")]
    pub port: u16,
    /// Needs to be large enough to hold an entire telegram plus some extra.
    #[serde(
        rename = "buffersize",
        default = "default_buffer_size",
        deserialize_with = "number"
    )]
    pub buffer_size: u16,
}

const fn default_port() -> u16 {
    2323
}

const fn default_buffer_size() -> u16 {
    4096
}

#[derive(Debug, Clone, Deserialize)]
pub struct InfluxDbOptions {
    /// For example `http://192.168.34.6:8086/`.
    #[serde(rename = "baseurl")]
    pub base_url: String,
    /// Token with write access to the bucket.
    pub token: String,
    pub organization: String,
    pub bucket: String,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ObisMappingsOptions {
    /// Key of the device in the mappings file, e.g. `EON_HU_SX631`.
    #[serde(rename = "devicename")]
    pub device_name: String,
    /// Path of the mappings file; relative paths are resolved against the executable's directory.
    #[serde(rename = "mappingfile")]
    pub mapping_file: PathBuf,
}

/// The `Logging:LogLevel` section: `Default` and per-target levels, using .NET level names.
#[derive(Debug, Clone, Default, Deserialize)]
pub struct LoggingOptions {
    #[serde(rename = "loglevel", default)]
    pub log_level: BTreeMap<String, String>,
}

impl LoggingOptions {
    /// Converts the levels into `tracing_subscriber::EnvFilter` directives.
    /// Targets use `.` or `::` as separators, e.g. `P1Monitor.Parser` means the `p1monitor::parser` module.
    #[must_use]
    pub fn filter_directives(&self) -> String {
        let mut directives = vec!["info".to_owned()];
        for (target, level) in &self.log_level {
            let Some(level) = tracing_level(level) else {
                continue;
            };
            if target == "default" {
                level.clone_into(&mut directives[0]);
            } else {
                directives.push(format!("{}={level}", target.replace('.', "::")));
            }
        }
        directives.join(",")
    }
}

fn tracing_level(dotnet_level: &str) -> Option<&'static str> {
    Some(match dotnet_level.to_ascii_lowercase().as_str() {
        "trace" => "trace",
        "debug" => "debug",
        "information" | "info" => "info",
        "warning" | "warn" => "warn",
        "error" | "critical" => "error",
        "none" | "off" => "off",
        _ => return None,
    })
}

/// Accepts a number from a JSON file or a string from an environment variable or the command line.
fn number<'de, D, T>(deserializer: D) -> Result<T, D::Error>
where
    D: Deserializer<'de>,
    T: FromStr + TryFrom<u64>,
{
    #[derive(Deserialize)]
    #[serde(untagged)]
    enum NumberOrString {
        Number(u64),
        String(String),
    }
    match NumberOrString::deserialize(deserializer)? {
        NumberOrString::Number(n) => {
            T::try_from(n).map_err(|_| de::Error::custom(format!("{n} is out of range")))
        }
        NumberOrString::String(s) => s
            .trim()
            .parse()
            .map_err(|_| de::Error::custom(format!("`{s}` is not a valid number"))),
    }
}

impl Settings {
    /// Loads the settings from `dir`, the environment variables `env` and the command line `args`
    /// (without the program name).
    pub fn load(
        dir: &Path,
        args: &[String],
        env: impl IntoIterator<Item = (String, String)>,
    ) -> Result<Self, ConfigError> {
        let env: Vec<(String, String)> = env.into_iter().collect();
        let args = parse_args(args)?;

        let environment = args
            .iter()
            .find(|(key, _)| key.eq_ignore_ascii_case("environment"))
            .or_else(|| {
                env.iter()
                    .find(|(key, _)| key.eq_ignore_ascii_case("DOTNET_ENVIRONMENT"))
            })
            .map_or(DEFAULT_ENVIRONMENT, |(_, value)| value.as_str());

        let mut root = Value::Object(Map::new());
        for file in [
            dir.join("appsettings.json"),
            dir.join(format!("appsettings.{environment}.json")),
        ] {
            if let Some(value) = read_json(&file)? {
                merge(&mut root, value);
            }
        }
        for (key, value) in &env {
            if key.len() > ENV_PREFIX.len()
                && key[..ENV_PREFIX.len()].eq_ignore_ascii_case(ENV_PREFIX)
            {
                set_path(
                    &mut root,
                    &key[ENV_PREFIX.len()..].replace("__", ":"),
                    value,
                );
            }
        }
        for (key, value) in &args {
            set_path(&mut root, &key.replace("__", ":"), value);
        }

        Ok(serde_json::from_value(root)?)
    }
}

/// Reads an optional JSON (with comments) file, with all keys lowercased.
fn read_json(path: &Path) -> Result<Option<Value>, ConfigError> {
    let text = match std::fs::read_to_string(path) {
        Ok(text) => text,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(source) => {
            return Err(ConfigError::Io {
                path: path.to_owned(),
                source,
            });
        }
    };
    let text = text.strip_prefix('\u{feff}').unwrap_or(&text);
    let value = json5::from_str(text).map_err(|source| ConfigError::Json {
        path: path.to_owned(),
        source,
    })?;
    Ok(Some(lowercase_keys(value)))
}

fn lowercase_keys(value: Value) -> Value {
    match value {
        Value::Object(map) => Value::Object(
            map.into_iter()
                .map(|(k, v)| (k.to_lowercase(), lowercase_keys(v)))
                .collect(),
        ),
        Value::Array(items) => Value::Array(items.into_iter().map(lowercase_keys).collect()),
        other => other,
    }
}

fn merge(base: &mut Value, overlay: Value) {
    match (base, overlay) {
        (Value::Object(base), Value::Object(overlay)) => {
            for (key, value) in overlay {
                match base.get_mut(&key) {
                    Some(existing) => merge(existing, value),
                    None => {
                        base.insert(key, value);
                    }
                }
            }
        }
        (base, overlay) => *base = overlay,
    }
}

/// Sets the value at a `:`-separated path, creating sections as needed.
fn set_path(root: &mut Value, path: &str, value: &str) {
    let mut node = root;
    for key in path.split(':').map(str::to_lowercase) {
        if !node.is_object() {
            *node = Value::Object(Map::new());
        }
        let Value::Object(map) = node else {
            unreachable!()
        };
        node = map.entry(key).or_insert(Value::Null);
    }
    *node = Value::String(value.to_owned());
}

/// Parses command line arguments the way .NET's command line configuration provider does.
fn parse_args(args: &[String]) -> Result<Vec<(String, String)>, ConfigError> {
    let mut result = Vec::new();
    let mut args = args.iter();
    while let Some(arg) = args.next() {
        let (key, has_prefix) = match arg.strip_prefix("--").or_else(|| arg.strip_prefix('/')) {
            Some(key) => (key, true),
            None => (arg.as_str(), false),
        };
        if let Some((key, value)) = key.split_once('=') {
            result.push((key.to_owned(), value.to_owned()));
        } else if has_prefix {
            let value = args
                .next()
                .ok_or_else(|| ConfigError::MissingArgumentValue(arg.clone()))?;
            result.push((key.to_owned(), value.clone()));
        }
    }
    Ok(result)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_dir(name: &str) -> PathBuf {
        let dir =
            std::env::temp_dir().join(format!("p1monitor-config-{name}-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    fn strings(items: &[&str]) -> Vec<String> {
        items.iter().map(|&s| s.to_owned()).collect()
    }

    #[test]
    fn loads_shipped_appsettings() {
        let settings = Settings::load(Path::new(env!("CARGO_MANIFEST_DIR")), &[], []).unwrap();
        assert_eq!(settings.dsmr_reader.port, 2323);
        assert_eq!(settings.dsmr_reader.buffer_size, 4096);
        assert_eq!(settings.obis_mapping.device_name, "EON_HU_SX631");
        assert_eq!(
            settings.obis_mapping.mapping_file,
            Path::new("obismappings.json")
        );
        assert_eq!(
            settings.logging.filter_directives(),
            "info,microsoft=warn,microsoft::hosting::lifetime=info"
        );
    }

    #[test]
    fn layers_sources() {
        let dir = temp_dir("layers");
        std::fs::write(
            dir.join("appsettings.json"),
            r#"{
                // comments are allowed
                "DsmrReader": { "Host": "file", "Port": 1 },
                "InfluxDb": { "BaseUrl": "http://file/", "Token": "file", "Organization": "file", "Bucket": "file" },
                "ObisMapping": { "DeviceName": "file", "MappingFile": "file.json" },
            }"#,
        )
        .unwrap();
        std::fs::write(
            dir.join("appsettings.Development.json"),
            r#"{ "influxdb": { "token": "development" } }"#,
        )
        .unwrap();

        let env = [
            ("DOTNET_ENVIRONMENT".to_owned(), "Development".to_owned()),
            ("P1MONITOR_DsmrReader__Port".to_owned(), "2".to_owned()),
            ("P1Monitor_InfluxDb__Bucket".to_owned(), "env".to_owned()),
            (
                "OTHER_InfluxDb__Organization".to_owned(),
                "ignored".to_owned(),
            ),
        ];
        let args = strings(&[
            "--InfluxDb:Bucket=cli",
            "/ObisMapping:DeviceName",
            "cli",
            "DsmrReader:BufferSize=100",
        ]);
        let settings = Settings::load(&dir, &args, env).unwrap();
        std::fs::remove_dir_all(&dir).unwrap();

        assert_eq!(settings.dsmr_reader.host, "file");
        assert_eq!(settings.dsmr_reader.port, 2);
        assert_eq!(settings.dsmr_reader.buffer_size, 100);
        assert_eq!(settings.influx_db.token, "development");
        assert_eq!(settings.influx_db.organization, "file");
        assert_eq!(settings.influx_db.bucket, "cli");
        assert_eq!(settings.obis_mapping.device_name, "cli");
    }

    #[test]
    fn works_without_files() {
        let dir = temp_dir("nofiles");
        let args = strings(&[
            "--DsmrReader:Host=h",
            "--InfluxDb:BaseUrl=http://x/",
            "--InfluxDb:Token=t",
            "--InfluxDb:Organization=o",
            "--InfluxDb:Bucket=b",
            "--ObisMapping:DeviceName=d",
            "--ObisMapping:MappingFile=m",
        ]);
        let settings = Settings::load(&dir, &args, []).unwrap();
        std::fs::remove_dir_all(&dir).unwrap();
        assert_eq!(settings.dsmr_reader.port, 2323);
        assert_eq!(settings.dsmr_reader.buffer_size, 4096);
    }

    #[test]
    fn reports_missing_and_invalid_values() {
        let dir = temp_dir("invalid");
        assert!(matches!(
            Settings::load(&dir, &[], []),
            Err(ConfigError::Invalid(_))
        ));
        assert!(matches!(
            Settings::load(&dir, &strings(&["--DsmrReader:Host"]), []),
            Err(ConfigError::MissingArgumentValue(_))
        ));
        std::fs::write(dir.join("appsettings.json"), "{ not json").unwrap();
        assert!(matches!(
            Settings::load(&dir, &[], []),
            Err(ConfigError::Json { .. })
        ));
        std::fs::remove_dir_all(&dir).unwrap();
    }

    #[test]
    fn converts_log_levels() {
        let options = LoggingOptions {
            log_level: [
                ("default", "Warning"),
                ("p1monitor.parser", "Trace"),
                ("x", "bogus"),
            ]
            .into_iter()
            .map(|(k, v)| (k.to_owned(), v.to_owned()))
            .collect(),
        };
        assert_eq!(options.filter_directives(), "warn,p1monitor::parser=trace");
    }
}
