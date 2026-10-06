//! P1Monitor reads DSMR telegrams from a smart meter's P1 port exposed over TCP (for example by
//! esp-link), validates and parses them, and writes the values to InfluxDB.
//!
//! The pipeline is [`reader::DsmrReader`] (TCP and buffering) → [`parser::DsmrParser`] (telegram
//! framing, CRC and OBIS lines) → [`reader::TelegramSink`] ([`influx::InfluxDbSink`] in the
//! service) → [`influx::InfluxDbWriter`] (HTTP).

pub mod config;
pub mod crc;
pub mod influx;
pub mod intern;
pub mod latin1;
pub mod mapping;
pub mod model;
pub mod parser;
pub mod reader;
pub mod value;

#[cfg(test)]
mod test_support;
