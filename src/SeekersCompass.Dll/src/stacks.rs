//! A test for the stack-building Grace Items, switched on with `Enabled = 1` under [StackTest] in
//! seekers_compass.ini. It adds no items. It answers two questions:
//!
//! 1. Can the player's landed hits be counted? The Winged Sword Insignia counts successive hits
//!    with an effect row of state 303: it adds up on every hit, drains over time, and applies
//!    another effect to its holder when it passes a threshold. Here an unused row (7250) is set up
//!    that way with a threshold of 1, so every hit applies a marker row (7240) to the player. Each
//!    time the marker shows up it is counted, logged and taken off again.
//! 2. Can attacks be made faster? `AttackSpeed = 120` writes 1.2 to the animation speed of the
//!    player's behaviour module every frame. This is every animation, not attacks alone; whether
//!    the game keeps the value, and what it looks like, is what the test is for.

use eldenring::cs::{CSTaskGroupIndex, CSTaskImp, ChrInsExt, SoloParamRepository, SpEffectParam, WorldChrMan};
use fromsoftware_shared::FromStatic;

/// Unused area-scaling rows like the ones Damage Dial and the rest buff borrow.
const COUNTER_ROW: i32 = 7250;
const MARK_ROW: i32 = 7240;
/// The state of the Winged Sword Insignia's row (320800).
const COUNT_HITS: u16 = 303;
const CHECK_EVERY: u32 = 60;
const SETTLE: u32 = 30;

fn log(text: &str) {
    crate::log("stacks", text);
}

pub fn start(tasks: &CSTaskImp) -> Result<(), String> {
    crate::run(tasks, "stacks", CSTaskGroupIndex::FrameBegin, State::default(), State::tick);
    Ok(())
}

/// Percent of normal animation speed, 50 to 300; 100 leaves the game alone.
fn read_speed() -> u32 {
    let mut speed = 100;
    for (key, value) in crate::ini("StackTest") {
        if key == "attackspeed"
            && let Ok(value) = value.parse::<u32>()
        {
            speed = value.clamp(50, 300);
        }
    }
    speed
}

fn prepare_rows() -> Result<(), String> {
    let params = unsafe { SoloParamRepository::instance_mut() }.map_err(|e| format!("params not ready: {e:?}"))?;
    let row = crate::dial::claim(params.get_mut::<SpEffectParam>(COUNTER_ROW as u32), COUNTER_ROW)?;
    row.set_state_info(COUNT_HITS);
    row.set_accumu_over_fire_id(MARK_ROW);
    row.set_accumu_over_val(1);
    row.set_accumu_val(-1);
    row.set_motion_interval(0.5);
    row.set_effect_target_friend(true);
    row.set_effect_target_player(true);
    row.set_effect_target_ai(true);
    row.set_effect_target_live(true);
    row.set_effect_target_ghost(true);
    let mark = crate::dial::claim(params.get_mut::<SpEffectParam>(MARK_ROW as u32), MARK_ROW)?;
    mark.set_effect_endurance(0.5);
    mark.set_effect_target_friend(true);
    mark.set_effect_target_player(true);
    mark.set_effect_target_ai(true);
    mark.set_effect_target_live(true);
    mark.set_effect_target_ghost(true);
    Ok(())
}

#[derive(Default)]
struct State {
    frames_in_world: u32,
    rows_refused: bool,
    hits: u32,
    marked: bool,
    speed: u32,
    /// What was last written to the animation speed, and how often the game had changed it back.
    wrote: Option<f32>,
    resets: u32,
}

impl State {
    fn tick(&mut self) {
        let Ok(world) = (unsafe { WorldChrMan::instance_mut() }) else { return };
        let Some(player) = world.main_player.as_mut() else {
            self.frames_in_world = 0;
            self.marked = false;
            self.wrote = None;
            return;
        };
        self.frames_in_world = self.frames_in_world.saturating_add(1);
        if self.frames_in_world < SETTLE {
            return;
        }

        if self.frames_in_world == SETTLE || self.frames_in_world % CHECK_EVERY == 0 {
            let speed = read_speed();
            if speed != self.speed {
                log(&format!("AttackSpeed={speed}"));
                self.speed = speed;
            }
            match prepare_rows() {
                Ok(()) => {
                    self.rows_refused = false;
                    if !player.chr_ins.special_effect.entries().any(|e| e.param_id == COUNTER_ROW) {
                        player.apply_speffect(COUNTER_ROW, true);
                        log("hit counter applied");
                    }
                }
                Err(e) => {
                    if !self.rows_refused {
                        log(&format!("{e}; hits are not counted"));
                        self.rows_refused = true;
                    }
                }
            }
        }

        // A hit: the marker has appeared since the last frame.
        let marked = player.chr_ins.special_effect.entries().any(|e| e.param_id == MARK_ROW);
        if marked && !self.marked {
            self.hits += 1;
            log(&format!("hit {}", self.hits));
            player.remove_speffect(MARK_ROW);
        }
        self.marked = marked;

        let behavior = &mut player.chr_ins.modules.behavior;
        let now = behavior.animation_speed;
        if self.speed != 100 {
            let want = self.speed as f32 / 100.0;
            match self.wrote {
                None => log(&format!("animation speed was {now}; writing {want} every frame")),
                Some(wrote) if now != wrote => {
                    self.resets += 1;
                    if self.resets.is_power_of_two() {
                        log(&format!("the game had set the animation speed to {now} (seen {} times)", self.resets));
                    }
                }
                _ => {}
            }
            behavior.animation_speed = want;
            self.wrote = Some(want);
        } else if self.wrote.take().is_some() {
            behavior.animation_speed = 1.0;
            log("animation speed back to 1");
        }
    }
}
