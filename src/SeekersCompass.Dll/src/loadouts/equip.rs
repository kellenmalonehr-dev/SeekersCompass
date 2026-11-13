//! Reading the player's equipment and putting a loadout back on.
//!
//! Equipping goes through the game's own routines, the ones its equipment menu uses, so stats,
//! weight, the character model and the HUD all update as they would by hand. They're found by
//! byte pattern; the patterns and calling conventions follow The Grand Archives' Elden Ring cheat
//! table (`equipItem`, `changeMagic`), which documents them.
//!
//! Slot numbers are the game's equipment slot ids (ChrAsmEquipEntries order):
//! 0-5 weapons (left/right x 3), 6-11 arrows/bolts, 12-15 head/chest/arms/legs, 16 unused,
//! 17-20 talismans, 21 the unused fifth talisman ("covenant"), 22-31 quick items, 32-37 pouch.

use std::fmt::Write as _;

use eldenring::cs::{EquipGameData, EquipInventoryDataListEntry, InventoryItemsData, PlayerGameData};

use crate::scan;

pub const GEAR_SLOTS: usize = 22;
pub const GOODS_SLOTS: usize = 16;
pub const SPELL_SLOTS: usize = 14;

#[derive(Clone, Copy)]
pub struct Gear {
    pub item: u32,
    /// The gaitem handle of the exact copy that was equipped (weapons keep their ash of war and
    /// upgrade there), so a loadout picks the same copy when several are carried.
    pub handle: u32,
}

#[derive(Clone, Copy)]
pub struct Goods {
    pub item: u32,
    pub handle: u32,
    /// The inventory index the game recorded for this quick/pouch slot.
    pub index: i32,
}

#[derive(Clone)]
pub struct Loadout {
    pub gear: [Gear; GEAR_SLOTS],
    pub goods: [Goods; GOODS_SLOTS],
    /// Magic param ids by memory slot; -1 = empty.
    pub spells: [i32; SPELL_SLOTS],
}

impl Loadout {
    /// The game's "no item" id.
    pub const EMPTY: u32 = u32::MAX;
    pub const GOODS_BASE: usize = GEAR_SLOTS;
}

impl Default for Loadout {
    fn default() -> Self {
        Loadout {
            gear: [Gear { item: Self::EMPTY, handle: 0 }; GEAR_SLOTS],
            goods: [Goods { item: Self::EMPTY, handle: 0, index: -1 }; GOODS_SLOTS],
            spells: [-1; SPELL_SLOTS],
        }
    }
}

// Slots that can be left empty. Weapon and armor slots are never empty: bare hands and bare body
// parts are items of their own (Unarmed, and the bare head/chest/arms/legs).
const OPTIONAL_GEAR: [usize; 9] = [17, 18, 19, 20, 21, 6, 7, 8, 9];
/// Talismans first (they can change memory slots and equip load), then armor, then weapons.
const GEAR_ORDER: [usize; 19] = [17, 18, 19, 20, 21, 12, 13, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
const GOODS_CATEGORY: u32 = 0x4000_0000;

/// A 4-byte game value (item ids, handles) as its raw bits.
fn raw<T>(v: &T) -> u32 {
    const { assert!(size_of::<T>() == 4) };
    unsafe { *(v as *const T as *const u32) }
}

/// The equipped item ids of all 38 slots (ChrAsmEquipEntries, read as the array it is).
fn equipped(egd: &EquipGameData) -> [u32; 38] {
    const { assert!(size_of::<eldenring::cs::ChrAsmEquipEntries>() >= 38 * 4) };
    unsafe { *(&egd.equipment_entries as *const _ as *const [u32; 38]) }
}

pub fn read_current(player: &PlayerGameData) -> Loadout {
    let egd = &player.equipment;
    let ids = equipped(egd);
    let mut l = Loadout::default();
    for s in 0..GEAR_SLOTS {
        l.gear[s] = Gear { item: ids[s], handle: raw(&egd.chr_asm.gaitem_handles[s]) };
    }
    let items = &egd.equip_item_data;
    for (i, slot) in items.quick_slots.iter().chain(items.pouch_slots.iter()).enumerate() {
        l.goods[i] = Goods { item: ids[Loadout::GOODS_BASE + i], handle: raw(&slot.gaitem_handle), index: slot.index };
    }
    for (s, e) in egd.equip_magic_data.entries.iter().enumerate() {
        l.spells[s] = e.param_id;
    }

    // Check the inventory index scheme (see Inventory) against what the game itself recorded.
    let inv = Inventory(&egd.equip_inventory_data.items_data);
    for g in l.goods.iter().filter(|g| g.item != Loadout::EMPTY) {
        let found = inv.find(g.item, g.handle);
        if found != Some(g.index as u32) {
            super::log(&format!("note: item {:08X} has game index {}, lookup gave {found:?}", g.item, g.index));
        }
    }
    l
}

/// The player's inventory. Equip calls take a single index across both lists: key items first
/// (their own index), then normal items at `key_items_capacity + index`.
struct Inventory<'a>(&'a InventoryItemsData);

