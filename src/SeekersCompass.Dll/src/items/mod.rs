//! Grace Items: a buff from resting at graces, earned with kills and set by items found at graces.
//!
//! 1. Items are defined in grace_items.txt, in tracks that upgrade in place. Discovering a grace (or
//!    having discovered it before the mod) gives the items found there.
//! 2. Each character's items, equipped set, unbanked kills and running buff are kept in
//!    characters/<slot and name>.txt, so the buff survives quitting.
//! 3. On a sit, kills since the last sit unlock the equipped keepsakes' bonuses for the equipped
//!    seat's time; the stronger buff wins; dying ends it.
//! 4. The equipped seat and keepsakes appear where the player sits, after the rest resets the world.
//!
//! 5. The grace menu's Camp line lists owned items (equipped ones glow); choosing one equips or
//!    unequips it (menu.rs and DESIGN.md).
//!
//! 6. Found items queue a pop-up shown when Camp is next opened (its line glows meanwhile); Camp's
//!    top lines show the buff's minutes left and the kills banked at the last rest. The buff shows
//!    an icon on screen while it runs.
//!
//! A new track fills an empty slot by itself. With TestKeys = 1 in seekers_compass.ini,
//! F7 counts as a sit and F10 adds 5 test kills.


mod buff;
mod items;
mod menu;
mod store;

use std::collections::{HashMap, HashSet};
use std::time::{Duration, Instant, SystemTime};

use eldenring::cs::{
    AssetEnvironmentGeometryParam, CSTaskGroupIndex, CSWorldGeomMan, ChrInsExt, FieldInsHandle,
    GameMan, GeometrySpawnParameters, SoloParamRepository, WorldChrMan,
};
use eldenring::position::BlockPosition;
use eldenring::util::input;
use fromsoftware_shared::FromStatic;

use crate::home;
use buff::{Buff, Grant, Keepsake};
use items::{Item, Kind};
use store::Store;

const KEY_F7: i32 = 0x76;
const KEY_F9: i32 = 0x78;
const KEY_F10: i32 = 0x79;

/// Frames out of the sit range before the player counts as standing, so the short gaps between
/// the sit-down, idle and stand-up animations don't count.
const STAND_FRAMES: u32 = 90;
/// Frames to wait after the player appears, so nothing happens mid-load.
const SETTLE: u32 = 120;
/// Props built since the last rest or load (resting and loading remove them), so a stuck sit
/// detector can't pile up objects.
const MAX_SPAWNS: u32 = 60;
/// How long the rest animation must have been over before props are spawned.
const AFTER_REST: Duration = Duration::from_millis(1000);
/// Spawns anyway if the rest animation never shows, as happens on a rest with a loading screen.
const SPAWN_ANYWAY: Duration = Duration::from_millis(4000);
/// Resets and loads drop every character's HP to zero; none of those are kills.
const RESET_WINDOW: Duration = Duration::from_secs(5);
/// ASSET_HIT_CREATE_TYPE_ENUM: build no collision mesh.
const HIT_NONE: i8 = 2;
/// ASSET_COLLISION_TYPE: the player and camera pass through.
const COLLISION_MISSES_PLAYER_AND_CAMERA: u8 = 57;
/// Deaths this close count even without a hit from the player.
const NEARBY_METERS: f32 = 30.0;
/// Frames to wait before rechecking a death that wasn't credited to the player.
const RECHECK_FRAMES: u32 = 30;
/// Frames between writes of the character's file, so the buff's time left is never far off.
const SAVE_EVERY: u32 = 600;
const MAX_KEEPSAKES: usize = 2;
/// Frames between checks for newly discovered graces.
const DISCOVERY_EVERY: u32 = 120;
/// The queued pop-up that says items from earlier graces are waiting.
const WELCOME: &str = "*welcome";

pub(crate) fn log(text: &str) {
    crate::log("items", text);
}

/// Starts the feature: its task runs after physics each frame, the slot the library's spawn
/// example uses.
pub fn start(tasks: &eldenring::cs::CSTaskImp) -> Result<(), String> {
    crate::run(tasks, "items", CSTaskGroupIndex::ChrIns_PostPhysics, State::default(), State::tick);
    Ok(())
}

