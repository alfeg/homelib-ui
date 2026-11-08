//! The server's JSON API (`/api/*`), as the app uses it.

use std::io;

use homelib_client::{Client, urlencode};
use serde::Deserialize;
use serde::de::DeserializeOwned;

/// Rows per server page. Small enough that a page fits on a 6" screen in one or two views.
pub const PAGE_SIZE: u32 = 20;

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Book {
    pub uid: String,
    #[serde(default)]
    pub title: String,
    #[serde(default)]
    pub authors: String,
    #[serde(default)]
    pub author_list: Vec<String>,
    #[serde(default)]
    pub series: String,
    pub series_index: Option<i64>,
    #[serde(default)]
    pub genres: Vec<String>,
    #[serde(default)]
    pub lang: String,
    #[serde(default)]
    pub ext: String,
    #[serde(default)]
    pub size: u64,
    /// When the book entered the collection — not a publication date.
    pub added: Option<String>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct Hit {
    pub name: String,
    pub count: u64,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SearchPage {
    #[serde(default)]
    pub books: Vec<Book>,
    #[serde(default)]
    pub total: u64,
    #[serde(default = "one")]
    pub page: u32,
    #[serde(default = "one")]
    pub total_pages: u32,
    #[serde(default)]
    pub author_hits: Vec<Hit>,
    #[serde(default)]
    pub series_hits: Vec<Hit>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SeriesEntry {
    pub name: String,
    pub count: u64,
    pub first_year: Option<i32>,
    pub last_year: Option<i32>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AuthorPage {
    #[serde(default)]
    pub series: Vec<SeriesEntry>,
    #[serde(default)]
    pub books: Vec<Book>,
    #[serde(default)]
    pub total: u64,
    #[serde(default = "one")]
    pub page: u32,
    #[serde(default = "one")]
    pub total_pages: u32,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SeriesPage {
    #[serde(default)]
    pub books: Vec<Book>,
    #[serde(default)]
    pub total: u64,
    #[serde(default = "one")]
    pub total_pages: u32,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Status {
    #[serde(default)]
    pub ready: bool,
    #[serde(default)]
    pub message: String,
    pub total_books: Option<u64>,
    pub collection_title: Option<String>,
}

fn one() -> u32 {
    1
}

fn get_json<T: DeserializeOwned>(client: &Client, path: &str) -> io::Result<T> {
    let resp = client.get(path)?;
    match resp.status {
        200 => serde_json::from_slice(&resp.body)
            .map_err(|e| io::Error::new(io::ErrorKind::InvalidData, format!("JSON: {e}"))),
        503 => Err(io::Error::other("Сервер ещё индексирует библиотеку. Попробуйте позже.")),
        code => Err(io::Error::other(format!("HTTP {code}: {}", short(&resp.text())))),
    }
}

fn short(s: &str) -> String {
    s.chars().take(160).collect()
}

pub fn status(client: &Client) -> io::Result<Status> {
    // 503 still carries the status body, so read it directly.
    let resp = client.get("/api/status")?;
    serde_json::from_slice(&resp.body)
        .map_err(|e| io::Error::new(io::ErrorKind::InvalidData, format!("JSON: {e}")))
}

pub fn search(client: &Client, q: &str, page: u32, newest: bool) -> io::Result<SearchPage> {
    let mut path = format!("/api/search?page={page}&size={PAGE_SIZE}");
    if !q.is_empty() {
        path.push_str(&format!("&q={}", urlencode(q)));
    }
    if newest {
        path.push_str("&sort=new");
    }
    get_json(client, &path)
}

pub fn author(client: &Client, name: &str, page: u32) -> io::Result<AuthorPage> {
    get_json(client, &format!("/api/author?name={}&page={page}&size={PAGE_SIZE}", urlencode(name)))
}

pub fn series(client: &Client, name: &str, author: Option<&str>, page: u32, size: u32) -> io::Result<SeriesPage> {
    let mut path = format!("/api/series?name={}&page={page}&size={size}", urlencode(name));
    if let Some(a) = author {
        path.push_str(&format!("&author={}", urlencode(a)));
    }
    get_json(client, &path)
}

/// Every book of a series in reading order (the API caps a page at 100).
pub fn series_all(client: &Client, name: &str, author: Option<&str>) -> io::Result<Vec<Book>> {
    let mut books = Vec::new();
    let mut page = 1;
    loop {
        let data = series(client, name, author, page, 100)?;
        books.extend(data.books);
        if page >= data.total_pages {
            return Ok(books);
        }
        page += 1;
    }
}
