//! The grace menu's rune line: use carried golden runes (and the other items that only give
//! runes) to get the held runes up to what the next level costs.
//!
//! The line is part of the grace talk script, which can take items away but can't count runes;
//! this side can count but has no way to take an item. So the two share the work through event
//! flags (the generator's RuneLine.cs is the other half):
//!
//! - `AVAILABLE` is kept on while the carried rune items can cover what is missing. The line
//!   shows only then.
//! - Choosing the line raises `REQUEST`. This side picks the items that overshoot the least and
//!   raises `STEP + i` for one item at a time; the script takes one of item `i` and lowers it.
//! - When all are taken, the runes of whatever really left the inventory are added, and
//!   `REQUEST` is lowered, which sends the script back to the grace menu.
//!
//! The items are data/rune_items.txt, by position; what each is worth is read from the running
//! game's own data, so an overhaul's amounts are followed.

use std::time::{Duration, Instant};

use eldenring::cs::{CSEventFlagMan, CSTaskGroupIndex, CSTaskImp, EquipParamGoods, GameDataMan, PlayerGameData, SoloParamRepository, SpEffectParam, WorldChrMan};
use fromsoftware_shared::FromStatic;

// Event flags shared with the talk script; the generator checks that nothing else uses them.
const AVAILABLE: u32 = 76700;
const REQUEST: u32 = 76701;
const STEP: u32 = 76710;
const MAX_ITEMS: usize = 40;
const GOODS_CATEGORY: u32 = 0x4000_0000;
const MAX_LEVEL: u32 = 713;
const MAX_RUNES: u64 = 999_999_999;
/// How long the script gets to take one item before the rest is called off.
const STEP_TIMEOUT: Duration = Duration::from_secs(3);

fn log(text: &str) {
    crate::log("runes", text);
}

pub fn start(tasks: &CSTaskImp) -> Result<(), String> {
    let items: Vec<u32> = include_str!("../../../data/rune_items.txt")
        .lines()
        .filter_map(|l| l.split('#').next()?.split_whitespace().next()?.parse().ok())
        .collect();
    if items.is_empty() || items.len() > MAX_ITEMS {
        return Err(format!("rune_items.txt has {} items; 1 to {MAX_ITEMS} are supported", items.len()));
    }
    crate::run(tasks, "runes", CSTaskGroupIndex::FrameBegin, State { items, values: None, run: None, fresh: true, frame: 0 }, State::tick);
    Ok(())
}

/// Hides the line; run every frame while the feature is switched off.
pub fn hide_menu() {
    if let Ok(man) = unsafe { CSEventFlagMan::instance_mut() } {
        man.virtual_memory_flag.set_flag(AVAILABLE, false);
    }
}

struct State {
    /// Goods ids, by position in rune_items.txt.
    items: Vec<u32>,
    /// What one of each gives, once the game's data could be read (0: not in this game).
    values: Option<Vec<u32>>,
    run: Option<Run>,
    /// Not yet seen in the world since the title screen: leftover requests are dropped first.
    fresh: bool,
    frame: u32,
}

/// A request being carried out.
struct Run {
    /// Items still to take, one entry each, taken from the end.
    queue: Vec<usize>,
    /// How many of each were carried when the request came.
    before: Vec<u32>,
    /// The item the script has been asked to take, and since when.
    pending: Option<(usize, Instant)>,
}

/// What the next level costs at `level`: the game's own curve.
fn level_cost(level: u32) -> u64 {
    let l = level as f64 + 81.0;
    let x = ((l - 92.0) * 0.02).max(0.0);
    ((x + 0.1) * l * l).floor() as u64 + 1
}