impl Inventory<'_> {
    fn normal(&self) -> impl Iterator<Item = (u32, &EquipInventoryDataListEntry)> {
        let base = self.0.key_items_capacity;
        self.0.normal_entries().iter().take(self.0.normal_items_len as usize).enumerate()
            .filter_map(move |(i, e)| e.as_option().map(|e| (base + i as u32, e)))
    }

    fn key(&self) -> impl Iterator<Item = (u32, &EquipInventoryDataListEntry)> {
        self.0.key_entries().iter().enumerate().filter_map(|(i, e)| e.as_option().map(|e| (i as u32, e)))
    }

    fn at(&self, index: u32) -> Option<&EquipInventoryDataListEntry> {
        self.normal().chain(self.key()).find(|(i, _)| *i == index).map(|(_, e)| e)
    }

    /// The index of `item`, preferring the exact copy with `handle`.
    fn find(&self, item: u32, handle: u32) -> Option<u32> {
        let all = || self.normal().chain(self.key());
        all().find(|(_, e)| raw(&e.item_id) == item && raw(&e.gaitem_handle) == handle)
            .or_else(|| all().find(|(_, e)| raw(&e.item_id) == item))
            .map(|(i, _)| i)
    }

    /// The position of `item` in the normal list alone (what the spell call is given).
    fn normal_position(&self, item: u32) -> Option<u32> {
        let base = self.0.key_items_capacity;
        self.normal().find(|(_, e)| raw(&e.item_id) == item).map(|(i, _)| i - base)
    }
}

type EquipGearFn = unsafe extern "C" fn(*mut EquipGameData, u32, *const u32, u32, u32, u32, u32) -> u64;
type EquipGoodsFn = unsafe extern "C" fn(*mut EquipGameData, u32, *const u32, u32) -> u64;
type ChangeMagicFn = unsafe extern "C" fn(u32, *mut u8) -> u64;

pub struct GameCalls {
    equip_gear: EquipGearFn,
    equip_goods: EquipGoodsFn,
    change_magic: ChangeMagicFn,
}

impl GameCalls {
    pub fn find() -> Result<GameCalls, String> {
        let gear = scan::unique("?? 8b f1 ?? 8b d8 ?? 63 ea ?? 8b f9")? - 0x17;
        let goods = scan::unique("?? fa ?? ?? 0f ?? 81 c1 ?? ?? ?? ?? ?? 8b c1 e9")?;
        let magic = scan::unique("?? 89 5c ?? ?? ?? 89 74 ?? ?? 57 ?? 83 ec ?? ?? 8b c2 8b f9 ?? 8b c8")?;
        let exe = unsafe { windows::Win32::System::LibraryLoader::GetModuleHandleW(None) }.map(|m| m.0 as usize).unwrap_or(0);
        super::log(&format!("equip functions at +{:X} +{:X} +{:X}", gear - exe, goods - exe, magic - exe));
        unsafe {
            Ok(GameCalls {
                equip_gear: std::mem::transmute::<usize, EquipGearFn>(gear),
                equip_goods: std::mem::transmute::<usize, EquipGoodsFn>(goods),
                change_magic: std::mem::transmute::<usize, ChangeMagicFn>(magic),
            })
        }
    }

    /// Equips the inventory item at `index` in a gear slot, or empties the slot (`Loadout::EMPTY`).
    /// The game stores the value it's handed as the slot's item handle, so it must be the
    /// inventory entry's own handle: handed the item id instead, the slot is left with a handle
    /// that names nothing.
    fn gear(&self, egd: *mut EquipGameData, slot: usize, item: u32, index: u32) {
        let handle = if item == Loadout::EMPTY {
            item
        } else {
            let inv = Inventory(unsafe { &(*egd).equip_inventory_data.items_data });
            match inv.at(index) {
                Some(e) => raw(&e.gaitem_handle),
                None => {
                    super::log(&format!("slot {slot}: nothing at inventory index {index}; left as it is"));
                    return;
                }
            }
        };
        super::log(&format!("  gear slot {slot}: item {item:08X} handle {handle:08X} index {index}"));
        unsafe { (self.equip_gear)(egd, slot as u32, &handle, index, 1, 1, 0) };
    }

    fn goods(&self, egd: *mut EquipGameData, slot: usize, item: u32, index: u32) {
        unsafe { (self.equip_goods)(egd, (slot - Loadout::GOODS_BASE) as u32, &item, index) };
    }

