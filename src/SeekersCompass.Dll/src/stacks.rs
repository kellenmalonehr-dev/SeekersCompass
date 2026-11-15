//! A test for the stack-building Grace Items, switched on with `Enabled = 1` under [StackTest] in
//! seekers_compass.ini. It adds no items. It answers two questions:
//!
//! 1. Can the player's landed hits be counted? Every character remembers who hit it last, which
//!    the kill counter of Grace Items already relies on. A hit is a drop in the health of a
//!    character whose last hit came from the player. Each is logged with a running count, and so
//!    is the count a stack would get: at most one per STACK_GAP.
//!    (The first try used the Winged Sword Insignia's mechanism, an effect row of state 303 with
//!    a threshold of 1 that applies a marker row to its holder. It fired 4 times in 10 or so hits.)
//! 2. Can attacks be made faster? `AttackSpeed = 120` writes 1.2 every frame to the animation
//!    speed multiplier in the player's behaviour data, the one the game's own scripts set. This
//!    is every animation, not attacks alone. (The first try wrote `animation_speed` in the
//!    behaviour module: the value stayed and changed nothing.)

use std::collections::HashMap;
use std::time::{Duration, Instant};

use eldenring::cs::{CSTaskGroupIndex, CSTaskImp, FieldInsHandle, WorldChrMan};
use fromsoftware_shared::FromStatic;

const CHECK_EVERY: u32 = 60;
const SETTLE: u32 = 30;
/// The least time between two hits that would each add a stack.
const STACK_GAP: Duration = Duration::from_millis(400);

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

#[derive(Default)]
struct State {
    frames_in_world: u32,
    hp: HashMap<FieldInsHandle, i32>,
    hits: u32,
    stacks: u32,
    last_stack: Option<Instant>,
    speed: u32,
    /// What was last written to the animation speed, and how often the game had changed it back.
    wrote: Option<f32>,
    resets: u32,
}

impl State {
    fn tick(&mut self) {
        let Ok(world) = (unsafe { WorldChrMan::instance_mut() }) else { return };
        let Some(me) = world.main_player.as_ref().map(|p| p.chr_ins.field_ins_handle) else {
            self.frames_in_world = 0;
            self.hp.clear();
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
        }

        // Hits: health lost by anyone whose last hit came from the player.
        let mut seen = 0;
        for entry in world.chr_inses_by_distance.iter() {
            let chr = unsafe { entry.chr_ins.as_ref() };
            let handle = chr.field_ins_handle;
            if handle == me {
                continue;
            }
            seen += 1;
            let hp = chr.modules.data.hp;
            let Some(before) = self.hp.insert(handle, hp) else { continue };
            if hp >= before || chr.last_hit_by != me {
                continue;
            }
            self.hits += 1;
            let now = Instant::now();
            let counts = self.last_stack.is_none_or(|t| now.duration_since(t) >= STACK_GAP);
            if counts {
                self.stacks += 1;
                self.last_stack = Some(now);
            }
            log(&format!(
                "hit {}: character {} HP {before} -> {hp} at {:.1}m; {}",
                self.hits,
                chr.character_id,
                entry.distance,
                if counts { format!("stack {}", self.stacks) } else { "too soon for a stack".into() }
            ));
        }
        // The list holds only who is loaded nearby; forget the rest now and then.
        if self.hp.len() > seen + 200 {
            self.hp.clear();
        }

        let Some(player) = world.main_player.as_mut() else { return };
        let data = &mut player.chr_ins.modules.behavior_data;
        let now = data.hks_animation_speed_multiplier;
        if self.speed != 100 {
            let want = self.speed as f32 / 100.0;
            match self.wrote {
                None => log(&format!("animation speed multiplier was {now}; writing {want} every frame")),
                Some(wrote) if now != wrote => {
                    self.resets += 1;
                    if self.resets.is_power_of_two() {
                        log(&format!("the game had set the multiplier to {now} (seen {} times)", self.resets));
                    }
                }
                _ => {}
            }
            data.hks_animation_speed_multiplier = want;
            self.wrote = Some(want);
        } else if self.wrote.take().is_some() {
            data.hks_animation_speed_multiplier = 1.0;
            log("animation speed multiplier back to 1");
        }
    }
}
