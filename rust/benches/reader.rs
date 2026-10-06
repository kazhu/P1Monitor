//! Port of the BenchmarkDotNet benchmarks of the C# version.

use std::hint::black_box;
use std::path::Path;
use std::sync::Arc;

use criterion::{Criterion, criterion_group, criterion_main};
use p1monitor::influx::LineProtocolFormatter;
use p1monitor::mapping::ObisMappingList;
use p1monitor::reader::{DsmrReader, TelegramSink};
use p1monitor::value::DsmrValue;

const LINES: &[u8] = include_bytes!("../testdata/lines.txt");
const SAMPLE: &[u8] = include_bytes!("../testdata/sample.txt");

struct NullSink;

impl TelegramSink for NullSink {
    fn insert(&mut self, _: &[DsmrValue]) {}
}

#[derive(Default)]
struct Keep(Vec<DsmrValue>);

impl TelegramSink for Keep {
    fn insert(&mut self, values: &[DsmrValue]) {
        self.0 = values.to_vec();
    }
}

fn mappings() -> Arc<ObisMappingList> {
    let path = Path::new(env!("CARGO_MANIFEST_DIR")).join("obismappings.json");
    Arc::new(ObisMappingList::load(&path, "EON_HU_SX631").expect("valid mappings file"))
}

fn benchmarks(c: &mut Criterion) {
    let mappings = mappings();

    let mut reader = DsmrReader::new(Arc::clone(&mappings), NullSink);
    let mut buffer = LINES.to_vec();
    c.bench_function("process_buffer", |b| {
        b.iter(|| {
            buffer.copy_from_slice(LINES);
            black_box(reader.process_buffer(&mut buffer))
        });
    });

    let mut keep = DsmrReader::new(Arc::clone(&mappings), Keep::default());
    keep.process_buffer(&mut SAMPLE.to_vec());
    let values = &keep.sink().0;
    let formatter = LineProtocolFormatter::new(mappings);
    let mut lines = String::with_capacity(4096);
    c.bench_function("generate_lines", |b| {
        b.iter(|| {
            lines.clear();
            formatter.format(black_box(values), &mut lines);
            black_box(lines.len())
        });
    });
}

criterion_group!(benches, benchmarks);
criterion_main!(benches);