/// How many of each item to use so that their runes reach `need` with the least left over.
/// None if nothing is needed or the items can't cover it.
fn plan(need: u64, counts: &[u32], values: &[u32]) -> Option<Vec<u32>> {
    let total: u64 = counts.iter().zip(values).map(|(&c, &v)| c as u64 * v as u64).sum();
    if need == 0 || total < need {
        return None;
    }
    fn gcd(a: u64, b: u64) -> u64 {
        if b == 0 { a } else { gcd(b, a % b) }
    }
    let carried = || (0..counts.len()).filter(|&i| counts[i] > 0 && values[i] > 0);
    let unit = carried().fold(0, |g, i| gcd(g, values[i] as u64));
    let largest = carried().map(|i| values[i] as u64).max()?;
    // A smallest covering set overshoots by less than its largest item, so sums up to here suffice.
    let need_units = need.div_ceil(unit) as usize;
    let top = need_units + (largest / unit) as usize;
    let mut take = vec![0u32; counts.len()];
    if top > 4_000_000 {
        // Far beyond any level's cost; largest first is good enough.
        let mut order: Vec<usize> = carried().collect();
        order.sort_by_key(|&i| std::cmp::Reverse(values[i]));
        let mut sum = 0;
        for i in order {
            while take[i] < counts[i] && sum < need {
                take[i] += 1;
                sum += values[i] as u64;
            }
        }
        return Some(take);
    }

    // Each item's count as lots of 1, 2, 4, ... so every number of it can be made from few lots.
    let mut lots: Vec<(usize, u32)> = Vec::new();
    let mut order: Vec<usize> = carried().collect();
    order.sort_by_key(|&i| std::cmp::Reverse(values[i]));
    for i in order {
        let (mut left, mut n) = (counts[i], 1);
        while left > 0 {
            let lot = n.min(left);
            lots.push((i, lot));
            left -= lot;
            n *= 2;
        }
    }
    // from[s]: the lot that first made the sum s (in units); NONE if no set of lots makes it.
    const NONE: u16 = u16::MAX;
    let weight = |&(i, n): &(usize, u32)| (n as u64 * values[i] as u64 / unit) as usize;
    let mut from = vec![NONE; top + 1];
    for (g, lot) in lots.iter().enumerate() {
        let w = weight(lot);
        if w > top {
            continue;
        }
        // Downward, so a sum made here is built only on sums of earlier lots.
        for s in (w..=top).rev() {
            if from[s] == NONE && (s == w || from[s - w] != NONE) {
                from[s] = g as u16;
            }
        }
    }
    let mut s = (need_units..=top).find(|&s| from[s] != NONE)?;
    while s > 0 {
        let lot = &lots[from[s] as usize];
        take[lot.0] += lot.1;
        s -= weight(lot);
    }
    Some(take)
}

impl State {
    /// How many of each rune item the player carries.
    fn counts(&self, player: &PlayerGameData) -> Vec<u32> {
        let mut counts = vec![0; self.items.len()];
        for e in player.equipment.equip_inventory_data.items_data.items() {
            const { assert!(size_of::<eldenring::cs::ItemId>() == 4) };
            let id = unsafe { *(&e.item_id as *const _ as *const u32) };
            if let Some(i) = self.items.iter().position(|&g| g | GOODS_CATEGORY == id) {
                counts[i] += e.quantity;
            }
        }
        counts
    }

    /// The rune amount of each item's use effect, from the game's data.
    fn read_values(&self) -> Option<Vec<u32>> {
        let params = unsafe { SoloParamRepository::instance() }.ok()?;
        let values: Vec<u32> = self
            .items
            .iter()
            .map(|&id| {
                params
                    .get::<EquipParamGoods>(id)
                    .and_then(|g| u32::try_from(g.ref_id_default()).ok())
                    .and_then(|effect| params.get::<SpEffectParam>(effect))
                    .map_or(0, |e| e.soul().max(0) as u32)
            })
            .collect();
        log(&format!("{} of {} rune items found in the game's data", values.iter().filter(|&&v| v > 0).count(), values.len()));
        Some(values)
    }