/// Hides the Camp line, for when the feature is switched off.
pub fn hide_menu() {
    menu::hide();
}

#[derive(Clone)]
struct Settings {
    /// Gives every defined item, for testing.
    grant_all: bool,
    /// F7 counts as a sit, F10 adds 5 kills.
    test_keys: bool,
    /// Keeps writing the player's position to position.txt, for picking spots in game.
    write_position: bool,
    keep_kills_on_death: bool,
    sit_min: i32,
    sit_max: i32,
    rest_anim: i32,
    no_collision: bool,
    turn: f32,
    /// The buff's on-screen icon (SpEffectParam iconId); 0 for none.
    buff_icon: i32,
    /// How far, and at what angle from straight ahead, a player standing up on a seat steps off it.
    step_off: f32,
    step_off_turn: f32,
}

impl Default for Settings {
    fn default() -> Self {
        Settings {
            grant_all: false,
            test_keys: false,
            write_position: false,
            keep_kills_on_death: false,
            // 68000 is touching a grace for the first time, which is not a sit.
            sit_min: 68010,
            sit_max: 68049,
            rest_anim: 68050,
            no_collision: true,
            turn: 0.0,
            // The stat-boost icon of Exalted Flesh and the stat-knot Crystal Tears.
            buff_icon: 20406,
            step_off: 0.8,
            step_off_turn: 0.0,
        }
    }
}

fn read_settings() -> Settings {
    let mut s = Settings::default();
    for (key, value) in crate::ini("GraceItems") {
        let value = value.as_str();
        match key.as_str() {
            "grantall" => s.grant_all = value != "0",
            "testkeys" => s.test_keys = value != "0",
            "writeposition" => s.write_position = value != "0",
            "keepkillsondeath" => s.keep_kills_on_death = value != "0",
            "sitanimmin" => s.sit_min = value.parse().unwrap_or(s.sit_min),
            "sitanimmax" => s.sit_max = value.parse().unwrap_or(s.sit_max),
            "restanim" => s.rest_anim = value.parse().unwrap_or(s.rest_anim),
            "nocollision" => s.no_collision = value != "0",
            "turn" => s.turn = value.parse().unwrap_or(s.turn),
            "bufficon" => s.buff_icon = value.parse().unwrap_or(s.buff_icon),
            "stepoff" => s.step_off = value.parse().unwrap_or(s.step_off),
            "stepoffturn" => s.step_off_turn = value.parse().unwrap_or(s.step_off_turn),
            _ => {}
        }
    }
    s
}

/// AEG###_###. The spawn request copies this into a fixed-size buffer without checking it.
pub(crate) fn valid_asset(name: &str) -> bool {
    let b = name.as_bytes();
    b.len() == 10
        && b.starts_with(b"AEG")
        && b[6] == b'_'
        && b[3..6].iter().chain(&b[7..10]).all(u8::is_ascii_digit)
}

/// A death not credited to the player, looked at again in case the hit is recorded late.
struct Recheck {
    handle: FieldInsHandle,
    frames: u32,
}

#[derive(Default)]
struct State {
    frames_in_world: u32,
    last_anim: i32,
    settings: Settings,
    items: Vec<Item>,
    /// The loaded character's file; None between characters.
    store: Option<Store>,
    sitting: bool,
    /// Last frame's animation was a sit or the rest.
    was_in_range: bool,
    frames_out_of_range: u32,
    /// Waiting for the rest to finish before spawning; holds when the sit began.
    pending_spawn: Option<Instant>,
    /// When the rest animation last showed during this sit.
    last_rest: Option<Instant>,
    spawns: u32,
    hp: HashMap<FieldInsHandle, i32>,
    /// Characters the player has hit, for deaths whose last hit came from something else.
    touched: HashSet<FieldInsHandle>,
    rechecks: Vec<Recheck>,
    reset_until: Option<Instant>,
    /// Kills since the last sit, with deaths from anything else alongside.
    kills: u32,
    other_deaths: u32,
    ignored_resets: u32,
    buff: Buff,
    /// The player was dead last frame.
    dead: bool,
    /// Kills banked at the last sit, for the Camp status line.
    last_sit_kills: u32,
    /// After lifting the player onto a seat: when to log where they settled, and the floor height.
    seat_check: Option<(Instant, f32)>,
    /// The floor height where the player sat, when they were lifted onto a seat at this sit.
    seat_floor: Option<f32>,
    /// grace_items.txt's modified time when it was last read, to pick up edits without a restart.
    items_modified: Option<SystemTime>,
}

