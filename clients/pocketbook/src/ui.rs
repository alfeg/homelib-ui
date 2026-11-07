//! Drawing, modelled on the thin `/m` pages so the two feel like one product:
//! a bold heading over a double rule, full-width tappable rows (title, grey meta line,
//! `›`), small uppercase captions, bordered pager and download buttons, black on white.
//!
//! A screen is a list of blocks. Blocks that do not fit are split into "views" flipped
//! with the pager or the page keys; at the last view the pager moves on to the server's
//! next page instead.

use std::ffi::CString;

use inkview::bindings::{self, Inkview, ifont};

pub const BLACK: i32 = 0x000000;
/// Secondary text. The web pages moved from #666 to #333 because mid-grey turns to mush
/// on e-ink; same here.
pub const GRAY: i32 = 0x333333;
pub const RULE: i32 = 0x999999;

const TEXT_FLAGS: i32 = (bindings::ALIGN_LEFT | bindings::VALIGN_TOP | bindings::DOTS) as i32;

#[derive(Clone, Copy, Debug, Default)]
pub struct Rect {
    pub x: i32,
    pub y: i32,
    pub w: i32,
    pub h: i32,
}

impl Rect {
    pub fn contains(&self, x: i32, y: i32) -> bool {
        x >= self.x && x < self.x + self.w && y >= self.y && y < self.y + self.h
    }
}

#[derive(Clone)]
pub enum Block<T> {
    Caption(String),
    Row { title: String, meta: Option<String>, trailing: Option<String>, target: Option<T> },
    Fact { label: String, value: String },
    Button { label: String, target: T },
    Note(String),
}

impl<T> Block<T> {
    pub fn row(title: impl Into<String>, meta: Option<String>, target: T) -> Self {
        Block::Row { title: title.into(), meta, trailing: Some("›".into()), target: Some(target) }
    }
}

pub struct Screen<T> {
    pub heading: String,
    pub subtitle: Option<String>,
    pub blocks: Vec<Block<T>>,
    /// Server paging: where ‹ goes from the first view, › from the last.
    pub prev: Option<T>,
    pub next: Option<T>,
    pub page_caption: Option<String>,
    /// Current view (screenful) within this screen's blocks.
    pub view: usize,
}

impl<T> Screen<T> {
    pub fn new(heading: impl Into<String>) -> Self {
        Screen { heading: heading.into(), subtitle: None, blocks: Vec::new(), prev: None, next: None, page_caption: None, view: 0 }
    }
}

#[derive(Clone)]
pub enum Hit<T> {
    Target(T),
    Back,
    Exit,
    Search,
    PrevView,
    NextView,
}

pub struct Fonts {
    heading: *mut ifont,
    title: *mut ifont,
    meta: *mut ifont,
    caption: *mut ifont,
    button: *mut ifont,
}

pub struct Painter {
    iv: &'static Inkview,
    fonts: Fonts,
    pub width: i32,
    pub height: i32,
    s: f32,
    soft_updates: u32,
}

/// What a render produced: tap targets and how many views the screen has.
pub struct Layout<T> {
    pub hits: Vec<(Rect, Hit<T>)>,
    pub views: usize,
}

fn cstr(s: &str) -> CString {
    CString::new(s.replace('\0', " ")).unwrap_or_default()
}

impl Painter {
    pub fn new(iv: &'static Inkview, regular: &str, bold: &str) -> Self {
        let (width, height) = unsafe { (iv.ScreenWidth(), iv.ScreenHeight()) };
        // Sizes were picked on a 758 px wide 6" panel; scale for others.
        let s = width.min(height) as f32 / 758.0;
        let px = |v: f32| (v * s).round() as i32;
        let open = |name: &str, size: i32| unsafe { iv.OpenFont(cstr(name).as_ptr(), size, 1) };
        let fonts = Fonts {
            heading: open(bold, px(36.0)),
            title: open(regular, px(31.0)),
            meta: open(regular, px(26.0)),
            caption: open(regular, px(23.0)),
            button: open(bold, px(29.0)),
        };
        Painter { iv, fonts, width, height, s, soft_updates: 0 }
    }

    fn px(&self, v: f32) -> i32 {
        (v * self.s).round() as i32
    }

    fn text_height(&self, font: *mut ifont, w: i32, text: &str) -> i32 {
        unsafe {
            self.iv.SetFont(font, BLACK);
            self.iv.TextRectHeight(w, cstr(text).as_ptr(), TEXT_FLAGS)
        }
    }

    fn line_height(&self, font: *mut ifont) -> i32 {
        self.text_height(font, 10_000, "Ag")
    }

