//! Saved loadouts, one text file per character: `loadouts/slot<N> <name>.txt` next to the DLL.
//!
//! ```text
//! loadout 1
//! gear 1 0010C8E0 80800042     <- equipment slot, item id, gaitem handle (hex)
//! goods 22 400003E8 00000000 2 <- quick/pouch slot, item id, gaitem handle, inventory index
//! spell 0 00000FA0             <- memory slot, spell (magic param) id
//! ```
//! Unlisted slots were empty. The file is rewritten whole after every save.

use std::fmt::Write as _;
use std::path::PathBuf;

use super::SLOTS;
use super::equip::{Gear, Goods, Loadout};

pub struct Store {
    /// "slot<N> <name>": the save slot and character name, which together pick the file.
    pub key: String,
    loadouts: Vec<Option<Loadout>>,
}

pub fn character_key(save_slot: i32, name: &[u16]) -> String {
    let name = String::from_utf16_lossy(&name[..name.iter().position(|&c| c == 0).unwrap_or(name.len())]);
    // Keep the name readable in a file name, but drop characters Windows forbids.
    let safe: String = name.chars().map(|c| if "<>:\"/\\|?*".contains(c) || c.is_control() { '_' } else { c }).collect();
    format!("slot{save_slot} {}", safe.trim())
}

impl Store {
    fn path(key: &str) -> PathBuf {
        crate::home().join("loadouts").join(format!("{key}.txt"))
    }

    pub fn load(key: String) -> Store {
        let mut loadouts: Vec<Option<Loadout>> = (0..SLOTS).map(|_| None).collect();
        if let Ok(text) = std::fs::read_to_string(Self::path(&key)) {
            let mut current: Option<usize> = None;
            for line in text.lines() {
                let f: Vec<&str> = line.split_whitespace().collect();
                let hex = |i: usize| f.get(i).and_then(|s| u32::from_str_radix(s, 16).ok());
                let dec = |i: usize| f.get(i).and_then(|s| s.parse::<usize>().ok());
                match f.first().copied() {
                    Some("loadout") => {
                        current = dec(1).filter(|&n| n >= 1 && n <= SLOTS as usize).map(|n| n - 1);
                        if let Some(n) = current {
                            loadouts[n] = Some(Loadout::default());
                        }
                    }
                    Some("gear") => {
                        if let (Some(l), Some(slot), Some(item), Some(handle)) = (current.and_then(|n| loadouts[n].as_mut()), dec(1), hex(2), hex(3)) {
                            if let Some(g) = l.gear.get_mut(slot) {
                                *g = Gear { item, handle };
                            }
                        }
                    }
                    Some("goods") => {
                        if let (Some(l), Some(slot), Some(item), Some(handle), Some(index)) =
                            (current.and_then(|n| loadouts[n].as_mut()), dec(1), hex(2), hex(3), f.get(4).and_then(|s| s.parse::<i32>().ok()))
                        {
                            if let Some(g) = slot.checked_sub(Loadout::GOODS_BASE).and_then(|i| l.goods.get_mut(i)) {
                                *g = Goods { item, handle, index };
                            }
                        }
                    }
                    Some("spell") => {
                        if let (Some(l), Some(slot), Some(id)) = (current.and_then(|n| loadouts[n].as_mut()), dec(1), hex(2)) {
                            if let Some(s) = l.spells.get_mut(slot) {
                                *s = id as i32;
                            }
                        }
                    }
                    _ => {}
                }
            }
        }
        Store { key, loadouts }
    }

    pub fn count(&self) -> usize {
        self.loadouts.iter().flatten().count()
    }

    pub fn get(&self, n: u32) -> Option<&Loadout> {
        self.loadouts.get(n as usize)?.as_ref()
    }

    pub fn set(&mut self, n: u32, loadout: Loadout) {
        self.loadouts[n as usize] = Some(loadout);
        self.write();
    }

    fn write(&self) {
        let mut out = String::from("# Grace Loadouts\n");
        for (n, l) in self.loadouts.iter().enumerate() {
            let Some(l) = l else { continue };
            let _ = writeln!(out, "loadout {}", n + 1);
            for (slot, g) in l.gear.iter().enumerate().filter(|(_, g)| g.item != Loadout::EMPTY) {
                let _ = writeln!(out, "gear {slot} {:08X} {:08X}", g.item, g.handle);
            }
            for (i, g) in l.goods.iter().enumerate().filter(|(_, g)| g.item != Loadout::EMPTY) {
                let _ = writeln!(out, "goods {} {:08X} {:08X} {}", Loadout::GOODS_BASE + i, g.item, g.handle, g.index);
            }
            for (slot, &id) in l.spells.iter().enumerate().filter(|(_, id)| **id >= 0) {
                let _ = writeln!(out, "spell {slot} {:08X}", id as u32);
            }
        }
        let path = Self::path(&self.key);
        let result = std::fs::create_dir_all(path.parent().unwrap())
            // Write beside, then replace, so a crash mid-write can't lose the old file.
            .and_then(|_| std::fs::write(path.with_extension("tmp"), &out))
            .and_then(|_| std::fs::rename(path.with_extension("tmp"), &path));
        if let Err(e) = result {
            super::log(&format!("couldn't write {}: {e}", path.display()));
        }
    }
}
