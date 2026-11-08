# HomeLib iroh tunnel

Lets the [PocketBook app](../clients/pocketbook/README.md) reach a HomeLib server that sits
at home behind NAT — no port forwarding, no VPN, and without telling anyone the home
address.

```
            away from home                                at home
PocketBook ──iroh──► relay (small VPS) ──iroh──┐    PocketBook ──direct (LAN)──┐
                                               ▼                               ▼
                                         homelib-gate ──HTTP──► HomeLib server (docker compose)
```

- **Away from home** every byte goes through the relay. The relay runs without address
  discovery, so neither side learns the other's public IP and no direct path forms
  across the internet: the home address stays private. The relay only sees encrypted
  QUIC packets.
- **At home** the app connects straight to the gate's LAN address — fast, and it keeps
  working when the relay or the internet is down.
- The relay is addressed by a **domain name**: if its IP gets blocked, move it to another
  VPS and update DNS. Existing tickets keep working.

| Piece | Where it runs | What it is |
|---|---|---|
| `relay/` | a VPS with a public IP | stock [`iroh-relay`](https://github.com/n0-computer/iroh) with a shared-token check |
| `gate` → `homelib-gate` | next to the HomeLib server | iroh endpoint that forwards authenticated streams to HTTP |
| `client` | inside the app | blocking HTTP-over-iroh client (`examples/get.rs` for testing) |
| `proto` | both | the ticket format, ALPN and per-stream token handshake |

## The shared ticket

```
homelib2:<gate endpoint id>:<token>:[<relay url>[,<relay url>…]][#<LAN ip:port>]
```

One string gives the app everything: whom to dial, which relays, the gate's LAN address
and the token. Relays and LAN addresses are each optional, not both:

| Ticket | Works |
|---|---|
| `homelib2:<id>:<token>:#192.168.1.10:11204` | at home only, no relay involved |
| `homelib2:<id>:<token>:http://relay…:3340/#192.168.1.10:11204` | at home directly, elsewhere through the relay |

A common setup hands out the **LAN-only ticket** and gives the relay separately as a
`relay=` line in the app's `homelib.cfg` — see
[the app's config](../clients/pocketbook/homelib.cfg.example), which can also set every
part of the ticket on its own. The token is checked twice — the relay forwards only for clients that
present it, and the gate drops every stream that does not open with it (a direct LAN
connection never touches the relay). **Anyone with the ticket can use the library**, so
share it like a password. Changing the token revokes every ticket.

## Set up

### 1. Relay (once, on a VPS) — optional

Skip this if the library only needs to work on the home network.

Point a domain at the VPS (DNS only, no CDN proxy), e.g. `relay.example.net`, then:

```bash
cd tunnel/relay
cp .env.example .env                 # put a token in: openssl rand -hex 24
docker compose up -d
curl http://relay.example.net:3340/  # → 200
```

TCP port 3340 must be open on the VPS.

### 2. Gate (next to the HomeLib server)

Add to the server's `.env` (see `.env.example`):

```bash
HOMELIB_GATE_SECRET=...                      # docker run --rm ghcr.io/alfeg/homelib-gate keygen
HOMELIB_TUNNEL_TOKEN=...                     # the relay's IROH_RELAY_ACCESS_TOKEN
HOMELIB_RELAY_URL=http://relay.example.net:3340   # optional: leave out for LAN only
HOMELIB_GATE_PORT=11204                      # UDP, for direct connections at home
HOMELIB_GATE_DIRECT=192.168.1.10:11204       # this machine's LAN IP + that port
```

```bash
docker compose --profile iroh up -d
docker compose --profile iroh run --rm iroh-gate ticket              # → homelib2:…
docker compose --profile iroh run --rm iroh-gate ticket --no-relay   # LAN-only ticket
```

The secret pins the gate's identity, so the ticket stays valid across restarts and
upgrades. If the host runs a firewall (firewalld, ufw), allow UDP 11204 from the LAN.

### 3. Check it

```bash
cargo run -p homelib-client --example get -- "<ticket>" /api/status
# connected in 74ms … route: direct 192.168.1.10:11204   (at home)
# connected in 470ms … route: relay http://relay.example.net:3340/   (elsewhere)
```

## Building

```bash
cargo test                              # proto + client unit tests
docker build -t homelib-gate tunnel     # gate image (CARGO_BUILD_JOBS=2 by default)
```

The gate image is also published as `ghcr.io/alfeg/homelib-gate`. The iroh build is
heavy: on a small home server prefer the published image, or build elsewhere and
`docker save | ssh server docker load`.

## Why not dumbpipe

[dumbpipe](https://github.com/n0-computer/dumbpipe) does the same piping but is hard-wired
to n0's public relays, so it cannot use your own. The gate is the same idea in ~250 lines,
plus the token check, multiple relays and LAN addresses.

## Known limits

- The server's per-client download limit groups clients by IP address. Through the gate
  every app user arrives from the gate's address and shares one bucket (30 at once,
  10 a minute).
- Browsers, OPDS readers and the `/m` pages are not covered: they speak plain HTTPS and
  still need the server published some other way.
