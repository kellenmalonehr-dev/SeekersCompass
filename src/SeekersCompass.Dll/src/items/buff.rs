//! The rest buff. Sitting at a grace banks the kills since the last sit: they set how many stacks
//! each equipped keepsake can hold and how long stacks last, and the seat sets how long the buff
//! runs. While it runs, every hit the player lands adds a stack; stacks drop one at a time once no
//! hit has landed for the fall-off time. Being hit costs nothing.
//!
//! What stacks do depends on the keepsake:
//! - Fury: more damage to enemies for each stack.
//! - Second Wind: faster stamina recovery for each stack.
//! - Bulwark: less poise damage taken for each stack.
//! - Ward: on reaching its full stacks, a shell that cuts the damage of the next few hits taken.
//! - Leech: on reaching its full stacks, a charge that heals the player on their next landed hit.
//!
//! A spent shell or charge comes back when the stacks drop below full and are built up again.
//! One not yet used is lost when the last stack falls off.
//!
//! Nothing on disk changes. In memory this borrows three unused area-scaling rows of
//! SpEffectParam, like the 7280 that Damage Dial borrows (no param or event script in vanilla or
//! Convergence refers to them): 7270 carries the stacks' effects and is put back on the player
//! with new values whenever they change, 7250 is the glow shown at full stacks, 7240 the heal.
//! The damage rates used are the ones for enemies, so other players are never affected.

use std::time::{Duration, Instant};

use eldenring::cs::{ChrInsExt, PlayerIns, SoloParamRepository, SpEffectParam};
use fromsoftware_shared::FromStatic;

use super::log;

pub const ROW: i32 = 7270;
const GLOW_ROW: i32 = 7250;
const HEAL_ROW: i32 = 7240;
/// The game's own flash for HP restored by the Taker's Cameo.
const HEAL_VFX: i32 = 350300;

/// The least time between two hits that each add a stack.
const STACK_GAP: Duration = Duration::from_millis(400);
/// Once stacks start to fall off, one goes at a time, this far apart.
const DROP_EVERY: Duration = Duration::from_secs(1);
/// Hits a Ward shell cuts before it breaks.
const WARD_HITS: u32 = 3;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Effect {
    Fury,
    Ward,
    Leech,
    SecondWind,
    Bulwark,
}

impl Effect {
    pub fn parse(name: &str) -> Option<Effect> {
        let name: String = name.chars().filter(|c| c.is_ascii_alphabetic()).collect();
        Some(match name.to_ascii_lowercase().as_str() {
            "fury" => Effect::Fury,
            "ward" => Effect::Ward,
            "leech" => Effect::Leech,
            "secondwind" => Effect::SecondWind,
            "bulwark" => Effect::Bulwark,
            _ => return None,
        })
    }

    /// What one stack is worth unless grace_items.txt says otherwise (`per_stack`):
    /// Fury 2% damage, Second Wind 2 points of stamina recovery (the Green Turtle Talisman is 8),
    /// Bulwark 3% less poise damage. Ward and Leech count the stacks of their full charge: 4% of
    /// damage cut and 0.8% of max HP healed for each.
    pub fn per_stack(self) -> f32 {
        match self {
            Effect::Fury => 2.0,
            Effect::SecondWind => 2.0,
            Effect::Bulwark => 3.0,
            Effect::Ward => 4.0,
            Effect::Leech => 0.8,
        }
    }
}

/// What an equipped keepsake contributes: its effect, the most stacks it can hold, the kills
/// since the last sit that unlock each of them, and what one stack is worth.
#[derive(Clone, Copy, Debug)]
pub struct Keepsake {
    pub effect: Effect,
    pub max: u32,
    pub kills_per_point: u32,
    pub per_stack: f32,
}

/// One keepsake's part of a buff: the stacks it can hold in this buff.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Add {
    pub effect: Effect,
    pub cap: u32,
    pub per_stack: f32,
}

/// One buff: the stacks each keepsake can hold, how long stacks last, and how long the buff runs.
#[derive(Clone, Debug)]
pub struct Grant {
    pub adds: Vec<Add>,
    /// All the caps together, used to decide which of two buffs is stronger.
    pub total: u32,
    pub length: Duration,
    /// Time without landing a hit before stacks start to drop.
    pub fall_off: Duration,
}

