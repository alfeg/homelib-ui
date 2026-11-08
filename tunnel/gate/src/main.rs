//! homelib-gate: lets iroh clients reach the HomeLib HTTP server.
//!
//! Each accepted QUIC stream must open with the shared token (see `homelib-proto`); after
//! that its bytes are copied to and from a fresh TCP connection to the upstream server.
//! The gate does not look at HTTP at all.
//!
//! Configuration is environment only, so it drops into docker-compose:
//!
//! | Variable | Meaning |
//! |---|---|
//! | `HOMELIB_GATE_SECRET` | iroh secret key (hex). Fixes the endpoint id, so the ticket stays valid across restarts. `homelib-gate keygen` makes one. |
//! | `HOMELIB_TUNNEL_TOKEN` | Shared token. Must equal the relay's `IROH_RELAY_ACCESS_TOKEN`. |
//! | `HOMELIB_RELAY_URL` | Relay URL(s), comma-separated, e.g. `http://relay.example.net:3340`. Optional: without one the gate is reachable on the LAN only. |
//! | `HOMELIB_UPSTREAM` | HTTP server to forward to. Default `127.0.0.1:8080`. |
//! | `HOMELIB_GATE_PORT` | Fixed UDP port, needed for `HOMELIB_GATE_DIRECT` (random otherwise). |
//! | `HOMELIB_GATE_DIRECT` | LAN `ip:port` address(es) put in the ticket, so devices at home connect directly. Optional. |
//!
//! Commands: `serve` (default), `ticket` (print the shared ticket and exit),
//! `ticket --no-relay` (the same without relays: a LAN-only ticket, to hand out with a
//! separate `relay=` line in the app's config), `keygen`.

use std::{
    env,
    net::{Ipv4Addr, SocketAddr, SocketAddrV4},
    process::ExitCode,
    sync::Arc,
    time::Duration,
};

use homelib_proto::{ALPN, Ticket, endpoint_builder, read_hello, validate_token};
use iroh::{RelayUrl, SecretKey, endpoint::Connection};
use tokio::{io::AsyncWriteExt, net::TcpStream};
use tracing::{debug, info, warn};

/// A stream must present its token this quickly, or it is dropped.
const HELLO_TIMEOUT: Duration = Duration::from_secs(10);

struct Config {
    secret: SecretKey,
    token: String,
    relays: Vec<RelayUrl>,
    direct: Vec<SocketAddr>,
    upstream: String,
    port: Option<u16>,
}

impl Config {
    fn from_env() -> Result<Self, String> {
        let var = |name: &str| env::var(name).map_err(|_| format!("{name} is not set"));
        let secret = var("HOMELIB_GATE_SECRET")?
            .trim()
            .parse::<SecretKey>()
            .map_err(|e| format!("HOMELIB_GATE_SECRET: {e}"))?;
        let token = var("HOMELIB_TUNNEL_TOKEN")?.trim().to_string();
        validate_token(&token).map_err(|e| format!("HOMELIB_TUNNEL_TOKEN: {e}"))?;
        let relays = env::var("HOMELIB_RELAY_URL")
            .unwrap_or_default()
            .split(',')
            .map(str::trim)
            .filter(|r| !r.is_empty())
            .map(|r| r.parse::<RelayUrl>().map_err(|e| format!("HOMELIB_RELAY_URL {r}: {e}")))
            .collect::<Result<Vec<_>, _>>()?;
        let direct = env::var("HOMELIB_GATE_DIRECT")
            .unwrap_or_default()
            .split(',')
            .map(str::trim)
            .filter(|a| !a.is_empty())
            .map(|a| a.parse::<SocketAddr>().map_err(|e| format!("HOMELIB_GATE_DIRECT {a}: {e}")))
            .collect::<Result<Vec<_>, _>>()?;
        let upstream = env::var("HOMELIB_UPSTREAM").unwrap_or_else(|_| "127.0.0.1:8080".into());
        let port = match env::var("HOMELIB_GATE_PORT") {
            Ok(p) if !p.trim().is_empty() => {
                Some(p.trim().parse().map_err(|e| format!("HOMELIB_GATE_PORT: {e}"))?)
            }
            _ => None,
        };
        if !direct.is_empty() && port.is_none() {
            return Err("HOMELIB_GATE_DIRECT needs a fixed HOMELIB_GATE_PORT".into());
        }
        if relays.is_empty() && direct.is_empty() {
            return Err("set HOMELIB_RELAY_URL, HOMELIB_GATE_DIRECT, or both: clients need a way in".into());
        }
        Ok(Config { secret, token, relays, direct, upstream, port })
    }

