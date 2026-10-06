//! CRC16 (IBM/ANSI, reflected polynomial `0xA001`) used to protect DSMR telegrams.

const TABLE: [u16; 256] = build_table();

const fn build_table() -> [u16; 256] {
    let mut table = [0u16; 256];
    let mut i = 0;
    while i < 256 {
        #[allow(clippy::cast_possible_truncation)]
        let mut crc = i as u16;
        let mut bit = 0;
        while bit < 8 {
            crc = if crc & 1 == 1 {
                (crc >> 1) ^ 0xA001
            } else {
                crc >> 1
            };
            bit += 1;
        }
        table[i] = crc;
        i += 1;
    }
    table
}

/// Computes the CRC16 of `data`.
#[must_use]
pub fn crc16(data: &[u8]) -> u16 {
    data.iter().fold(0u16, |crc, &byte| {
        (crc >> 8) ^ TABLE[usize::from(crc.to_le_bytes()[0] ^ byte)]
    })
}

/// Checks `data` against `crc_text`, the four uppercase hexadecimal digits that follow the `!` of a telegram.
#[must_use]
pub fn check_crc(data: &[u8], crc_text: &[u8]) -> bool {
    const HEX: &[u8; 16] = b"0123456789ABCDEF";
    let crc = crc16(data);
    let expected = [
        HEX[usize::from(crc >> 12)],
        HEX[usize::from((crc >> 8) & 0xF)],
        HEX[usize::from((crc >> 4) & 0xF)],
        HEX[usize::from(crc & 0xF)],
    ];
    crc_text == expected
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn table_matches_reference_values() {
        assert_eq!(TABLE[0], 0x0000);
        assert_eq!(TABLE[1], 0xC0C1);
        assert_eq!(TABLE[255], 0x4040);
    }

    #[test]
    fn checks_crc() {
        assert!(check_crc(b"/abc512\r\n\r\n\r\n!", b"774B"));
        assert!(!check_crc(b"/abc512\r\n\r\n\r\n!", b"774b"));
        assert!(!check_crc(b"/abc512\r\n\r\n\r\n!", b"0000"));
        assert!(!check_crc(b"/abc512\r\n\r\n\r\n!", b"774"));
    }
}