impl Grant {
    /// Each keepsake can hold a stack for every `kills_per_point` kills, up to its most.
    pub fn earned(keepsakes: &[Keepsake], kills: u32, minutes: f32, fall_off: Duration) -> Grant {
        let adds: Vec<Add> = keepsakes
            .iter()
            .map(|k| Add { effect: k.effect, cap: (kills / k.kills_per_point.max(1)).min(k.max), per_stack: k.per_stack })
            .filter(|a| a.cap > 0)
            .collect();
        Self::from_parts(adds, (minutes.max(0.1) * 60.0) as u64, fall_off)
    }

    pub fn from_parts(adds: Vec<Add>, length_secs: u64, fall_off: Duration) -> Grant {
        let total = adds.iter().map(|a| a.cap).sum();
        Grant { adds, total, length: Duration::from_secs(length_secs), fall_off }
    }

    /// The most stacks the player can hold: the largest cap among the keepsakes.
    fn top(&self) -> u32 {
        self.adds.iter().map(|a| a.cap).max().unwrap_or(0)
    }

    pub fn describe(&self) -> String {
        let parts: Vec<String> = self.adds.iter().map(|a| format!("{:?} up to {} stacks", a.effect, a.cap)).collect();
        format!("{} for {} min, stacks last {:.0} s", parts.join(", "), self.length.as_secs() / 60, self.fall_off.as_secs_f32())
    }
}

struct Active {
    grant: Grant,
    until: Instant,
}

/// What the effect row holds: the rates and points the current stacks give.
#[derive(Clone, Copy, PartialEq, Debug)]
struct Values {
    /// Damage to enemies, as a multiplier.
    fury: f32,
    /// Stamina recovery, in the game's points.
    wind: i32,
    /// Poise damage taken, as a multiplier.
    bulwark: f32,
    /// Damage taken from enemies, as a multiplier.
    ward: f32,
}

const NEUTRAL: Values = Values { fury: 1.0, wind: 0, bulwark: 1.0, ward: 1.0 };

#[derive(Default)]
pub struct Buff {
    active: Option<Active>,
    stacks: u32,
    last_hit: Option<Instant>,
    last_stack: Option<Instant>,
    last_drop: Option<Instant>,
    /// Hits the Ward shell still cuts; 0 when there is none.
    ward_hits: u32,
    /// The Leech charge is held: the next landed hit heals.
    leech_ready: bool,
    /// The player's HP last frame, to see hits taken.
    last_hp: Option<i32>,
    /// What the effect row holds while it is on the player.
    written: Option<Values>,
    /// The row must be written again though its values are the same (the icon changed).
    stale: bool,
    glowing: bool,
    row_refused: bool,
    frames: u32,
    /// The on-screen icon written into the row.
    icon: i32,
    /// The glow shown at full stacks (a SpEffectVfxParam row); 0 for none.
    glow: i32,
}