impl State {
    fn tick(&mut self) {
        let Some(wcm) = (unsafe { WorldChrMan::instance() }).ok() else { return };
        let Some(player) = wcm.main_player.as_ref() else {
            if self.frames_in_world != 0 {
                log("player gone (load or quit)");
                self.save();
            }
            self.frames_in_world = 0;
            self.hp.clear();
            self.touched.clear();
            self.rechecks.clear();
            self.sitting = false;
            self.was_in_range = false;
            self.seat_floor = None;
            self.pending_spawn = None;
            self.last_rest = None;
            // A load removes every prop.
            self.spawns = 0;
            return;
        };
        self.frames_in_world = self.frames_in_world.saturating_add(1);
        if self.frames_in_world < SETTLE {
            return;
        }
        if self.frames_in_world == SETTLE {
            self.enter_world();
        }
        if self.frames_in_world % 60 == 0 {
            self.settings = read_settings();
            self.buff.set_icon(self.settings.buff_icon);
            if items::modified() != self.items_modified {
                self.items_modified = items::modified();
                self.items = items::load();
                log(&format!("grace_items.txt changed; {} items reloaded", self.items.len()));
            }
        }
        if self.frames_in_world % SAVE_EVERY == 0 {
            self.save();
        }
        if self.frames_in_world % DISCOVERY_EVERY == 0 {
            self.check_discovered();
        }

        let time_act = &player.chr_ins.modules.time_act;
        let anim = time_act.anim_queue[time_act.read_idx as usize % 10].anim_id;
        if anim != self.last_anim {
            log(&format!("anim {} -> {}", self.last_anim, anim));
            self.last_anim = anim;
        }

        // For picking spots in game, and for the test helper that walks the character around.
        if self.settings.write_position && self.frames_in_world % 6 == 0 {
            let p = &player.block_position;
            let _ = std::fs::write(home().join("position.txt"), format!("{:.3} {:.3} {:.3} {:.3} {anim}", p.x, p.y, p.z, p.yaw));
        }

        self.count_kills();
        self.tick_buff();
        menu::keep_alive();
        self.handle_menu();
        self.tick_announcements();
        if self.frames_in_world % 30 == 0 {
            menu::status(self.buff.minutes_left(), self.last_sit_kills);
        }

        if self.settings.test_keys && input::is_key_pressed(KEY_F10) {
            self.kills += 5;
            log(&format!("F10: 5 test kills added; kills now {}", self.kills));
        }
        // Does an effect this DLL puts on show its icon? 3515 is the Strength-knot Crystal Tear
        // (+10 Strength for 3 minutes), which has the buff's default icon in the game's own data.
        if self.settings.test_keys && input::is_key_pressed(KEY_F9) {
            if let Some(player) = unsafe { WorldChrMan::instance_mut() }.ok().and_then(|w| w.main_player.as_mut()) {
                // Synced this time: with "don't sync" (what the buff uses) no icon showed in testing.
                player.apply_speffect(3515, false);
                log("F9: put the game's own effect 3515 on (synced), to see whether its icon shows");
            }
        }
        if self.settings.test_keys && input::is_key_pressed(KEY_F7) {
            log("F7: counting this as a sit");
            self.report_sit();
        }

        self.watch_sit(anim);
        self.check_seat();
    }

