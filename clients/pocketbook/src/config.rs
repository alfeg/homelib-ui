//! Settings from `homelib.cfg` next to the app: `key=value` lines, `#` comments.
//!
//! Every connection setting can come from a ticket, from its own key, or both — the keys
//! refine the ticket. That lets one ticket serve everybody (LAN only) while each reader
//! adds the relay it should use:
//!
//! ```text
//! ticket=homelib2:<server id>:<token>:#192.168.1.10:11204   # LAN only
//! relay=http://relay.example.net:3340                       # + reachable from anywhere
//! ```
//!
//! | Key | Meaning |
//! |---|---|
//! | `ticket` | `homelib1:`/`homelib2:` ticket from the server (`iroh-gate ticket`) |
//! | `server` | server endpoint id — replaces the ticket's |
//! | `token` | access token — replaces the ticket's |
//! | `relay` | relay URL, added to the ticket's; repeat or comma-separate; `off` drops all relays |
//! | `direct` | server LAN `ip:port`, added to the ticket's; repeat or comma-separate; `off` drops them |
//! | `download_dir` | where books are saved (default `/mnt/ext1/HomeLib`) |
//! | `font`, `font_bold` | font file names without extension (default: Literata if present) |
//!
//! Without a `ticket` key the ticket baked in at build time (`HOMELIB_TICKET`) is used.

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
    let text = candidates.iter().find_map(|p| fs::read_to_string(p).ok()).unwrap_or_default();
    let file = Entries::parse(&text);

    Config {
        ticket: connection(&file, option_env!("HOMELIB_TICKET")),
        download_dir: file.last("download_dir").map(PathBuf::from).unwrap_or_else(|| PathBuf::from(DEFAULT_DOWNLOAD_DIR)),
        font: file.last("font").map(str::to_string).unwrap_or_else(|| pick_font(REGULAR_FONTS)),
        font_bold: file.last("font_bold").map(str::to_string).unwrap_or_else(|| pick_font(BOLD_FONTS)),
    }
}

/// All `key=value` pairs in file order; keys may repeat.
struct Entries(Vec<(String, String)>);

impl Entries {
    fn parse(text: &str) -> Self {
        Entries(
            text.lines()
                .map(str::trim)
                .filter(|l| !l.starts_with('#'))
                .filter_map(|l| l.split_once('='))
                .map(|(k, v)| {
                    // A trailing "# comment" after the value is allowed.
                    let v = v.split(" #").next().unwrap_or_default();
                    (k.trim().to_string(), v.trim().to_string())
                })
                .filter(|(_, v)| !v.is_empty())
                .collect(),
        )
    }

    fn last(&self, key: &str) -> Option<&str> {
        self.0.iter().rev().find(|(k, _)| k == key).map(|(_, v)| v.as_str())
    }

    /// Every value of a list key, comma-separated values split.
    fn list(&self, key: &str) -> Vec<String> {
        self.0
            .iter()
            .filter(|(k, _)| k == key)
            .flat_map(|(_, v)| v.split(','))
            .map(|v| v.trim().to_string())
            .filter(|v| !v.is_empty())
            .collect()
    }
}

/// The ticket, refined by the individual keys. Rebuilt as a `homelib2` string so the
/// ticket parser does all the validation.
fn connection(file: &Entries, baked: Option<&str>) -> Result<Ticket, String> {
    let base = match file.last("ticket").or(baked) {
        Some(t) => Some(t.parse::<Ticket>().map_err(|e| e.to_string())?),
        None => None,
    };

    let server = file
        .last("server")
        .map(str::to_string)
        .or_else(|| base.as_ref().map(|t| t.endpoint.to_string()))
        .ok_or_else(|| missing("ticket= или server="))?;
    let token = file
        .last("token")
        .map(str::to_string)
        .or_else(|| base.as_ref().map(|t| t.token.clone()))
        .ok_or_else(|| missing("token="))?;

    let mut relays: Vec<String> = base.as_ref().map(|t| t.relays.iter().map(|r| r.to_string()).collect()).unwrap_or_default();
    let mut direct: Vec<String> = base.as_ref().map(|t| t.direct.iter().map(|a| a.to_string()).collect()).unwrap_or_default();
    merge(&mut relays, file.list("relay"));
    merge(&mut direct, file.list("direct"));

    if relays.is_empty() && direct.is_empty() {
        return Err(missing("relay= или direct="));
    }
    let mut text = format!("homelib2:{server}:{token}:{}", relays.join(","));
    if !direct.is_empty() {
        text.push('#');
        text.push_str(&direct.join(","));
    }
    text.parse::<Ticket>().map_err(|e| e.to_string())
}

/// Adds configured entries to the ticket's; `off` clears everything before it.
fn merge(into: &mut Vec<String>, configured: Vec<String>) {
    for value in configured {
        if value.eq_ignore_ascii_case("off") {
            into.clear();
        } else if !into.contains(&value) {
            into.push(value);
        }
    }
}

fn missing(what: &str) -> String {
    format!(
        "Не настроено подключение: в {CONFIG_NAME} нет {what}. Положите {CONFIG_NAME} рядом с приложением ({}).",
        app_dir().display()
    )
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

#[cfg(test)]
mod tests {
    use super::*;

    const ID: &str = "bb7f89e2cdda8e12bd3034a5aa6580fb2e2e2f87ce11b8508c82dff5f2a061f5";

    fn ticket(cfg: &str) -> Result<Ticket, String> {
        connection(&Entries::parse(cfg), None)
    }

    #[test]
    fn lan_ticket_plus_relay_key() {
        let t = ticket(&format!(
            "ticket=homelib2:{ID}:tok:#192.168.1.10:11204\nrelay=http://relay.example.net:3340  # anywhere\n"
        ))
        .unwrap();
        assert_eq!(t.relays.len(), 1);
        assert_eq!(t.direct.len(), 1);
        assert_eq!(t.token, "tok");
    }

    #[test]
    fn parts_without_a_ticket() {
        let t = ticket(&format!("server={ID}\ntoken=tok\ndirect=192.168.1.10:11204,10.0.0.5:11204\n")).unwrap();
        assert!(t.relays.is_empty());
        assert_eq!(t.direct.len(), 2);
    }

    #[test]
    fn off_drops_the_tickets_relays() {
        let t = ticket(&format!(
            "ticket=homelib2:{ID}:tok:http://relay.example.net:3340/#192.168.1.10:11204\nrelay=off\n"
        ))
        .unwrap();
        assert!(t.relays.is_empty());
        assert_eq!(t.direct.len(), 1);
    }

    #[test]
    fn nothing_to_dial_is_an_error() {
        assert!(ticket(&format!("server={ID}\ntoken=tok\n")).is_err());
        assert!(ticket("").is_err());
    }
}