impl Buff {
    /// Sets the buff's icon and glow; a running buff picks them up as it is next put on.
    pub fn set_look(&mut self, icon: i32, glow: i32) {
        if icon != self.icon {
            self.icon = icon;
            self.stale = true;
        }
        if glow != self.glow {
            self.glow = glow;
            self.glowing = false;
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

    /// Picks up a buff saved for this character, or none. Time spent outside the game doesn't
    /// count. Stacks are not kept: they start from none.
    pub fn resume(&mut self, saved: Option<(Grant, u64)>) {
        self.active = saved.filter(|(grant, left)| *left > 0 && grant.total > 0).map(|(grant, left)| {
            log(&format!("buff: resumed {} with {left} s left", grant.describe()));
            Active { grant, until: Instant::now() + Duration::from_secs(left) }
        });
        self.clear_stacks();
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
        // Caps may be lower than the stacks held.
        self.stacks = 0;
        self.ward_hits = 0;
        self.leech_ready = false;
    }

    fn clear_stacks(&mut self) {
        self.stacks = 0;
        self.ward_hits = 0;
        self.leech_ready = false;
        self.last_hit = None;
        self.last_stack = None;
        self.last_drop = None;
        // A load clears the player's effects.
        self.written = None;
        self.glowing = false;
    }

    pub fn end(&mut self, player: &mut PlayerIns, why: &str) {
        if self.active.take().is_some() {
            player.remove_speffect(ROW);
            player.remove_speffect(GLOW_ROW);
            self.clear_stacks();
            log(&format!("buff: ended ({why})"));
        }
    }

    /// Adds a stack, for testing without a fight.
    pub fn test_hit(&mut self, player: &mut PlayerIns) {
        self.last_stack = None;
        self.tick(player, true);
    }

    /// Runs every frame. `landed` says a hit of the player's took health off someone this frame.
    pub fn tick(&mut self, player: &mut PlayerIns, landed: bool) {
        let hp = player.chr_ins.modules.data.hp;
        let lost = self.last_hp.replace(hp).map_or(0, |before| before - hp);
        let Some(active) = &self.active else { return };
        if hp <= 0 {
            self.end(player, "died");
            return;
        }
        let now = Instant::now();
        if now >= active.until {
            self.end(player, "time up");
            return;
        }
        let adds = active.grant.adds.clone();
        let top = active.grant.top();
        let fall_off = active.grant.fall_off;
        let before = self.stacks;

        if landed {
            self.last_hit = Some(now);
            // A charge held before this hit is spent on it.
            if self.leech_ready {
                self.leech_ready = false;
                let add = adds.iter().find(|a| a.effect == Effect::Leech);
                let percent = add.map_or(0.0, |a| a.per_stack * a.cap as f32);
                match heal(player, percent) {
                    Ok(()) => log(&format!("stacks: Leech heals {percent:.1}% of max HP (HP was {hp} of {})", player.chr_ins.modules.data.max_hp)),
                    Err(e) => log(&format!("stacks: Leech heal not applied: {e}")),
                }
            }
            if self.stacks < top && self.last_stack.is_none_or(|t| now.duration_since(t) >= STACK_GAP) {
                self.stacks += 1;
                self.last_stack = Some(now);
            }
        } else if self.stacks > 0
            && self.last_hit.is_none_or(|t| now.duration_since(t) >= fall_off)
            && self.last_drop.is_none_or(|t| now.duration_since(t) >= DROP_EVERY)
        {
            self.stacks -= 1;
            self.last_drop = Some(now);
            // With the last stack goes any shell or charge not yet used.
            if self.stacks == 0 {
                self.ward_hits = 0;
                self.leech_ready = false;
            }
        }

        // A hit taken uses up one of the shell's hits. Small losses (poison and the like) don't.
        if self.ward_hits > 0 && lost > player.chr_ins.modules.data.max_hp / 100 {
            self.ward_hits -= 1;
            log(&format!("stacks: Ward cut a hit (HP -{lost}); {} left", self.ward_hits));
        }

        if self.stacks != before {
            for add in &adds {
                if before < add.cap && self.stacks >= add.cap {
                    match add.effect {
                        Effect::Ward => self.ward_hits = WARD_HITS,
                        Effect::Leech => self.leech_ready = true,
                        _ => continue,
                    }
                    log(&format!("stacks: {:?} charged at {} stacks", add.effect, add.cap));
                }
            }
            log(&format!("stacks: {} of {top}", self.stacks));
        }

        let mut want = NEUTRAL;
        for add in &adds {
            let held = self.stacks.min(add.cap) as f32;
            match add.effect {
                Effect::Fury => want.fury = 1.0 + add.per_stack * held / 100.0,
                Effect::SecondWind => want.wind = (add.per_stack * held).round() as i32,
                Effect::Bulwark => want.bulwark = (1.0 - add.per_stack * held / 100.0).max(0.1),
                Effect::Ward if self.ward_hits > 0 => want.ward = (1.0 - add.per_stack * add.cap as f32 / 100.0).max(0.1),
                Effect::Ward | Effect::Leech => {}
            }
        }
        self.frames += 1;
        self.keep_row(player, want);
        self.keep_glow(player, top > 0 && self.stacks >= top && self.glow > 0);
    }

    /// Keeps the effect row on the player with these values, or off when they change nothing.
    /// Rests and loads can clear effects, so it goes back on when it's missing.
    fn keep_row(&mut self, player: &mut PlayerIns, want: Values) {
        if want == NEUTRAL {
            if self.written.take().is_some() {
                player.remove_speffect(ROW);
            }
            return;
        }
        let changed = self.stale || self.written != Some(want);
        if !changed && self.frames % 30 != 0 {
            return;
        }
        let on = player.chr_ins.special_effect.entries().any(|e| e.param_id == ROW);
        if !changed && on {
            return;
        }
        if changed {
            if let Err(e) = write_row(want, self.icon) {
                if !self.row_refused {
                    log(&format!("stacks: {e}; not applied"));
                    self.row_refused = true;
                }
                return;
            }
            self.row_refused = false;
            self.stale = false;
            if on {
                // The game reads the values as the effect goes on, so it comes off and on again.
                player.remove_speffect(ROW);
            }
        }
        // Not synced: other players without the mod would see the row's original values.
        player.apply_speffect(ROW, true);
        self.written = Some(want);
        log(&format!(
            "stacks: effect on: damage x{:.2}, stamina recovery +{}, poise damage taken x{:.2}, damage taken x{:.2}{}",
            want.fury,
            want.wind,
            want.bulwark,
            want.ward,
            if changed { "" } else { " (it was missing)" }
        ));
    }

    fn keep_glow(&mut self, player: &mut PlayerIns, wanted: bool) {
        if !wanted {
            if self.glowing {
                player.remove_speffect(GLOW_ROW);
                self.glowing = false;
            }
            return;
        }
        if self.glowing && (self.frames % 30 != 0 || player.chr_ins.special_effect.entries().any(|e| e.param_id == GLOW_ROW)) {
            return;
        }
        match write_glow(self.glow) {
            Ok(()) => {
                player.apply_speffect(GLOW_ROW, true);
                if !self.glowing {
                    log("stacks: full; glow on");
                }
                self.glowing = true;
            }
            Err(e) if !self.row_refused => {
                log(&format!("stacks: {e}; no glow"));
                self.row_refused = true;
            }
            Err(_) => {}
        }
    }

    pub fn status(&self) -> String {
        match &self.active {
            Some(a) => format!("{} ({} s left)", a.grant.describe(), a.until.saturating_duration_since(Instant::now()).as_secs()),
            None => "none".into(),
        }
    }
}

/// Makes the row ours and writes the stacks' values into it. Refuses if the row holds anything
/// other than the vanilla area-scaling values or our own, because another mod is then using it.
fn write_row(v: Values, icon: i32) -> Result<(), String> {
    let params = unsafe { SoloParamRepository::instance_mut() }.map_err(|e| format!("params not ready: {e:?}"))?;
    let row = crate::dial::claim(params.get_mut::<SpEffectParam>(ROW as u32), ROW)?;
    // Damage to enemies only; hits on other players have their own rates, which stay at 1.
    row.set_atk_enemy_dmg_correct_rate_physics(v.fury);
    row.set_atk_enemy_dmg_correct_rate_magic(v.fury);
    row.set_atk_enemy_dmg_correct_rate_fire(v.fury);
    row.set_atk_enemy_dmg_correct_rate_thunder(v.fury);
    row.set_atk_enemy_dmg_correct_rate_dark(v.fury);
    row.set_stamina_recover_change_speed(v.wind);
    // What the Bull-Goat's Talisman sets (0.75).
    row.set_toughness_damage_cut_rate(v.bulwark);
    // Damage from enemies only.
    row.set_def_enemy_dmg_correct_rate_physics(v.ward);
    row.set_def_enemy_dmg_correct_rate_magic(v.ward);
    row.set_def_enemy_dmg_correct_rate_fire(v.ward);
    row.set_def_enemy_dmg_correct_rate_thunder(v.ward);
    row.set_def_enemy_dmg_correct_rate_dark(v.ward);
    // The DLL keeps time itself, so the game never ends the effect on its own.
    row.set_effect_endurance(-1.0);
    row.set_icon_id(if icon > 0 { icon } else { -1 });
    Ok(())
}

fn write_glow(vfx: i32) -> Result<(), String> {
    let params = unsafe { SoloParamRepository::instance_mut() }.map_err(|e| format!("params not ready: {e:?}"))?;
    let row = crate::dial::claim(params.get_mut::<SpEffectParam>(GLOW_ROW as u32), GLOW_ROW)?;
    row.set_effect_endurance(-1.0);
    row.set_vfx_id(vfx);
    Ok(())
}

/// Restores this percent of the player's max HP, the way the Taker's Cameo does: an effect that
/// changes HP once and is gone.
fn heal(player: &mut PlayerIns, percent: f32) -> Result<(), String> {
    let params = unsafe { SoloParamRepository::instance_mut() }.map_err(|e| format!("params not ready: {e:?}"))?;
    let row = crate::dial::claim(params.get_mut::<SpEffectParam>(HEAL_ROW as u32), HEAL_ROW)?;
    row.set_effect_endurance(0.0);
    row.set_change_hp_rate(-percent);
    row.set_vfx_id(HEAL_VFX);
    player.apply_speffect(HEAL_ROW, true);
    Ok(())
}