    /// Runs once the player has settled after a load: picks up this character's file.
    fn enter_world(&mut self) {
        self.settings = read_settings();
        self.buff.set_icon(self.settings.buff_icon);
        self.reset_until = Some(Instant::now() + RESET_WINDOW);
        self.items_modified = items::modified();
        self.items = items::load();
        let Some(key) = character_key() else {
            log("in world, but the character couldn't be identified; items and buff are off");
            return;
        };
        if self.store.as_ref().map(|s| &s.key) != Some(&key) {
            self.save();
            let mut s = Store::load(key);
            // 0.8 and earlier stored equipped item ids; now it's tracks.
            for e in s.equipped.iter_mut() {
                if let Some(i) = self.items.iter().find(|i| i.id == *e) {
                    *e = i.track.clone();
                }
            }
            // Drop ids from older test builds that match no item or track any more.
            // Skipped if grace_items.txt didn't load, so a broken file can't wipe a character.
            let before = s.equipped.len();
            if !self.items.is_empty() {
                s.equipped.retain(|t| self.items.iter().any(|i| i.track == *t));
                s.owned.retain(|id| self.items.iter().any(|i| i.id == *id));
            }
            if s.equipped.len() != before {
                log(&format!("dropped {} equipped entries that match no item", before - s.equipped.len()));
            }
            s.equipped.dedup();
            self.kills = s.kills;
            self.buff.resume(s.buff.take());
            log(&format!("character {}: {} items owned, equipped [{}], {} kills unbanked", s.key, s.owned.len(), s.equipped.join(", "), s.kills));
            self.store = Some(s);
        }
        if self.settings.grant_all {
            let ids: Vec<String> = self.items.iter().map(|i| i.id.clone()).collect();
            if let Some(s) = self.store.as_mut() {
                s.owned.extend(ids);
            }
            log("GrantAll: every item owned");
        }
        self.check_discovered();
        self.save();
        self.show_menu();
        menu::announce(self.next_announcement());
        menu::status(self.buff.minutes_left(), self.last_sit_kills);
    }

    /// The item a character uses from a track: the highest tier it owns.
    fn current(&self, track: &str) -> Option<&Item> {
        let s = self.store.as_ref()?;
        self.items.iter().filter(|i| i.track == track && s.owned.contains(&i.id)).max_by_key(|i| i.tier)
    }

    /// Mirrors the camp menu's lines into its flags: each track shows only its current item, lit
    /// while the track is equipped.
    fn show_menu(&self) {
        let Some(s) = &self.store else { return };
        let shown = |n: usize| {
            self.items.iter().find(|i| i.index == n).and_then(|i| self.current(&i.track)).is_some_and(|c| c.index == n)
        };
        let lit = |n: usize| shown(n) && self.items.iter().find(|i| i.index == n).is_some_and(|i| s.equipped.contains(&i.track));
        menu::show(shown, lit);
    }

    /// Acts on an item chosen in the camp menu: unequips its track if it's equipped; otherwise
    /// equips it, replacing the seat, or the keepsake equipped first when both slots are full.
    fn handle_menu(&mut self) {
        let Some(index) = menu::take_request() else { return };
        let item = self.items.iter().find(|i| i.index == index).cloned();
        let owned = item.as_ref().is_some_and(|i| self.current(&i.track).is_some());
        match item {
            Some(item) if owned && self.store.is_some() => {
                let same_kind: Vec<String> = self
                    .store
                    .as_ref()
                    .map(|s| s.equipped.iter().filter(|t| self.items.iter().any(|i| &i.track == *t && i.kind == item.kind)).cloned().collect())
                    .unwrap_or_default();
                let s = self.store.as_mut().expect("checked above");
                if let Some(pos) = s.equipped.iter().position(|t| *t == item.track) {
                    s.equipped.remove(pos);
                    log(&format!("menu: unequipped {}", item.name));
                } else {
                    let limit = if item.kind == Kind::Seat { 1 } else { MAX_KEEPSAKES };
                    if same_kind.len() >= limit {
                        s.equipped.retain(|t| *t != same_kind[0]);
                        log(&format!("menu: {} taken off to make room", same_kind[0]));
                    }
                    s.equipped.push(item.track.clone());
                    log(&format!("menu: equipped {}", item.name));
                }
                self.save();
            }
            _ => log(&format!("menu: item {index} isn't owned; ignored")),
        }
        self.show_menu();
        menu::finish_request();
    }

    fn save(&mut self) {
        let snapshot = self.buff.snapshot();
        if let Some(s) = self.store.as_mut() {
            s.kills = self.kills;
            s.buff = snapshot;
            s.save();
        }
    }

