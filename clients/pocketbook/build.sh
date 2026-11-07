#!/bin/sh
# Builds dist/homelib.app for B288 PocketBooks (glibc 2.23) inside Docker.
#
#   ./build.sh                                # ticket read from homelib.cfg on the device
#   HOMELIB_TICKET=homelib1:... ./build.sh    # ticket baked into the binary
set -e
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
target=armv7-unknown-linux-gnueabi

docker build -q -t homelib-pb-builder -f "$here/Dockerfile.build" "$here" >/dev/null
docker run --rm \
  -e HOMELIB_TICKET \
  -v "$repo:/src" \
  -v homelib-pb-cargo:/usr/local/cargo/registry \
  homelib-pb-builder \
  sh -c "cargo zigbuild --release --target $target.2.23 && chown -R $(id -u):$(id -g) target"

mkdir -p "$here/dist"
# homelib.app is the launcher script (firmware 6.11 needs a glibc tunable set before the
# binary starts); the binary itself is homelib.bin.
cp "$here/target/$target/release/homelib" "$here/dist/homelib.bin"
tr -d '\r' < "$here/launcher.sh" > "$here/dist/homelib.app"
echo "built $here/dist/homelib.app + homelib.bin"
