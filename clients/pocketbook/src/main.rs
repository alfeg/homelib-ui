//! HomeLib for PocketBook: search the library, browse authors and series, download books
//! (one, or a whole series) and open them in the reader.
//!
//! The server is reached over iroh using a shared ticket (see `config.rs`), so the reader
//! needs no VPN or public address. All UI work runs on InkView's single thread; network
//! calls block it behind the hourglass, as the stock apps do.

mod api;
mod config;
mod download;
mod launcher_icon;
mod ui;

use std::{
    cell::RefCell,
    ffi::{CStr, CString, c_char},
    path::Path,
    sync::OnceLock,
    time::Duration,
};

use api::Book;
use homelib_client::Client;
use inkview::{Event, bindings, bindings::Inkview, event::Key};
use ui::{Block, Hit, Layout, Painter, Screen};

const USER_AGENT: &str = concat!("HomeLib-PocketBook/", env!("CARGO_PKG_VERSION"));
const MAX_RETRY_WAIT: Duration = Duration::from_secs(90);

static IV: OnceLock<Inkview> = OnceLock::new();

thread_local! {
    static APP: RefCell<Option<App>> = const { RefCell::new(None) };
    /// Text from the on-screen keyboard, waiting for the timer that runs the search.
    static PENDING_QUERY: RefCell<Option<String>> = const { RefCell::new(None) };
}

/// Keyboard buffer: InkView writes the typed text here.
static mut KEYBOARD_BUF: [u8; 512] = [0; 512];

/// The reader shows nothing when an app dies, so panics go to `homelib.log` next to the app.
fn install_panic_log() {
    std::panic::set_hook(Box::new(|info| {
        let line = format!("panic: {info}\n");
        eprint!("{line}");
        let path = config::app_dir().join("homelib.log");
        if let Ok(mut f) = std::fs::OpenOptions::new().create(true).append(true).open(path) {
            use std::io::Write;
            let _ = f.write_all(line.as_bytes());
        }
    }));
}

/// libinkview by bare name first (the firmware's library path), then where it usually lives.
/// On firmware 6.11 this fails unless the launcher sets
/// `GLIBC_TUNABLES=glibc.rtld.execstack=2`: some system libraries libinkview pulls in still
/// ask for an executable stack, which that glibc refuses by default.
fn load_inkview() -> Inkview {
    let mut errors = Vec::new();
    for path in ["libinkview.so", "/ebrmain/lib/libinkview.so", "/usr/lib/libinkview.so"] {
        match unsafe { Inkview::new(path) } {
            Ok(iv) => return iv,
            Err(e) => errors.push(format!("{path}: {e}")),
        }
    }
    panic!("cannot load libinkview: {}", errors.join("; "));
}

fn iv() -> &'static Inkview {
    IV.get().expect("inkview loaded")
}

#[derive(Clone)]
enum Action {
    PromptSearch,
    CheckServer,
    Search { q: String, page: u32 },
    Recent { page: u32 },
    Author { name: String, page: u32 },
    Series { name: String, author: Option<String>, page: u32 },
    Book(Box<Book>),
    Download { book: Box<Book>, zipped: bool, read: bool },
    DownloadSeries { name: String, author: Option<String> },
}

struct App {
    cfg: config::Config,
    painter: Option<Painter>,
    client: Option<Client>,
    stack: Vec<Screen<Action>>,
    layout: Option<Layout<Action>>,
    last_query: String,
}

fn main() {
    install_panic_log();
    let _ = IV.set(load_inkview());
    APP.with(|a| *a.borrow_mut() = Some(App::new()));

    inkview::iv_main(iv(), |event| {
        APP.with(|a| {
            // Some InkView calls run a nested event loop; skip events that arrive meanwhile.
            let Ok(mut slot) = a.try_borrow_mut() else { return None };
            slot.as_mut().and_then(|app| app.handle(event))
        })
    });
}

impl App {
    fn new() -> Self {
        App { cfg: config::load(), painter: None, client: None, stack: Vec::new(), layout: None, last_query: String::new() }
    }

