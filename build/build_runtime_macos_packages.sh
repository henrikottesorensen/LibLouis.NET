#!/bin/sh
# Builds the native liblouis binary for the macOS runtime identifiers and packs one NuGet package
# per RID. Runs on a macOS host with the Xcode command line tools.
#
# Both slices are built on whichever architecture the host happens to be. clang is a native cross
# compiler and the macOS SDK is universal, so -arch plus an explicit --host is enough. Passing
# --host also puts autoconf into cross-compilation mode, which stops configure from trying to
# execute test binaries it cannot run for the foreign slice.

. "$(dirname "$0")/common.sh"

fetch_upstream

build_runtime_nuget_macos() {
    arch=$1          # value for clang -arch
    host=$2          # autotools host triplet
    rid=$3           # .NET runtime identifier
    file_arch=$4     # what file(1) calls this architecture

    echo "==> Building liblouis for $rid ($arch)"

    (
        cd "$LIBLOUIS_SOURCE_DIR"

        export CC=clang
        export CFLAGS="-arch $arch"
        export LDFLAGS="-arch $arch"
        export CPPFLAGS="-arch $arch"

        ./configure --enable-ucs4 --enable-year2038 --host="$host"
        make -j"$(cpu_count)"

        target_dir="$REPO_ROOT/runtime.$rid.liblouis/runtimes/$rid/native"
        mkdir -p "$target_dir"
        cp "liblouis/.libs/liblouis.dylib" "$target_dir/liblouis.dylib"

        make distclean
    )

    # A silently mis-targeted binary would produce a package that only fails on the end user's
    # machine, so confirm the slice before packing it.
    built="$REPO_ROOT/runtime.$rid.liblouis/runtimes/$rid/native/liblouis.dylib"
    if ! file "$built" | grep -q "$file_arch"; then
        echo "Built library is for the wrong architecture:" >&2
        file "$built" >&2
        exit 1
    fi

    pack_runtime_package "$rid"
}

build_runtime_nuget_macos arm64  aarch64-apple-darwin osx-arm64 arm64
build_runtime_nuget_macos x86_64 x86_64-apple-darwin  osx-x64   x86_64

echo "==> macOS runtime packages written to $PACKAGE_OUTPUT_DIR"
