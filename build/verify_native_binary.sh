#!/bin/sh
# Verifies one built native liblouis binary before it is packed.
#
#   verify_native_binary.sh <rid> <path-to-binary>
#
# common.sh calls this from pack_runtime_package, so every runtime package is checked on the
# machine that built it. Run it by hand against an extracted package to audit a published one.
#
# The checks exist because each of them corresponds to something that actually went wrong, or that
# would only surface on an end user's machine:
#
#   architecture   a mis-targeted binary produces a package that restores fine and never loads.
#   exports        the expected symbols come from the EntryPoint attributes in NativeMethod.cs
#                  rather than a list kept here, so the check cannot drift away from the wrapper.
#   dependencies   win-x86 once imported libgcc_s_dw2-1.dll, a mingw runtime DLL not shipped in the
#                  package. It works on a developer machine that has mingw and fails everywhere
#                  else.
#   glibc          the floor decides which distributions can consume the Linux packages. It is a
#                  property of the build image, so it can rise silently when that image is bumped.
#
# Exits non-zero listing every failure, rather than stopping at the first.

set -eu

if [ $# -ne 2 ]; then
    echo "usage: $0 <rid> <path-to-binary>" >&2
    exit 2
fi

rid=$1
binary=$2

REPO_ROOT=$(cd "$(dirname "$0")/.." && pwd)
NATIVE_METHODS="$REPO_ROOT/LibLouis.NET/NativeMethod.cs"

# Highest glibc symbol version the Linux binaries may require. Raising this drops support for
# distributions older than the new value, so it is a deliberate decision, not a build detail.
# 2.34 covers RHEL 9 (2.34), Debian 12 (2.36) and Ubuntu 22.04 (2.35) and later.
MAX_GLIBC=2.34

failures=0
fail() {
    echo "  FAIL  $*" >&2
    failures=$((failures + 1))
}
ok() {
    echo "  ok    $*"
}

if [ ! -f "$binary" ]; then
    echo "verify_native_binary: '$binary' does not exist" >&2
    exit 1
fi

# llvm ships its tools with a version suffix on Debian and Ubuntu, and unsuffixed in llvm-mingw.
find_tool() {
    if command -v "$1" >/dev/null 2>&1; then
        command -v "$1"
        return 0
    fi
    for candidate in $(ls /usr/bin/"$1"-* /usr/lib/llvm-*/bin/"$1" 2>/dev/null | sort -Vr); do
        if [ -x "$candidate" ]; then
            echo "$candidate"
            return 0
        fi
    done
    return 1
}

# Every EntryPoint the managed wrapper P/Invokes. If the wrapper gains a function and the native
# library does not export it, that is a runtime EntryPointNotFoundException, so catch it here.
expected_symbols() {
    grep -o 'EntryPoint = "[^"]*"' "$NATIVE_METHODS" | sed 's/.*"\(.*\)"/\1/' | sort -u
}

echo "verifying $rid: $binary"

case "$rid" in
    linux-*)
        readelf=$(find_tool readelf) || { echo "readelf not found" >&2; exit 1; }

        case "$rid" in
            linux-x86)   want_class=ELF32; want_machine="Intel 80386" ;;
            linux-x64)   want_class=ELF64; want_machine="X86-64" ;;
            linux-arm64) want_class=ELF64; want_machine="AArch64" ;;
            *) echo "unknown rid $rid" >&2; exit 2 ;;
        esac

        header=$("$readelf" -h "$binary")
        if echo "$header" | grep -q "$want_class" && echo "$header" | grep -q "$want_machine"; then
            ok "architecture $want_class $want_machine"
        else
            fail "architecture: expected $want_class $want_machine, got $(echo "$header" | grep Machine:)"
        fi

        missing=""
        exports=$("$(find_tool nm)" -D --defined-only "$binary" | awk '$2 == "T" { print $3 }')
        for symbol in $(expected_symbols); do
            echo "$exports" | grep -qx "$symbol" || missing="$missing $symbol"
        done
        [ -z "$missing" ] && ok "all $(expected_symbols | wc -l | tr -d ' ') P/Invoke symbols exported" \
                          || fail "not exported:$missing"

        # ld-linux is the loader, not a library that has to be shipped.
        unexpected=$("$readelf" -d "$binary" | sed -n 's/.*NEEDED.*\[\(.*\)\].*/\1/p' \
            | grep -vE '^(libc\.so\.6|libm\.so\.6|libdl\.so\.2|libpthread\.so\.0|ld-linux.*)$' || true)
        [ -z "$unexpected" ] && ok "no dependencies outside the allowlist" \
                             || fail "unexpected dependencies: $(echo "$unexpected" | tr '\n' ' ')"

        required=$("$readelf" -V "$binary" 2>/dev/null | grep -o 'GLIBC_[0-9.]*' | sed 's/GLIBC_//' \
            | sort -uV | tail -1)
        if [ -z "$required" ]; then
            ok "no versioned glibc references"
        elif [ "$(printf '%s\n%s\n' "$required" "$MAX_GLIBC" | sort -V | tail -1)" = "$MAX_GLIBC" ]; then
            ok "requires at most GLIBC_$required (floor is $MAX_GLIBC)"
        else
            fail "requires GLIBC_$required, above the declared floor of $MAX_GLIBC. Either the build image changed or new code pulled in a newer symbol; raising MAX_GLIBC drops support for older distributions."
        fi
        ;;

    win-*)
        # binutils cannot read aarch64 PE, so all three Windows targets go through llvm.
        readobj=$(find_tool llvm-readobj) || { echo "llvm-readobj not found" >&2; exit 1; }

        case "$rid" in
            win-x86)   want_machine=IMAGE_FILE_MACHINE_I386 ;;
            win-x64)   want_machine=IMAGE_FILE_MACHINE_AMD64 ;;
            win-arm64) want_machine=IMAGE_FILE_MACHINE_ARM64 ;;
            *) echo "unknown rid $rid" >&2; exit 2 ;;
        esac

        if "$readobj" --file-headers "$binary" 2>/dev/null | grep -q "$want_machine"; then
            ok "architecture $want_machine"
        else
            fail "architecture: expected $want_machine"
        fi

        missing=""
        exports=$("$readobj" --coff-exports "$binary" 2>/dev/null | sed -n 's/.*Name: \(.*\)/\1/p')
        for symbol in $(expected_symbols); do
            echo "$exports" | grep -qx "$symbol" || missing="$missing $symbol"
        done
        [ -z "$missing" ] && ok "all $(expected_symbols | wc -l | tr -d ' ') P/Invoke symbols exported" \
                          || fail "not exported:$missing"

        # Anything outside this list has to ship with the package, and nothing else does.
        unexpected=$("$readobj" --coff-imports "$binary" 2>/dev/null | sed -n 's/.*Name: \(.*\)/\1/p' \
            | sort -u | grep -viE '^(KERNEL32\.dll|msvcrt\.dll|USER32\.dll|ADVAPI32\.dll|api-ms-win-.*\.dll)$' || true)
        [ -z "$unexpected" ] && ok "no dependencies outside the allowlist" \
                             || fail "unexpected dependencies: $(echo "$unexpected" | tr '\n' ' ')"
        ;;

    osx-*)
        case "$rid" in
            osx-x64)   want_arch=x86_64 ;;
            osx-arm64) want_arch=arm64 ;;
            *) echo "unknown rid $rid" >&2; exit 2 ;;
        esac

        if file -b "$binary" | grep -q "$want_arch"; then
            ok "architecture $want_arch"
        else
            fail "architecture: expected $want_arch, got $(file -b "$binary")"
        fi

        missing=""
        # Mach-O prefixes symbols with an underscore.
        exports=$(nm -gU "$binary" | awk '$2 == "T" { print $3 }' | sed 's/^_//')
        for symbol in $(expected_symbols); do
            echo "$exports" | grep -qx "$symbol" || missing="$missing $symbol"
        done
        [ -z "$missing" ] && ok "all $(expected_symbols | wc -l | tr -d ' ') P/Invoke symbols exported" \
                          || fail "not exported:$missing"

        # The first otool -L line is the library's own install name, not a dependency.
        unexpected=$(otool -L "$binary" | tail -n +3 | sed 's/^[[:space:]]*//; s/ (.*//' \
            | grep -vE '^(/usr/lib/libSystem\.B\.dylib|/usr/lib/libc\+\+\.1\.dylib)$' || true)
        [ -z "$unexpected" ] && ok "no dependencies outside the allowlist" \
                             || fail "unexpected dependencies: $(echo "$unexpected" | tr '\n' ' ')"
        ;;

    *)
        echo "verify_native_binary: unknown runtime identifier '$rid'" >&2
        exit 2
        ;;
esac

if [ "$failures" -ne 0 ]; then
    echo "verify_native_binary: $rid failed $failures check(s)" >&2
    exit 1
fi
