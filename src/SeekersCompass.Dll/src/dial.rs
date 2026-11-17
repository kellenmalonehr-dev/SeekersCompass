//! Damage Dial: `DamageTaken = 50` under [DamageDial] in seekers_compass.ini means half damage taken,
//! and `DamageDealt = 50` means the player's own hits do half damage.
//!
//! Nothing on disk changes. In memory, this borrows SpEffectParam row 7280, an unused area-scaling
//! row, makes it neutral, sets its damage cut rates (which multiply calculated damage on the
//! defender's side) and its damage-to-enemies rates (which multiply the holder's hits on enemies
//! but not on other players), and keeps that effect on the local player. Row 7260 carries the
//! damage dealt alone and is kept on the player's spirit summons. The bleed and frostbite bursts
//! are a share of the target's HP, so for those the rows the player's attacks apply are scaled.
//!
//! Bleed and frostbite use the same row's damage rates, which also leave buildup alone. Poison and
//! rot have no such rate, so their tick damage is scaled directly on the rows enemies use. Those
//! rows hold the buildup in separate fields, and player weapons, items and spells use other rows.
//!
//! The ini is checked once a second so 100 and 50 can be compared in one session. Every drop in the
//! player's HP is logged to seekers_compass.log so the result can be measured.


use std::collections::HashMap;

use eldenring::cs::{CSTaskGroupIndex, CSTaskImp, ChrInsExt, SoloParamRepository, SpEffectParam, WorldChrMan};
use eldenring::param::SP_EFFECT_PARAM_ST;
use fromsoftware_shared::FromStatic;

/// Unused in vanilla and Convergence: no param or event script refers to it.
const ROW: i32 = 7280;
/// Frames between checks of the ini, the row and the effect.
const CHECK_EVERY: u32 = 60;
/// Frames to wait after the player appears, so nothing is applied mid-load.
const SETTLE: u32 = 30;

/// SpEffect state types of the rows that deal poison and rot ticks.
const POISON: u16 = 2;
const ROT: u16 = 5;
/// And of the rows that deal the bleed and frostbite bursts.
const BLEED: u16 = 6;
const FROST: u16 = 260;
/// A second unused row of the same kind as 7280, kept on the player's spirit summons.
const SUMMON_ROW: i32 = 7260;

/// Rows that deal poison or rot ticks to the player: enemy attacks, Malenia, and swamps.
fn enemy_tick_rows() -> impl Iterator<Item = u32> {
    (20000..=20999)
        .chain(21000..=21999)
        .chain(18430..=18433)
        .chain([4002, 4004, 4051, 4053, 4055, 20020100])
}

/// Rows that deal the bleed or frostbite burst when the player's attacks fill the bar: weapons and
/// greases (6400s, 6700s), weapons by upgrade level (105000s, 107500s), and spells, skills and
/// thrown items (below 4000). Enemies' attacks use 22000s, 24000s and rows in the millions.
fn player_proc_rows() -> impl Iterator<Item = u32> {
    (0..4000).chain(6400..=6415).chain(6700..=6705).chain(105000..=105999).chain(107500..=107999)
}

fn log(text: &str) {
    crate::log("dial", text);
}

pub fn start(tasks: &CSTaskImp) -> Result<(), String> {
    crate::run(tasks, "dial", CSTaskGroupIndex::FrameBegin, State::default(), State::tick);
    Ok(())
}

#[derive(Clone, Copy, PartialEq, Debug)]
struct Settings {
    /// Percent of normal damage, 1 to 1000.
    damage_taken: u32,
    /// Percent of normal damage dealt by the player's own hits, 1 to 1000.
    damage_dealt: u32,
    /// Whether the game shares the effect with other players. Off by default: a player without
    /// the mod would see row 7280's original values on this character.
    sync: bool,
}

fn read_settings() -> Settings {
    let mut s = Settings { damage_taken: 100, damage_dealt: 100, sync: false };
    for (key, value) in crate::ini("DamageDial") {
        let Ok(value) = value.parse::<u32>() else { continue };
        match key.as_str() {
            "damagetaken" => s.damage_taken = value.clamp(1, 1000),
            "damagedealt" => s.damage_dealt = value.clamp(1, 1000),
            "sync" => s.sync = value != 0,
            _ => {}
        }
    }
    s
}

#[derive(Default)]
struct State {
    settings: Option<Settings>,
    /// The sync mode the effect was last applied with; None until applied in this load.
    applied_sync: Option<bool>,
    frames_in_world: u32,
    last_hp: Option<i32>,
    row_refused: bool,
    tick_base: HashMap<u32, (f32, i32)>,
    /// The DamageTaken poison and rot ticks were last scaled to.
    ticks_percent: Option<u32>,
    proc_base: HashMap<u32, (f32, i32)>,
    /// The DamageDealt the player's bleed and frostbite rows were last scaled to.
    procs_percent: Option<u32>,
}

