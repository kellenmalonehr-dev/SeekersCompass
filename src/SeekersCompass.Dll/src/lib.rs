//! Seeker's Compass, the native half: one DLL for every feature that needs code in the game.
//!
//! - `loadouts`: save and equip gear sets from the grace menu (was grace_loadouts.dll)
//! - `items`: Grace Items, the rest buff and Camp menu (was grace_items.dll)
//! - `dial`: Damage Dial, scaled damage taken (was damage_dial.dll)
//! - `pickup`: things in reach are picked up without pressing the button
//!
//! Each feature has a section in seekers_compass.ini with an `Enabled` switch, runs as its own
//! task on the game's frame loop, and is turned off on its own if it fails, so one feature that a
//! game update breaks doesn't take the others with it. seekers_compass.log starts every launch
//! with the game's version and which features are on.

mod dial;
mod items;
mod loadouts;
mod pickup;
mod runes;
mod scan;

use std::fs::OpenOptions;
use std::io::Write;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::path::PathBuf;
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use eldenring::cs::{CSTaskGroupIndex, CSTaskImp};
use eldenring::fd4::FD4TaskData;
use fromsoftware_shared::SharedTaskImpExt;
use pelite::pe64::{Pe, PeView};
use windows::Win32::Foundation::{ERROR_ALREADY_EXISTS, GetLastError, HMODULE};
use windows::Win32::System::LibraryLoader::{GetModuleFileNameW, GetModuleHandleW};
use windows::Win32::System::SystemServices::DLL_PROCESS_ATTACH;
use windows::Win32::System::Threading::CreateMutexW;
use windows::core::PCWSTR;

/// The game version this build was tested on, as eldenring.exe reports it.
const TESTED_GAME: &str = "2.7.2.0";
const LOG_FILE: &str = "seekers_compass.log";
const INI_FILE: &str = "seekers_compass.ini";

static HOME: OnceLock<PathBuf> = OnceLock::new();
static LOG: Mutex<()> = Mutex::new(());

/// The folder this DLL was loaded from; the ini, the log and each character's files are kept there.
pub fn home() -> &'static PathBuf {
    HOME.get().expect("home set in DllMain")
}

pub fn log(feature: &str, text: &str) {
    let _guard = LOG.lock();
    let ms = SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_millis()).unwrap_or(0);
    if let Ok(mut f) = OpenOptions::new().create(true).append(true).open(home().join(LOG_FILE)) {
        let _ = writeln!(f, "[{}.{:03}] [{feature}] {text}", ms / 1000, ms % 1000);
    }
}

/// The keys (lower case) and values of one section of seekers_compass.ini, read fresh each call so
/// saved changes apply without a restart. Section names ignore case; `;` and `#` start a comment.
pub fn ini(section: &str) -> Vec<(String, String)> {
    let Ok(text) = std::fs::read_to_string(home().join(INI_FILE)) else { return Vec::new() };
    let mut found = Vec::new();
    let mut inside = false;
    for line in text.lines() {
        let line = line.split([';', '#']).next().unwrap_or("").trim();
        if let Some(name) = line.strip_prefix('[').and_then(|l| l.strip_suffix(']')) {
            inside = name.trim().eq_ignore_ascii_case(section);
        } else if inside && let Some((key, value)) = line.split_once('=') {
            found.push((key.trim().to_ascii_lowercase(), value.trim().to_string()));
        }
    }
    found
}

fn enabled(section: &str, default: bool) -> bool {
    ini(section).iter().find(|(k, _)| k == "enabled").map_or(default, |(_, v)| v != "0")
}

/// Runs a feature's `tick` every frame for the life of the game. A panic in it is logged once and
/// turns off that feature alone until the game is restarted.
pub fn run<S: Send + 'static>(tasks: &CSTaskImp, name: &'static str, group: CSTaskGroupIndex, state: S, tick: fn(&mut S)) {
    let mut state = state;
    let mut broken = false;
    let handle = tasks.run_recurring(
        move |_: &FD4TaskData| {
            if broken {
                return;
            }
            if let Err(p) = catch_unwind(AssertUnwindSafe(|| tick(&mut state))) {
                let msg = p.downcast_ref::<String>().cloned().or_else(|| p.downcast_ref::<&str>().map(|s| s.to_string()));
                log(name, &format!("error, turned off until restart: {}", msg.unwrap_or_default()));
                broken = true;
            }
        },
        group,
    );
    std::mem::forget(handle);
}

/// Takes a per-game name; false if another DLL in this game already holds it. The name is held
/// for as long as the game runs.
fn claim(name: &str) -> bool {
    let name: Vec<u16> = format!("Local\\{name}.{}\0", std::process::id()).encode_utf16().collect();
    unsafe { CreateMutexW(None, false, PCWSTR(name.as_ptr())) }.is_ok() && unsafe { GetLastError() } != ERROR_ALREADY_EXISTS
}