    /// Attunes `spell` (a magic param id, or None to clear) to memory slot `slot`.
    fn magic(&self, slot: usize, spell: Option<(i32, u32)>) {
        let mut args = [0u8; 128];
        let put = |args: &mut [u8; 128], at: usize, v: u32| args[at..at + 4].copy_from_slice(&v.to_le_bytes());
        put(&mut args, 0x08, slot as u32);
        match spell {
            Some((id, index)) => {
                put(&mut args, 0x48, index);
                put(&mut args, 0x4C, id as u32 + GOODS_CATEGORY);
                put(&mut args, 0x50, id as u32 + 0xB000_0000);
            }
            None => {
                put(&mut args, 0x48, u32::MAX);
                put(&mut args, 0x4C, 0x4FFF_FFFF);
                put(&mut args, 0x50, 0xBFFF_FFFF);
            }
        }
        unsafe { (self.change_magic)(slot as u32, args.as_mut_ptr()) };
    }

    /// Puts `l` on. Items no longer carried are skipped and named in the returned report.
    pub fn apply(&self, player: &mut PlayerGameData, l: &Loadout) -> String {
        let player: *mut PlayerGameData = player;
        let egd: *mut EquipGameData = unsafe { &mut (*player).equipment };
        // Re-read after every call: the game rewrites these as it equips.
        let now = || unsafe { equipped(&*egd) };
        let handle_now = |s: usize| unsafe { raw(&(*egd).chr_asm.gaitem_handles[s]) };
        let inv = || Inventory(unsafe { &(*egd).equip_inventory_data.items_data });
        let (mut changed, mut missing) = (0, Vec::new());

        // Empty optional slots that will change first, so an item moving between two slots (a
        // talisman, an arrow, a quick item) is free when its new slot is filled.
        for &s in &OPTIONAL_GEAR {
            if now()[s] != l.gear[s].item && now()[s] != Loadout::EMPTY {
                self.gear(egd, s, Loadout::EMPTY, u32::MAX);
            }
        }
        for g in Loadout::GOODS_BASE..Loadout::GOODS_BASE + GOODS_SLOTS {
            if now()[g] != l.goods[g - Loadout::GOODS_BASE].item && now()[g] != Loadout::EMPTY {
                self.goods(egd, g, Loadout::EMPTY, u32::MAX);
            }
        }

        for &s in &GEAR_ORDER {
            let want = l.gear[s];
            if now()[s] == want.item && (want.handle == 0 || handle_now(s) == want.handle) {
                continue;
            }
            if want.item == Loadout::EMPTY {
                self.gear(egd, s, Loadout::EMPTY, u32::MAX);
                changed += 1;
                continue;
            }
            match inv().find(want.item, want.handle) {
                Some(index) => {
                    self.gear(egd, s, want.item, index);
                    changed += 1;
                }
                None => missing.push(want.item),
            }
        }

        for (i, want) in l.goods.iter().enumerate() {
            let g = Loadout::GOODS_BASE + i;
            if now()[g] == want.item || want.item == Loadout::EMPTY {
                continue;
            }
            // The recorded index, if it still holds this item; else look it up.
            let recorded = (want.index >= 0 && inv().at(want.index as u32).is_some_and(|e| raw(&e.item_id) == want.item)).then_some(want.index as u32);
            match recorded.or_else(|| inv().find(want.item, want.handle)) {
                Some(index) => {
                    self.goods(egd, g, want.item, index);
                    changed += 1;
                }
                None => missing.push(want.item),
            }
        }

        // Spells: clear what differs, then attune, so a spell moving between slots isn't refused
        // as already attuned. Only slots the character has now are filled.
        let magic = || unsafe { &*(*egd).equip_magic_data };
        let usable = memory_slots(player);
        for s in 0..SPELL_SLOTS {
            let cur = magic().entries[s].param_id;
            if cur != l.spells[s] && cur >= 0 {
                self.magic(s, None);
            }
        }
        for s in 0..SPELL_SLOTS.min(usable) {
            let want = l.spells[s];
            if want < 0 || magic().entries[s].param_id == want {
                continue;
            }
            match inv().normal_position(want as u32 + GOODS_CATEGORY) {
                // The spell call counts from one before the normal list (as the table's does).
                Some(pos) => {
                    let index = (inv().0.key_items_capacity + pos).wrapping_sub(1);
                    self.magic(s, Some((want, index)));
                    changed += 1;
                }
                None => missing.push(want as u32 + GOODS_CATEGORY),
            }
        }
        let skipped = l.spells.iter().skip(usable).filter(|&&id| id >= 0).count();

        let mut report = format!("{changed} slots changed");
        if !missing.is_empty() {
            let _ = write!(report, ", not carried: {}", missing.iter().map(|m| format!("{m:08X}")).collect::<Vec<_>>().join(" "));
        }
        if skipped > 0 {
            let _ = write!(report, ", {skipped} spells need more memory slots");
        }
        report
    }
}

/// How many memory slots the character has right now (stones plus talismans). Read from the
/// offset the cheat table uses; anything implausible falls back to all 14.
fn memory_slots(player: *const PlayerGameData) -> usize {
    let n = unsafe { *(player as *const u8).add(0xA74).cast::<u32>() } as usize;
    if (1..=SPELL_SLOTS).contains(&n) { n } else { SPELL_SLOTS }
}