    /// Height of `text` wrapped to `w`, capped at `max_lines`.
    fn clamp_height(&self, font: *mut ifont, w: i32, text: &str, max_lines: i32) -> i32 {
        self.text_height(font, w, text).min(self.line_height(font) * max_lines)
    }

    fn text(&self, font: *mut ifont, color: i32, r: Rect, text: &str, flags: i32) {
        unsafe {
            self.iv.SetFont(font, color);
            self.iv.DrawTextRect(r.x, r.y, r.w, r.h, cstr(text).as_ptr(), flags);
        }
    }

    fn string_width(&self, font: *mut ifont, text: &str) -> i32 {
        unsafe {
            self.iv.SetFont(font, BLACK);
            self.iv.StringWidth(cstr(text).as_ptr())
        }
    }

    fn fill(&self, r: Rect, color: i32) {
        unsafe { self.iv.FillArea(r.x, r.y, r.w, r.h, color) }
    }

    fn frame(&self, r: Rect, thickness: i32) {
        for i in 0..thickness.max(1) {
            unsafe { self.iv.DrawRect(r.x + i, r.y + i, r.w - 2 * i, r.h - 2 * i, BLACK) }
        }
    }

    fn pad(&self) -> i32 {
        self.px(20.0)
    }

    fn content_width(&self) -> i32 {
        self.width - 2 * self.pad()
    }

    fn footer_height(&self) -> i32 {
        self.px(96.0)
    }

    fn block_height<T>(&self, block: &Block<T>) -> i32 {
        let w = self.content_width();
        match block {
            Block::Caption(_) => self.px(26.0) + self.line_height(self.fonts.caption) + self.px(8.0),
            Block::Row { title, meta, .. } => {
                let tw = w - self.px(40.0);
                let mut h = self.px(14.0) + self.clamp_height(self.fonts.title, tw, title, 2);
                if let Some(m) = meta.as_deref().filter(|m| !m.is_empty()) {
                    h += self.px(4.0) + self.clamp_height(self.fonts.meta, tw, m, 2);
                }
                h + self.px(14.0) + 1
            }
            Block::Fact { label, value } => {
                let lw = w * 2 / 5;
                let lh = self.clamp_height(self.fonts.meta, lw - self.px(10.0), label, 2);
                let vh = self.clamp_height(self.fonts.meta, w - lw, value, 3);
                self.px(20.0) + lh.max(vh) + 1
            }
            Block::Button { .. } => self.line_height(self.fonts.button) + self.px(40.0) + self.px(14.0),
            Block::Note(text) => self.px(20.0) + self.clamp_height(self.fonts.meta, w, text, 6),
        }
    }

    /// Splits blocks into screenfuls of the body area. A caption never ends a view.
    fn views<T>(&self, blocks: &[Block<T>], body_h: i32) -> Vec<std::ops::Range<usize>> {
        let mut views = Vec::new();
        let mut start = 0;
        let mut used = 0;
        for (i, block) in blocks.iter().enumerate() {
            let h = self.block_height(block);
            if used + h > body_h && i > start {
                let mut end = i;
                if matches!(blocks[end - 1], Block::Caption(_)) && end - 1 > start {
                    end -= 1;
                }
                views.push(start..end);
                start = end;
                used = blocks[start..=i].iter().map(|b| self.block_height(b)).sum();
            } else {
                used += h;
            }
        }
        views.push(start..blocks.len());
        views
    }

    pub fn render<T: Clone>(&mut self, screen: &mut Screen<T>, can_back: bool, full: bool) -> Layout<T> {
        let mut hits = Vec::new();
        unsafe { self.iv.ClearScreen() };

        let header_bottom = self.draw_header(screen, can_back, &mut hits);
        let body_top = header_bottom + self.px(10.0);
        let body_h = self.height - body_top - self.footer_height();
        let views = self.views(&screen.blocks, body_h);
        if screen.view >= views.len() {
            screen.view = views.len() - 1;
        }

        let mut y = body_top;
        for block in &screen.blocks[views[screen.view].clone()] {
            let h = self.block_height(block);
            self.draw_block(block, y, h, &mut hits);
            y += h;
        }

        self.draw_pager(screen, views.len(), &mut hits);
        self.flush(full);
        Layout { hits, views: views.len() }
    }

