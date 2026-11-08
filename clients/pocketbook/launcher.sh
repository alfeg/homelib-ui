#!/bin/sh
# Installed as applications/homelib.app; the binary sits next to it as homelib.bin.

# Firmware 6.11.1480's glibc refuses system libraries that request an executable stack,
# and libinkview pulls some in. KOReader's launcher sets the same tunable.
export GLIBC_TUNABLES="${GLIBC_TUNABLES}${GLIBC_TUNABLES:+:}glibc.rtld.execstack=2"
export LC_ALL="en_US.UTF-8"

DIR="$(dirname "$0")"
LOG="$DIR/homelib.log"

# Keep the log short: only the latest run, plus enough device facts to debug a crash.
{
    echo "=== $(date)"
    uname -a
    grep -m1 -E "^(Hardware|model name)" /proc/cpuinfo
    ls /lib/ld-linux* /lib/libc.so* 2>/dev/null
    ls /ebrmain/lib/libinkview.so* /usr/lib/libinkview.so* 2>/dev/null
} > "$LOG" 2>&1

"$DIR/homelib.bin" "$@" >> "$LOG" 2>&1
echo "exit code $?" >> "$LOG"