    /// The current item of each equipped track of this kind.
    fn equipped(&self, kind: Kind) -> Vec<&Item> {
        let Some(s) = &self.store else { return Vec::new() };
        s.equipped.iter().filter_map(|t| self.current(t)).filter(|i| i.kind == kind).collect()
    }

    fn watch_sit(&mut self, anim: i32) {
        let now = Instant::now();
        if self.sitting && anim == self.settings.rest_anim {
            self.last_rest = Some(now);
        }
        let in_range = (self.settings.sit_min..=self.settings.sit_max).contains(&anim) || anim == self.settings.rest_anim;
        let entered = in_range && !self.was_in_range;
        // Standing up on a seat would leave the player on top of it, where the game won't let them sit
        // again, and the seat holds them up if they're only lowered. So they step off it, toward the
        // grace, where there's always open floor. Skipped if the seat pushed them aside instead.
        if !in_range && self.was_in_range {
            if let (Some(floor), Some((_, y, _, yaw))) = (self.seat_floor.take(), player_position()) {
                if y > floor + 0.2 {
                    let dir = yaw + self.settings.step_off_turn.to_radians();
                    let d = self.settings.step_off;
                    move_player(-dir.sin() * d, floor - y, -dir.cos() * d);
                    log(&format!("stepped the player {d:.2} m off the seat, down {:.2} m", y - floor));
                } else {
                    log("the player wasn't on the seat; left where they are");
                }
            }
        }
        self.was_in_range = in_range;
        if in_range {
            self.frames_out_of_range = 0;
            // Standing up and sitting straight back down is a new sit (and a new rest), even
            // before the stand has lasted long enough to log.
            if !self.sitting || entered {
                if self.sitting {
                    log("stood up and sat straight back down");
                }
                self.sitting = true;
                log(&format!("sit detected from anim {anim}"));
                self.report_sit();
                self.last_rest = None;
                self.pending_spawn = Some(now);
                self.reset_until = Some(now + RESET_WINDOW);
            }
            if let Some(began) = self.pending_spawn {
                let rest_over = self.last_rest.is_some_and(|t| now - t >= AFTER_REST);
                if rest_over || now - began >= SPAWN_ANYWAY {
                    self.pending_spawn = None;
                    log(&format!("{} ms into the sit; rest seen: {}", (now - began).as_millis(), self.last_rest.is_some()));
                    // After the rest, which wipes props, and once the sit has walked the player up.
                    self.spawn_props();
                }
            }
        } else if self.sitting {
            self.frames_out_of_range += 1;
            if self.frames_out_of_range >= STAND_FRAMES {
                self.sitting = false;
                self.pending_spawn = None;
                log(&format!("stood up (anim {anim})"));
            }
        }
    }

    /// Gives every item whose grace the character has discovered (the grace's unlock flag), so
    /// graces found before the mod count too. A track new to the character fills an empty slot.
    fn check_discovered(&mut self) {
        let Some(s) = &self.store else { return };
        let Some(params) = (unsafe { SoloParamRepository::instance() }).ok() else { return };
        let discovered = |grace: u32| {
            params
                .get_by_bonfire_warp_param_by_entity_id(grace)
                .is_some_and(|row| row.eventflag_id() != 0 && menu::flag_on(row.eventflag_id()))
        };
        // A character new to the mod gets one pop-up for everything already discovered.
        let fresh = s.fresh;
        let new: Vec<Item> = self
            .items
            .iter()
            .filter(|i| !s.owned.contains(&i.id) && i.found.iter().any(|&g| discovered(g)))
            .cloned()
            .collect();
        if new.is_empty() {
            if let Some(s) = self.store.as_mut() {
                s.fresh = false;
            }
            return;
        }
        for item in new {
            let had_track = self.current(&item.track).is_some();
            let seats = self.equipped(Kind::Seat).len();
            let keepsakes = self.equipped(Kind::Keepsake).len();
            let s = self.store.as_mut().expect("checked above");
            s.owned.insert(item.id.clone());
            if !fresh {
                s.announce.push(item.id.clone());
            }
            log(&format!("FOUND: {} ({})", item.name, item.id));
            let room = match item.kind {
                Kind::Seat => seats < 1,
                Kind::Keepsake => keepsakes < MAX_KEEPSAKES,
            };
            if !had_track && room {
                s.equipped.push(item.track.clone());
                log("  equipped it in an empty slot");
            }
        }
        if let Some(s) = self.store.as_mut() {
            if fresh {
                s.announce.push(WELCOME.into());
            }
            s.fresh = false;
        }
        self.save();
        self.show_menu();
        menu::announce(self.next_announcement());
    }

