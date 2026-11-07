//! Settings: a `homelib.cfg` next to the app (`key=value` lines), with the ticket also
//! allowed to come from the build (`HOMELIB_TICKET` at compile time).
//!
//! ```text
//! ticket=homelib1:<endpoint id>:<token>:<relay url>
//! download_dir=/mnt/ext1/HomeLib
//! font=Literata
//! font_bold=Literata-Bold
//! ```

use std::{
    env, fs,
    path::{Path, PathBuf},
};

use homelib_proto::Ticket;

pub const CONFIG_NAME: &str = "homelib.cfg";
const DEFAULT_DOWNLOAD_DIR: &str = "/mnt/ext1/HomeLib";

/// Serif faces to try, in order, the same family preference as the thin web pages.
const REGULAR_FONTS: &[&str] = &["Literata-Regular", "Literata", "PTSerif-Regular", "NotoSerif-Regular", "LiberationSerif"];
const BOLD_FONTS: &[&str] = &["Literata-Bold", "PTSerif-Bold", "NotoSerif-Bold", "LiberationSerif-Bold"];
const FONT_DIRS: &[&str] = &["/mnt/ext1/system/fonts", "/ebrmain/fonts", "/ebrmain/share/fonts"];

pub struct Config {
    pub ticket: Result<Ticket, String>,
    pub download_dir: PathBuf,
    pub font: String,
    pub font_bold: String,
}

pub fn app_dir() -> PathBuf {
    env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(Path::to_path_buf))
        .unwrap_or_else(|| PathBuf::from("/mnt/ext1/applications"))
}

pub fn load() -> Config {
    let candidates = [app_dir().join(CONFIG_NAME), PathBuf::from("/mnt/ext1/system/config").join(CONFIG_NAME)];
    let text = candidates
        .iter()
        .find_map(|p| fs::read_to_string(p).ok())
        .unwrap_or_default();

    let value = |key: &str| {
        text.lines()
            .map(str::trim)
            .filter(|l| !l.starts_with('#'))
            .filter_map(|l| l.split_once('='))
            .find(|(k, _)| k.trim() == key)
            .map(|(_, v)| v.trim().to_string())
            .filter(|v| !v.is_empty())
    };

    let ticket = value("ticket")
        .or_else(|| option_env!("HOMELIB_TICKET").map(str::to_string))
        .ok_or_else(|| format!("Нет билета подключения. Положите {CONFIG_NAME} со строкой ticket=homelib1:… рядом с приложением ({}).", app_dir().display()))
        .and_then(|t| t.parse::<Ticket>().map_err(|e| e.to_string()));

    Config {
        ticket,
        download_dir: value("download_dir").map(PathBuf::from).unwrap_or_else(|| PathBuf::from(DEFAULT_DOWNLOAD_DIR)),
        font: value("font").unwrap_or_else(|| pick_font(REGULAR_FONTS)),
        font_bold: value("font_bold").unwrap_or_else(|| pick_font(BOLD_FONTS)),
    }
}

/// First face with a font file on the device; the last entry is the fallback.
fn pick_font(names: &[&str]) -> String {
    for name in names {
        for dir in FONT_DIRS {
            for ext in ["ttf", "otf"] {
                if Path::new(dir).join(format!("{name}.{ext}")).exists() {
                    return name.to_string();
                }
            }
        }
    }
    names.last().unwrap().to_string()
}