    fn handle(&mut self, event: Event) -> Option<()> {
        match event {
            Event::Init => {
                launcher_icon::install();
                self.painter = Some(Painter::new(iv(), &self.cfg.font, &self.cfg.font_bold));
                let home = self.home_screen();
                self.stack.push(home);
                self.render(true);
            }
            Event::Show | Event::Repaint => self.render(false),
            Event::PointerUp { x, y } => self.tap(x, y),
            Event::KeyDown { key } => match key {
                Key::Back => self.back(),
                Key::Prev | Key::Left | Key::Up | Key::Prev2 => self.flip(-1),
                Key::Next | Key::Right | Key::Down | Key::Next2 => self.flip(1),
                Key::Menu => self.prompt_search(),
                Key::Home => exit(),
                _ => return None,
            },
            Event::Exit => {
                self.client = None;
                self.painter = None;
            }
            _ => return None,
        }
        Some(())
    }

    fn render(&mut self, full: bool) {
        let can_back = self.stack.len() > 1;
        let (Some(painter), Some(screen)) = (self.painter.as_mut(), self.stack.last_mut()) else { return };
        self.layout = Some(painter.render(screen, can_back, full));
    }

    fn tap(&mut self, x: i32, y: i32) {
        let hit = self
            .layout
            .as_ref()
            .and_then(|l| l.hits.iter().find(|(r, _)| r.contains(x, y)).map(|(_, h)| h.clone()));
        match hit {
            Some(Hit::Target(action)) => self.run(action, false),
            Some(Hit::Back) => self.back(),
            Some(Hit::Exit) => exit(),
            Some(Hit::Search) => self.prompt_search(),
            Some(Hit::PrevView) => self.flip(-1),
            Some(Hit::NextView) => self.flip(1),
            None => {}
        }
    }

    fn back(&mut self) {
        if self.stack.len() > 1 {
            self.stack.pop();
            self.render(true);
        } else {
            exit();
        }
    }

    /// Page within the screen; past either end, load the server's neighbouring page.
    fn flip(&mut self, dir: i32) {
        let views = self.layout.as_ref().map_or(1, |l| l.views);
        let Some(screen) = self.stack.last_mut() else { return };
        if dir < 0 && screen.view > 0 {
            screen.view -= 1;
            self.render(false);
        } else if dir > 0 && screen.view + 1 < views {
            screen.view += 1;
            self.render(false);
        } else if let Some(action) = if dir < 0 { screen.prev.clone() } else { screen.next.clone() } {
            self.run(action, true);
        }
    }

    fn prompt_search(&mut self) {
        unsafe {
            let buf = &mut *std::ptr::addr_of_mut!(KEYBOARD_BUF);
            buf.fill(0);
            let q = self.last_query.as_bytes();
            let n = q.len().min(buf.len() - 1);
            buf[..n].copy_from_slice(&q[..n]);
            let title = CString::new("Название, автор или серия").unwrap();
            iv().OpenKeyboard(title.as_ptr(), buf.as_mut_ptr() as *mut c_char, 255, bindings::KBD_NORMAL as i32, Some(on_keyboard));
        }
    }

    fn run_pending_query(&mut self) {
        let Some(q) = PENDING_QUERY.with(|p| p.borrow_mut().take()) else { return };
        let q = q.trim().to_string();
        if q.is_empty() {
            self.render(false);
            return;
        }
        self.last_query = q.clone();
        self.run(Action::Search { q, page: 1 }, false);
    }

    fn client(&mut self) -> Result<&Client, String> {
        if self.client.is_none() {
            let ticket = self.cfg.ticket.clone()?;
            self.client = Some(Client::new(ticket, USER_AGENT).map_err(|e| e.to_string())?);
        }
        Ok(self.client.as_ref().unwrap())
    }

    /// Brings Wi-Fi up (asking the user if needed) before the first request.
    fn ensure_network(&self) -> Result<(), String> {
        unsafe {
            if iv().QueryNetwork() as u32 & bindings::NET_CONNECTED != 0 {
                return Ok(());
            }
            if iv().NetConnect2(std::ptr::null(), true) != 0 {
                return Err("Нет подключения к Wi-Fi.".into());
            }
        }
        Ok(())
    }

