//! Auto pickup: things in reach are picked up without pressing the interact button.
//!
//! The game asks one function, `CSActionButtonManImp::ExecActionButton(this, id, ...)`, whether
//! the interaction `id` (a row of ActionButtonParam) was triggered, and only asks for things in
//! reach. A hook at its entry answers yes for the rows of the kinds switched on under
//! [AutoPickup] in seekers_compass.ini, and runs the real function for everything else, so
//! doors, levers, graces and conversations are untouched.
//!
//! The mechanism, the row lists and the shape of the hook follow ALR2310's Auto Pickup
//! (https://github.com/ALR2310/ERMods, MIT licence, Copyright (c) 2026 ALR2310), whose notes
//! record it as confirmed in game on 2.7.2.0. Which row is an enemy drop comes from this mod's own
//! log in play.

use std::sync::atomic::{AtomicBool, AtomicU8, AtomicU32, AtomicUsize, Ordering};

use eldenring::cs::{CSTaskGroupIndex, CSTaskImp, GameSystemCommonParam, SoloParamRepository};
use fromsoftware_shared::FromStatic;
use windows::Win32::System::Memory::{MEM_COMMIT, MEM_RESERVE, PAGE_EXECUTE_READWRITE, PAGE_PROTECTION_FLAGS, VirtualAlloc, VirtualProtect};

use crate::scan;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
enum Kind {
    Materials,
    Items,
    Remains,
    EnemyDrops,
    LostRunes,
    Chests,
}

impl Kind {
    const ALL: [Kind; 6] = [Kind::Materials, Kind::Items, Kind::Remains, Kind::EnemyDrops, Kind::LostRunes, Kind::Chests];

    const fn bit(self) -> u8 {
        1 << self as u8
    }

    /// The ini key and whether it is on when the ini doesn't say.
    const fn setting(self) -> (&'static str, bool) {
        match self {
            Kind::Materials => ("materials", true),
            Kind::Items => ("items", true),
            Kind::Remains => ("remains", true),
            Kind::EnemyDrops => ("enemydrops", false),
            Kind::LostRunes => ("lostrunes", true),
            Kind::Chests => ("chests", false),
        }
    }

    const fn rows(self) -> &'static [u32] {
        match self {
            Kind::Materials => &MATERIALS,
            Kind::Items => &ITEMS,
            Kind::Remains => &REMAINS,
            Kind::EnemyDrops => &ENEMY_DROPS,
            Kind::LostRunes => &LOST_RUNES,
            Kind::Chests => &CHESTS,
        }
    }
}

/// Plants, mushrooms and other crafting materials (base game, then the DLC's parallel rows).
const MATERIALS: [u32; 112] = [
    7800, 7810, 7811, 7812, 7813, 7814, 7815, 7816, 7817, 7818, 7819, 7820, 7821, 7822, 7823, 7824,
    7825, 7826, 7827, 7828, 7850, 7860, 7861, 7862, 7863, 7864, 7865, 7866, 7867, 7868, 7869, 7870,
    7871, 7872, 7873, 7874, 7875, 7876, 7877, 7878, 207800, 207810, 207811, 207812, 207813, 207814,
    207815, 207816, 207817, 207818, 207819, 207820, 207821, 207822, 207823, 207824, 207825, 207826,
    207827, 207828, 207829, 207830, 207831, 207832, 207833, 207834, 207835, 207836, 207837, 207838,
    207839, 207840, 207841, 207842, 207843, 207844, 207850, 207860, 207861, 207862, 207863, 207864,
    207865, 207866, 207867, 207868, 207869, 207870, 207871, 207872, 207873, 207874, 207875, 207876,
    207877, 207878, 207879, 207880, 207881, 207882, 207883, 207884, 207885, 207886, 207887, 207888,
    207889, 207890, 207891, 207892, 207893, 207894,
];
/// Items placed in the world ("Pick up item"); 4250 is the glowing item on the ground.
const ITEMS: [u32; 13] = [4110, 4250, 4251, 4252, 4253, 4260, 4261, 4270, 4280, 4350, 6361, 209210, 209524];
/// Remains placed in the world ("Pillage remains").
const REMAINS: [u32; 4] = [4200, 4201, 4202, 9532];
/// What a killed enemy leaves: 4100, seen in game on 2.7.2.0. 4000, the plain "Pick up item" row,
/// has not come up in play yet and is kept with it, so it stays off unless asked for.
const ENEMY_DROPS: [u32; 2] = [4000, 4100];
/// Your dropped runes after dying.
const LOST_RUNES: [u32; 1] = [1000];
/// "Search treasure chest". Off unless asked for: a mimic opens the same way.
const CHESTS: [u32; 1] = [4300];

