//! Custom launcher icon, registered the way Better Stats does it on the same firmware:
//! the two BMPs go to `applications/icons/`, and one entry for the app is added to the
//! desktop's `system/config/desktop/view.json` (backed up once beforehand). The reader
//! shows the icon after it rescans its apps — on the next launch or after a reboot.

use std::{fs, path::Path};

use serde_json::{Map, Value, json};

const ICON_DIR: &str = "/mnt/ext1/applications/icons";
const ICON_REL: &str = "applications/icons/homelib.bmp";
const ICON_FOCUSED_REL: &str = "applications/icons/homelib_f.bmp";
/// `system/config` is the active profile on the device (what Better Stats edits). Every
/// profile under `system/profiles/*` gets the entry too, so the icon shows whoever is
/// signed in; already-registered files are left alone, so the overlap is harmless.
const VIEW_JSON_ACTIVE: &str = "/mnt/ext1/system/config/desktop/view.json";
const PROFILES_DIR: &str = "/mnt/ext1/system/profiles";

const APP_ID: &str = "U_homelib";
const APP_PATH: &str = "/mnt/ext1/applications/homelib.app";
const TITLE: &str = "HomeLib";

const ICON: &[u8] = include_bytes!("../icons/homelib.bmp");
const ICON_FOCUSED: &[u8] = include_bytes!("../icons/homelib_f.bmp");

/// Best effort: a failure here only means the default icon stays.
pub fn install() {
    let _ = fs::create_dir_all(ICON_DIR);
    write_if_changed(&Path::new(ICON_DIR).join("homelib.bmp"), ICON);
    write_if_changed(&Path::new(ICON_DIR).join("homelib_f.bmp"), ICON_FOCUSED);
    let mut files = vec![std::path::PathBuf::from(VIEW_JSON_ACTIVE)];
    if let Ok(entries) = fs::read_dir(PROFILES_DIR) {
        files.extend(entries.flatten().map(|e| e.path().join("config/desktop/view.json")));
    }
    for file in files {
        register(&file);
    }
}

/// Compared rather than skipped when present, so a new release can ship a new icon.
fn write_if_changed(path: &Path, bytes: &[u8]) {
    if fs::read(path).ok().as_deref() != Some(bytes) {
        let _ = fs::write(path, bytes);
    }
}

fn register(view_json: &Path) {
    let Ok(raw) = fs::read_to_string(view_json) else { return };
    let backup = format!("{}.homelib-backup", view_json.display());
    let Ok(Value::Object(mut root)) = serde_json::from_str::<Value>(&raw) else { return };

    let apps = root.entry("applications").or_insert_with(|| Value::Object(Map::new()));
    let Some(apps) = apps.as_object_mut() else { return };
    if apps.contains_key(APP_ID) {
        return;
    }
    if !Path::new(&backup).exists() && fs::copy(view_json, &backup).is_err() {
        return; // never edit the launcher config without a way back
    }
    apps.insert(
        APP_ID.into(),
        json!({
            "path": APP_PATH,
            "title": TITLE,
            "icon": { "path": ICON_REL },
            "focused_icon": { "path": ICON_FOCUSED_REL },
        }),
    );

    // Also list it in the first group, as Better Stats does, so it shows on the desktop.
    if let Some(list) = root
        .get_mut("view")
        .and_then(|v| v.get_mut("groups"))
        .and_then(|g| g.get_mut(0))
        .and_then(|g| g.get_mut("apps"))
        .and_then(Value::as_array_mut)
    {
        if !list.iter().any(|v| v == APP_ID) {
            list.push(Value::from(APP_ID));
        }
    }

    if let Ok(text) = serde_json::to_string_pretty(&Value::Object(root)) {
        let tmp = format!("{}.homelib-tmp", view_json.display());
        if fs::write(&tmp, text).is_ok() {
            let _ = fs::rename(&tmp, view_json);
        }
    }
}
