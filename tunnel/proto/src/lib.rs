//! What the HomeLib iroh gate and its clients agree on.
//!
//! The gate is an iroh endpoint sitting next to the HomeLib server. A client dials it by
//! endpoint id, opens one QUIC stream per HTTP request, proves it holds the shared token,
//! and from then on the stream is a plain byte pipe to the server's HTTP port. Nothing
//! about the HTTP API changes: the gate never parses it.
//!
//! All of that is carried by one **shared ticket**:
//!
//! ```text
//! homelib1:<endpoint id>:<token>:<relay url>
//! homelib2:<endpoint id>:<token>:<relay url>[,<relay url>…][#<ip:port>[,<ip:port>…]]
//! ```
//!
//! `homelib2` adds more relays and the gate's LAN addresses. On the home network a client
//! reaches the gate at a LAN address directly, without any relay or even internet access;
//! elsewhere that address is unreachable and the connection goes through a relay. The
//! relays run without QUIC address discovery, so neither side learns the other's public IP
//! and no direct path forms across the internet: away from home all traffic is relayed and
//! the server's home address stays hidden.
//!
//! The token does double duty. The relay (an `iroh-relay` with `access.shared_token`)
//! only forwards for clients presenting it, and the gate only forwards streams that open
//! with it. A direct connection bypasses the relay, so the gate check is the one that
//! actually guards the library.

use std::{fmt, io, net::SocketAddr, str::FromStr};

use iroh::{
    Endpoint, EndpointAddr, EndpointId, RelayConfig, RelayMap, RelayMode, RelayUrl,
    endpoint::{Builder, presets},
};
use tokio::io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt};

/// ALPN of the tunnel. Bump the suffix on any incompatible change to the handshake.
pub const ALPN: &[u8] = b"homelib/http/1";

/// Longest token the handshake can carry (its length travels in one byte).
pub const MAX_TOKEN_LEN: usize = 255;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Ticket {
    pub endpoint: EndpointId,
    pub token: String,
    /// At least one. The gate homes on one of them; clients know them all.
    pub relays: Vec<RelayUrl>,
    /// LAN addresses of the gate, for direct connections at home. May be empty.
    pub direct: Vec<SocketAddr>,
}

impl Ticket {
    /// Where to dial: the gate's id, every relay, and its LAN addresses. No discovery
    /// service is used, so the ticket is the only way to find the gate.
    pub fn addr(&self) -> EndpointAddr {
        let mut addr = EndpointAddr::new(self.endpoint);
        for relay in &self.relays {
            addr = addr.with_relay_url(relay.clone());
        }
        for ip in &self.direct {
            addr = addr.with_ip_addr(*ip);
        }
        addr
    }
}

impl fmt::Display for Ticket {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let relays = join(&self.relays);
        if self.relays.len() == 1 && self.direct.is_empty() {
            return write!(f, "homelib1:{}:{}:{relays}", self.endpoint, self.token);
        }
        write!(f, "homelib2:{}:{}:{relays}", self.endpoint, self.token)?;
        if !self.direct.is_empty() {
            write!(f, "#{}", join(&self.direct))?;
        }
        Ok(())
    }
}

fn join<T: fmt::Display>(items: &[T]) -> String {
    items.iter().map(T::to_string).collect::<Vec<_>>().join(",")
}

#[derive(Debug)]
pub struct TicketError(String);

impl fmt::Display for TicketError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "invalid ticket: {}", self.0)
    }
}

impl std::error::Error for TicketError {}

fn bad(m: impl Into<String>) -> TicketError {
    TicketError(m.into())
}

impl FromStr for Ticket {
    type Err = TicketError;

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        // The relays come last because URLs contain colons of their own.
        let mut parts = s.trim().splitn(4, ':');
        let version = parts.next().unwrap_or_default();
        if version != "homelib1" && version != "homelib2" {
            return Err(bad("must start with homelib1: or homelib2:"));
        }
        let endpoint = parts
            .next()
            .ok_or_else(|| bad("missing endpoint id"))?
            .parse::<EndpointId>()
            .map_err(|e| bad(format!("bad endpoint id: {e}")))?;
        let token = parts.next().ok_or_else(|| bad("missing token"))?.to_string();
        validate_token(&token).map_err(|e| bad(e.to_string()))?;
        let rest = parts.next().ok_or_else(|| bad("missing relay url"))?;