    /// Runs an action: actions that produce a screen push it (or replace the current one
    /// when paging); downloads report and stay put.
    fn run(&mut self, action: Action, replace: bool) {
        let result = match action {

            Action::PromptSearch => {
                self.prompt_search();
                return;
            }
            Action::Book(book) => Ok(Some(book_screen(&book))),
            Action::Download { book, zipped, read } => {
                self.download_one(&book, zipped, read);
                Ok(None)
            }
            Action::DownloadSeries { name, author } => {
                self.download_series(&name, author.as_deref());
                Ok(None)
            }
            other => self.fetch_screen(other).map(Some),
        };
        match result {
            Ok(Some(screen)) => {
                if replace {
                    self.stack.pop();
                }
                self.stack.push(screen);
                self.render(true);
            }
            Ok(None) => self.render(false),
            Err(e) => {
                self.render(false);
                message(bindings::ICON_ERROR, "HomeLib", &e, 6000);
            }
        }
    }

    fn fetch_screen(&mut self, action: Action) -> Result<Screen<Action>, String> {
        self.ensure_network()?;
        unsafe { iv().ShowHourglassForce() };
        let result = self.client().and_then(|c| build_screen(c, &action).map_err(|e| e.to_string()));
        unsafe { iv().HideHourglass() };
        result
    }

    fn home_screen(&self) -> Screen<Action> {
        let mut s = Screen::new("HomeLib");
        s.blocks.push(Block::Caption("Обзор".into()));
        s.blocks.push(Block::row("Недавно добавленные", None, Action::Recent { page: 1 }));
        s.blocks.push(Block::row("Найти…", Some("Название, автор или серия".into()), Action::PromptSearch));
        s.blocks.push(Block::row("Проверить подключение", None, Action::CheckServer));
        match &self.cfg.ticket {
            Ok(t) => s.blocks.push(Block::Note(format!(
                "Книги сохраняются в {}. Сервер {} через {}.",
                self.cfg.download_dir.display(),
                t.endpoint.fmt_short(),
                t.relays.iter().map(|r| r.to_string()).collect::<Vec<_>>().join(", ")
            ))),
            Err(e) => s.blocks.push(Block::Note(e.clone())),
        }
        s
    }

    fn download_one(&mut self, book: &Book, zipped: bool, read: bool) {
        if let Err(e) = self.ensure_network() {
            return message(bindings::ICON_ERROR, "HomeLib", &e, 5000);
        }
        let dir = self.cfg.download_dir.clone();
        unsafe { iv().ShowHourglassForce() };
        let outcome = match self.client() {
            Ok(client) => download_with_retry(client, book, &dir, zipped, 2, |_| {}),
            Err(e) => download::Outcome::Failed(e),
        };
        unsafe { iv().HideHourglass() };
        match outcome {
            download::Outcome::Saved(path) if read => open_book(&path),
            download::Outcome::Saved(path) => message(
                bindings::ICON_INFORMATION,
                "Сохранено",
                &path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default(),
                3000,
            ),
            download::Outcome::Busy(_, text) | download::Outcome::Failed(text) => {
                message(bindings::ICON_ERROR, "Не удалось скачать", &text, 6000)
            }
        }
    }

