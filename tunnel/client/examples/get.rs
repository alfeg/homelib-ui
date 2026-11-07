//! Fetch one path through the tunnel: `cargo run -p homelib-client --example get -- <ticket> /api/status`
use std::io::Write;

fn main() {
    tracing_subscriber::fmt().with_env_filter(
        tracing_subscriber::EnvFilter::try_from_default_env().unwrap_or_else(|_| "warn".into()),
    ).init();
    let mut args = std::env::args().skip(1);
    let ticket = args.next().expect("ticket").parse().expect("valid ticket");
    let path = args.next().unwrap_or_else(|| "/api/status".into());
    let client = homelib_client::Client::new(ticket, "homelib-get/0.1").unwrap();
    let started = std::time::Instant::now();
    client.connect().expect("connect");
    eprintln!("connected in {:?}", started.elapsed());
    let started = std::time::Instant::now();
    let resp = client.get(&path).expect("request");
    eprintln!("HTTP {} in {:?}, {} bytes", resp.status, started.elapsed(), resp.body.len());
    eprintln!("route: {}", client.route().unwrap_or_else(|| "?".into()));
    for (k, v) in &resp.headers {
        eprintln!("{k}: {v}");
    }
    std::io::stdout().write_all(&resp.body[..resp.body.len().min(600)]).unwrap();
    println!();
}