/// eldenring.exe's file version, such as "2.7.2.0".
fn game_version() -> Option<String> {
    let base = unsafe { GetModuleHandleW(None) }.ok()?.0 as *const u8;
    let pe = unsafe { PeView::module(base) };
    let v = pe.resources().ok()?.version_info().ok()?.fixed()?.dwFileVersion;
    Some(format!("{}.{}.{}.{}", v.Major, v.Minor, v.Patch, v.Build))
}

/// # Safety
/// Called by the Windows loader only.
#[unsafe(no_mangle)]
pub unsafe extern "system" fn DllMain(module: HMODULE, reason: u32, _: *mut ()) -> bool {
    if reason != DLL_PROCESS_ATTACH {
        return true;
    }
    let mut buf = [0u16; 1024];
    let len = unsafe { GetModuleFileNameW(Some(module), &mut buf) } as usize;
    let dll = PathBuf::from(String::from_utf16_lossy(&buf[..len]));
    let _ = HOME.set(dll.parent().map(PathBuf::from).unwrap_or_default());

    // Nothing here may wait on the game: we're inside the loader lock.
    std::thread::spawn(start);
    true
}

struct Feature {
    name: &'static str,
    /// Its section in the ini.
    section: &'static str,
    on_by_default: bool,
    /// The name the feature's own DLL took when it was a separate mod, so the two never both act.
    old_claim: &'static str,
    old_mod: &'static str,
    start: fn(&CSTaskImp) -> Result<(), String>,
    /// Run every frame while the feature is switched off in the ini, to hide what it shows.
    when_off: Option<fn()>,
}

const FEATURES: [Feature; 5] = [
    Feature { name: "loadouts", section: "Loadouts", on_by_default: true, old_claim: "GraceLoadouts", old_mod: "Grace Loadouts", start: loadouts::start, when_off: None },
    Feature { name: "items", section: "GraceItems", on_by_default: true, old_claim: "GraceItems", old_mod: "Grace Items", start: items::start, when_off: Some(items::hide_menu) },
    Feature { name: "dial", section: "DamageDial", on_by_default: false, old_claim: "DamageDial", old_mod: "Damage Dial", start: dial::start, when_off: None },
    Feature { name: "pickup", section: "AutoPickup", on_by_default: true, old_claim: "SeekersCompassAutoPickup", old_mod: "auto pickup", start: pickup::start, when_off: None },
    Feature { name: "runes", section: "GoldenRunes", on_by_default: true, old_claim: "SeekersCompassGoldenRunes", old_mod: "golden rune line", start: runes::start, when_off: Some(runes::hide_menu) },
];

fn start() {
    // Keep one earlier log, and start fresh once this one passes 4 MB.
    let path = home().join(LOG_FILE);
    if std::fs::metadata(&path).is_ok_and(|m| m.len() > 4 << 20) {
        let _ = std::fs::rename(&path, path.with_extension("old.log"));
    }
    let game = game_version().unwrap_or_else(|| "unknown".into());
    let note = if game == TESTED_GAME { String::new() } else { format!(" (this build was tested on {TESTED_GAME})") };
    log("core", &format!("Seeker's Compass DLL {} loaded; game {game}{note}", env!("CARGO_PKG_VERSION")));
    // Two loaders may both load a copy (say Mod Engine and Elden Mod Loader); only the first acts.
    if !claim("SeekersCompass") {
        log("core", "another copy of seekers_compass.dll is already running in this game; this one stays off");
        return;
    }
    if !home().join(INI_FILE).is_file() {
        log("core", &format!("{INI_FILE} not found beside the DLL; using the defaults"));
    }
    let Ok(tasks) = CSTaskImp::wait_for_instance(Duration::MAX) else {
        log("core", "task runner not found; every feature stays off");
        return;
    };
    for f in &FEATURES {
        let state = if !claim(f.old_claim) {
            format!("off: the separate {} mod is also loaded; remove it to use this one", f.old_mod)
        } else if !enabled(f.section, f.on_by_default) {
            if let Some(hide) = f.when_off {
                run(tasks, f.name, CSTaskGroupIndex::FrameBegin, hide, |hide| hide());
            }
            format!("off (Enabled = 0 under [{}] in {INI_FILE})", f.section)
        } else {
            match (f.start)(tasks) {
                Ok(()) => "on".to_string(),
                Err(e) => format!("off: {e}"),
            }
        };
        log("core", &format!("{}: {state}", f.name));
    }
    log("core", "ready");
}
