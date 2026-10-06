use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;

use anyhow::Context as _;
use p1monitor::config::Settings;
use p1monitor::influx::{InfluxDbSink, InfluxDbWriter};
use p1monitor::mapping::ObisMappingList;
use p1monitor::reader::DsmrReader;
use tokio::sync::mpsc;
use tracing::info;
use tracing_subscriber::EnvFilter;

/// Configuration directory when running as a systemd service.
const SERVICE_CONFIG_DIR: &str = "/etc/p1monitor";
/// How long queued values may take to be written to InfluxDB on shutdown.
const SHUTDOWN_TIMEOUT: Duration = Duration::from_secs(5);

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let is_service = systemd::is_service();
    let exe_dir = executable_dir()?;
    let config_dir = if is_service {
        PathBuf::from(SERVICE_CONFIG_DIR)
    } else {
        exe_dir.clone()
    };

    let args: Vec<String> = std::env::args().skip(1).collect();
    let settings = Settings::load(&config_dir, &args, std::env::vars())
        .with_context(|| format!("loading configuration from {}", config_dir.display()))?;
    init_logging(&settings, is_service);

    let mapping_file = exe_dir.join(&settings.obis_mapping.mapping_file);
    info!("Loading mappings file {}", mapping_file.display());
    let mappings = Arc::new(ObisMappingList::load(
        &mapping_file,
        &settings.obis_mapping.device_name,
    )?);
    info!(
        "Using {} device mappings",
        settings.obis_mapping.device_name
    );

    let writer = InfluxDbWriter::new(&settings.influx_db)?;
    let (queue, queued) = mpsc::unbounded_channel();
    let writer = tokio::spawn(writer.run(queued));

    let mut reader = DsmrReader::new(Arc::clone(&mappings), InfluxDbSink::new(mappings, queue));
    let reader_options = settings.dsmr_reader.clone();
    let reader = tokio::spawn(async move { reader.run(&reader_options).await });

    systemd::notify_ready();
    shutdown_signal().await;
    systemd::notify_stopping();

    // Stopping the reader closes the queue; the writer then flushes what is left and stops.
    reader.abort();
    let _ = reader.await;
    info!("DSMR reader stopped");
    if tokio::time::timeout(SHUTDOWN_TIMEOUT, writer)
        .await
        .is_err()
    {
        info!("InfluxDB writer did not finish in time, unwritten values are lost");
    }
    Ok(())
}

fn executable_dir() -> anyhow::Result<PathBuf> {
    let exe = std::env::current_exe().context("locating the executable")?;
    Ok(exe.parent().map(Path::to_path_buf).unwrap_or_default())
}

fn init_logging(settings: &Settings, is_service: bool) {
    // RUST_LOG, when set, overrides the levels of the configuration.
    let filter = EnvFilter::try_from_default_env()
        .unwrap_or_else(|_| EnvFilter::new(settings.logging.filter_directives()));
    let builder = tracing_subscriber::fmt().with_env_filter(filter);
    if is_service {
        // journald adds timestamps itself and does not render colors.
        builder.with_ansi(false).without_time().init();
    } else {
        builder.init();
    }
}

async fn shutdown_signal() {
    #[cfg(unix)]
    {
        use tokio::signal::unix::{SignalKind, signal};
        let mut terminate =
            signal(SignalKind::terminate()).expect("installing the SIGTERM handler");
        tokio::select! {
            _ = tokio::signal::ctrl_c() => {}
            _ = terminate.recv() => {}
        }
    }
    #[cfg(not(unix))]
    {
        let _ = tokio::signal::ctrl_c().await;
    }
    info!("Shutting down");
}

#[cfg(target_os = "linux")]
mod systemd {
    use sd_notify::NotifyState;

    /// Whether the process was started by systemd (as `UseSystemd` / `IsSystemdService` detect it in .NET).
    pub fn is_service() -> bool {
        std::os::unix::process::parent_id() == 1 || std::env::var_os("INVOCATION_ID").is_some()
    }

    pub fn notify_ready() {
        let _ = sd_notify::notify(&[NotifyState::Ready]);
    }

    pub fn notify_stopping() {
        let _ = sd_notify::notify(&[NotifyState::Stopping]);
    }
}

#[cfg(not(target_os = "linux"))]
mod systemd {
    pub fn is_service() -> bool {
        false
    }

    pub fn notify_ready() {}

    pub fn notify_stopping() {}
}
