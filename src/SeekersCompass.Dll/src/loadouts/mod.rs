//! Loadouts: save and equip whole gear sets from the Site of Grace menu.
//!
//! The grace menu is a talk script, and talk scripts can't equip items. So the menu asks through
//! event flags: "Equip loadout N" sets `EQUIP_FLAG + N - 1`, "Save gear to loadout N" sets
//! `SAVE_FLAG + N - 1`. This feature's task sees the flag, clears it and does the work. It also
//! keeps `STORED_FLAG + N - 1` on for every slot that holds a loadout, which makes that slot's
//! Equip line glow in the menu.
//!
//! Loadouts live in `loadouts/` next to the DLL, one file per save slot and character name.

mod equip;
mod store;

use eldenring::cs::{CSEventFlagMan, CSTaskGroupIndex, CSTaskImp, GameDataMan, GameMan, WorldChrMan};
use fromsoftware_shared::FromStatic;

use store::Store;

/// Loadout slots per character. The flag blocks and menu lines below are sized for this.
pub const SLOTS: u32 = 5;
// Event flags shared with the talk script. The generator checks that nothing in the game or its
// base layer uses them.
pub const EQUIP_FLAG: u32 = 78700;
pub const SAVE_FLAG: u32 = 78705;
pub const STORED_FLAG: u32 = 78710;

fn log(text: &str) {
    crate::log("loadouts", text);
}

/// Finds the game's equip functions and starts the feature; an error means this game version
/// isn't supported.
pub fn start(tasks: &CSTaskImp) -> Result<(), String> {
    let calls = equip::GameCalls::find().map_err(|e| format!("equip functions not found ({e}); this game version isn't supported"))?;
    crate::run(tasks, "loadouts", CSTaskGroupIndex::FrameBegin, State { calls, store: None }, State::tick);
    Ok(())
}

struct State {
    calls: equip::GameCalls,
    /// The loaded character's loadouts; None at the title screen.
    store: Option<Store>,
}

impl State {
    fn tick(&mut self) {
        let in_world = unsafe { WorldChrMan::instance() }.map(|w| w.main_player.is_some()).unwrap_or(false);
        let (Ok(game_data), Ok(game_man), Ok(flags)) =
            (unsafe { GameDataMan::instance_mut() }, unsafe { GameMan::instance() }, unsafe { CSEventFlagMan::instance_mut() })
        else {
            return;
        };
        if !in_world {
            self.store = None;
            return;
        }
        let player = &mut *game_data.main_player_game_data;
        let flags = &mut flags.virtual_memory_flag;

        // A different character (or the first one since launch): load its loadouts and light the
        // menu lines of the slots it has.
        let key = store::character_key(game_man.save_slot, &player.character_name);
        if self.store.as_ref().map(|s| &s.key) != Some(&key) {
            let s = Store::load(key);
            log(&format!("character {}: {} saved loadouts", s.key, s.count()));
            for n in 0..SLOTS {
                flags.set_flag(STORED_FLAG + n, s.get(n).is_some());
            }
            self.store = Some(s);
        }
        let store = self.store.as_mut().unwrap();

        for n in 0..SLOTS {
            if flags.get_flag(SAVE_FLAG + n) {
                flags.set_flag(SAVE_FLAG + n, false);
                let loadout = equip::read_current(player);
                store.set(n, loadout);
                flags.set_flag(STORED_FLAG + n, true);
                log(&format!("saved loadout {}", n + 1));
            }
            if flags.get_flag(EQUIP_FLAG + n) {
                flags.set_flag(EQUIP_FLAG + n, false);
                match store.get(n) {
                    Some(loadout) => {
                        let report = self.calls.apply(player, loadout);
                        log(&format!("equipped loadout {}: {report}", n + 1));
                    }
                    None => log(&format!("loadout {} is empty", n + 1)),
                }
            }
        }
    }
}
