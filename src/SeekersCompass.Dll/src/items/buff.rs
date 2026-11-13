//! The rest buff. Equipped keepsakes set what it raises and how far; kills since the last sit
//! unlock that strength; the seat sets how long it lasts.
//!
//! Nothing on disk changes. In memory this borrows SpEffectParam row 7270, an unused area-scaling
//! row like the 7280 that Damage Dial borrows (no param or event script in vanilla or Convergence
//! refers to either), makes it neutral, writes the buff's stat bonuses into it and keeps it on the
//! player until the time runs out or the player dies.

use std::time::{Duration, Instant};

use eldenring::cs::{ChrInsExt, PlayerIns, SoloParamRepository, SpEffectParam};
use eldenring::param::SP_EFFECT_PARAM_ST;
use fromsoftware_shared::FromStatic;

use super::log;

pub const ROW: i32 = 7270;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Stat {
    Vigor,
    Mind,
    Endurance,
    Strength,
    Dexterity,
    Intelligence,
    Faith,
    Arcane,
    /// Percent of max stamina.
    Stamina,
}

impl Stat {
    pub fn parse(name: &str) -> Option<Stat> {
        Some(match name.to_ascii_lowercase().as_str() {
            "vigor" => Stat::Vigor,
            "mind" => Stat::Mind,
            "endurance" => Stat::Endurance,
            "strength" => Stat::Strength,
            "dexterity" => Stat::Dexterity,
            "intelligence" => Stat::Intelligence,
            "faith" => Stat::Faith,
            "arcane" => Stat::Arcane,
            "stamina" => Stat::Stamina,
            _ => return None,
        })
    }
}

/// What an equipped keepsake contributes: the stat it raises, its full strength, and the kills
/// since the last sit that unlock each point.
#[derive(Clone, Copy, Debug)]
pub struct Keepsake {
    pub stat: Stat,
    pub max: u32,
    pub kills_per_point: u32,
}

/// One buff: what it adds and for how long.
#[derive(Clone, Debug)]
pub struct Grant {
    pub adds: Vec<(Stat, u32)>,
    /// All the points together, used to decide which of two buffs is stronger.
    pub total: u32,
    pub length: Duration,
}

impl Grant {
    /// Each keepsake gains a point for every `kills_per_point` kills, up to its full strength.
    pub fn earned(keepsakes: &[Keepsake], kills: u32, minutes: f32) -> Grant {
        let adds: Vec<(Stat, u32)> = keepsakes
            .iter()
            .map(|k| (k.stat, (kills / k.kills_per_point.max(1)).min(k.max)))
            .filter(|(_, p)| *p > 0)
            .collect();
        Self::from_parts(adds, (minutes.max(0.1) * 60.0) as u64)
    }

    pub fn from_parts(adds: Vec<(Stat, u32)>, length_secs: u64) -> Grant {
        let total = adds.iter().map(|(_, p)| p).sum();
        Grant { adds, total, length: Duration::from_secs(length_secs) }
    }

    fn amount(&self, stat: Stat) -> u32 {
        self.adds.iter().filter(|(s, _)| *s == stat).map(|(_, p)| p).sum()
    }

    pub fn describe(&self) -> String {
        let parts: Vec<String> = self
            .adds
            .iter()
            .map(|(s, p)| if *s == Stat::Stamina { format!("+{p}% stamina") } else { format!("+{p} {s:?}") })
            .collect();
        format!("{} for {} min", parts.join(", "), self.length.as_secs() / 60)
    }
}

struct Active {
    grant: Grant,
    until: Instant,
}

#[derive(Default)]
pub struct Buff {
    active: Option<Active>,
    /// The row holds the active grant's values.
    written: bool,
    row_refused: bool,
    frames: u32,
    /// Frames until the player's stats are logged; the game works them out a little after the
    /// effect goes on.
    log_stats_in: Option<u32>,
    /// The on-screen icon written into the row.
    icon: i32,
}

impl Buff {
    /// Sets the buff's icon; a running buff is put back on to show the change.
    pub fn set_icon(&mut self, icon: i32) {
        if icon != self.icon {
            self.icon = icon;
            self.written = false;
        }
    }

    /// Whole minutes left, rounded up; 0 when no buff runs.
    pub fn minutes_left(&self) -> u32 {
        self.active.as_ref().map_or(0, |a| a.until.saturating_duration_since(Instant::now()).as_secs().div_ceil(60) as u32)
    }

    /// The running buff and its seconds left, for the character's file.
    pub fn snapshot(&self) -> Option<(Grant, u64)> {
        self.active.as_ref().map(|a| (a.grant.clone(), a.until.saturating_duration_since(Instant::now()).as_secs()))
    }

    /// Picks up a buff saved for this character, or none. Time spent outside the game doesn't count.
    pub fn resume(&mut self, saved: Option<(Grant, u64)>) {
        self.active = saved.filter(|(_, left)| *left > 0).map(|(grant, left)| {
            log(&format!("buff: resumed {} with {left} s left", grant.describe()));
            Active { grant, until: Instant::now() + Duration::from_secs(left) }
        });
        self.written = false;
    }