/// Row 7280 is written for enemies, with 5.8 times the HP among other things. Everything that
/// differs from a neutral row goes back to neutral, in both vanilla and Convergence.
fn neutralize(row: &mut SP_EFFECT_PARAM_ST) {
    row.set_max_hp_rate(1.0);
    row.set_max_stamina_rate(1.0);
    row.set_physics_attack_power_rate(1.0);
    row.set_magic_attack_power_rate(1.0);
    row.set_fire_attack_power_rate(1.0);
    row.set_thunder_attack_power_rate(1.0);
    row.set_dark_attack_power_rate(1.0);
    row.set_physics_diffence_rate(1.0);
    row.set_magic_diffence_rate(1.0);
    row.set_fire_diffence_rate(1.0);
    row.set_thunder_diffence_rate(1.0);
    row.set_dark_diffence_rate(1.0);
    row.set_stamina_attack_rate(1.0);
    row.set_regist_poizon_change_rate(1.0);
    row.set_regist_disease_change_rate(1.0);
    row.set_regist_blood_change_rate(1.0);
    row.set_regist_freeze_change_rate(1.0);
    row.set_regist_sleep_change_rate(1.0);
    row.set_regist_madness_change_rate(1.0);
    row.set_mag_param_change(false);
    row.set_miracle_param_change(false);
    row.set_effect_target_oppose_target(false);
    row.set_effect_target_friendly_target(false);
}

fn set_cut(row: &mut SP_EFFECT_PARAM_ST, rate: f32) {
    row.set_neutral_damage_cut_rate(rate);
    row.set_slash_damage_cut_rate(rate);
    row.set_blow_damage_cut_rate(rate);
    row.set_thrust_damage_cut_rate(rate);
    row.set_magic_damage_cut_rate(rate);
    row.set_fire_damage_cut_rate(rate);
    row.set_thunder_damage_cut_rate(rate);
    row.set_dark_damage_cut_rate(rate);
    // Percent, stored in one byte, so bleed and frostbite stop at 255.
    let status = (rate * 100.0).round().clamp(0.0, 255.0) as u8;
    row.set_blood_damage_rate(status);
    row.set_freeze_damage_rate(status);
}

/// These rates multiply the damage the holder's hits do to enemies, per damage type, and leave
/// hits on other players alone (those have their own fields, which stay at 1). The game's own
/// effects use them the same way (172 rows). The last one scales stance damage.
fn set_dealt(row: &mut SP_EFFECT_PARAM_ST, rate: f32) {
    row.set_atk_enemy_dmg_correct_rate_physics(rate);
    row.set_atk_enemy_dmg_correct_rate_magic(rate);
    row.set_atk_enemy_dmg_correct_rate_fire(rate);
    row.set_atk_enemy_dmg_correct_rate_thunder(rate);
    row.set_atk_enemy_dmg_correct_rate_dark(rate);
    row.set_sa_attack_power_rate(rate);
}

/// Scales the HP loss of status rows from their original values, which are kept in `base`.
fn scale_rows(ids: impl Iterator<Item = u32>, states: &[u16], rate: f32, base: &mut HashMap<u32, (f32, i32)>) -> Result<usize, String> {
    let params = unsafe { SoloParamRepository::instance_mut() }.map_err(|e| format!("params not ready: {e:?}"))?;
    let mut count = 0;
    for id in ids {
        let Some(row) = params.get_mut::<SpEffectParam>(id) else { continue };
        if !states.contains(&row.state_info()) {
            continue;
        }
        let (hp_rate, hp_point) = *base.entry(id).or_insert((row.change_hp_rate(), row.change_hp_point()));
        if hp_rate == 0.0 && hp_point == 0 {
            continue;
        }
        row.set_change_hp_rate(hp_rate * rate);
        row.set_change_hp_point((hp_point as f32 * rate).round() as i32);
        count += 1;
    }
    Ok(count)
}

/// Makes the row ours. Refuses if it holds anything other than the values we expect, because
/// another mod is then using it and it must be left alone.
fn prepare_row(taken: f32, dealt: f32) -> Result<(), String> {
    let params = unsafe { SoloParamRepository::instance_mut() }.map_err(|e| format!("params not ready: {e:?}"))?;
    let row = claim(params.get_mut::<SpEffectParam>(ROW as u32), ROW)?;
    set_cut(row, taken);
    set_dealt(row, dealt);
    // The summons' row carries only the damage dealt, so they take damage as they always do.
    let row = claim(params.get_mut::<SpEffectParam>(SUMMON_ROW as u32), SUMMON_ROW)?;
    set_dealt(row, dealt);
    Ok(())
}

