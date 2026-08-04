#!/bin/sh
# Shared helpers for the build scripts. Source this, do not run it.
#
#   . "$(dirname "$0")/common.sh"
#
# Every script in this directory is POSIX sh so it runs unchanged in the Debian-based build
# container, on the GitHub Actions macOS runner, and on a developer machine.

set -eu

REPO_ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$REPO_ROOT"

# Read a property out of Directory.Build.props so the upstream version lives in exactly one place.
read_msbuild_property() {
    value=$(sed -n "s|.*<$1>\\(.*\\)</$1>.*|\\1|p" "$REPO_ROOT/Directory.Build.props" | head -n 1)
    if [ -z "$value" ]; then
        echo "Could not read <$1> from Directory.Build.props" >&2
        exit 1
    fi
    echo "$value"
}

LIBLOUIS_VERSION=$(read_msbuild_property LiblouisVersion)
LIBLOUIS_TARBALL_SHA384=$(read_msbuild_property LiblouisTarballSha384)

# Where the finished .nupkg files land. The container overrides this to /packages so the final
# scratch stage can export them.
PACKAGE_OUTPUT_DIR=${PACKAGE_OUTPUT_DIR:-$REPO_ROOT/packages}

UPSTREAM_DIR="$REPO_ROOT/upstream"
LIBLOUIS_SOURCE_DIR="$UPSTREAM_DIR/liblouis-$LIBLOUIS_VERSION"
LIBLOUIS_TARBALL="$UPSTREAM_DIR/liblouis-$LIBLOUIS_VERSION.tar.gz"

# Usable cores, for make -j. nproc is coreutils and absent on macOS; sysctl is the BSD equivalent.
cpu_count() {
    if command -v nproc >/dev/null 2>&1; then
        nproc
    elif command -v sysctl >/dev/null 2>&1; then
        sysctl -n hw.ncpu
    else
        echo 1
    fi
}

# macOS has shasum but not sha384sum; Debian has sha384sum but not always shasum. Accept either
# rather than making the macOS job install coreutils.
sha384_of() {
    if command -v sha384sum >/dev/null 2>&1; then
        sha384sum "$1" | cut -d ' ' -f 1
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 384 "$1" | cut -d ' ' -f 1
    else
        openssl dgst -sha384 "$1" | awk '{ print $NF }'
    fi
}

# Download, verify and unpack the upstream release. Idempotent, so several scripts can call it in
# the same working tree without re-downloading.
fetch_upstream() {
    mkdir -p "$UPSTREAM_DIR"

    if [ ! -f "$LIBLOUIS_TARBALL" ]; then
        echo "Downloading liblouis $LIBLOUIS_VERSION"
        curl -fL --output "$LIBLOUIS_TARBALL" \
            "https://github.com/liblouis/liblouis/releases/download/v$LIBLOUIS_VERSION/liblouis-$LIBLOUIS_VERSION.tar.gz"
    fi

    actual=$(sha384_of "$LIBLOUIS_TARBALL")
    if [ "$actual" != "$LIBLOUIS_TARBALL_SHA384" ]; then
        echo "Checksum mismatch for $LIBLOUIS_TARBALL" >&2
        echo "  expected: $LIBLOUIS_TARBALL_SHA384" >&2
        echo "  actual:   $actual" >&2
        exit 1
    fi

    if [ ! -d "$LIBLOUIS_SOURCE_DIR" ]; then
        tar xfz "$LIBLOUIS_TARBALL" -C "$UPSTREAM_DIR"
    fi
}

# Copy the upstream braille tables into the LibLouis.NET.Tables project.
stage_tables() {
    fetch_upstream
    rm -rf "$REPO_ROOT/LibLouis.NET.Tables/tables"
    cp -R "$LIBLOUIS_SOURCE_DIR/tables" "$REPO_ROOT/LibLouis.NET.Tables/tables"
}

# Pack a per-RID runtime package. The native binary must already be staged into
# runtime.<rid>.liblouis/runtimes/<rid>/native/, which RuntimePackage.props verifies.
#
# The binary is inspected before packing rather than after, so a bad build never becomes a package
# at all. Set SKIP_NATIVE_VERIFICATION=1 to bypass, which is only reasonable when deliberately
# building something the checks are not written for.
pack_runtime_package() {
    rid=$1

    if [ -z "${SKIP_NATIVE_VERIFICATION:-}" ]; then
        binary=$(find "$REPO_ROOT/runtime.$rid.liblouis/runtimes/$rid/native" -type f | head -n 1)
        sh "$REPO_ROOT/build/verify_native_binary.sh" "$rid" "$binary"
    fi

    mkdir -p "$PACKAGE_OUTPUT_DIR"
    dotnet pack "$REPO_ROOT/runtime.$rid.liblouis/runtime.$rid.liblouis.csproj" \
        --configuration Release \
        --output "$PACKAGE_OUTPUT_DIR"
}