    /// The pop-up the Camp menu should show next: the oldest queued item that is still its
    /// track's current one (an upgrade found since makes the older pop-up pointless).
    fn next_announcement(&mut self) -> Option<usize> {
        loop {
            let id = self.store.as_ref()?.announce.first()?.clone();
            if id == WELCOME {
                return Some(menu::WELCOME_INDEX);
            }
            let still_current = self.items.iter().find(|i| i.id == id).and_then(|i| self.current(&i.track)).filter(|c| c.id == id).map(|c| c.index);
            match still_current {
                Some(index) if index < menu::MAX_ITEMS => return Some(index),
                _ => {
                    log(&format!("pop-up for {id} skipped: no longer the track's current item"));
                    self.store.as_mut()?.announce.remove(0);
                }
            }
        }
    }

    /// Once the menu has shown a pop-up, drops it from the queue and puts up the next.
    fn tick_announcements(&mut self) {
        if !menu::acknowledged() {
            return;
        }
        if let Some(s) = self.store.as_mut() {
            if !s.announce.is_empty() {
                let shown = s.announce.remove(0);
                log(&format!("pop-up shown: {shown}"));
            }
        }
        self.save();
        menu::announce(self.next_announcement());
        menu::finish_ack();
    }

    fn tick_buff
(&mut self) {
        let Some(wcm) = (unsafe { WorldChrMan::instance_mut() }).ok() else { return };
        let Some(player) = wcm.main_player.as_mut() else { return };
        let dead = player.chr_ins.modules.data.hp <= 0;
        let died = dead && !self.dead;
        if died {
            log(&format!("player died with {} kills not yet banked", self.kills));
            if !self.settings.keep_kills_on_death {
                self.kills = 0;
                self.touched.clear();
            }
        }
        self.dead = dead;
        self.buff.tick(player);
        if died {
            self.save();
        }
    }

    fn report_sit(&mut self) {
        let keepsakes: Vec<Keepsake> = self
            .equipped(Kind::Keepsake)
            .iter()
            .filter_map(|i| Some(Keepsake { stat: i.stat?, max: i.max, kills_per_point: i.kills_per_point }))
            .collect();
        match self.equipped(Kind::Seat).first() {
            Some(seat) => self.buff.offer(Grant::earned(&keepsakes, self.kills, seat.minutes)),
            None => log("buff: no seat equipped, so no buff"),
        }
        log(&format!("buff now: {}", self.buff.status()));
        log(&format!(
            "SIT: {} kills since the last sit, {} other deaths, {} reset deaths ignored; all reset",
            self.kills, self.other_deaths, self.ignored_resets
        ));
        self.last_sit_kills = self.kills;
        self.kills = 0;
        self.other_deaths = 0;
        self.ignored_resets = 0;
        self.touched.clear();
        // Resting removes the props from the last sit.
        self.spawns = 0;
        self.save();
    }

