//! Blocking HTTP client for a HomeLib server reached through the iroh gate.
//!
//! Built for an e-ink app's single UI thread: every call blocks, while a small tokio
//! runtime underneath keeps the iroh connection alive between calls.
//!
//! Each request is one QUIC stream speaking HTTP/1.0 with `Connection: close`. HTTP/1.0
//! keeps the server from answering with chunked encoding, so the body is simply
//! everything up to the end of the stream; no HTTP library is needed.

use std::{
    io::{self, Write},
    time::Duration,
};

use homelib_proto::{ALPN, Ticket, endpoint_builder, write_hello};
use iroh::{Endpoint, endpoint::Connection};
use tokio::{
    io::{AsyncRead, AsyncReadExt},
    runtime::Runtime,
    sync::Mutex,
};

const CONNECT_TIMEOUT: Duration = Duration::from_secs(30);
/// Longest wait for the next chunk of a response (TorrServe can be slow to start a book).
const READ_TIMEOUT: Duration = Duration::from_secs(240);
const MAX_HEADER_BYTES: usize = 64 * 1024;

pub struct Response {
    pub status: u16,
    pub headers: Vec<(String, String)>,
    pub body: Vec<u8>,
}

impl Response {
    /// Header value, matched case-insensitively.
    pub fn header(&self, name: &str) -> Option<&str> {
        header(&self.headers, name)
    }

    pub fn text(&self) -> String {
        String::from_utf8_lossy(&self.body).into_owned()
    }
}

/// Status and headers of a streamed download; the body went to the writer.
pub struct Head {
    pub status: u16,
    pub headers: Vec<(String, String)>,
}

impl Head {
    pub fn header(&self, name: &str) -> Option<&str> {
        header(&self.headers, name)
    }
}

fn header<'a>(headers: &'a [(String, String)], name: &str) -> Option<&'a str> {
    headers.iter().find(|(k, _)| k.eq_ignore_ascii_case(name)).map(|(_, v)| v.as_str())
}

pub struct Client {
    rt: Runtime,
    ticket: Ticket,
    endpoint: Mutex<Option<Endpoint>>,
    conn: Mutex<Option<Connection>>,
    user_agent: String,
}

impl Client {
    pub fn new(ticket: Ticket, user_agent: impl Into<String>) -> io::Result<Self> {
        let rt = tokio::runtime::Builder::new_multi_thread()
            .worker_threads(2)
            .enable_all()
            .build()?;
        Ok(Client {
            rt,
            ticket,
            endpoint: Mutex::new(None),
            conn: Mutex::new(None),
            user_agent: user_agent.into(),
        })
    }

    pub fn ticket(&self) -> &Ticket {
        &self.ticket
    }

    /// How the connection currently reaches the gate: `"direct 192.168.1.5:11204"` on the
    /// home network, `"relay http://…"` elsewhere. `None` before the first connection.
    pub fn route(&self) -> Option<String> {
        self.rt.block_on(async {
            let slot = self.conn.lock().await;
            let conn = slot.as_ref()?;
            let paths = conn.paths();
            let path = paths.iter().find(|p| p.is_selected())?;
            Some(match path.remote_addr() {
                iroh::TransportAddr::Ip(ip) => format!("direct {ip}"),
                iroh::TransportAddr::Relay(url) => format!("relay {url}"),
                other => format!("{other:?}"),
            })
        })
    }

    /// Dials the gate now rather than on the first request.
    pub fn connect(&self) -> io::Result<()> {
        self.rt.block_on(async { self.connection().await.map(|_| ()) })
    }

    /// GET a path such as `/api/search?q=x` and buffer the whole response.
    pub fn get(&self, path: &str) -> io::Result<Response> {
        let mut body = Vec::new();
        let head = self.get_to(path, &mut body)?;
        Ok(Response { status: head.status, headers: head.headers, body })
    }

    /// GET a path, streaming the body into `out` (for downloads).
    pub fn get_to(&self, path: &str, out: &mut dyn Write) -> io::Result<Head> {
        self.rt.block_on(async {
            // A stale connection (Wi-Fi slept, gate restarted) fails on open; retry once.
            match self.request(path, out).await {
                Err(e) if e.kind() == io::ErrorKind::NotConnected => {
                    *self.conn.lock().await = None;
                    self.request(path, out).await
                }
                other => other,
            }
        })
    }

    async fn connection(&self) -> io::Result<Connection> {
        let mut slot = self.conn.lock().await;
        if let Some(conn) = slot.as_ref() {
            if conn.close_reason().is_none() {
                return Ok(conn.clone());
            }
        }
        let endpoint = self.endpoint().await?;
        let conn = tokio::time::timeout(CONNECT_TIMEOUT, endpoint.connect(self.ticket.addr(), ALPN))
            .await
            .map_err(|_| io::Error::new(io::ErrorKind::TimedOut, "server did not answer"))?
            .map_err(|e| io::Error::new(io::ErrorKind::ConnectionRefused, e.to_string()))?;
        *slot = Some(conn.clone());
        Ok(conn)
    }