    fn download_series(&mut self, name: &str, author: Option<&str>) {
        if let Err(e) = self.ensure_network() {
            return message(bindings::ICON_ERROR, "HomeLib", &e, 5000);
        }
        let dir = self.cfg.download_dir.clone();
        unsafe { iv().ShowHourglassForce() };
        let books = self.client().and_then(|c| api::series_all(c, name, author).map_err(|e| e.to_string()));
        unsafe { iv().HideHourglass() };
        let books = match books {
            Ok(b) if !b.is_empty() => b,
            Ok(_) => return message(bindings::ICON_INFORMATION, name, "В серии нет книг.", 3000),
            Err(e) => return message(bindings::ICON_ERROR, "HomeLib", &e, 6000),
        };

        let total = books.len();
        let (mut done, mut failed, mut last_error) = (0, 0, String::new());
        for (i, book) in books.iter().enumerate() {
            let line = format!("{} из {}: {}", i + 1, total, book.title);
            let fraction = i as f32 / total as f32;
            if let Some(p) = self.painter.as_mut() {
                p.progress(name, &line, fraction);
            }
            let painter = &mut self.painter;
            let client = self.client.as_ref().expect("client created above");
            let outcome = download_with_retry(client, book, &dir, false, 4, |wait| {
                if let Some(p) = painter.as_mut() {
                    p.progress(name, &format!("{line}\n\nСервер занят, ждём {} с…", wait.as_secs()), fraction);
                }
            });
            match outcome {
                download::Outcome::Saved(_) => done += 1,
                download::Outcome::Busy(_, e) | download::Outcome::Failed(e) => {
                    failed += 1;
                    last_error = e;
                }
            }
        }

        let mut summary = format!("Скачано {done} из {total} в {}", dir.display());
        if failed > 0 {
            summary.push_str(&format!("\nОшибок: {failed}. Последняя: {last_error}"));
        }
        message(bindings::ICON_INFORMATION, name, &summary, 8000);
    }
}

fn download_with_retry(
    client: &Client,
    book: &Book,
    dir: &Path,
    zipped: bool,
    tries: u32,
    mut on_wait: impl FnMut(Duration),
) -> download::Outcome {
    let mut attempt = 1;
    loop {
        match download::download(client, book, dir, zipped) {
            download::Outcome::Busy(wait, _) if attempt < tries => {
                let wait = wait.min(MAX_RETRY_WAIT);
                on_wait(wait);
                std::thread::sleep(wait);
                attempt += 1;
            }
            other => return other,
        }
    }
}