fn classify(id: u32) -> Option<Kind> {
    Kind::ALL.into_iter().find(|k| k.rows().binary_search(&id).is_ok())
}

fn log(text: &str) {
    crate::log("pickup", text);
}

/// The function's first 61 bytes, unique in the game's code on 2.6.2.0 through 2.7.2.0.
const PATTERN: &str = "48 8b c4 55 41 54 41 55 41 56 41 57 48 8d 68 c9 48 81 ec f0 00 00 00 48 c7 45 b7 fe ff ff ff 48 89 58 08 48 89 70 10 48 89 78 18 0f 29 70 c8 0f 29 78 b8 45 0f b6 f1 0f 28 fa 8b f2 4c 8b e9";
/// The first seven instructions, up to `sub rsp, 0xF0`. None reads an argument register or
/// RIP-relative data, so they can run from a copy elsewhere.
const RELOCATE_LEN: usize = 0x10;

/// Kinds answered yes, as a mask of `Kind::bit`. Zero until the settings are read.
static ENABLED: AtomicU8 = AtomicU8::new(0);
/// Set while the player is in combat and PauseInCombat is on: the hook then answers nothing.
static PAUSED: AtomicBool = AtomicBool::new(false);
/// Where the copied start of the real function lives; the trampoline jumps there.
static RELOCATED: AtomicUsize = AtomicUsize::new(0);
/// Row ids the hook has seen, for the log: the id, with the top bit set when it answered yes.
/// Fixed size and lock-free, because the hook must not allocate, lock or write files.
static SEEN: [AtomicU32; 64] = [const { AtomicU32::new(0) }; 64];
const ANSWERED: u32 = 1 << 31;

unsafe extern "C" {
    fn seekers_compass_pickup_trampoline();
}

// Saves the argument registers, asks `filter(this, id)`, and either returns 1 to the game or
// puts the registers back and runs the real function from its copied start. The copy ends in a
// jump through r11: the function's start leaves rax holding the stack pointer and goes on using
// it, so rax must not be touched on the way back.
std::arch::global_asm!(
    r#"
.global seekers_compass_pickup_trampoline
seekers_compass_pickup_trampoline:
    push    rbp
    mov     rbp, rsp
    sub     rsp, 0x80
    mov     [rsp + 0x20], rcx
    mov     [rsp + 0x28], rdx
    mov     [rsp + 0x30], r8
    mov     [rsp + 0x38], r9
    movups  [rsp + 0x40], xmm0
    movups  [rsp + 0x50], xmm1
    movups  [rsp + 0x60], xmm2
    movups  [rsp + 0x70], xmm3
    call    {filter}
    test    eax, eax
    jnz     2f
    mov     rcx, [rsp + 0x20]
    mov     rdx, [rsp + 0x28]
    mov     r8,  [rsp + 0x30]
    mov     r9,  [rsp + 0x38]
    movups  xmm0, [rsp + 0x40]
    movups  xmm1, [rsp + 0x50]
    movups  xmm2, [rsp + 0x60]
    movups  xmm3, [rsp + 0x70]
    mov     rsp, rbp
    pop     rbp
    mov     r11, [rip + {relocated}]
    jmp     r11
2:
    mov     rsp, rbp
    pop     rbp
    mov     eax, 1
    ret
"#,
    filter = sym filter,
    relocated = sym RELOCATED,
);

