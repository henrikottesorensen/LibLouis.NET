#!/bin/sh
LIBLOUIS_VERSION=3.33.0
LIBLOUIS_TARBALL_SHA384=2906b0787781c195b4386ee52090d1038990db4134705ad207b2820e790d33d9384ddea1818e32762b8c1f4f9e380a77

# Download source tarball
if [ ! -f upstream/liblouis-${LIBLOUIS_VERSION}.tar.gz ]; then
	curl -L --create-dirs --output-dir ./upstream https://github.com/liblouis/liblouis/releases/download/v${LIBLOUIS_VERSION}/liblouis-${LIBLOUIS_VERSION}.tar.gz -o liblouis-${LIBLOUIS_VERSION}.tar.gz
fi

if ! echo "${LIBLOUIS_TARBALL_SHA384} upstream/liblouis-${LIBLOUIS_VERSION}.tar.gz" | sha384sum --check; then
    echo "Checksum failed" >&2
    exit 1
fi

# Unpack upstream content
mkdir upstream
tar xvfz upstream/liblouis-${LIBLOUIS_VERSION}.tar.gz -C ./upstream/

build_runtime_nuget_macos() {
    ARCH=$1
    DOTNETARCH=$2
    CONFIGURE_PARAMETERS="--enable-ucs4 --enable-year2038"

    cd upstream/liblouis-${LIBLOUIS_VERSION}/

    export CC=clang
    export CFLAGS="-arch ${ARCH}"
    export LDFLAGS="-arch ${ARCH}"
    export CPPFLAGS="-arch ${ARCH}"

    ./configure ${CONFIGURE_PARAMETERS} --host=${ARCH}-apple-darwin
    make
    mkdir -p ../../runtime.${DOTNETARCH}.liblouis/runtimes/${DOTNETARCH}/native/
    cp liblouis/.libs/liblouis.dylib ../../runtime.${DOTNETARCH}.liblouis/runtimes/${DOTNETARCH}/native/liblouis.dylib

    if ! file ../../runtime.${DOTNETARCH}.liblouis/runtimes/${DOTNETARCH}/native/liblouis.dylib | grep ${ARCH}; then
        echo "Library is for the wrong archtecture."
        exit 1;
    fi

    make distclean

    cd ../../

    dotnet build --configuration=Release runtime.${DOTNETARCH}.liblouis/runtime.${DOTNETARCH}.liblouis.csproj
    dotnet pack --output ./packages --configuration Release runtime.${DOTNETARCH}.liblouis/runtime.${DOTNETARCH}.liblouis.csproj
}

# mac os runtimes
build_runtime_nuget_macos arm64 osx-arm64
build_runtime_nuget_macos x86_64 osx-x64
