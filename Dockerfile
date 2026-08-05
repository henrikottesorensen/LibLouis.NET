# The pinned platforms below are deliberate, see the comment on the first FROM.
# check=skip=FromPlatformFlagConstDisallowed

# Builds the native liblouis binaries for the Linux and Windows runtime identifiers, packs one
# NuGet package per RID, and packs the runtime.liblouis metapackage.
#
# The macOS packages are not built here; they need a macOS host and are produced by the
# native-macos CI job. The managed packages are not built here either, because building them
# resolves runtime.liblouis, which depends on the macOS packages this container cannot produce.
#
# Compiling and packing are separate stages, each on an image chosen for the job.
#
# The compilers do not need a .NET SDK: dotnet appears exactly once in the native build, to pack an
# already-compiled binary into a .nupkg. Building C on a dotnet/sdk image meant apt-get installing a
# toolchain onto an image picked for something else, and pulling in packages the build never uses -
# which is how a 404 on linux-libc-dev, a dependency of build-essential, once failed CI.
#
# So: toolchain images compile and stage binaries, and the SDK image packs whatever it finds. The
# SDK stage installs nothing at all.
#
# --platform is pinned because the cross toolchain package names below only exist for amd64. On an
# Apple Silicon machine this runs under emulation: slower, but it works.


# The five targets Ubuntu has cross compilers for.
FROM --platform=linux/amd64 ubuntu:noble AS gcc-build

# Retried, because a single apt-get run is a coin flip against archive.ubuntu.com: the index and
# the pool are not updated atomically, so a package version can be listed after it has been removed
# and the fetch 404s. Each attempt refreshes the index first, since a newer index is usually what
# resolves it. The explicit ok check matters: without it a loop that never succeeds still falls
# through and the layer builds with nothing installed.
RUN set -eu; \
    ok=0; \
    for attempt in 1 2 3; do \
        if apt-get update && apt-get install -y --no-install-recommends \
                build-essential \
                ca-certificates \
                curl \
                m4 \
                gcc-i686-linux-gnu \
                gcc-aarch64-linux-gnu \
                gcc-mingw-w64-i686 \
                gcc-mingw-w64-x86-64 \
                libc6-dev-i386-cross \
                libc6-dev-arm64-cross; then \
            ok=1; break; \
        fi; \
        echo "apt attempt $attempt failed, retrying" >&2; \
        sleep 10; \
    done; \
    [ "$ok" = 1 ] || exit 1; \
    rm -rf /var/lib/apt/lists/*
# The cross gcc packages above only Recommend their target libc, so with --no-install-recommends
# they would install a compiler that cannot link. libc6-dev-*-cross are named explicitly rather
# than dropping the flag, so the requirement is visible.

ENV SKIP_PACK=1
WORKDIR /source
COPY . /source
RUN sh ./build/build_runtime_packages.sh gcc


# win-arm64. Ubuntu has no aarch64 mingw-w64 cross compiler, so this uses llvm-mingw, taken from
# the image its own author publishes and pinned to a dated release rather than downloaded and
# checksummed by hand. The image already carries make, m4, curl and the toolchain on PATH, so this
# stage installs nothing.
#
# It is a separate stage from the gcc targets, and that separation is load bearing: llvm-mingw also
# ships i686-w64-mingw32-gcc and x86_64-w64-mingw32-gcc, so having it on PATH alongside the Ubuntu
# cross compilers silently takes over the win-x86 and win-x64 builds. That is not hypothetical - it
# happened, and it broke win-x86, because clang treats the -Wincompatible-pointer-types that gnulib
# trips on mingw as an error where gcc only warns.
FROM --platform=linux/amd64 mstorsjo/llvm-mingw:20260616 AS llvm-build

ENV SKIP_PACK=1
WORKDIR /source
COPY . /source
RUN sh ./build/build_runtime_packages.sh llvm


# Packs what the toolchain stages produced, and the metapackage, which needs no native binary at
# all. Installs nothing: dotnet pack is the only thing this stage does.
#
# The SDK version barely affects the output here - these packages are netstandard2.0 metadata around
# an already-compiled binary, with IncludeBuildOutput off - but .NET 8 goes out of support in
# November 2026, and there is no reason for the build to be the thing still on it.
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0-noble AS pack
LABEL org.opencontainers.image.source=https://github.com/Notalib/LibLouis.NET/

ENV PACKAGE_OUTPUT_DIR=/packages
WORKDIR /source
COPY . /source

COPY --from=gcc-build /source/runtime.linux-x86.liblouis/runtimes   /source/runtime.linux-x86.liblouis/runtimes
COPY --from=gcc-build /source/runtime.linux-x64.liblouis/runtimes   /source/runtime.linux-x64.liblouis/runtimes
COPY --from=gcc-build /source/runtime.linux-arm64.liblouis/runtimes /source/runtime.linux-arm64.liblouis/runtimes
COPY --from=gcc-build /source/runtime.win-x86.liblouis/runtimes     /source/runtime.win-x86.liblouis/runtimes
COPY --from=gcc-build /source/runtime.win-x64.liblouis/runtimes     /source/runtime.win-x64.liblouis/runtimes
COPY --from=llvm-build /source/runtime.win-arm64.liblouis/runtimes  /source/runtime.win-arm64.liblouis/runtimes

RUN sh ./build/pack_runtime_packages.sh && \
    sh ./build/build_metapackage.sh


# `docker build --output=packages .` exports just the .nupkg files into ./packages.
FROM scratch
COPY --from=pack /packages/* /