/// Called by the trampoline with the real function's first two arguments. Nothing in here can
/// panic, allocate or block.
extern "C" fn filter(_this: *mut u8, id: u32) -> i32 {
    let yes = !PAUSED.load(Ordering::Relaxed) && classify(id).is_some_and(|k| ENABLED.load(Ordering::Relaxed) & k.bit() != 0);
    let entry = (id & !ANSWERED) | if yes { ANSWERED } else { 0 };
    for slot in &SEEN {
        match slot.load(Ordering::Relaxed) {
            0 => {
                let _ = slot.compare_exchange(0, entry, Ordering::Relaxed, Ordering::Relaxed);
                break;
            }
            seen if seen == entry => break,
            _ => {}
        }
    }
    yes as i32
}

/// Finds the function and redirects its start to the trampoline.
fn install() -> Result<(), String> {
    let entry = scan::unique(PATTERN).map_err(|e| format!("the game's interaction check wasn't found ({e}); another auto-pickup mod may have changed it, or this game version isn't supported"))? as *mut u8;
    unsafe {
        let copy = VirtualAlloc(None, 64, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE) as *mut u8;
        if copy.is_null() {
            return Err("no memory for the hook".into());
        }
        // The copied start, then `mov r11, entry + 16; jmp r11`.
        std::ptr::copy_nonoverlapping(entry, copy, RELOCATE_LEN);
        let mut back = [0x49u8, 0xBB, 0, 0, 0, 0, 0, 0, 0, 0, 0x41, 0xFF, 0xE3];
        back[2..10].copy_from_slice(&((entry as u64) + RELOCATE_LEN as u64).to_le_bytes());
        std::ptr::copy_nonoverlapping(back.as_ptr(), copy.add(RELOCATE_LEN), back.len());
        RELOCATED.store(copy as usize, Ordering::Release);

        // `mov rax, trampoline; jmp rax`, padded with nops to the instruction boundary.
        let mut patch = [0x90u8; RELOCATE_LEN];
        patch[..2].copy_from_slice(&[0x48, 0xB8]);
        patch[2..10].copy_from_slice(&(seekers_compass_pickup_trampoline as *const () as u64).to_le_bytes());
        patch[10..12].copy_from_slice(&[0xFF, 0xE0]);
        let mut old = PAGE_PROTECTION_FLAGS(0);
        VirtualProtect(entry.cast(), RELOCATE_LEN, PAGE_EXECUTE_READWRITE, &mut old).map_err(|e| format!("couldn't make the game code writable: {e}"))?;
        std::ptr::copy_nonoverlapping(patch.as_ptr(), entry, RELOCATE_LEN);
        let _ = VirtualProtect(entry.cast(), RELOCATE_LEN, old, &mut old);
    }
    Ok(())
}

pub fn start(tasks: &CSTaskImp) -> Result<(), String> {
    install()?;
    crate::run(tasks, "pickup", CSTaskGroupIndex::FrameBegin, State::default(), State::tick);
    Ok(())
}

#[derive(Default)]
struct State {
    frames: u32,
    mask: Option<u8>,
    /// The game's own pick-up animations for placed items and remains, kept to put back.
    animations: Option<(i32, i32)>,
    /// Whether the animation is currently switched off.
    no_animation: Option<bool>,
    /// PauseInCombat from the ini.
    pause_in_combat: bool,
    /// How many `SEEN` slots have been logged.
    logged: usize,
}