    fn ticket(&self) -> Ticket {
        Ticket {
            endpoint: self.secret.public(),
            token: self.token.clone(),
            relays: self.relays.clone(),
            direct: self.direct.clone(),
        }
    }
}

#[tokio::main]
async fn main() -> ExitCode {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "homelib_gate=info,iroh=warn".into()),
        )
        .init();

    let command = env::args().nth(1).unwrap_or_else(|| "serve".into());
    if command == "keygen" {
        println!("{}", hex::encode(SecretKey::generate().to_bytes()));
        return ExitCode::SUCCESS;
    }

    let config = match Config::from_env() {
        Ok(c) => c,
        Err(e) => {
            eprintln!("homelib-gate: {e}");
            return ExitCode::FAILURE;
        }
    };

    match command.as_str() {
        "ticket" => {
            let mut ticket = config.ticket();
            if env::args().any(|a| a == "--no-relay") {
                if ticket.direct.is_empty() {
                    eprintln!("homelib-gate: --no-relay needs HOMELIB_GATE_DIRECT");
                    return ExitCode::FAILURE;
                }
                ticket.relays.clear();
            }
            println!("{ticket}");
            ExitCode::SUCCESS
        }
        "serve" => match serve(config).await {
            Ok(()) => ExitCode::SUCCESS,
            Err(e) => {
                eprintln!("homelib-gate: {e}");
                ExitCode::FAILURE
            }
        },
        other => {
            eprintln!("homelib-gate: unknown command '{other}' (serve | ticket | keygen)");
            ExitCode::FAILURE
        }
    }
}

async fn serve(config: Config) -> Result<(), String> {
    let mut builder = endpoint_builder(&config.relays, &config.token)
        .secret_key(config.secret.clone())
        .alpns(vec![ALPN.to_vec()]);
    if let Some(port) = config.port {
        builder = builder
            .bind_addr(SocketAddrV4::new(Ipv4Addr::UNSPECIFIED, port))
            .map_err(|e| format!("bind: {e}"))?;
    }
    let endpoint = builder.bind().await.map_err(|e| format!("bind: {e}"))?;

    info!("endpoint {} forwarding to {}", endpoint.id(), config.upstream);
    info!("ticket: {}", config.ticket());
    if tokio::time::timeout(Duration::from_secs(15), endpoint.online()).await.is_err() {
        warn!("no relay reachable yet; direct LAN connections still work");
    }

    let config = Arc::new(config);
    loop {
        tokio::select! {
            incoming = endpoint.accept() => {
                let Some(incoming) = incoming else { break };
                let config = config.clone();
                tokio::spawn(async move {
                    let accepting = match incoming.accept() {
                        Ok(a) => a,
                        Err(e) => return debug!("incoming dropped: {e}"),
                    };
                    match accepting.await {
                        Ok(conn) => serve_connection(conn, config).await,
                        Err(e) => debug!("handshake failed: {e}"),
                    }
                });
            }
            _ = tokio::signal::ctrl_c() => break,
        }
    }
    endpoint.close().await;
    Ok(())
}

async fn serve_connection(conn: Connection, config: Arc<Config>) {
    let peer = conn.remote_id();
    debug!("connection from {peer}");
    while let Ok((send, recv)) = conn.accept_bi().await {
        let config = config.clone();
        tokio::spawn(async move {
            if let Err(e) = serve_stream(send, recv, &config).await {
                debug!("stream from {} ended: {e}", peer.fmt_short());
            }
        });
    }
    debug!("connection from {} closed", peer.fmt_short());
}

async fn serve_stream(
    mut send: iroh::endpoint::SendStream,
    mut recv: iroh::endpoint::RecvStream,
    config: &Config,
) -> std::io::Result<()> {
    let authorized = tokio::time::timeout(HELLO_TIMEOUT, read_hello(&mut recv, &config.token))
        .await
        .map_err(|_| std::io::Error::new(std::io::ErrorKind::TimedOut, "no hello"))??;
    if !authorized {
        warn!("rejected stream with a wrong token");
        let _ = send.reset(1u32.into());
        return Ok(());
    }

    let tcp = TcpStream::connect(&config.upstream).await?;
    let (mut tcp_read, mut tcp_write) = tcp.into_split();

    // Request bytes go upstream as they arrive. The upstream's write side is never shut
    // down early: Kestrel treats a half-closed socket as an aborted request.
    let upload = tokio::spawn(async move {
        let _ = tokio::io::copy(&mut recv, &mut tcp_write).await;
        tcp_write
    });

    // The response ends when the server closes its side (clients send Connection: close).
    let copied = tokio::io::copy(&mut tcp_read, &mut send).await;
    upload.abort();
    let _ = send.finish();
    let _ = send.flush().await;
    copied.map(|_| ())
}
