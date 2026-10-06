//! DSMR telegrams are Latin-1 (ISO 8859-1) encoded; every byte maps to the Unicode code point of the same value.

/// Decodes Latin-1 bytes into a `String`.
#[must_use]
pub fn decode(bytes: &[u8]) -> String {
    bytes.iter().map(|&b| char::from(b)).collect()
}

/// Displays Latin-1 bytes without allocating, for log messages.
pub struct Latin1<'a>(pub &'a [u8]);

impl std::fmt::Display for Latin1<'_> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        use std::fmt::Write;
        self.0.iter().try_for_each(|&b| f.write_char(char::from(b)))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn decodes_high_bytes() {
        assert_eq!(decode(b"a\xffb"), "aÿb");
        assert_eq!(Latin1(b"a\xffb").to_string(), "aÿb");
    }
}