        let (relays, direct) = match (version, rest.split_once('#')) {
            ("homelib2", Some((relays, direct))) => (relays, direct),
            _ => (rest, ""),
        };
        let relays = if version == "homelib1" {
            vec![parse_relay(relays)?]
        } else {
            relays.split(',').filter(|r| !r.is_empty()).map(parse_relay).collect::<Result<Vec<_>, _>>()?
        };
        if relays.is_empty() {
            return Err(bad("no relay url"));
        }
        let direct = direct
            .split(',')
            .filter(|a| !a.is_empty())
            .map(|a| a.parse::<SocketAddr>().map_err(|e| bad(format!("bad address {a}: {e}"))))
            .collect::<Result<Vec<_>, _>>()?;
        Ok(Ticket { endpoint, token, relays, direct })
    }
}

fn parse_relay(s: &str) -> Result<RelayUrl, TicketError> {
    s.trim().parse::<RelayUrl>().map_err(|e| bad(format!("bad relay url {s}: {e}")))
}

pub fn validate_token(token: &str) -> io::Result<()> {
    let ok = !token.is_empty()
        && token.len() <= MAX_TOKEN_LEN
        && token.bytes().all(|b| b.is_ascii_graphic() && !matches!(b, b':' | b',' | b'#'));
    if ok {
        Ok(())
    } else {
        Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            "token must be 1-255 printable ASCII characters without ':', ',' or '#'",
        ))
    }
}

/// An endpoint builder that uses only the given relays, authenticated with the token.
///
/// The relays run over plain HTTP without QUIC address discovery, so no QUIC probe is
/// configured for them. Starts from the minimal preset: no n0 relays, no DNS/pkarr lookup.
pub fn endpoint_builder(relays: &[RelayUrl], token: &str) -> Builder {
    let map: RelayMap = relays
        .iter()
        .map(|url| {
            let mut config = RelayConfig::from(url.clone()).with_auth_token(token);
            config.quic = None;
            config
        })
        .collect();
    Endpoint::builder(presets::Minimal).relay_mode(RelayMode::Custom(map))
}

/// Client side of the per-stream handshake: one length byte, then the token.
pub async fn write_hello<W: AsyncWrite + Unpin>(send: &mut W, token: &str) -> io::Result<()> {
    validate_token(token)?;
    let mut hello = Vec::with_capacity(token.len() + 1);
    hello.push(token.len() as u8);
    hello.extend_from_slice(token.as_bytes());
    send.write_all(&hello).await
}

/// Gate side of the handshake. Returns whether the stream presented the expected token.
pub async fn read_hello<R: AsyncRead + Unpin>(recv: &mut R, expected: &str) -> io::Result<bool> {
    let len = recv.read_u8().await? as usize;
    let mut got = vec![0u8; len];
    recv.read_exact(&mut got).await?;
    Ok(constant_time_eq(&got, expected.as_bytes()))
}

fn constant_time_eq(a: &[u8], b: &[u8]) -> bool {
    if a.len() != b.len() {
        return false;
    }
    a.iter().zip(b).fold(0u8, |acc, (x, y)| acc | (x ^ y)) == 0
}

#[cfg(test)]
mod tests {
    use super::*;

    fn id() -> EndpointId {
        iroh::SecretKey::from_bytes(&[7u8; 32]).public()
    }

    #[test]
    fn v1_ticket_round_trips() {
        let ticket = Ticket {
            endpoint: id(),
            token: "abc123".into(),
            relays: vec!["http://relay.example.net:3340".parse().unwrap()],
            direct: vec![],
        };
        let text = ticket.to_string();
        assert!(text.starts_with("homelib1:"));
        assert_eq!(text.parse::<Ticket>().unwrap(), ticket);
    }

    #[test]
    fn v2_ticket_round_trips() {
        let ticket = Ticket {
            endpoint: id(),
            token: "abc123".into(),
            relays: vec![
                "http://relay.example.net:3340".parse().unwrap(),
                "https://other.example.org".parse().unwrap(),
            ],
            direct: vec!["192.168.1.10:11204".parse().unwrap()],
        };
        let text = ticket.to_string();
        assert!(text.starts_with("homelib2:"));
        assert!(text.ends_with("#192.168.1.10:11204"));
        assert_eq!(text.parse::<Ticket>().unwrap(), ticket);
    }

    #[test]
    fn ticket_rejects_garbage() {
        assert!("nope".parse::<Ticket>().is_err());
        assert!("homelib1:zz:tok:http://x".parse::<Ticket>().is_err());
        assert!(format!("homelib2:{}:tok:", id()).parse::<Ticket>().is_err());
    }

    #[tokio::test]
    async fn hello_checks_token() {
        let mut buf = Vec::new();
        write_hello(&mut buf, "secret").await.unwrap();
        assert!(read_hello(&mut buf.as_slice(), "secret").await.unwrap());
        assert!(!read_hello(&mut buf.as_slice(), "other!").await.unwrap());
    }
}
