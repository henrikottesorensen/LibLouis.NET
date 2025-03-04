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

# Build native versions of 
cd upstream/liblouis-${LIBLOUIS_VERSION}/

# Linux i686
CONFIGURE_PARAMETERS=--enable-ucs4 --enable-year2038

./configure ${CONFIGURE_PARAMETERS} --prefix=/build/i686 --host=i686-linux-gnu
make
mkdir -p /source/runtime.liblouis/runtimes/linux-x86/native/
cp /source/upstream/liblouis-${LIBLOUIS_VERSION}/liblouis/.libs/liblouis.so /source/runtime.liblouis/runtimes/linux-x86/native/liblouis.so
make distclean

# Linux amd64
./configure ${CONFIGURE_PARAMETERS} --prefix=/build/amd64 --host=x86_64-linux-gnu
make
mkdir -p /source/runtime.liblouis/runtimes/linux-x64/native/
cp /source/upstream/liblouis-${LIBLOUIS_VERSION}/liblouis/.libs/liblouis.so /source/runtime.liblouis/runtimes/linux-x64/native/liblouis.so
make distclean

# Linux arm64
./configure ${CONFIGURE_PARAMETERS} --prefix=/build/arm64 --host=aarch64-linux-gnu
make
mkdir -p /source/runtime.liblouis/runtimes/linux-arm64/native/
cp /source/upstream/liblouis-${LIBLOUIS_VERSION}/liblouis/.libs/liblouis.so /source/runtime.liblouis/runtimes/linux-arm64/native/liblouis.so
make distclean

# Mingw32
./configure ${CONFIGURE_PARAMETERS} --prefix=/build/mingw32 --host=i686-w64-mingw32
make
mkdir -p /source/runtime.liblouis/runtimes/win-x86/native/
cp /source/upstream/liblouis-${LIBLOUIS_VERSION}/liblouis/.libs/liblouis.dll /source/runtime.liblouis/runtimes/win-x86/native/liblouis.dll
make distclean

# Mingw64
./configure ${CONFIGURE_PARAMETERS} --prefix=/build/mingw64 --host=x86_64-w64-mingw32
make
mkdir -p /source/runtime.liblouis/runtimes/win-x64/native/
cp /source/upstream/liblouis-${LIBLOUIS_VERSION}/liblouis/.libs/liblouis.dll /source/runtime.liblouis/runtimes/win-x64/native/liblouis.dll
make distclean

# Build and pack solution.
cd /source
mkdir -p /tmp/nugetRepo
dotnet nuget add source /tmp/nugetRepo

dotnet clean LibLouis.NET.sln && \
# Build and publish locally the runtimes.any.liblouis package, that the other packages depend on.
dotnet build --configuration=Release runtime.liblouis/runtime.any.liblouis.csproj && \
dotnet pack --output /packages --include-symbols --configuration Release runtime.liblouis/runtime.any.liblouis.csproj && \
dotnet nuget push --source /tmp/nugetRepo /packages/runtime.any.liblouis.${LIBLOUIS_VERSION}.nupkg

dotnet build --configuration=Release LibLouis.NET.sln && \
dotnet pack --output /packages --include-symbols --configuration Release LibLouis.NET.sln