fn build_screen(client: &Client, action: &Action) -> std::io::Result<Screen<Action>> {
    Ok(match action {
        Action::CheckServer => {
            let st = api::status(client)?;
            let mut s = Screen::new(st.collection_title.clone().unwrap_or_else(|| "HomeLib".into()));
            s.subtitle = Some(if st.ready { "подключено".into() } else { st.message.clone() });
            s.blocks.push(Block::Caption("Сервер".into()));
            s.blocks.push(Block::Fact { label: "Состояние".into(), value: st.message.clone() });
            if let Some(n) = st.total_books {
                s.blocks.push(Block::Fact { label: "Книг".into(), value: thousands(n) });
            }
            s.blocks.push(Block::Fact { label: "Узел".into(), value: client.ticket().endpoint.fmt_short().to_string() });
            let relays: Vec<String> = client.ticket().relays.iter().map(|r| r.to_string()).collect();
            s.blocks.push(Block::Fact { label: "Ретрансляторы".into(), value: relays.join(", ") });
            // Direct on the home LAN, through a relay elsewhere.
            let route = match client.route() {
                Some(r) if r.starts_with("direct") => format!("напрямую ({})", &r[7..]),
                Some(r) if r.starts_with("relay") => format!("через ретранслятор ({})", &r[6..]),
                Some(r) => r,
                None => "неизвестно".into(),
            };
            s.blocks.push(Block::Fact { label: "Маршрут".into(), value: route });
            s
        }
        Action::Search { q, page } => {
            let data = api::search(client, q, *page, false)?;
            let mut s = Screen::new(q.clone());
            s.subtitle = Some(found(data.total));
            if *page == 1 && !data.author_hits.is_empty() {
                s.blocks.push(Block::Caption(if data.author_hits.len() == 1 { "Автор" } else { "Авторы" }.into()));
                for hit in &data.author_hits {
                    s.blocks.push(Block::row(&hit.name, Some(books_count(hit.count)), Action::Author { name: hit.name.clone(), page: 1 }));
                }
            }
            if *page == 1 && !data.series_hits.is_empty() {
                s.blocks.push(Block::Caption(if data.series_hits.len() == 1 { "Серия" } else { "Совпадения по сериям" }.into()));
                for hit in &data.series_hits {
                    s.blocks.push(Block::row(&hit.name, Some(books_count(hit.count)), Action::Series { name: hit.name.clone(), author: None, page: 1 }));
                }
            }
            if s.blocks.len() > 0 && !data.books.is_empty() {
                s.blocks.push(Block::Caption("Книги".into()));
            }
            push_books(&mut s, &data.books, true, false);
            if data.books.is_empty() && s.blocks.is_empty() {
                s.blocks.push(Block::Note("Ничего не найдено.".into()));
            }
            pager(&mut s, data.page, data.total_pages, |p| Action::Search { q: q.clone(), page: p });
            s
        }
        Action::Recent { page } => {
            let data = api::search(client, "", *page, true)?;
            let mut s = Screen::new("Недавно добавленные");
            push_books(&mut s, &data.books, true, false);
            pager(&mut s, data.page, data.total_pages, |p| Action::Recent { page: p });
            s
        }
        Action::Author { name, page } => {
            let data = api::author(client, name, *page)?;
            let mut s = Screen::new(name.clone());
            s.subtitle = Some(books_count(data.total));
            if *page == 1 && !data.series.is_empty() {
                s.blocks.push(Block::Caption(format!("Серии ({})", data.series.len())));
                for entry in &data.series {
                    let mut meta = books_count(entry.count);
                    if let Some(first) = entry.first_year {
                        let years = match entry.last_year {
                            Some(last) if last != first => format!("{first}–{last}"),
                            _ => first.to_string(),
                        };
                        meta.push_str(&format!(" · добавлены {years}"));
                    }
                    s.blocks.push(Block::row(&entry.name, Some(meta), Action::Series { name: entry.name.clone(), author: Some(name.clone()), page: 1 }));
                }
            }
            if !data.books.is_empty() {
                s.blocks.push(Block::Caption("Книги".into()));
            }
            push_books(&mut s, &data.books, false, false);
            pager(&mut s, data.page, data.total_pages, |p| Action::Author { name: name.clone(), page: p });
            s
        }
        Action::Series { name, author, page } => {
            let data = api::series(client, name, author.as_deref(), *page, api::PAGE_SIZE)?;
            let mut s = Screen::new(name.clone());
            s.subtitle = Some(author.clone().unwrap_or_else(|| books_count(data.total)));
            s.blocks.push(Block::Button {
                label: format!("Скачать всю серию ({})", data.total),
                target: Action::DownloadSeries { name: name.clone(), author: author.clone() },
            });
            push_books(&mut s, &data.books, author.is_none(), true);
            pager(&mut s, *page, data.total_pages, |p| Action::Series { name: name.clone(), author: author.clone(), page: p });
            s
        }
        Action::PromptSearch
        | Action::Book(_)
        | Action::Download { .. }
        | Action::DownloadSeries { .. } => {
            unreachable!("not a fetched screen")
        }
    })
}

fn book_screen(book: &Book) -> Screen<Action> {
    let mut s = Screen::new(book.title.clone());
    s.subtitle = Some(book.authors.clone());
    if !book.series.is_empty() {
        s.blocks.push(Block::Caption("Серия".into()));
        let label = match book.series_index {
            Some(n) => format!("{} #{n}", book.series),
            None => book.series.clone(),
        };
        let author = book.author_list.first().cloned();
        s.blocks.push(Block::row(label, None, Action::Series { name: book.series.clone(), author, page: 1 }));
    }
    if !book.author_list.is_empty() {
        s.blocks.push(Block::Caption(if book.author_list.len() == 1 { "Автор" } else { "Авторы" }.into()));
        for a in &book.author_list {
            s.blocks.push(Block::row(a, None, Action::Author { name: a.clone(), page: 1 }));
        }
    }
    s.blocks.push(Block::Caption("Подробности".into()));
    let mut fact = |label: &str, value: String| {
        if !value.is_empty() {
            s.blocks.push(Block::Fact { label: label.into(), value });
        }
    };
    fact("Формат", book.ext.clone());
    fact("Язык", book.lang.clone());
    fact("Размер", size(book.size));
    // The INPX date is when the book entered the collection, not when it was published.
    fact("Добавлена", book.added.clone().unwrap_or_default());
    fact("Жанры", book.genres.join(", "));

    let ext = if book.ext.is_empty() { "fb2" } else { &book.ext };
    s.blocks.push(Block::Caption("Скачать".into()));
    let b = Box::new(book.clone());
    s.blocks.push(Block::Button { label: format!("Читать .{ext}"), target: Action::Download { book: b.clone(), zipped: false, read: true } });
    s.blocks.push(Block::Button { label: format!(".{ext}"), target: Action::Download { book: b.clone(), zipped: false, read: false } });
    s.blocks.push(Block::Button { label: format!(".{ext}.zip"), target: Action::Download { book: b, zipped: true, read: false } });
    s
}