    /// Watches nearby characters for HP reaching zero and works out who killed them.
    fn count_kills(&mut self) {
        let Some(wcm) = (unsafe { WorldChrMan::instance() }).ok() else { return };
        let Some(player) = wcm.main_player.as_ref() else { return };
        let me = player.chr_ins.field_ins_handle;
        let resetting = self.reset_until.is_some_and(|t| Instant::now() < t);
        for r in &mut self.rechecks {
            r.frames += 1;
        }
        for entry in wcm.chr_inses_by_distance.iter() {
            let chr = unsafe { entry.chr_ins.as_ref() };
            let handle = chr.field_ins_handle;
            if handle == me {
                continue;
            }
            if chr.last_hit_by == me {
                self.touched.insert(handle);
            }
            if let Some(i) = self.rechecks.iter().position(|r| r.handle == handle && r.frames >= RECHECK_FRAMES) {
                self.rechecks.swap_remove(i);
                let credited = chr.last_hit_by == me;
                if credited {
                    self.kills += 1;
                    self.other_deaths = self.other_deaths.saturating_sub(1);
                }
                log(&format!("recheck: character {} credited to player: {credited}; kills now {}", chr.character_id, self.kills));
            }
            let hp = chr.modules.data.hp;
            let before = self.hp.insert(handle, hp);
            if !before.is_some_and(|b| b > 0) || hp > 0 {
                continue;
            }
            if resetting {
                self.ignored_resets += 1;
                continue;
            }
            let mine = chr.last_hit_by == me;
            let touched = self.touched.remove(&handle);
            // Enemies killed by other enemies, falls and the like count if they die near the
            // player. The player's own spirit ashes and Torrent dying must not.
            // No fight goes on while the player sits at a grace, so deaths then that aren't the
            // player's are the rest's resets.
            if self.sitting && !mine && !touched {
                self.ignored_resets += 1;
                log(&format!("death while sitting: character {} ignored as a reset", chr.character_id));
                continue;
            }
            let summon = wcm.summon_buddy_chr_set.characters().any(|c| c.field_ins_handle == handle);
            let nearby = !summon && entry.distance.abs() <= NEARBY_METERS;
            if mine || touched || nearby {
                self.kills += 1;
            } else {
                self.other_deaths += 1;
                self.rechecks.push(Recheck { handle, frames: 0 });
            }
            log(&format!(
                "death: character {} npc {} at {:.1}m; by player: {mine}, hit earlier: {touched}, near: {nearby}, summon: {summon}; kills now {}",
                chr.character_id,
                chr.npc_param_id,
                entry.distance,
                self.kills
            ));
        }
        self.rechecks.retain(|r| r.frames < RECHECK_FRAMES * 4);
    }

    /// The seat where the player sits, a keepsake a pace to their right and another to their left.
    fn spawn_props(&mut self) {
        let Some((x, y, z, yaw)) = player_position() else { return };
        log(&format!("player sitting at ({x:.2}, {y:.2}, {z:.2}) yaw {yaw:.3}"));
        let (right_x, right_z) = (yaw.cos(), -yaw.sin());
        let mut props: Vec<(Item, f32, f32)> = Vec::new();
        if let Some(seat) = self.equipped(Kind::Seat).first() {
            props.push(((*seat).clone(), x, z));
        }
        for (n, k) in self.equipped(Kind::Keepsake).iter().enumerate() {
            let side = if n == 0 { 0.9 } else { -0.9 };
            props.push(((*k).clone(), x + right_x * side, z + right_z * side));
        }
        let mut lift = 0.0;
        for (item, px, pz) in props {
            // Lift only onto a seat that was actually built.
            // A sitting player faces the grace: (-sin, -cos) of their yaw.
            let (fx, fz) = (-yaw.sin(), -yaw.cos());
            let px = px + right_x * item.right + fx * item.forward;
            let pz = pz + right_z * item.right + fz * item.forward;
            if self.spawn(&item.model, item.scale, px, y, pz, yaw + item.turn.to_radians()) && item.kind == Kind::Seat {
                lift = item.lift;
            }
        }
        // A seat spawned around a sitting player pushes them out sideways. Raising them to its top
        // first leaves them sitting on it.
        if lift > 0.0 {
            move_player(0.0, lift, 0.0);
            self.seat_floor = Some(y);
            log(&format!("lifted the player {lift:.2} m onto the seat"));
            self.seat_check = Some((Instant::now() + Duration::from_millis(1500), y));
        }
    }

    /// Logs where the player settled after being lifted onto a seat, to tune each seat's lift.
    fn check_seat(&mut self) {
        let Some((when, floor)) = self.seat_check else { return };
        if Instant::now() < when {
            return;
        }
        self.seat_check = None;
        if let Some((x, y, z, _)) = player_position() {
            log(&format!("settled at ({x:.2}, {y:.2}, {z:.2}): {:.2} m above the floor", y - floor));
        }
    }

