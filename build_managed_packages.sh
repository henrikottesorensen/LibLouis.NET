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

# Build and pack solution.
dotnet clean LibLouis.NET.sln && \
dotnet build --configuration=Release LibLouis.NET.sln && \
dotnet pack --output /packages --configuration Release LibLouis.NET.sln && \
dotnet pack --output /packages --include-symbols --configuration Release LibLouis.NET/LibLouis.NET.csproj
