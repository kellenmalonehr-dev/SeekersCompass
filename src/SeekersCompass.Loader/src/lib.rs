//! Offline mods loader (Seeker's Compass, Grace Loadouts): Mod Engine 3's file swapping, without
//! its launcher, for every mod installed in Game\OfflineMods.
//!
//! The game folder's xinput1_4.dll (proxy/, a small C proxy that also forwards XInput) loads this
//! only when it is safe to: no Easy Anti-Cheat, and no Mod Engine already loading mods. It then
//! calls [`offline_mods_attach`] while the game is still starting up, before any game code
//! has run - the same point at which Mod Engine 3 attaches to a suspended game.
//!
//! From there this follows Mod Engine 3's own attach (crates/mod-host/src/lib.rs, v0.13.0) with
//! a fixed profile: the installed packages (see [`packages`]), the mods' own save unless Seamless
//! Co-op keeps its own, Arxan neutralized, online play off. Anything that needs worker threads
//! waits until the game's entry point, outside the Windows loader lock; mods' DLLs load there too.
#![feature(fn_traits)]
#![feature(tuple_trait)]
#![feature(unboxed_closures)]

use std::{
    ffi::OsString,
    fs::File,
    os::windows::ffi::OsStringExt,
    panic::{self, AssertUnwindSafe},
    path::PathBuf,
    slice,
    sync::{Arc, Mutex},
};

use indexmap::IndexMap;
use me3_binary_analysis::{dlrf, fd4_step::Fd4StepTables, rtti};
use me3_launcher_attach_protocol::AttachConfig;
use me3_mod_host_assets::mapping::VfsOverrideMapping;
use me3_mod_host_types::dlrf::RuntimeClassEntry;
use me3_mod_protocol::{package::Package, Game};
use tracing::{error, info, Span};

use crate::{
    deferred::{defer_init, Deferred},
    executable::Executable,
    host::{game_properties, ModHost},
};

mod asset_hooks;
mod deferred;
mod detour;
mod executable;
mod filesystem;
mod host;
mod native;
mod packages;
mod savefile;

/// The mods' own save, used unless Seamless Co-op (which keeps its own) is being started.
/// (Seeker's Compass's name; kept so existing offline characters stay where they are.)
const MOD_SAVEFILE: &str = "ER0000.skc";

/// Called once by the proxy, during the game's startup. `mods_dir` is Game\OfflineMods (UTF-16,
/// `len` units); `seamless` says Seamless Co-op's launcher started the game. Returns false if
/// nothing could be set up; the game then runs unmodded.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn offline_mods_attach(mods_dir: *const u16, len: usize, seamless: bool) -> bool {
    let mods_dir = PathBuf::from(OsString::from_wide(unsafe { slice::from_raw_parts(mods_dir, len) }));

    if let Ok(log) = File::create(mods_dir.join("loader.log")) {
        let _ = tracing_subscriber::fmt()
            .with_writer(Mutex::new(log))
            .with_ansi(false)
            .with_max_level(tracing::Level::INFO)
            .try_init();
    }

    guarded("the offline mods could not start; the game runs unmodded", || attach(mods_dir, seamless))
}

/// Runs a setup step, logging an error or panic instead of letting it take the game down.
fn guarded(what: &str, step: impl FnOnce() -> Result<(), eyre::Error>) -> bool {
    match panic::catch_unwind(AssertUnwindSafe(step)) {
        Ok(Ok(())) => true,
        Ok(Err(e)) => {
            error!("error" = &*e, "{what}");
            false
        }
        Err(payload) => {
            let message = payload
                .downcast_ref::<&str>()
                .map(|s| s.to_string())
                .or_else(|| payload.downcast_ref::<String>().cloned())
                .unwrap_or_default();
            error!(panic = %message, "{what}");
            false
        }
    }
}

fn attach(mods_dir: PathBuf, seamless: bool) -> Result<(), eyre::Error> {
    // Mod Engine 3's launcher tells its game-type code which game it started (me3_env::GameVars).
    // SAFETY: the game's own threads haven't started yet.
    unsafe { std::env::set_var("ME3_GAME_LAUNCHED", r#""EldenRing""#) };

    let installed = packages::scan(&mods_dir);
    if installed.is_empty() {
        eyre::bail!("no mods installed in {}", mods_dir.display());
    }
    let natives: Vec<PathBuf> = installed.iter().filter_map(|p| p.native.clone()).collect();

    let config = Arc::new(AttachConfig {
        game: Game::EldenRing,
        natives: Vec::new(),
        early_natives: Vec::new(),
        packages: installed.iter().map(|p| Package::new(p.files.clone())).collect(),
        savefile: (!seamless).then(|| MOD_SAVEFILE.to_owned()),
        cache_path: None,
        suspend: false,
        boot_boost: false,
        skip_logos: false,
        start_online: false,
        disable_arxan: true,
        mem_patch: false,
        mem_patch_heap_size: None,
        skip_steam_init: false,
        property_overrides: IndexMap::new(),
    });
    info!(?mods_dir, seamless, mods = installed.len(), "offline mods loader attaching");

    // SAFETY: called before the game's entry point, so no game code is running yet.
    let exe = unsafe { Executable::new() };
    match exe.version() {
        Ok(ver) => info!("game: {ver}"),
        Err(e) => info!("error" = %e, "could not read the game version"),
    }

    ModHost::new(&config)?.attach();

    // Neutralizes Arxan at the entry point, then runs the rest there (outside the loader lock).
    defer_init(Span::current(), Deferred::BeforeMain, move || {
        guarded("the offline mods could not start; the game runs unmodded", || before_main(config, exe));
        load_natives(&natives);
    })
}

/// Mods' own DLLs (e.g. grace_loadouts.dll), loaded for the rest of the game's life.
fn load_natives(natives: &[PathBuf]) {
    for path in natives {
        // SAFETY: the DLLs come from installed mods, which expect to be loaded into the game.
        match unsafe { libloading::Library::new(path) } {
            Ok(lib) => {
                std::mem::forget(lib);
                info!(?path, "loaded");
            }
            Err(e) => error!(?path, "error" = %e, "could not load"),
        }
    }
}

fn before_main(config: Arc<AttachConfig>, exe: Executable) -> Result<(), eyre::Error> {
    let mut mapping = VfsOverrideMapping::new()?;
    mapping.scan_directories(config.packages.iter())?;
    savefile::attach_override(&config, &mut mapping)?;
    let mapping = Arc::new(mapping);

    filesystem::attach_override(mapping.clone())?;

    defer_init(Span::current(), Deferred::AfterMain, move || {
        guarded("the offline mods could not swap the game's files", || after_main(config, exe, mapping));
    })?;

    info!("file and save hooks in place");
    Ok(())
}

// Mod Engine 3's after_game_main, without native DLL loading or the memory patch.
fn after_main(config: Arc<AttachConfig>, exe: Executable, mapping: Arc<VfsOverrideMapping>) -> Result<(), eyre::Error> {
    let class_map = Arc::new(rtti::classes(exe)?);
    let step_tables = Fd4StepTables::from_initialized_data(exe)?;
    let runtime_classes = unsafe { RuntimeClassEntry::from_analysis(dlrf::runtime_classes(exe)?) };

    game_properties::attach_override(config.game, runtime_classes)?;
    game_properties::start_offline();

    savefile::oversized_regulation_fix(config.clone(), exe, &step_tables, mapping.clone())?;
    asset_hooks::attach_override(config, exe, class_map, &step_tables, mapping)?;

    info!("asset hooks in place");
    Ok(())
}
