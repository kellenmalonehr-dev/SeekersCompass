//! The mods installed in Game\OfflineMods: each subfolder with a package.ini.
//!
//! ```text
//! # package.ini (written by each mod's Install_Without_ModEngine)
//! name = Grace Loadouts
//! order = 20                  # lower loads first; a later package wins a file both replace
//! files = menu                # folder (relative to this one) with the game files to swap in
//! native = grace_loadouts.dll # optional DLL to load once the game is running
//! # optional: `files` was built on top of another package's file; while that file is missing
//! # or has changed since (size or time), `fallback` is used instead.
//! base = D:\...\Game\OfflineMods\SeekersCompass\script\talk\m00_00_00_00.talkesdbnd.dcx  # or relative to this folder
//! base_size = 123456
//! base_time = 134000000000000000   # last write time, Windows file time
//! fallback = standalone
//! ```
//! Seeker's Compass 1.2's own folder (Game\SeekersCompass, marked by its installer) loads too,
//! as if it had `order = 10` and `files = .`.

use std::{
    collections::HashMap,
    fs,
    os::windows::fs::MetadataExt,
    path::{Path, PathBuf},
};

use tracing::{info, warn};

pub struct Installed {
    pub name: String,
    pub order: i64,
    pub files: PathBuf,
    pub native: Option<PathBuf>,
}

pub fn scan(mods_dir: &Path) -> Vec<Installed> {
    let mut found = Vec::new();
    if let Ok(entries) = fs::read_dir(mods_dir) {
        for dir in entries.flatten().map(|e| e.path()).filter(|p| p.is_dir()) {
            let ini = dir.join("package.ini");
            match fs::read_to_string(&ini) {
                Ok(text) => found.extend(parse(&dir, &text)),
                Err(_) => continue,
            }
        }
    }

    let legacy = mods_dir.parent().map(|game| game.join("SeekersCompass"));
    if let Some(legacy) = legacy.filter(|d| d.join(".installed-by-seekers-compass").exists()) {
        found.push(Installed { name: "Seeker's Compass (1.2 install)".into(), order: 10, files: legacy, native: None });
    }

    found.sort_by(|a, b| a.order.cmp(&b.order).then_with(|| a.name.cmp(&b.name)));
    found
}

fn parse(dir: &Path, text: &str) -> Option<Installed> {
    let keys: HashMap<&str, &str> = text
        .lines()
        .map(|l| l.split('#').next().unwrap_or("").trim())
        .filter_map(|l| l.split_once('='))
        .map(|(k, v)| (k.trim(), v.trim()))
        .collect();
    let name = keys.get("name").copied().unwrap_or("?").to_owned();
    let order = keys.get("order").and_then(|v| v.parse().ok()).unwrap_or(0);
    let mut files = dir.join(keys.get("files").copied().unwrap_or("."));

    if let (Some(base), Some(fallback)) = (keys.get("base"), keys.get("fallback")) {
        let want = (keys.get("base_size").and_then(|v| v.parse::<u64>().ok()), keys.get("base_time").and_then(|v| v.parse::<u64>().ok()));
        let have = fs::metadata(dir.join(base)).ok().map(|m| (Some(m.file_size()), Some(m.last_write_time())));
        if have != Some(want) {
            warn!(%name, base, "the mod this one was merged with is gone or has changed: using its own files (run its installer again to merge)");
            files = dir.join(fallback);
        }
    }
    if !files.is_dir() {
        warn!(%name, ?files, "files folder missing: skipped");
        return None;
    }
    let native = keys.get("native").map(|n| dir.join(n)).filter(|p| p.is_file());
    info!(%name, order, ?files, ?native, "package");
    Some(Installed { name, order, files, native })
}
