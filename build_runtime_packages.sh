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
cp -r upstream/liblouis-${LIBLOUIS_VERSION}/tables/ LibLouis.NET.Tables/tables

build_runtime_nuget() {
    ARCH=$1
    DOTNETARCH=$2
    LIBEXTENSION=$3
    CONFIGURE_PARAMETERS="--enable-ucs4 --enable-year2038"

    cd upstream/liblouis-${LIBLOUIS_VERSION}/

    ./configure ${CONFIGURE_PARAMETERS} --host=${ARCH}
    make
    mkdir -p ../../runtime.${DOTNETARCH}.liblouis/runtimes/${DOTNETARCH}/native/
    cp liblouis/.libs/liblouis.${LIBEXTENSION} ../../runtime.${DOTNETARCH}.liblouis/runtimes/${DOTNETARCH}/native/liblouis.${LIBEXTENSION}
    
    make distclean

    cd ../../

    dotnet build --configuration=Release runtime.${DOTNETARCH}.liblouis/runtime.${DOTNETARCH}.liblouis.csproj
    dotnet pack --output ./packages --configuration Release runtime.${DOTNETARCH}.liblouis/runtime.${DOTNETARCH}.liblouis.csproj
}

# Linux glibc runtimes
build_runtime_nuget i686-linux-gnu linux-x86 so
build_runtime_nuget x86_64-linux-gnu linux-x64 so 
build_runtime_nuget aarch64-linux-gnu linux-arm64 so

# Windows mingw runtines
build_runtime_nuget i686-w64-mingw32 win-x86 dll
build_runtime_nuget x86_64-w64-mingw32 win-x64 dll

# Build and publish locally the runtimes.liblouis package, that the other packages depend on.
dotnet build --configuration=Release runtime.liblouis.crosscompile.sln && \
dotnet pack --output /packages --configuration Release runtime.liblouis.crosscompile.sln

# Push runtime package to tmp repo
mkdir -p /tmp/nugetRepo
dotnet nuget add source /tmp/nugetRepo
dotnet nuget push --source /tmp/nugetRepo /packages/runtime.liblouis.${LIBLOUIS_VERSION}.nupkg