impl State {
    fn tick(&mut self) {
        self.frames = self.frames.wrapping_add(1);
        // Every frame, so pick-ups stop the moment a fight starts.
        PAUSED.store(self.pause_in_combat && in_combat().unwrap_or(false), Ordering::Relaxed);
        if self.frames % 60 != 1 {
            return;
        }
        let ini = crate::ini("AutoPickup");
        let mask = Kind::ALL.into_iter().fold(0u8, |mask, k| {
            let (key, default) = k.setting();
            let on = ini.iter().find(|(name, _)| name == key).map_or(default, |(_, v)| v != "0");
            mask | if on { k.bit() } else { 0 }
        });
        if self.mask != Some(mask) {
            let on: Vec<String> = Kind::ALL.into_iter().filter(|k| mask & k.bit() != 0).map(|k| format!("{k:?}")).collect();
            log(&format!("picking up: {}", if on.is_empty() { "nothing".into() } else { on.join(", ") }));
            self.mask = Some(mask);
            ENABLED.store(mask, Ordering::Relaxed);
        }
        let pause = ini.iter().find(|(name, _)| name == "pauseincombat").is_some_and(|(_, v)| v != "0");
        if pause != self.pause_in_combat {
            self.pause_in_combat = pause;
            log(if pause { "pausing while in combat" } else { "picking up in combat too" });
        }
        let skip = ini.iter().find(|(name, _)| name == "animation").is_some_and(|(_, v)| v == "0");
        if self.no_animation != Some(skip) && self.set_animation(skip) {
            log(if skip { "pick-up animation off" } else { "pick-up animation as in the game" });
            self.no_animation = Some(skip);
        }
        // What the hook has met so far, once each: tells which row a thing in the world uses.
        while self.logged < SEEN.len() {
            let entry = SEEN[self.logged].load(Ordering::Relaxed);
            if entry == 0 {
                break;
            }
            self.logged += 1;
            let id = entry & !ANSWERED;
            match classify(id) {
                Some(kind) if entry & ANSWERED != 0 => log(&format!("row {id} ({kind:?}): picked up automatically")),
                Some(kind) => log(&format!("row {id} ({kind:?}): left to the button, its kind is switched off")),
                None => log(&format!("row {id}: another interaction, left to the button")),
            }
        }
    }
}

/// An animation id the game doesn't have, so nothing plays.
const NO_ANIMATION: i32 = 999_999;

impl State {
    /// Placed items and remains take their pick-up animation from one row of
    /// GameSystemCommonParam (after ALR2310's notes, confirmed there on 2.7.2.0). False while the
    /// game's tables aren't loaded yet.
    fn set_animation(&mut self, skip: bool) -> bool {
        let Ok(params) = (unsafe { SoloParamRepository::instance_mut() }) else { return false };
        let Some(row) = params.get_mut::<GameSystemCommonParam>(0) else { return false };
        let (shine, corpse) = *self.animations.get_or_insert_with(|| {
            (row.msb_event_geom_treasure_info_item_get_anim_id_shine(), row.msb_event_geom_treasure_info_item_get_anim_id_corpse())
        });
        row.set_msb_event_geom_treasure_info_item_get_anim_id_shine(if skip { NO_ANIMATION } else { shine });
        row.set_msb_event_geom_treasure_info_item_get_anim_id_corpse(if skip { NO_ANIMATION } else { corpse });
        true
    }
}

/// The game's "player is in combat" flag, as ALR2310's notes locate it: the CSSound singleton
/// points at +0x328 to a struct whose byte +4 is set in combat. None while it can't be read.
#[repr(C)]
#[fromsoftware_shared::singleton("CSSound")]
struct CSSound {
    unk0: [u8; 0x328],
    combat: *const CombatState,
}

#[repr(C)]
struct CombatState {
    unk0: [u8; 4],
    in_combat: u8,
}

fn in_combat() -> Option<bool> {
    let sound = unsafe { CSSound::instance() }.ok()?;
    if (sound.combat as usize) < 0x10000 {
        return None;
    }
    Some(unsafe { (*sound.combat).in_combat } != 0)
}
