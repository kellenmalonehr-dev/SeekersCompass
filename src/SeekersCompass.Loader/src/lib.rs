//! Seeker's Compass loader: Mod Engine 3's file swapping, without its launcher.
//!
//! The game folder's xinput1_4.dll (proxy/, a small C proxy that also forwards XInput) loads this
//! only when it is safe to: no Easy Anti-Cheat, and no Mod Engine already loading mods. It then
//! calls [`seekers_compass_attach`] while the game is still starting up, before any game code
//! has run - the same point at which Mod Engine 3 attaches to a suspended game.
//!
//! From there this follows Mod Engine 3's own attach (crates/mod-host/src/lib.rs, v0.13.0) with
//! a fixed profile: one package (the mod folder), the mod's own save unless Seamless Co-op keeps
//! its own, Arxan neutralized, online play off. Anything that needs worker threads waits until
//! the game's entry point, outside the Windows loader lock.
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
mod savefile;

/// The mod's own save, used unless Seamless Co-op (which keeps its own) is being started.
const MOD_SAVEFILE: &str = "ER0000.skc";

/// Called once by the proxy, during the game's startup. `mod_dir` is the package folder (UTF-16,
/// `len` units); `seamless` says Seamless Co-op's launcher started the game. Returns false if
/// nothing could be set up; the game then runs unmodded.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn seekers_compass_attach(mod_dir: *const u16, len: usize, seamless: bool) -> bool {
    let mod_dir = PathBuf::from(OsString::from_wide(unsafe { slice::from_raw_parts(mod_dir, len) }));

    if let Ok(log) = File::create(mod_dir.join("loader.log")) {
        let _ = tracing_subscriber::fmt()
            .with_writer(Mutex::new(log))
            .with_ansi(false)
            .with_max_level(tracing::Level::INFO)
            .try_init();
    }

    guarded("Seeker's Compass could not start; the game runs unmodded", || attach(mod_dir, seamless))
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

fn attach(mod_dir: PathBuf, seamless: bool) -> Result<(), eyre::Error> {
    // Mod Engine 3's launcher tells its game-type code which game it started (me3_env::GameVars).
    // SAFETY: the game's own threads haven't started yet.
    unsafe { std::env::set_var("ME3_GAME_LAUNCHED", r#""EldenRing""#) };

    let config = Arc::new(AttachConfig {
        game: Game::EldenRing,
        natives: Vec::new(),
        early_natives: Vec::new(),
        packages: vec![Package::new(mod_dir.clone())],
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
    info!(?mod_dir, seamless, "Seeker's Compass loader attaching");

    // SAFETY: called before the game's entry point, so no game code is running yet.
    let exe = unsafe { Executable::new() };
    match exe.version() {
        Ok(ver) => info!("game: {ver}"),
        Err(e) => info!("error" = %e, "could not read the game version"),
    }

    ModHost::new(&config)?.attach();

    // Neutralizes Arxan at the entry point, then runs the rest there (outside the loader lock).
    defer_init(Span::current(), Deferred::BeforeMain, move || {
        guarded("Seeker's Compass could not start; the game runs unmodded", || before_main(config, exe));
    })
}

fn before_main(config: Arc<AttachConfig>, exe: Executable) -> Result<(), eyre::Error> {
    let mut mapping = VfsOverrideMapping::new()?;
    mapping.scan_directories(config.packages.iter())?;
    savefile::attach_override(&config, &mut mapping)?;
    let mapping = Arc::new(mapping);

    filesystem::attach_override(mapping.clone())?;

    defer_init(Span::current(), Deferred::AfterMain, move || {
        guarded("Seeker's Compass could not swap the game's files", || after_main(config, exe, mapping));
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
