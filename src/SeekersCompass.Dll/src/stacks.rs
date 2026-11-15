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
use std::sync::atomic::{AtomicU32, Ordering};
use std::time::{Duration, Instant};

use eldenring::cs::{CSTaskGroupIndex, CSTaskImp, FieldInsHandle, WorldChrMan};
use fromsoftware_shared::FromStatic;

/// AttackSpeed, SpeedPhase and SpeedField from the ini, shared with the tasks of the other phases.
static SPEED: AtomicU32 = AtomicU32::new(100);
static PHASE: AtomicU32 = AtomicU32::new(1);
static FIELD: AtomicU32 = AtomicU32::new(2);

const CHECK_EVERY: u32 = 60;
const SETTLE: u32 = 30;
/// The least time between two hits that would each add a stack.
const STACK_GAP: Duration = Duration::from_millis(400);

fn log(text: &str) {
    crate::log("stacks", text);
}

pub fn start(tasks: &CSTaskImp) -> Result<(), String> {
    crate::run(tasks, "stacks", CSTaskGroupIndex::FrameBegin, State::default(), State::tick);
    // The speed can be written at other points of the frame (SpeedPhase), to find one that
    // comes after the game's scripts set it and before the animation uses it.
    crate::run(tasks, "stacks speed 2", CSTaskGroupIndex::ChrIns_PreBehavior, (), |_| write_speed(2));
    crate::run(tasks, "stacks speed 3", CSTaskGroupIndex::ChrIns_BehaviorSafe, (), |_| write_speed(3));
    crate::run(tasks, "stacks speed 4", CSTaskGroupIndex::ChrIns_PostPhysics, (), |_| write_speed(4));
    Ok(())
}

/// AttackSpeed: percent of normal animation speed, 50 to 300; 100 leaves the game alone.
/// SpeedPhase: 1 frame begin, 2 before behaviour, 3 after behaviour, 4 after physics.
/// SpeedField: 1 the behaviour module's animation speed, 2 the behaviour data's multiplier.
fn read_settings() -> (u32, u32, u32) {
    let (mut speed, mut phase, mut field) = (100, 1, 2);
    for (key, value) in crate::ini("StackTest") {
        let Ok(value) = value.parse::<u32>() else { continue };
        match key.as_str() {
            "attackspeed" => speed = value.clamp(50, 300),
            "speedphase" => phase = value.clamp(1, 4),
            "speedfield" => field = value.clamp(1, 2),
            _ => {}
        }
    }
    (speed, phase, field)
}

/// Writes the wanted speed if this is the chosen phase.
fn write_speed(phase: u32) {
    let speed = SPEED.load(Ordering::Relaxed);
    if speed == 100 || PHASE.load(Ordering::Relaxed) != phase {
        return;
    }
    let Ok(world) = (unsafe { WorldChrMan::instance_mut() }) else { return };
    let Some(player) = world.main_player.as_mut() else { return };
    let want = speed as f32 / 100.0;
    if FIELD.load(Ordering::Relaxed) == 1 {
        player.chr_ins.modules.behavior.animation_speed = want;
    } else {
        player.chr_ins.modules.behavior_data.hks_animation_speed_multiplier = want;
    }
}

#[derive(Default)]
struct State {
    frames_in_world: u32,
    hp: HashMap<FieldInsHandle, i32>,
    hits: u32,
    stacks: u32,
    last_stack: Option<Instant>,
    speed: u32,
}

impl State {
    fn tick(&mut self) {
        let Ok(world) = (unsafe { WorldChrMan::instance_mut() }) else { return };
        let Some(me) = world.main_player.as_ref().map(|p| p.chr_ins.field_ins_handle) else {
            self.frames_in_world = 0;
            self.hp.clear();
            return;
        };
        self.frames_in_world = self.frames_in_world.saturating_add(1);
        if self.frames_in_world < SETTLE {
            return;
        }
        if self.frames_in_world == SETTLE || self.frames_in_world % CHECK_EVERY == 0 {
            let (speed, phase, field) = read_settings();
            if (speed, phase, field) != (self.speed, PHASE.load(Ordering::Relaxed), FIELD.load(Ordering::Relaxed)) {
                log(&format!("AttackSpeed={speed} SpeedPhase={phase} SpeedField={field}"));
                self.speed = speed;
                SPEED.store(speed, Ordering::Relaxed);
                PHASE.store(phase, Ordering::Relaxed);
                FIELD.store(field, Ordering::Relaxed);
                // The module's value stays where it is put, so it goes back to normal on every change.
                if let Some(player) = world.main_player.as_mut() {
                    player.chr_ins.modules.behavior.animation_speed = 1.0;
                }
            }
        }
        write_speed(1);

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
    }
}
