# P1Monitor (Rust)

Rust port of P1Monitor. It reads DSMR telegrams from a smart meter's P1 port exposed over TCP (for example by esp-link), validates them, and writes the values to InfluxDB 2.x.

## Build and run

```sh
cargo build --release
cargo test
cargo bench          # criterion port of the BenchmarkDotNet benchmarks
```

The binary is `target/release/p1monitor`. It reads `appsettings.json` from its own directory, or from `/etc/p1monitor/` when it runs as a systemd service.

## Configuration

The settings keep the same format as the .NET version, so an existing `appsettings.json` works unchanged (comments are allowed):

| Key | Default | Meaning |
| --- | --- | --- |
| `DsmrReader:Host` | (required) | Name or address of the P1 device |
| `DsmrReader:Port` | `2323` | TCP port |
| `DsmrReader:BufferSize` | `4096` | Must hold a whole telegram plus some extra |
| `InfluxDb:BaseUrl` | (required) | e.g. `http://192.168.34.6:8086/` |
| `InfluxDb:Token`, `:Organization`, `:Bucket` | (required) | Token needs write access to the bucket |
| `ObisMapping:DeviceName` | (required) | Device key in the mappings file, e.g. `EON_HU_SX631` |
| `ObisMapping:MappingFile` | (required) | Relative paths are resolved against the executable's directory |
| `Logging:LogLevel:Default` | `Information` | .NET level names; other keys are module targets such as `P1Monitor.Parser` |

Later sources override earlier ones:

1. `appsettings.json`
2. `appsettings.{Environment}.json`, where the environment comes from `--environment` or `DOTNET_ENVIRONMENT` and defaults to `Production`
3. Environment variables such as `P1Monitor_InfluxDb__Token=...`
4. Command line arguments such as `--InfluxDb:Token=...`

`RUST_LOG`, when set, overrides the configured log levels.

## Debian package

```sh
cargo install cargo-deb
cargo deb            # target/debian/p1monitor_*.deb
```

The package installs the binary and `obismappings.json` to `/usr/lib/p1monitor/`, the configuration to `/etc/p1monitor/appsettings.json`, and the `p1monitor` systemd unit (`Type=notify`). The maintainer scripts are the same as in the .NET version. To build for a Raspberry Pi, use `cross build --release --target aarch64-unknown-linux-gnu` and then `cargo deb --no-build --target aarch64-unknown-linux-gnu`.

## Layout

| Module | C# counterpart |
| --- | --- |
| `crc` | `DsmrCrc` |
| `parser` | `DsmrParser` |
| `value` | `DsmrValue` and its subclasses, as the `Value` enum |
| `intern` | `DsmrStringInternCache` |
| `model`, `mapping` | `Model/*`, `ObisMappingList`, `ObisMappingsProvider` |
| `reader` | `DsmrReader` (the `TelegramSink` trait replaces `IInfluxDbWriter.Insert`) |
| `influx` | `InfluxDbWriter` |
| `config` | `Options/*` and the configuration setup in `Program` |
| `main.rs` | `Program` (hosting, systemd integration) |

## Differences from the .NET version

- **Reconnect delay.** After a connection error the reader waits 5 seconds before reconnecting. The .NET version retried immediately, which busy-loops while the device is unreachable. Connecting also times out after 30 seconds.
- **Line protocol escaping.** Spaces, commas, `=` and `\` in tag values and keys are escaped, and text is sent as real UTF-8. Before, a value containing these characters produced an invalid line, and non-ASCII text was sent as Latin-1 while being labeled UTF-8.
- **Query parameters.** The organization and bucket are URL-encoded.
- **Ambiguous local times.** When a local time occurs twice, at the end of daylight saving time, the telegram's `S`/`W` flag picks the summer or the winter occurrence.
- **Mapping lookup.** OBIS ids are looked up in a `HashMap<Box<[u8]>, _>` by byte slice, which needs no allocation, instead of the custom trie.
- **Shutdown.** On SIGTERM or Ctrl+C, telegrams that are already queued are still written, waiting at most 5 seconds.
- **Debug log.** The "Enqueuing values" debug message prints the time in RFC 3339 format.
- **Systemd detection.** A process counts as running under systemd when its parent is PID 1 or `INVOCATION_ID` is set.
