//! Byte-pattern search over eldenring.exe's code, so the equip functions are found by their
//! instructions rather than by addresses that move with every game patch.

use pelite::pe64::{Pe, PeView};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;

/// Parses "48 8b ?? 05" into bytes, with None for wildcards.
fn parse(pattern: &str) -> Vec<Option<u8>> {
    pattern
        .split_whitespace()
        .map(|t| if t == "??" { None } else { Some(u8::from_str_radix(t, 16).expect("hex byte")) })
        .collect()
}

/// The address of the only place `pattern` occurs in the game's executable sections.
/// Zero or several matches mean the game changed and the pattern can't be trusted.
pub fn unique(pattern: &str) -> Result<usize, String> {
    let pat = parse(pattern);
    let base = unsafe { GetModuleHandleW(None) }.map_err(|e| e.to_string())?.0 as *const u8;
    let pe = unsafe { PeView::module(base) };
    let mut hits = Vec::new();
    for s in pe.section_headers() {
        // IMAGE_SCN_MEM_EXECUTE
        if s.Characteristics & 0x2000_0000 == 0 {
            continue;
        }
        let start = base as usize + s.VirtualAddress as usize;
        let bytes = unsafe { std::slice::from_raw_parts(start as *const u8, s.VirtualSize as usize) };
        for (i, w) in bytes.windows(pat.len()).enumerate() {
            if pat.iter().zip(w).all(|(p, b)| p.is_none_or(|p| p == *b)) {
                hits.push(start + i);
                if hits.len() > 1 {
                    return Err(format!("pattern \"{pattern}\" matches more than once"));
                }
            }
        }
    }
    hits.pop().ok_or_else(|| format!("pattern \"{pattern}\" not found"))
}
