//! Saving `/download/{uid}` responses as files the reader's library will pick up.

use std::{
    fs::{self, File},
    io::{self, BufWriter},
    path::{Path, PathBuf},
    time::Duration,
};

use homelib_client::{Client, urlencode};

use crate::api::Book;

/// Leaves room for the extension and a " (2)" suffix inside FAT32's 255-byte limit.
const MAX_STEM_BYTES: usize = 200;

pub enum Outcome {
    Saved(PathBuf),
    /// Server busy (429/503); try again after this long.
    Busy(Duration, String),
    Failed(String),
}

pub fn download(client: &Client, book: &Book, dir: &Path, zipped: bool) -> Outcome {
    if let Err(e) = fs::create_dir_all(dir) {
        return Outcome::Failed(format!("Нет доступа к {}: {e}", dir.display()));
    }
    let tmp = dir.join(format!(".homelib-{}.part", book.uid));
    let result = fetch(client, book, &tmp, zipped);
    let head = match result {
        Ok(h) => h,
        Err(e) => {
            let _ = fs::remove_file(&tmp);
            return Outcome::Failed(e.to_string());
        }
    };

    if head.status != 200 {
        let body = fs::read(&tmp).unwrap_or_default();
        let _ = fs::remove_file(&tmp);
        let text: String = String::from_utf8_lossy(&body).chars().take(200).collect();
        if matches!(head.status, 429 | 503) {
            let wait = head.header("retry-after").and_then(|v| v.trim().parse().ok()).unwrap_or(30);
            return Outcome::Busy(Duration::from_secs(wait), text);
        }
        return Outcome::Failed(format!("HTTP {}: {text}", head.status));
    }

    let fallback = format!(
        "{} - {}.{}{}",
        book.authors,
        if book.title.is_empty() { &book.uid } else { &book.title },
        if book.ext.is_empty() { "fb2" } else { &book.ext },
        if zipped { ".zip" } else { "" }
    );
    let name = safe_filename(
        &head.header("content-disposition").and_then(filename_from).unwrap_or(fallback),
    );

    // Fetching the same book again (re-running a series) keeps the copy already there.
    let existing = dir.join(&name);
    if same_size(&existing, &tmp) {
        let _ = fs::remove_file(&tmp);
        return Outcome::Saved(existing);
    }
    let target = unique_path(dir, &name);
    match fs::rename(&tmp, &target) {
        Ok(()) => Outcome::Saved(target),
        Err(e) => {
            let _ = fs::remove_file(&tmp);
            Outcome::Failed(format!("Не удалось сохранить {}: {e}", target.display()))
        }
    }
}

fn fetch(client: &Client, book: &Book, tmp: &Path, zipped: bool) -> io::Result<homelib_client::Head> {
    let path = format!("/download/{}{}", urlencode(&book.uid), if zipped { ".zip" } else { "" });
    let mut out = BufWriter::new(File::create(tmp)?);
    client.get_to(&path, &mut out)
}

fn same_size(a: &Path, b: &Path) -> bool {
    match (fs::metadata(a), fs::metadata(b)) {
        (Ok(x), Ok(y)) => x.len() == y.len(),
        _ => false,
    }
}

fn unique_path(dir: &Path, name: &str) -> PathBuf {
    let first = dir.join(name);
    if !first.exists() {
        return first;
    }
    let (stem, ext) = match name.rfind('.') {
        Some(i) if i > 0 => (&name[..i], &name[i..]),
        _ => (name, ""),
    };
    (2..100)
        .map(|n| dir.join(format!("{stem} ({n}){ext}")))
        .find(|p| !p.exists())
        .unwrap_or(first)
}

/// Filename from a Content-Disposition header, preferring the RFC 5987 `filename*` form.
pub fn filename_from(header: &str) -> Option<String> {
    let lower = header.to_ascii_lowercase();
    if let Some(i) = lower.find("filename*=") {
        let value = header[i + 10..].split(';').next()?.trim();
        let encoded = value.strip_prefix("UTF-8''").or_else(|| value.strip_prefix("utf-8''"))?;
        return Some(percent_decode(encoded));
    }
    let i = lower.find("filename=")?;
    let value = header[i + 9..].split(';').next()?.trim();
    Some(value.trim_matches('"').to_string()).filter(|s| !s.is_empty())
}

fn percent_decode(s: &str) -> String {
    let bytes = s.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'%' && i + 2 < bytes.len() {
            if let Ok(b) = u8::from_str_radix(&s[i + 1..i + 3], 16) {
                out.push(b);
                i += 3;
                continue;
            }
        }
        out.push(bytes[i]);
        i += 1;
    }
    String::from_utf8_lossy(&out).into_owned()
}

/// A FAT32-safe filename: no reserved characters, stem cut on a UTF-8 boundary.
pub fn safe_filename(name: &str) -> String {
    let cleaned: String = name
        .chars()
        .map(|c| if "/\\:*?\"<>|".contains(c) || c.is_control() { '_' } else { c })
        .collect();
    let cleaned = cleaned.trim_matches(|c: char| c.is_whitespace() || c == '.');
    let (stem, ext) = split_ext(cleaned);
    let mut cut = stem.len().min(MAX_STEM_BYTES);
    while !stem.is_char_boundary(cut) {
        cut -= 1;
    }
    format!("{}{ext}", stem[..cut].trim_end())
}

/// Splits ".fb2.zip" as one extension, otherwise the last dot.
fn split_ext(name: &str) -> (&str, &str) {
    let lower = name.to_ascii_lowercase();
    if lower.ends_with(".zip") {
        if let Some(i) = name[..name.len() - 4].rfind('.') {
            if name.len() - 4 - i <= 6 {
                return (&name[..i], &name[i..]);
            }
        }
    }
    match name.rfind('.') {
        Some(i) if i > 0 && name.len() - i <= 6 => (&name[..i], &name[i..]),
        _ => (name, ""),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn prefers_rfc5987_name() {
        let h = "attachment; filename=\"a.fb2\"; filename*=UTF-8''%D0%9C%D0%B8%D1%80.fb2";
        assert_eq!(filename_from(h).as_deref(), Some("Мир.fb2"));
        assert_eq!(filename_from("attachment; filename=plain.fb2").as_deref(), Some("plain.fb2"));
    }

    #[test]
    fn sanitizes_and_truncates() {
        assert_eq!(safe_filename("a/b:c?.fb2"), "a_b_c_.fb2");
        let long = safe_filename(&format!("{}.fb2.zip", "ж".repeat(150)));
        assert!(long.ends_with(".fb2.zip"));
        assert!(long.len() <= MAX_STEM_BYTES + 8);
    }
}
