# The pinned platform below is deliberate, see the comment on the FROM line.
# check=skip=FromPlatformFlagConstDisallowed

# Builds the native liblouis binaries for the Linux and Windows runtime identifiers, packs one
# NuGet package per RID, and packs the runtime.liblouis metapackage.
#
# The macOS packages are not built here; they need a macOS host and are produced by the
# native-macos CI job. The managed packages are not built here either, because building them
# resolves runtime.liblouis, which depends on the macOS packages this container cannot produce.
#
# The gcc and llvm-mingw targets are separate stages on purpose. llvm-mingw also ships
# i686-w64-mingw32-gcc and x86_64-w64-mingw32-gcc, so having it installed alongside the Ubuntu cross
# compilers means it can displace them and silently change which toolchain builds win-x86 and
# win-x64. Keeping it out of that stage entirely makes the mistake impossible rather than merely
# documented. BuildKit also builds the independent stages concurrently, so the wall clock is the
# slowest stage rather than the sum.
#
# --platform is pinned because the cross toolchain package names below only exist for amd64. On an
# Apple Silicon machine this runs under emulation: slower, but it works.
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:8.0-jammy AS base
LABEL org.opencontainers.image.source=https://github.com/Notalib/LibLouis.NET/

RUN apt-get update && \
    apt-get upgrade -y && \
    apt-get install -y --no-install-recommends \
        build-essential \
        ca-certificates \
        curl \
        m4 \
        xz-utils \
        # build/verify_native_binary.sh reads PE files with llvm-readobj, because binutils cannot
        # read aarch64 PE. readelf and nm for the ELF checks come with build-essential.
        llvm \
    && rm -rf /var/lib/apt/lists/*

ENV PACKAGE_OUTPUT_DIR=/packages
WORKDIR /source


# The five targets Ubuntu has cross compilers for.
FROM base AS gcc-targets

RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        gcc-i686-linux-gnu \
        gcc-aarch64-linux-gnu \
        gcc-mingw-w64-i686 \
        gcc-mingw-w64-x86-64 \
        # The cross gcc packages only Recommend their target libc, so with
        # --no-install-recommends they install a compiler that cannot link. Name them explicitly
        # rather than dropping the flag, so the requirement is visible.
        libc6-dev-i386-cross \
        libc6-dev-arm64-cross \
    && rm -rf /var/lib/apt/lists/*

COPY . /source
RUN sh ./build/build_runtime_packages.sh gcc


# win-arm64. Ubuntu has no aarch64 mingw-w64 cross compiler, so this stage uses the prebuilt
# llvm-mingw toolchain, pinned by digest: an unpinned toolchain would silently change what the
# published binaries were built with. Bump both values together when moving to a newer release.
FROM base AS llvm-targets

ARG LLVM_MINGW_VERSION=20260616
ARG LLVM_MINGW_SHA256=534b92e067b22a6b4441f48ae9240a3341b17825d04d577eab0cf85c44b4deda
RUN set -eu; \
    archive="llvm-mingw-${LLVM_MINGW_VERSION}-ucrt-ubuntu-22.04-x86_64.tar.xz"; \
    curl -fL -o "/tmp/$archive" \
        "https://github.com/mstorsjo/llvm-mingw/releases/download/${LLVM_MINGW_VERSION}/$archive"; \
    echo "${LLVM_MINGW_SHA256}  /tmp/$archive" | sha256sum --check; \
    mkdir -p /opt/llvm-mingw; \
    tar xf "/tmp/$archive" -C /opt/llvm-mingw --strip-components=1; \
    rm "/tmp/$archive"

# The build script prepends this to PATH for the targets that need it. Left off PATH here so there
# is exactly one mechanism selecting the toolchain, in the script, where it is visible.
ENV LLVM_MINGW_BIN=/opt/llvm-mingw/bin

COPY . /source
RUN sh ./build/build_runtime_packages.sh llvm


# The metapackage is pure metadata and needs no toolchain at all.
FROM base AS metapackage

COPY . /source
RUN sh ./build/build_metapackage.sh


# `docker build --output=packages .` exports just the .nupkg files into ./packages.
FROM scratch
COPY --from=gcc-targets /packages/* /
COPY --from=llvm-targets /packages/* /
COPY --from=metapackage /packages/* /