    /// Called on every sit. The stronger buff wins: a stronger new one replaces the running one
    /// with a full timer, a weaker one leaves the running one and its timer alone.
    pub fn offer(&mut self, grant: Grant) {
        if grant.total == 0 {
            log("buff: nothing earned this sit");
            return;
        }
        if let Some(a) = &self.active {
            if grant.total <= a.grant.total {
                let left = a.until.saturating_duration_since(Instant::now()).as_secs();
                log(&format!("buff: kept the running one ({}, {left} s left); this sit earned {}", a.grant.describe(), grant.describe()));
                return;
            }
        }
        log(&format!("buff: new {}", grant.describe()));
        self.active = Some(Active { until: Instant::now() + grant.length, grant });
        self.written = false;
    }

    pub fn end(&mut self, player: &mut PlayerIns, why: &str) {
        if self.active.take().is_some() {
            player.remove_speffect(ROW);
            log(&format!("buff: ended ({why})"));
        }
    }
    /// Keeps the effect on the player while the buff lasts. Rests, deaths and loads can clear
    /// effects, so it goes back on whenever it's missing.
    pub fn tick(&mut self, player: &mut PlayerIns) {
        if let Some(n) = self.log_stats_in {
            if n == 0 {
                self.log_stats_in = None;
                log_stats(player);
            } else {
                self.log_stats_in = Some(n - 1);
            }
        }
        let Some(active) = &self.active else { return };
        if player.chr_ins.modules.data.hp <= 0 {
            self.end(player, "died");
            return;
        }
        if Instant::now() >= active.until {
            self.end(player, "time up");
            return;
        }
        self.frames += 1;
        if self.written && self.frames % 30 != 0 {
            return;
        }
        let on = player.chr_ins.special_effect.entries().any(|e| e.param_id == ROW);
        if self.written && on {
            return;
        }
        let grant = active.grant.clone();
        if !self.written {
            if let Err(e) = write_row(&grant, self.icon) {
                if !self.row_refused {
                    log(&format!("buff: {e}; not applied"));
                    self.row_refused = true;
                }
                return;
            }
            self.row_refused = false;
            if on {
                // Stats are worked out when the effect goes on, so it comes off and on again.
                player.remove_speffect(ROW);
            }
        }
        // Not synced: other players without the mod would see row 7270's original values.
        player.apply_speffect(ROW, true);
        let reason = if self.written { "it was missing" } else { "new values" };
        self.written = true;
        log(&format!("buff: effect applied ({reason})"));
        self.log_stats_in = Some(30);
    }

    pub fn status(&self) -> String {
        match &self.active {
            Some(a) => format!("{} ({} s left)", a.grant.describe(), a.until.saturating_duration_since(Instant::now()).as_secs()),
            None => "none".into(),
        }
    }
}

fn log_stats(player: &PlayerIns) {
    let data = unsafe { player.player_game_data.as_ref() };
    log(&format!(
        "stats (base -> with bonuses): STR {} -> {}, DEX {} -> {}, INT {} -> {}, FAI {} -> {}, ARC {} -> {}, max stamina {}",
        data.strength,
        data.effective_strength,
        data.dexterity,
        data.effective_dexterity,
        data.intelligence,
        data.effective_intelligence,
        data.faith,
        data.effective_faith,
        data.arcane,
        data.effective_arcane,
        data.current_max_stamina
    ));
}

/// Row 7270 is written for enemies, with 5.8 times the HP among other things. Everything that
/// differs from a neutral row goes back to neutral, as Damage Dial does for 7280.
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

/// Makes the row ours and writes the grant into it. Refuses if the row holds anything other than
/// the vanilla area-scaling values or our own, because another mod is then using it.
fn write_row(grant: &Grant, icon: i32) -> Result<(), String> {
    let params = unsafe { SoloParamRepository::instance_mut() }.map_err(|e| format!("params not ready: {e:?}"))?;
    let row = params.get_mut::<SpEffectParam>(ROW as u32).ok_or("row 7270 doesn't exist")?;
    let hp = row.max_hp_rate();
    let atk = row.physics_attack_power_rate();
    if (5.7..5.9).contains(&hp) && (2.7..2.8).contains(&atk) {
        neutralize(row);
        log("buff: row 7270 made neutral");
    } else if hp != 1.0 || atk != 1.0 {
        return Err(format!("row 7270 holds unexpected values (HP rate {hp}, attack rate {atk})"));
    }
    let clamp = |p: u32| p.min(99) as i8;
    row.set_add_life_force_status(clamp(grant.amount(Stat::Vigor)));
    row.set_add_willpower_status(clamp(grant.amount(Stat::Mind)));
    row.set_add_endure_status(clamp(grant.amount(Stat::Endurance)));
    row.set_add_strength_status(clamp(grant.amount(Stat::Strength)));
    row.set_add_dexterity_status(clamp(grant.amount(Stat::Dexterity)));
    row.set_add_magic_status(clamp(grant.amount(Stat::Intelligence)));
    row.set_add_faith_status(clamp(grant.amount(Stat::Faith)));
    row.set_add_luck_status(clamp(grant.amount(Stat::Arcane)));
    row.set_max_stamina_rate(1.0 + grant.amount(Stat::Stamina) as f32 / 100.0);
    // The DLL keeps time itself, so the game never ends the effect on its own.
    row.set_effect_endurance(-1.0);
    row.set_icon_id(if icon > 0 { icon } else { -1 });
    Ok(())
}
