//! Grace Item definitions, read from grace_items.txt next to the DLL:
//!
//! ```text
//! [chalice_strength_1]          <- the item's id; save files refer to items by it
//! name = Crystal of Strength 1
//! track = strength              <- items in a track upgrade in place; the highest tier owned counts
//! tier = 1
//! kind = keepsake               <- seat or keepsake
//! model = AEG258_158            <- the game model shown at the grace
//! scale = 0.4                   <- optional, 1 by default
//! effect = Fury                 <- keepsakes: Fury, Ward, Leech, Second Wind or Bulwark (buff.rs)
//! max = 3                       <- keepsakes: the most stacks it can hold
//! kills_per_point = 5           <- keepsakes: kills since the last sit for each stack it can hold
//! per_stack = 2                 <- keepsakes, optional: what one stack is worth (buff.rs has the defaults)
//! minutes = 15                  <- seats: how long the buff lasts
//! lift = 0.44                   <- seats: how high its top is; the sitting player is lifted onto it
//! right = 0.5, forward = -1.0  <- meters to shift the model from its usual spot, as the sitting player faces;
//!                                 turn = 90 turns it (degrees)
//! found = 1042361950           <- graces (bonfire entity ids, see graces.txt) that give it once
//!                                  discovered, including graces discovered before the mod
//! ```

use super::buff::Effect;
use super::log;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Kind {
    Seat,
    Keepsake,
}

#[derive(Clone, Debug)]
pub struct Item {
    /// Position in grace_items.txt, counting skipped items too: the camp menu numbers items the same way.
    pub index: usize,
    pub id: String,
    pub name: String,
    /// Items in one track upgrade in place: a character uses the highest tier it owns.
    pub track: String,
    pub tier: u32,
    pub kind: Kind,
    pub model: String,
    pub scale: f32,
    pub effect: Option<Effect>,
    /// What one stack is worth; None for the effect's usual amount.
    pub per_stack: Option<f32>,
    pub max: u32,
    pub kills_per_point: u32,
    pub minutes: f32,
    /// Seats: the height of the seat top. The sitting player is raised this far so they end up on it
    /// instead of being pushed aside by it. 0 for seats on the floor, like rugs.
    pub lift: f32,
    /// Meters to shift the model right and forward of its usual spot, and degrees to turn it.
    pub right: f32,
    pub forward: f32,
    pub turn: f32,
    pub found: Vec<u32>,
}

impl Item {
    fn new(index: usize, id: &str) -> Item {
        Item {
            index,
            id: id.to_string(),
            name: id.to_string(),
            track: id.to_string(),
            tier: 1,
            kind: Kind::Keepsake,
            model: String::new(),
            scale: 1.0,
            effect: None,
            per_stack: None,
            max: 0,
            kills_per_point: 5,
            minutes: 15.0,
            lift: 0.0,
            right: 0.0,
            forward: 0.0,
            turn: 0.0,
            found: Vec::new(),
        }
    }

    fn problem(&self) -> Option<&'static str> {
        if !super::valid_asset(&self.model) {
            return Some("its model isn't AEG###_###");
        }
        if self.kind == Kind::Keepsake && (self.effect.is_none() || self.max == 0) {
            return Some("a keepsake needs an effect and a max above 0");
        }
        None
    }
}

/// When grace_items.txt was last changed, so edits can be picked up while the game runs.
pub fn modified() -> Option<std::time::SystemTime> {
    std::fs::metadata(crate::home().join("grace_items.txt")).and_then(|m| m.modified()).ok()
}

/// Every valid item in the file. Problems are logged and those items skipped, so one typo can't
/// take the others down with it.
pub fn load() -> Vec<Item> {
    let path = crate::home().join("grace_items.txt");
    let Ok(text) = std::fs::read_to_string(&path) else {
        log("grace_items.txt not found; no Grace Items");
        return Vec::new();
    };
    let mut items: Vec<Item> = Vec::new();
    for (n, raw) in text.lines().enumerate() {
        let line = raw.split([';', '#']).next().unwrap_or("").trim();
        if line.is_empty() {
            continue;
        }
        if let Some(id) = line.strip_prefix('[').and_then(|l| l.strip_suffix(']')) {
            items.push(Item::new(items.len(), id.trim()));
            continue;
        }
        let (Some(item), Some((key, value))) = (items.last_mut(), line.split_once('=')) else {
            log(&format!("grace_items.txt line {}: not understood", n + 1));
            continue;
        };
        let value = value.trim();
        let ok = match key.trim().to_ascii_lowercase().as_str() {
            "name" => {
                item.name = value.to_string();
                true
            }
            "track" => {
                item.track = value.to_ascii_lowercase();
                true
            }
            "tier" => value.parse().map(|v| item.tier = v).is_ok(),
            "kind" => match value.to_ascii_lowercase().as_str() {
                "seat" => {
                    item.kind = Kind::Seat;
                    true
                }
                "keepsake" => {
                    item.kind = Kind::Keepsake;
                    true
                }
                _ => false,
            },
            "model" => {
                item.model = value.to_string();
                true
            }
            "scale" => value.parse().map(|v| item.scale = v).is_ok(),
            "effect" => Effect::parse(value).map(|e| item.effect = Some(e)).is_some(),
            "per_stack" => value.parse().map(|v: f32| item.per_stack = Some(v.clamp(0.0, 100.0))).is_ok(),
            "max" => value.parse().map(|v| item.max = v).is_ok(),
            "kills_per_point" => value.parse().map(|v: u32| item.kills_per_point = v.max(1)).is_ok(),
            "minutes" => value.parse().map(|v| item.minutes = v).is_ok(),
            "lift" => value.parse().map(|v: f32| item.lift = v.clamp(0.0, 2.0)).is_ok(),
            "right" => value.parse().map(|v: f32| item.right = v.clamp(-10.0, 10.0)).is_ok(),
            "forward" => value.parse().map(|v: f32| item.forward = v.clamp(-10.0, 10.0)).is_ok(),
            "turn" => value.parse().map(|v: f32| item.turn = v).is_ok(),
            "found" => {
                item.found = value.split(',').filter_map(|g| g.trim().parse().ok()).collect();
                true
            }
            _ => false,
        };
        if !ok {
            log(&format!("grace_items.txt line {}: \"{}\" not understood", n + 1, line));
        }
    }
    items.retain(|i| match i.problem() {
        Some(p) => {
            log(&format!("grace_items.txt: [{}] skipped: {p}", i.id));
            false
        }
        None => true,
    });
    log(&format!("{} Grace Items defined", items.len()));
    items
}