    async fn endpoint(&self) -> io::Result<Endpoint> {
        let mut slot = self.endpoint.lock().await;
        if let Some(ep) = slot.as_ref() {
            return Ok(ep.clone());
        }
        let ep = endpoint_builder(&self.ticket.relays, &self.ticket.token)
            .bind()
            .await
            .map_err(|e| io::Error::other(format!("iroh: {e}")))?;
        *slot = Some(ep.clone());
        Ok(ep)
    }

    async fn request(&self, path: &str, out: &mut dyn Write) -> io::Result<Head> {
        let conn = self.connection().await?;
        let (mut send, mut recv) = conn
            .open_bi()
            .await
            .map_err(|e| io::Error::new(io::ErrorKind::NotConnected, e.to_string()))?;

        write_hello(&mut send, &self.ticket.token).await?;
        let request = format!(
            "GET {path} HTTP/1.0\r\nHost: homelib\r\nUser-Agent: {}\r\nAccept: */*\r\nConnection: close\r\n\r\n",
            self.user_agent
        );
        send.write_all(request.as_bytes()).await?;
        let _ = send.finish();

        read_response(&mut recv, out).await
    }
}

impl Drop for Client {
    fn drop(&mut self) {
        let endpoint = self.endpoint.get_mut().take();
        if let Some(ep) = endpoint {
            self.rt.block_on(async {
                let _ = tokio::time::timeout(Duration::from_secs(2), ep.close()).await;
            });
        }
    }
}

async fn read_chunk<R: AsyncRead + Unpin>(recv: &mut R, buf: &mut [u8]) -> io::Result<usize> {
    tokio::time::timeout(READ_TIMEOUT, recv.read(buf))
        .await
        .map_err(|_| io::Error::new(io::ErrorKind::TimedOut, "server stopped responding"))?
}

async fn read_response<R: AsyncRead + Unpin>(recv: &mut R, out: &mut dyn Write) -> io::Result<Head> {
    let mut buf = Vec::with_capacity(8192);
    let mut chunk = vec![0u8; 64 * 1024];
    let split = loop {
        if let Some(i) = find(&buf, b"\r\n\r\n") {
            break i;
        }
        if buf.len() > MAX_HEADER_BYTES {
            return Err(io::Error::new(io::ErrorKind::InvalidData, "response header too large"));
        }
        let n = read_chunk(recv, &mut chunk).await?;
        if n == 0 {
            return Err(io::Error::new(
                io::ErrorKind::UnexpectedEof,
                "connection closed before a response (wrong token?)",
            ));
        }
        buf.extend_from_slice(&chunk[..n]);
    };

    let head = parse_head(&buf[..split])?;
    out.write_all(&buf[split + 4..])?;
    loop {
        let n = read_chunk(recv, &mut chunk).await?;
        if n == 0 {
            break;
        }
        out.write_all(&chunk[..n])?;
    }
    out.flush()?;
    Ok(head)
}

fn find(haystack: &[u8], needle: &[u8]) -> Option<usize> {
    haystack.windows(needle.len()).position(|w| w == needle)
}

fn parse_head(raw: &[u8]) -> io::Result<Head> {
    let text = String::from_utf8_lossy(raw);
    let mut lines = text.split("\r\n");
    let status_line = lines.next().unwrap_or_default();
    let status = status_line
        .split_whitespace()
        .nth(1)
        .and_then(|s| s.parse().ok())
        .ok_or_else(|| io::Error::new(io::ErrorKind::InvalidData, format!("bad status line: {status_line}")))?;
    let headers = lines
        .filter_map(|l| l.split_once(':'))
        .map(|(k, v)| (k.trim().to_string(), v.trim().to_string()))
        .collect();
    Ok(Head { status, headers })
}

/// Percent-encodes a query value.
pub fn urlencode(s: &str) -> String {
    let mut out = String::with_capacity(s.len() * 3);
    for b in s.bytes() {
        if b.is_ascii_alphanumeric() || matches!(b, b'-' | b'_' | b'.' | b'~') {
            out.push(b as char);
        } else {
            out.push_str(&format!("%{b:02X}"));
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn parses_response_and_streams_body() {
        let raw = b"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Disposition: attachment\r\n\r\n{\"a\":1}";
        let mut body = Vec::new();
        let head = read_response(&mut raw.as_slice(), &mut body).await.unwrap();
        assert_eq!(head.status, 200);
        assert_eq!(head.header("content-type"), Some("application/json"));
        assert_eq!(body, b"{\"a\":1}");
    }

    #[tokio::test]
    async fn empty_stream_is_an_error() {
        let mut body = Vec::new();
        assert!(read_response(&mut b"".as_slice(), &mut body).await.is_err());
    }

    #[test]
    fn encodes_cyrillic() {
        assert_eq!(urlencode("ж b"), "%D0%B6%20b");
    }
}
