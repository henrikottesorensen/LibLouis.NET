#!/bin/sh
LIBLOUIS_VERSION=3.32.0
LIBLOUIS_WIN32_SHA384=f64da98c8ed72c7e21f5edf2be37f316532cdeeeb6eed2fd7fa36f67cfb6fba016501038de58cb25020c9fb2a003e5cb
LIBLOUIS_WIN64_SHA384=46e51de2ca661920c21e407508b4208053fb3e96f90626577160ce4c1f7a954757ee66a29d333d4006c14a9e0cf08c1a

# Download windows builds of liblouis
if [ ! -f upstream/liblouis-${LIBLOUIS_VERSION}-win32.zip ]; then
	curl -L --create-dirs --output-dir ./upstream https://github.com/liblouis/liblouis/releases/download/v${LIBLOUIS_VERSION}/liblouis-${LIBLOUIS_VERSION}-win32.zip -o liblouis-${LIBLOUIS_VERSION}-win32.zip
fi

if [ ! -f upstream/liblouis-${LIBLOUIS_VERSION}-win64.zip ]; then	
	curl -L --create-dirs --output-dir ./upstream https://github.com/liblouis/liblouis/releases/download/v${LIBLOUIS_VERSION}/liblouis-${LIBLOUIS_VERSION}-win64.zip -o liblouis-${LIBLOUIS_VERSION}-win64.zip
fi

# Validate checksums of upstream releases.
if ! echo "${LIBLOUIS_WIN32_SHA384} upstream/liblouis-${LIBLOUIS_VERSION}-win32.zip" | sha384sum --check; then
    echo "Checksum failed" >&2
    exit 1
fi
if ! echo "${LIBLOUIS_WIN64_SHA384} upstream/liblouis-${LIBLOUIS_VERSION}-win64.zip" | sha384sum --check; then
    echo "Checksum failed" >&2
    exit 1
fi

# Unpack upstream content
unzip -o -j upstream/liblouis-${LIBLOUIS_VERSION}-win32.zip -d runtime.liblouis/runtimes/win-x86/native bin/liblouis.dll
unzip -o -j upstream/liblouis-${LIBLOUIS_VERSION}-win64.zip -d runtime.liblouis/runtimes/win-x64/native bin/liblouis.dll
unzip -o -j upstream/liblouis-${LIBLOUIS_VERSION}-win64.zip -d LibLouis.NET.Tables/tables/ share/liblouis/tables/*

# Build and pack solution.
dotnet clean LibLouis.NET.sln
dotnet build --configuration=Release LibLouis.NET.sln
dotnet pack --output packages --include-symbols --configuration Release LibLouis.NET.sln 
