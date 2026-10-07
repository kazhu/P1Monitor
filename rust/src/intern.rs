//! Small interning cache for strings decoded from telegrams.

use std::sync::Arc;

use crate::latin1;

/// Interns strings decoded from Latin-1 bytes, so repeated values (serial numbers, names)
/// share one allocation instead of allocating on every telegram.
///
/// The cache is a fixed-size FIFO: once full, the oldest entry is replaced. That is enough for
/// DSMR, where only a handful of distinct strings ever occur.
#[derive(Debug)]
pub struct StringInternCache {
    entries: Vec<(Box<[u8]>, Arc<str>)>,
    capacity: usize,
    oldest: usize,
}

impl StringInternCache {
    #[must_use]
    pub fn new(capacity: usize) -> Self {
        assert!(capacity > 0, "capacity must be positive");
        Self {
            entries: Vec::with_capacity(capacity),
            capacity,
            oldest: 0,
        }
    }

    /// Returns the interned string for `bytes`, decoding and caching it when it is not cached yet.
    pub fn get(&mut self, bytes: &[u8]) -> Arc<str> {
        if let Some((_, text)) = self.entries.iter().find(|(b, _)| **b == *bytes) {
            return Arc::clone(text);
        }
        let text: Arc<str> = latin1::decode(bytes).into();
        let entry = (bytes.into(), Arc::clone(&text));
        if self.entries.len() < self.capacity {
            self.entries.push(entry);
        } else {
            self.entries[self.oldest] = entry;
            self.oldest = (self.oldest + 1) % self.capacity;
        }
        text
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn interns_with_fifo_eviction() {
        let mut cache = StringInternCache::new(3);
        let abc = cache.get(b"abc");
        assert_eq!(&*abc, "abc");
        assert!(Arc::ptr_eq(&abc, &cache.get(b"abc")));
        assert_eq!(&*cache.get(b"def"), "def");
        assert!(Arc::ptr_eq(&abc, &cache.get(b"abc")));
        assert_eq!(&*cache.get(b"!"), "!");
        assert!(Arc::ptr_eq(&abc, &cache.get(b"abc")));
        assert_eq!(&*cache.get(b"ghi"), "ghi");
        assert!(!Arc::ptr_eq(&abc, &cache.get(b"abc")));
        assert_eq!(&*cache.get(b"jkl"), "jkl");
        assert_eq!(&*cache.get(b"mno"), "mno");
        assert_eq!(&*cache.get(b"pqr"), "pqr");
        assert!(Arc::ptr_eq(&cache.get(b"pqr"), &cache.get(b"pqr")));
    }
}
