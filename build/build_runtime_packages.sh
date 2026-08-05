#!/bin/sh
# Cross-compiles the native liblouis binary for the Linux and Windows runtime identifiers and packs
# one NuGet package per RID.
#
# Usage: build_runtime_packages.sh [gcc|llvm|all]
#
#   gcc   the five targets Ubuntu ships cross compilers for
#   llvm  win-arm64, which needs the llvm-mingw toolchain
#   all   both, the default
#
# The container builds the two groups in separate stages so the llvm-mingw toolchain is not even
# present while the gcc targets are built; see the Dockerfile. Expects the cross toolchains from
# that image, so it is normally invoked through ./build.sh rather than directly.

. "$(dirname "$0")/common.sh"

group=${1:-all}
case "$group" in
    gcc|llvm|all) ;;
    *) echo "Unknown target group '$group', expected gcc, llvm or all" >&2; exit 1 ;;
esac

fetch_upstream

if [ "$group" = "llvm" ]; then
    # configure.ac adds -Wl,--add-stdcall-alias for every *mingw* host, on the assumption that
    # mingw implies x86. The flag decorates i386 stdcall symbols, means nothing on ARM64, and lld
    # rejects unknown arguments outright where GNU ld tolerated it. Left in place it does not break
    # the library, which is linked before the flag reaches anything that matters, but every tool in
    # tools/ then fails to link and make carries on regardless, which buries real errors in noise.
    #
    # Patching it out is safe here only because this group builds win-arm64 alone, in its own
    # container stage with its own copy of the source. Doing it for the gcc group would break
    # win-x86, where stdcall decoration is meaningful.
    echo "==> Removing -Wl,--add-stdcall-alias from configure (meaningless on ARM64, rejected by lld)"
    sed -i 's|CFLAGS="$CFLAGS -Wl,--add-stdcall-alias"||' "$LIBLOUIS_SOURCE_DIR/configure"
fi

build_runtime_nuget() {
    host=$1          # autotools host triplet, selects the cross toolchain
    rid=$2           # .NET runtime identifier
    library=$3       # file name the .NET host will probe for
    extra_path=${4:-}     # toolchain directory to put ahead of PATH, if not the default one
    extra_cflags=${5:-}   # replaces the default CFLAGS when set
    extra_ldflags=${6:-}  # appended to LDFLAGS by configure

    echo "==> Building liblouis for $rid ($host)"

    (
        cd "$LIBLOUIS_SOURCE_DIR"

        # Scoped to this subshell so one target's toolchain cannot leak into the next.
        if [ -n "$extra_path" ]; then
            PATH="$extra_path:$PATH"
            export PATH
        fi
        if [ -n "$extra_cflags" ]; then
            CFLAGS="$extra_cflags"
            export CFLAGS
        fi
        if [ -n "$extra_ldflags" ]; then
            LDFLAGS="$extra_ldflags"
            export LDFLAGS
        fi

        # --enable-ucs4 makes widechar 4 bytes, which is what the managed marshalling assumes.
        ./configure --enable-ucs4 --enable-year2038 --host="$host"

        # liblouis/Makefile has all-am depend on liblouis-<soversion>.def, a file with no rule of
        # its own: it appears as a side effect of linking liblouis.la with -Wl,--output-def. Under
        # -j make is free to schedule the .def before that link and dies with "No rule to make
        # target". The .def is only generated for Windows, so the other targets parallelise fine.
        case "$host" in
            *mingw*) jobs=1 ;;
            *) jobs=$(cpu_count) ;;
        esac

        # Only the library is packaged, so only the library is built. The tree also contains
        # tools/, tables/, man/, doc/, tests/, python/ and windows/, none of which reach a package.
        #
        # This is not just speed. A failure in tools/ does not stop the top level make - the
        # win-arm64 build once emitted eight link errors there and still exited 0, which buried a
        # real problem in the log. Building only what is shipped means any failure is about the
        # thing being shipped. It also removes libtool's "could not determine the host path"
        # warnings, which come from wrapper scripts generated around uninstalled executables.
        #
        # gnulib first and explicitly: liblouis links against gnulib/libgnu.la, and make will not
        # build a sibling directory on demand - it stops with "No rule to make target".
        if [ -n "$extra_cflags" ]; then
            make -j"$jobs" -C gnulib CFLAGS="$extra_cflags"
            make -j"$jobs" -C liblouis CFLAGS="$extra_cflags"
        else
            make -j"$jobs" -C gnulib
            make -j"$jobs" -C liblouis
        fi

        target_dir="$REPO_ROOT/runtime.$rid.liblouis/runtimes/$rid/native"
        mkdir -p "$target_dir"
        # cp dereferences the .so symlink autotools leaves behind, giving us the real binary.
        cp "liblouis/.libs/$library" "$target_dir/$library"

        make distclean
    )

    pack_runtime_package "$rid"
}

if [ "$group" = "gcc" ] || [ "$group" = "all" ]; then
    # Linux, glibc
    build_runtime_nuget i686-linux-gnu    linux-x86   liblouis.so
    build_runtime_nuget x86_64-linux-gnu  linux-x64   liblouis.so
    build_runtime_nuget aarch64-linux-gnu linux-arm64 liblouis.so

    # Windows, mingw-w64.
    #
    # -static-libgcc matters for win-x86: 32-bit mingw uses DWARF-2 exception handling, whose
    # unwinder lives in libgcc_s_dw2-1.dll, so without it the DLL carries a load time dependency on
    # a mingw runtime that the package does not ship and an end user will not have. 64-bit mingw
    # uses SEH and does not need it, but it is passed there too so both Windows gcc targets are
    # built the same way.
    build_runtime_nuget i686-w64-mingw32   win-x86 liblouis.dll "" "" "-static-libgcc"
    build_runtime_nuget x86_64-w64-mingw32 win-x64 liblouis.dll "" "" "-static-libgcc"
fi

if [ "$group" = "llvm" ] || [ "$group" = "all" ]; then
    # Windows on ARM. Ubuntu has no aarch64 mingw-w64 gcc, so this one comes from llvm-mingw.
    #
    # clang rejects gnulib's DIR / struct gl_directory mismatch in metadata.c, which gcc only warns
    # about, so that one diagnostic is turned back into a warning. The default CFLAGS have to be
    # repeated because setting CFLAGS replaces them.
    build_runtime_nuget aarch64-w64-mingw32 win-arm64 liblouis.dll \
        "${LLVM_MINGW_BIN:-/opt/llvm-mingw/bin}" \
        "-g -O2 -Wno-incompatible-pointer-types"
fi

echo "==> Native runtime packages written to $PACKAGE_OUTPUT_DIR"
