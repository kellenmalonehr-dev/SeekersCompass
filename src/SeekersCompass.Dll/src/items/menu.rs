//! The camp menu's side of the event flags (see DESIGN.md; must match GraceItems.Gen's Builder.cs).
//! The talk script shows owned items and lights equipped ones from these flags, and asks for an
//! item by writing its index in binary and raising the request flag. It also shows found-item
//! pop-ups and two status lines from numbers the DLL keeps in flags.

use eldenring::cs::CSEventFlagMan;
use fromsoftware_shared::FromStatic;

const ALIVE_FLAG: u32 = 78715;
const REQUEST_FLAG: u32 = 78716;
const INDEX_BITS_FLAG: u32 = 78717;
const INDEX_BITS: u32 = 7;
const OWNED_FLAG: u32 = 78725;
const EQUIPPED_FLAG: u32 = 78810;
/// The owned and equipped ranges end here; the flags after them are the ones below.
pub const MAX_ITEMS: usize = 64;

const FOUND_PENDING_FLAG: u32 = 78789;
const FOUND_ACK_FLAG: u32 = 78790;
const FOUND_INDEX_FLAG: u32 = 78791;
const MINUTES_FLAG: u32 = 78874;
const KILLS_FLAG: u32 = 78881;
const NUMBER_BITS: u32 = 7;
/// The found index whose pop-up says items from earlier graces are waiting.
pub const WELCOME_INDEX: usize = 127;
pub const MAX_MINUTES: u32 = 120;
pub const MAX_KILLS: u32 = 100;

/// Whether any event flag is on, such as a grace's unlock flag.
pub fn flag_on(flag: u32) -> bool {
    unsafe { CSEventFlagMan::instance() }.is_ok_and(|man| man.virtual_memory_flag.get_flag(flag))
}

fn set_flag(flag: u32, on: bool) {
    if let Ok(man) = unsafe { CSEventFlagMan::instance_mut() } {
        man.virtual_memory_flag.set_flag(flag, on);
    }
}

fn set_number(start: u32, value: u32) {
    for b in 0..NUMBER_BITS {
        set_flag(start + b, (value >> b) & 1 == 1);
    }
}

/// Keeps the Camp line visible; called every frame in the world.
pub fn keep_alive() {
    set_flag(ALIVE_FLAG, true);
}

/// The item the menu asked for, if any, clearing the request.
pub fn take_request() -> Option<usize> {
    if !flag_on(REQUEST_FLAG) {
        return None;
    }
    let index = (0..INDEX_BITS).filter(|b| flag_on(INDEX_BITS_FLAG + b)).map(|b| 1usize << b).sum();
    Some(index)
}

/// Lets the menu rebuild its list; called once the request has been acted on.
pub fn finish_request() {
    set_flag(REQUEST_FLAG, false);
}

/// Shows which items (by position in grace_items.txt) are owned and equipped.
pub fn show(owned: impl Fn(usize) -> bool, equipped: impl Fn(usize) -> bool) {
    let Ok(man) = (unsafe { CSEventFlagMan::instance_mut() }) else { return };
    let flags = &mut man.virtual_memory_flag;
    for n in 0..MAX_ITEMS {
        flags.set_flag(OWNED_FLAG + n as u32, owned(n));
        flags.set_flag(EQUIPPED_FLAG + n as u32, equipped(n));
    }
}

/// Puts up the next found-item pop-up (an item's index, or the welcome index), or none.
pub fn announce(index: Option<usize>) {
    if let Some(n) = index {
        set_number(FOUND_INDEX_FLAG, n as u32);
    }
    set_flag(FOUND_PENDING_FLAG, index.is_some());
}

/// Whether the menu has just shown the pending pop-up. The caller puts up the next one with
/// `announce` and then calls `finish_ack`, which lets the menu go on.
pub fn acknowledged() -> bool {
    flag_on(FOUND_ACK_FLAG)
}

pub fn finish_ack() {
    set_flag(FOUND_ACK_FLAG, false);
}

/// The status lines: the running buff's minutes left (0 for none) and the kills banked at the last rest.
pub fn status(minutes: u32, kills: u32) {
    set_number(MINUTES_FLAG, minutes.min(MAX_MINUTES));
    set_number(KILLS_FLAG, kills.min(MAX_KILLS));
}

/// Hides the Camp line; called every frame while Grace Items is switched off.
pub fn hide() {
    set_flag(ALIVE_FLAG, false);
}