    fn tick(&mut self) {
        let in_world = unsafe { WorldChrMan::instance() }.map(|w| w.main_player.is_some()).unwrap_or(false);
        let (Ok(game_data), Ok(flags)) = (unsafe { GameDataMan::instance_mut() }, unsafe { CSEventFlagMan::instance_mut() }) else {
            return;
        };
        if !in_world {
            self.run = None;
            self.fresh = true;
            return;
        }
        let player = &mut *game_data.main_player_game_data;
        let flags = &mut flags.virtual_memory_flag;
        if self.values.is_none() {
            self.values = self.read_values();
        }
        let Some(values) = self.values.clone() else { return };
        if self.fresh {
            // A request cut short by quitting must not act on the next load.
            flags.set_flag(REQUEST, false);
            for i in 0..MAX_ITEMS as u32 {
                flags.set_flag(STEP + i, false);
            }
            self.fresh = false;
        }

        let counts = self.counts(player);
        let need = if player.level >= MAX_LEVEL { 0 } else { level_cost(player.level).saturating_sub(player.rune_count as u64) };
        let Some(mut run) = self.run.take() else {
            if flags.get_flag(REQUEST) {
                match plan(need, &counts, &values) {
                    Some(take) => {
                        let used: Vec<String> = take.iter().enumerate().filter(|(_, n)| **n > 0).map(|(i, n)| format!("{n} x {}", self.items[i])).collect();
                        let sum: u64 = take.iter().zip(&values).map(|(&n, &v)| n as u64 * v as u64).sum();
                        log(&format!("level {}: {} runes held, {need} missing; using {} for {sum} runes", player.level, player.rune_count, used.join(", ")));
                        let queue = take.iter().enumerate().flat_map(|(i, &n)| std::iter::repeat_n(i, n as usize)).collect();
                        self.run = Some(Run { queue, before: counts, pending: None });
                    }
                    None => {
                        log(&format!("asked, but nothing to do: {need} runes missing"));
                        flags.set_flag(AVAILABLE, false);
                        flags.set_flag(REQUEST, false);
                    }
                }
            } else {
                self.frame = self.frame.wrapping_add(1);
                if self.frame % 15 == 0 {
                    let total: u64 = counts.iter().zip(&values).map(|(&c, &v)| c as u64 * v as u64).sum();
                    flags.set_flag(AVAILABLE, need > 0 && total >= need);
                }
            }
            return;
        };

        if let Some((i, since)) = run.pending {
            if !flags.get_flag(STEP + i as u32) {
                run.pending = None;
            } else if since.elapsed() > STEP_TIMEOUT {
                log(&format!("the menu did not take item {}; the rest is called off", self.items[i]));
                flags.set_flag(STEP + i as u32, false);
                run.queue.clear();
                run.pending = None;
            }
        }
        if run.pending.is_some() {
            self.run = Some(run);
            return;
        }
        if let Some(i) = run.queue.pop() {
            flags.set_flag(STEP + i as u32, true);
            run.pending = Some((i, Instant::now()));
            self.run = Some(run);
            return;
        }
        // All taken: the runes of what really left the inventory.
        let gained: u64 = run.before.iter().zip(&counts).zip(&values).map(|((&b, &c), &v)| b.saturating_sub(c) as u64 * v as u64).sum();
        player.rune_count = (player.rune_count as u64 + gained).min(MAX_RUNES) as u32;
        player.rune_memory = (player.rune_memory as u64 + gained).min(MAX_RUNES) as u32;
        log(&format!("{gained} runes added; {} held now", player.rune_count));
        flags.set_flag(AVAILABLE, false);
        flags.set_flag(REQUEST, false);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cost_follows_the_game() {
        assert_eq!(level_cost(1), 673);
        assert_eq!(level_cost(2), 689);
        assert_eq!(level_cost(12), 1038);
    }

    #[test]
    fn plan_overshoots_the_least() {
        let values = [200, 400, 800, 50_000];
        // 500 missing: 200 + 400 = 600 beats 800 and the Lord's Rune.
        assert_eq!(plan(500, &[1, 1, 1, 1], &values), Some(vec![1, 1, 0, 0]));
        // Exactly coverable.
        assert_eq!(plan(1000, &[3, 0, 1, 0], &values), Some(vec![1, 0, 1, 0]));
        // Only the big one covers it.
        assert_eq!(plan(2000, &[1, 1, 1, 1], &values), Some(vec![0, 0, 0, 1]));
        // Not enough, and nothing needed.
        assert_eq!(plan(2000, &[1, 1, 1, 0], &values), None);
        assert_eq!(plan(0, &[1, 1, 1, 1], &values), None);
        // Many of one kind.
        assert_eq!(plan(4100, &[99, 0, 0, 0], &values), Some(vec![21, 0, 0, 0]));
    }
}
