# HomeLib for PocketBook

A native app for PocketBook e-readers: search a HomeLib library, browse authors and
series, download a book (or a whole series) and open it in the reader. It looks like the
server's `/m` pages and reaches the server through the [iroh tunnel](../../tunnel/README.md),
so the library can stay at home behind NAT — the reader needs only a **ticket** from
whoever runs the server.

Works on PocketBook readers with the **B288** chip (32-bit ARM, glibc 2.23+), e.g.
**Verse (PB629)**, Touch HD 3, Touch Lux 4/5. Tested on Verse with firmware 6.11.

## Install

1. Download `homelib-pocketbook-*.zip` from the
   [releases](https://github.com/alfeg/homelib-ui/releases) and unzip it.
2. Connect the reader by USB and copy into its `applications` folder:
   - `homelib.app`
   - `homelib.bin`
3. Copy `homelib.cfg.example` there as **`homelib.cfg`** and replace the `ticket=` line
   with the ticket you were given.
4. Eject the reader. **HomeLib** appears in *Applications*.

On the first start the app installs its icon (a house with books); it shows after the
reader rescans its apps, or after a reboot.

## Use

- **Найти…** or *Поиск* (top right), or the Menu key — search by title, author or series.
- Tap a row to open it; **‹ Назад** or the Back key goes back; **Выход** exits.
- Page keys or the ‹ › buttons turn pages.
- A book: **Читать** downloads and opens it; `.fb2` / `.fb2.zip` only download.
- A series: **Скачать всю серию** downloads every book in reading order.
- **Проверить подключение** shows the library, and whether the reader is connected
  directly (at home) or through the relay.

Books are saved to `/mnt/ext1/HomeLib` (the `HomeLib` folder on the reader), where the
library picks them up. Change it with `download_dir=` in `homelib.cfg`.

## Troubleshooting

Every start writes `applications/homelib.log` on the reader, including any error.

| Symptom | Likely cause |
|---|---|
| App closes at once | Started `homelib.bin` directly, or `homelib.app` is missing — the launcher sets a glibc option firmware 6.11 needs |
| "Нет билета подключения" | `homelib.cfg` missing, or no `ticket=` line in it |
| "invalid ticket" | The ticket got cut or wrapped when copying — it is one line |
| Works at home, not outside | The relay is unreachable from that network — ask the server owner |

To remove: delete `homelib.app`, `homelib.bin`, `homelib.cfg`, `homelib.log` and
`icons/homelib*.bmp` from `applications`.

## How it is built

Rust, on [inkview-rs](https://github.com/simmsb/inkview-rs), which loads `libinkview.so`
at run time — so no PocketBook SDK is needed — and with pure-Rust networking (no libcurl,
which broke apps built with older SDKs on firmware 6.10+).

`homelib.app` is a small shell launcher: firmware 6.11's glibc refuses system libraries
that ask for an executable stack, and libinkview pulls some in, so the launcher sets
`GLIBC_TUNABLES=glibc.rtld.execstack=2` (as KOReader does) and starts `homelib.bin`.

```bash
./build.sh                                  # → dist/homelib.app + dist/homelib.bin (needs Docker)
HOMELIB_TICKET='homelib2:…' ./build.sh      # bake a ticket in; no homelib.cfg needed
cargo test                                  # unit tests on the host
```

| File | Role |
|---|---|
| `src/main.rs` | screens, navigation, keys, downloads |
| `src/ui.rs` | drawing, modelled on the `/m` pages |
| `src/api.rs` | the server's JSON API |
| `src/download.rs` | saving books with FAT32-safe names |
| `src/config.rs` | `homelib.cfg` and font choice |
| `src/launcher_icon.rs` | installs the launcher icon (as [Better Stats](https://github.com/nikljuel/better-stats) does) |
| `icons/make_icons.py` | draws the icon BMPs |