    fn spawn(&mut self, asset: &str, scale: f32, x: f32, y: f32, z: f32, yaw: f32) -> bool {
        if self.spawns >= MAX_SPAWNS {
            log(&format!("spawn limit of {MAX_SPAWNS} reached; {asset} skipped"));
            return false;
        }
        let Some(wcm) = (unsafe { WorldChrMan::instance() }).ok() else { return false };
        let Some(player) = wcm.main_player.as_ref() else { return false };
        let block = player.chr_ins.block_id();
        let Some(data) = (unsafe { CSWorldGeomMan::instance_mut() }).ok().and_then(|g| g.geom_block_data_by_id_mut(&block)) else {
            log(&format!("no geometry block loaded for {block}; {asset} skipped"));
            return false;
        };
        let restore = self.settings.no_collision.then(|| clear_collision(asset)).flatten();
        let spawned = data.spawn_geometry(
            asset,
            &GeometrySpawnParameters {
                position: BlockPosition { x, y, z, yaw: 0.0 },
                rot_x: 0.0,
                rot_y: yaw.to_degrees() + self.settings.turn,
                rot_z: 0.0,
                scale_x: scale,
                scale_y: scale,
                scale_z: scale,
            },
        );
        if let Some(original) = restore {
            restore_collision(original);
        }
        self.spawns += 1;
        match spawned {
            Some(_) => {
                log(&format!("spawned {asset} at ({x:.2}, {y:.2}, {z:.2}) scale {scale}; {} of {MAX_SPAWNS}", self.spawns));
                true
            }
            None => {
                log(&format!("spawn of {asset} returned nothing"));
                false
            }
        }
    }
}

/// The loaded character's save slot and name.
fn character_key() -> Option<String> {
    let slot = unsafe { GameMan::instance() }.ok()?.save_slot;
    let wcm = unsafe { WorldChrMan::instance() }.ok()?;
    let data = unsafe { wcm.main_player.as_ref()?.player_game_data.as_ref() };
    if data.character_name[0] == 0 {
        return None;
    }
    Some(store::character_key(slot, &data.character_name))
}

/// Models keep their collision settings in a param row that's read as a prop is built, so a prop can
/// be built without collision by changing the row for that moment. Copies of the model placed in
/// the world are built on map load, so they keep theirs. Returns the row and its original values.
fn clear_collision(asset: &str) -> Option<(u32, i8, u8)> {
    let group: u32 = asset.get(3..6)?.parse().ok()?;
    let index: u32 = asset.get(7..10)?.parse().ok()?;
    let id = group * 1000 + index;
    let params = unsafe { SoloParamRepository::instance_mut() }.ok()?;
    let row = params.get_mut::<AssetEnvironmentGeometryParam>(id)?;
    let original = (id, row.hit_create_type(), row.collision_type());
    row.set_hit_create_type(HIT_NONE);
    row.set_collision_type(COLLISION_MISSES_PLAYER_AND_CAMERA);
    Some(original)
}

fn restore_collision((id, hit, collision): (u32, i8, u8)) {
    let Some(params) = (unsafe { SoloParamRepository::instance_mut() }).ok() else { return };
    if let Some(row) = params.get_mut::<AssetEnvironmentGeometryParam>(id) {
        row.set_hit_create_type(hit);
        row.set_collision_type(collision);
    }
}

/// Moves the player by this many meters, moving their collision with them.
fn move_player(dx: f32, dy: f32, dz: f32) {
    let Ok(wcm) = (unsafe { WorldChrMan::instance_mut() }) else { return };
    let Some(player) = wcm.main_player.as_mut() else { return };
    let physics = &mut player.chr_ins.modules.physics;
    physics.position.0 += dx;
    physics.position.1 += dy;
    physics.position.2 += dz;
    physics.chr_proxy_pos_update_requested = true;
}

fn player_position() -> Option<(f32, f32, f32, f32)> {
    let wcm = unsafe { WorldChrMan::instance() }.ok()?;
    let p = &wcm.main_player.as_ref()?.block_position;
    Some((p.x, p.y, p.z, p.yaw))
}
