//! What one character has, kept in `characters/slot<N> <name>.txt` next to the DLL:
//!
//! ```text
//! owned chalice_strength_1        <- an item found at a grace
//! equipped bedroll_worn           <- an equipped item (one seat, up to two keepsakes)
//! kills 7                         <- kills since the last sit, not yet banked
//! announce strength_3             <- a found item whose pop-up hasn't been shown yet
//! buff 612 900 Strength:1 Stamina:4  <- the running buff: seconds left, full length, bonuses
//! ```
//!
//! The game's own save is never touched. The file is rewritten whole when anything changes.

use std::collections::BTreeSet;
use std::fmt::Write as _;
use std::path::PathBuf;

use super::buff::{Grant, Stat};
use super::log;

/// "slot<N> <name>": the save slot and character name, which together pick the file. The same
/// key Grace Loadouts uses, so both mods name a character's files alike.
pub fn character_key(save_slot: i32, name: &[u16]) -> String {
    let name = String::from_utf16_lossy(&name[..name.iter().position(|&c| c == 0).unwrap_or(name.len())]);
    let safe: String = name.chars().map(|c| if "<>:\"/\\|?*".contains(c) || c.is_control() { '_' } else { c }).collect();
    format!("slot{save_slot} {}", safe.trim())
}

pub struct Store {
    pub key: String,
    pub owned: BTreeSet<String>,
    pub equipped: Vec<String>,
    pub kills: u32,
    /// The running buff and its seconds left.
    pub buff: Option<(Grant, u64)>,
    /// Found items waiting for their pop-up, oldest first ("*welcome" for the welcome one).
    pub announce: Vec<String>,
    /// The file didn't exist: the character is new to the mod.
    pub fresh: bool,
}

impl Store {
    fn path(key: &str) -> PathBuf {
        crate::home().join("characters").join(format!("{key}.txt"))
    }

    pub fn load(key: String) -> Store {
        let mut s = Store { key, owned: BTreeSet::new(), equipped: Vec::new(), kills: 0, buff: None, announce: Vec::new(), fresh: false };
        let Ok(text) = std::fs::read_to_string(Self::path(&s.key)) else {
            s.fresh = true;
            return s;
        };
        for line in text.lines() {
            let mut words = line.split_whitespace();
            match (words.next(), words.next()) {
                (Some("owned"), Some(id)) => {
                    s.owned.insert(id.to_string());
                }
                (Some("equipped"), Some(id)) => s.equipped.push(id.to_string()),
                (Some("announce"), Some(id)) => s.announce.push(id.to_string()),
                (Some("kills"), Some(n)) => s.kills = n.parse().unwrap_or(0),
                (Some("buff"), Some(left)) => {
                    let length = words.next().and_then(|l| l.parse().ok());
                    let adds: Vec<(Stat, u32)> = words
                        .filter_map(|w| w.split_once(':'))
                        .filter_map(|(stat, p)| Some((Stat::parse(stat)?, p.parse().ok()?)))
                        .collect();
                    if let (Ok(left), Some(length)) = (left.parse(), length) {
                        s.buff = Some((Grant::from_parts(adds, length), left));
                    }
                }
                _ => {}
            }
        }
        s
    }

    pub fn save(&self) {
        let mut out = String::new();
        for id in &self.owned {
            let _ = writeln!(out, "owned {id}");
        }
        for id in &self.equipped {
            let _ = writeln!(out, "equipped {id}");
        }
        for id in &self.announce {
            let _ = writeln!(out, "announce {id}");
        }
        let _ = writeln!(out, "kills {}", self.kills);
        if let Some((grant, left)) = &self.buff {
            let adds: Vec<String> = grant.adds.iter().map(|(s, p)| format!("{s:?}:{p}")).collect();
            let _ = writeln!(out, "buff {left} {} {}", grant.length.as_secs(), adds.join(" "));
        }
        let path = Self::path(&self.key);
        let result = std::fs::create_dir_all(path.parent().expect("characters folder"))
            .and_then(|_| std::fs::write(path.with_extension("tmp"), &out))
            .and_then(|_| std::fs::rename(path.with_extension("tmp"), &path));
        if let Err(e) = result {
            log(&format!("couldn't write {}: {e}", path.display()));
        }
    }
}