fn push_books(s: &mut Screen<Action>, books: &[Book], show_authors: bool, numbered: bool) {
    for book in books {
        let title = match (numbered, book.series_index) {
            (true, Some(n)) => format!("{n}. {}", book.title),
            _ => book.title.clone(),
        };
        let mut meta = Vec::new();
        if show_authors && !book.authors.is_empty() {
            meta.push(book.authors.clone());
        }
        if !numbered && !book.series.is_empty() {
            meta.push(match book.series_index {
                Some(n) => format!("{} #{n}", book.series),
                None => book.series.clone(),
            });
        }
        s.blocks.push(Block::row(title, Some(meta.join(" · ")).filter(|m| !m.is_empty()), Action::Book(Box::new(book.clone()))));
    }
}

fn pager(s: &mut Screen<Action>, page: u32, total_pages: u32, make: impl Fn(u32) -> Action) {
    if page > 1 {
        s.prev = Some(make(page - 1));
    }
    if page < total_pages {
        s.next = Some(make(page + 1));
    }
    if total_pages > 1 {
        s.page_caption = Some(format!("стр. {page} из {total_pages}"));
    }
}

fn thousands(n: u64) -> String {
    let digits = n.to_string();
    let mut out = String::new();
    for (i, c) in digits.chars().enumerate() {
        if i > 0 && (digits.len() - i) % 3 == 0 {
            out.push('\u{a0}');
        }
        out.push(c);
    }
    out
}

/// "1 книга", "3 книги", "5 книг".
fn books_count(n: u64) -> String {
    let word = match (n % 10, n % 100) {
        (1, r) if r != 11 => "книга",
        (2..=4, r) if !(12..=14).contains(&r) => "книги",
        _ => "книг",
    };
    format!("{} {word}", thousands(n))
}

fn found(n: u64) -> String {
    if n >= 100_000 { "найдено 100 000+".into() } else { format!("найдено {}", thousands(n)) }
}

fn size(bytes: u64) -> String {
    match bytes {
        0 => String::new(),
        b if b < 1024 * 1024 => format!("{} КБ", b.div_ceil(1024)),
        b => format!("{:.1} МБ", b as f64 / (1024.0 * 1024.0)),
    }
}

fn message(icon: u32, title: &str, text: &str, timeout_ms: i32) {
    let title = CString::new(title.replace('\0', " ")).unwrap_or_default();
    let text = CString::new(text.replace('\0', " ")).unwrap_or_default();
    unsafe { iv().Message(icon as i32, title.as_ptr(), text.as_ptr(), timeout_ms) };
}

fn open_book(path: &Path) {
    let p = CString::new(path.to_string_lossy().as_bytes()).unwrap_or_default();
    let empty = CString::default();
    unsafe { iv().OpenBook(p.as_ptr(), empty.as_ptr(), 0) };
}

fn exit() {
    unsafe { iv().CloseApp() };
}

unsafe extern "C" fn on_keyboard(text: *mut c_char) {
    if text.is_null() {
        return; // cancelled
    }
    let q = unsafe { CStr::from_ptr(text) }.to_string_lossy().into_owned();
    PENDING_QUERY.with(|p| *p.borrow_mut() = Some(q));
    // Run the search from a timer, once the keyboard has closed and the app is repainted.
    let name = CString::new("homelib_search").unwrap();
    unsafe { iv().SetHardTimer(name.as_ptr(), Some(run_pending_query), 50) };
}

unsafe extern "C" fn run_pending_query() {
    APP.with(|a| {
        if let Ok(mut slot) = a.try_borrow_mut() {
            if let Some(app) = slot.as_mut() {
                app.run_pending_query();
            }
        }
    });
}