/// The borrowed rows (7210 to 7280) are area-scaling rows for enemies (HP rate 5.3 to 5.8, attack rate 2.6 to 2.7).
pub(crate) fn claim(row: Option<&mut SP_EFFECT_PARAM_ST>, id: i32) -> Result<&mut SP_EFFECT_PARAM_ST, String> {
    let row = row.ok_or(format!("row {id} doesn't exist"))?;
    let hp = row.max_hp_rate();
    let atk = row.physics_attack_power_rate();
    if (5.2..5.9).contains(&hp) && (2.5..2.8).contains(&atk) {
        neutralize(row);
        log(&format!("row {id} made neutral"));
    } else if hp != 1.0 || atk != 1.0 {
        return Err(format!("row {id} holds unexpected values (HP rate {hp}, attack rate {atk})"));
    }
    Ok(row)
}

impl State {
    fn tick(&mut self) {
        let Ok(world) = (unsafe { WorldChrMan::instance_mut() }) else { return };
        let Some(player) = world.main_player.as_mut() else {
            self.frames_in_world = 0;
            self.last_hp = None;
            self.applied_sync = None;
            return;
        };
        self.frames_in_world = self.frames_in_world.saturating_add(1);
        if self.frames_in_world < SETTLE {
            return;
        }

        let hp = player.chr_ins.modules.data.hp;
        if let Some(last) = self.last_hp
            && hp < last
        {
            let on = player.chr_ins.special_effect.entries().any(|e| e.param_id == ROW);
            let percent = self.settings.map(|s| s.damage_taken).unwrap_or(0);
            log(&format!("hit: HP {last} -> {hp} (-{}), DamageTaken={percent}, effect {}", last - hp, if on { "on" } else { "OFF" }));
        }
        self.last_hp = Some(hp);

        // At once when the wait ends, then at every check.
        if self.frames_in_world != SETTLE && self.frames_in_world % CHECK_EVERY != 0 {
            return;
        }
        let settings = read_settings();
        if self.settings != Some(settings) {
            log(&format!("settings: DamageTaken={} DamageDealt={} Sync={}", settings.damage_taken, settings.damage_dealt, settings.sync as u8));
            self.settings = Some(settings);
        }
        if let Err(e) = prepare_row(settings.damage_taken as f32 / 100.0, settings.damage_dealt as f32 / 100.0) {
            if !self.row_refused {
                log(&format!("{e}; effect not applied"));
                self.row_refused = true;
            }
            return;
        }
        self.row_refused = false;

        if self.ticks_percent != Some(settings.damage_taken) {
            match scale_rows(enemy_tick_rows(), &[POISON, ROT], settings.damage_taken as f32 / 100.0, &mut self.tick_base) {
                Ok(count) => {
                    log(&format!("poison and rot ticks scaled on {count} rows"));
                    self.ticks_percent = Some(settings.damage_taken);
                }
                Err(e) => log(&e),
            }
        }
        if self.procs_percent != Some(settings.damage_dealt) {
            match scale_rows(player_proc_rows(), &[BLEED, FROST], settings.damage_dealt as f32 / 100.0, &mut self.proc_base) {
                Ok(count) => {
                    log(&format!("bleed and frostbite damage of the player's attacks scaled on {count} rows"));
                    self.procs_percent = Some(settings.damage_dealt);
                }
                Err(e) => log(&e),
            }
        }

        // Dying or travelling can clear the effect, so this runs again whenever it's missing.
        let on = player.chr_ins.special_effect.entries().any(|e| e.param_id == ROW);
        if !on || self.applied_sync != Some(settings.sync) {
            if on {
                player.remove_speffect(ROW);
            }
            player.apply_speffect(ROW, !settings.sync);
            self.applied_sync = Some(settings.sync);
            log(&format!("effect applied (Sync={})", settings.sync as u8));
        }

        // Spirit summons get their own row, each time one appears.
        let mut summons = 0;
        for chr in world.summon_buddy_chr_set.characters() {
            if !chr.special_effect.entries().any(|e| e.param_id == SUMMON_ROW) {
                chr.apply_speffect(SUMMON_ROW, true);
                log(&format!("summon list: character {} npc {} gets the DamageDealt effect", chr.character_id, chr.npc_param_id));
                summons += 1;
            }
        }
        if summons > 0 {
            log(&format!("DamageDealt effect applied to {summons} summoned spirit(s)"));
        }
    }
}
