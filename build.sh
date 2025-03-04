#!/bin/sh
LIBLOUIS_VERSION=3.33.0
LIBLOUIS_TARBALL_SHA384=2906b0787781c195b4386ee52090d1038990db4134705ad207b2820e790d33d9384ddea1818e32762b8c1f4f9e380a77
#LIBLOUIS_WIN32_SHA384=f64da98c8ed72c7e21f5edf2be37f316532cdeeeb6eed2fd7fa36f67cfb6fba016501038de58cb25020c9fb2a003e5cb
#LIBLOUIS_WIN64_SHA384=46e51de2ca661920c21e407508b4208053fb3e96f90626577160ce4c1f7a954757ee66a29d333d4006c14a9e0cf08c1a

# Download source tarball
if [ ! -f upstream/liblouis-${LIBLOUIS_VERSION}.tar.gz ]; then
	curl -L --create-dirs --output-dir ./upstream https://github.com/liblouis/liblouis/releases/download/v${LIBLOUIS_VERSION}/liblouis-${LIBLOUIS_VERSION}.tar.gz -o liblouis-${LIBLOUIS_VERSION}.tar.gz
fi
if ! echo "${LIBLOUIS_TARBALL_SHA384} upstream/liblouis-${LIBLOUIS_VERSION}.tar.gz" | sha384sum --check; then
    echo "Checksum failed" >&2
    exit 1
fi

# Download windows builds of liblouis
#if [ ! -f upstream/liblouis-${LIBLOUIS_VERSION}-win32.zip ]; then
#	curl -L --create-dirs --output-dir ./upstream https://github.com/liblouis/liblouis/releases/download/v${LIBLOUIS_VERSION}/liblouis-${LIBLOUIS_VERSION}-win32.zip -o liblouis-${LIBLOUIS_VERSION}-win32.zip
#fi
#if [ ! -f upstream/liblouis-${LIBLOUIS_VERSION}-win64.zip ]; then	
#	curl -L --create-dirs --output-dir ./upstream https://github.com/liblouis/liblouis/releases/download/v${LIBLOUIS_VERSION}/liblouis-${LIBLOUIS_VERSION}-win64.zip -o liblouis-${LIBLOUIS_VERSION}-win64.zip
#fi

# Validate checksums of upstream releases.
#if ! echo "${LIBLOUIS_WIN32_SHA384} upstream/liblouis-${LIBLOUIS_VERSION}-win32.zip" | sha384sum --check; then
#    echo "Checksum failed" >&2
#    exit 1
#fi
#if ! echo "${LIBLOUIS_WIN64_SHA384} upstream/liblouis-${LIBLOUIS_VERSION}-win64.zip" | sha384sum --check; then
#    echo "Checksum failed" >&2
#    exit 1
#fi

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

#unzip -o -j upstream/liblouis-${LIBLOUIS_VERSION}-win32.zip -d runtime.liblouis/runtimes/win-x86/native bin/liblouis.dll
#unzip -o -j upstream/liblouis-${LIBLOUIS_VERSION}-win64.zip -d runtime.liblouis/runtimes/win-x64/native bin/liblouis.dll

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