    fn draw_header<T>(&self, screen: &Screen<T>, can_back: bool, hits: &mut Vec<(Rect, Hit<T>)>) -> i32 {
        let pad = self.pad();
        let top = self.px(14.0);

        // Right: "Поиск" link, like the search box that heads every web page.
        let link = "Поиск";
        let lw = self.string_width(self.fonts.meta, link);
        let lh = self.line_height(self.fonts.meta);
        let lx = self.width - pad - lw;
        self.text(self.fonts.meta, BLACK, Rect { x: lx, y: top + self.px(6.0), w: lw + 2, h: lh }, link, TEXT_FLAGS);
        self.fill(Rect { x: lx, y: top + self.px(6.0) + lh, w: lw, h: 1.max(self.px(1.5)) }, BLACK);
        hits.push((Rect { x: lx - self.px(16.0), y: 0, w: lw + self.px(16.0) + pad, h: top + lh + self.px(20.0) }, Hit::Search));

        let hw = lx - pad - self.px(16.0);
        let hh = self.line_height(self.fonts.heading);
        self.text(self.fonts.heading, BLACK, Rect { x: pad, y: top, w: hw, h: hh }, &screen.heading, TEXT_FLAGS);
        let mut y = top + hh + self.px(4.0);

        // Crumbs: "‹ Назад" (or "Выход" on the first screen), then the subtitle.
        let crumb = if can_back { "‹ Назад" } else { "Выход" };
        let cw = self.string_width(self.fonts.meta, crumb);
        let ch = self.line_height(self.fonts.meta);
        let cy = y + self.px(6.0);
        self.text(self.fonts.meta, BLACK, Rect { x: pad, y: cy, w: cw + 2, h: ch }, crumb, TEXT_FLAGS);
        self.fill(Rect { x: pad, y: cy + ch, w: cw, h: 1.max(self.px(1.5)) }, BLACK);
        hits.push((
            Rect { x: 0, y: cy - self.px(12.0), w: pad + cw + self.px(24.0), h: ch + self.px(26.0) },
            if can_back { Hit::Back } else { Hit::Exit },
        ));
        if let Some(sub) = screen.subtitle.as_deref().filter(|s| !s.is_empty()) {
            let sx = pad + cw;
            self.text(self.fonts.meta, GRAY, Rect { x: sx, y: cy, w: self.width - pad - sx, h: ch }, &format!(" · {sub}"), TEXT_FLAGS);
        }
        y = cy + ch + self.px(12.0);

        // The web header's 3px double rule.
        let t = 1.max(self.px(1.5));
        self.fill(Rect { x: 0, y, w: self.width, h: t }, BLACK);
        self.fill(Rect { x: 0, y: y + t + self.px(3.0), w: self.width, h: t }, BLACK);
        y + 2 * t + self.px(3.0)
    }

    fn draw_block<T: Clone>(&self, block: &Block<T>, y: i32, h: i32, hits: &mut Vec<(Rect, Hit<T>)>) {
        let pad = self.pad();
        let w = self.content_width();
        match block {
            Block::Caption(text) => {
                let lh = self.line_height(self.fonts.caption);
                self.text(self.fonts.caption, GRAY, Rect { x: pad, y: y + self.px(26.0), w, h: lh }, &text.to_uppercase(), TEXT_FLAGS);
            }
            Block::Row { title, meta, trailing, target } => {
                let tw = w - self.px(40.0);
                let th = self.clamp_height(self.fonts.title, tw, title, 2);
                let ty = y + self.px(14.0);
                self.text(self.fonts.title, BLACK, Rect { x: pad, y: ty, w: tw, h: th }, title, TEXT_FLAGS);
                if let Some(m) = meta.as_deref().filter(|m| !m.is_empty()) {
                    let mh = self.clamp_height(self.fonts.meta, tw, m, 2);
                    self.text(self.fonts.meta, GRAY, Rect { x: pad, y: ty + th + self.px(4.0), w: tw, h: mh }, m, TEXT_FLAGS);
                }
                if let Some(tr) = trailing {
                    let flags = (bindings::ALIGN_RIGHT | bindings::VALIGN_TOP) as i32;
                    self.text(self.fonts.title, GRAY, Rect { x: pad + tw, y: ty, w: w - tw, h: th }, tr, flags);
                }
                self.fill(Rect { x: pad, y: y + h - 1, w, h: 1 }, RULE);
                if let Some(t) = target {
                    hits.push((Rect { x: 0, y, w: self.width, h }, Hit::Target(t.clone())));
                }
            }
            Block::Fact { label, value } => {
                let lw = w * 2 / 5;
                let ty = y + self.px(10.0);
                let lh = self.clamp_height(self.fonts.meta, lw - self.px(10.0), label, 2);
                let vh = self.clamp_height(self.fonts.meta, w - lw, value, 3);
                self.text(self.fonts.meta, GRAY, Rect { x: pad, y: ty, w: lw - self.px(10.0), h: lh }, label, TEXT_FLAGS);
                self.text(self.fonts.meta, BLACK, Rect { x: pad + lw, y: ty, w: w - lw, h: vh }, value, TEXT_FLAGS);
                self.fill(Rect { x: pad, y: y + h - 1, w, h: 1 }, RULE);
            }
            Block::Button { label, target } => {
                let r = Rect { x: pad, y, w, h: h - self.px(14.0) };
                self.frame(r, 1.max(self.px(2.0)));
                let flags = (bindings::ALIGN_CENTER | bindings::VALIGN_MIDDLE | bindings::DOTS) as i32;
                self.text(self.fonts.button, BLACK, r, label, flags);
                hits.push((r, Hit::Target(target.clone())));
            }
            Block::Note(text) => {
                let nh = self.clamp_height(self.fonts.meta, w, text, 6);
                self.text(self.fonts.meta, GRAY, Rect { x: pad, y: y + self.px(10.0), w, h: nh }, text, TEXT_FLAGS);
            }
        }
    }

    fn draw_pager<T>(&self, screen: &Screen<T>, views: usize, hits: &mut Vec<(Rect, Hit<T>)>) {
        let can_prev = screen.view > 0 || screen.prev.is_some();
        let can_next = screen.view + 1 < views || screen.next.is_some();
        let mut caption = screen.page_caption.clone().unwrap_or_default();
        if views > 1 {
            let part = format!("{}/{}", screen.view + 1, views);
            caption = if caption.is_empty() { part } else { format!("{caption} · {part}") };
        }
        if !can_prev && !can_next && caption.is_empty() {
            return;
        }

        let top = self.height - self.footer_height();
        self.fill(Rect { x: 0, y: top, w: self.width, h: 1 }, RULE);
        let bw = self.px(120.0);
        let bh = self.px(64.0);
        let by = top + (self.footer_height() - bh) / 2;
        let center = (bindings::ALIGN_CENTER | bindings::VALIGN_MIDDLE) as i32;
        self.text(self.fonts.meta, GRAY, Rect { x: 0, y: by, w: self.width, h: bh }, &caption, center);
        if can_prev {
            let r = Rect { x: self.pad(), y: by, w: bw, h: bh };
            self.frame(r, 1);
            self.text(self.fonts.heading, BLACK, r, "‹", center);
            hits.push((Rect { x: 0, y: top, w: r.x + bw + self.px(20.0), h: self.footer_height() }, Hit::PrevView));
        }
        if can_next {
            let r = Rect { x: self.width - self.pad() - bw, y: by, w: bw, h: bh };
            self.frame(r, 1);
            self.text(self.fonts.heading, BLACK, r, "›", center);
            hits.push((Rect { x: r.x - self.px(20.0), y: top, w: self.width - r.x + self.px(20.0), h: self.footer_height() }, Hit::NextView));
        }
    }

    /// A progress screen for long downloads, in the style of the web's progress bar.
    pub fn progress(&mut self, heading: &str, line: &str, fraction: f32) {
        unsafe { self.iv.ClearScreen() };
        let pad = self.pad();
        let w = self.content_width();
        let hh = self.line_height(self.fonts.heading);
        let y = self.height / 3;
        self.text(self.fonts.heading, BLACK, Rect { x: pad, y, w, h: hh }, heading, TEXT_FLAGS);
        let bar = Rect { x: pad, y: y + hh + self.px(24.0), w, h: self.px(30.0) };
        self.frame(bar, 1);
        let inner = ((w - 4) as f32 * fraction.clamp(0.0, 1.0)) as i32;
        self.fill(Rect { x: bar.x + 2, y: bar.y + 2, w: inner, h: bar.h - 4 }, BLACK);
        let th = self.clamp_height(self.fonts.meta, w, line, 4);
        self.text(self.fonts.meta, GRAY, Rect { x: pad, y: bar.y + bar.h + self.px(18.0), w, h: th }, line, TEXT_FLAGS);
        self.flush(false);
    }

    /// Soft updates avoid the black flash; every few screens a full update clears ghosting.
    pub fn flush(&mut self, full: bool) {
        self.soft_updates += 1;
        unsafe {
            if full || self.soft_updates >= 6 {
                self.soft_updates = 0;
                self.iv.FullUpdate();
            } else {
                self.iv.SoftUpdate();
            }
        }
    }
}

impl Drop for Painter {
    fn drop(&mut self) {
        let f = &self.fonts;
        for font in [f.heading, f.title, f.meta, f.caption, f.button] {
            unsafe { self.iv.CloseFont(font) };
        }
    }
}
