#!/bin/sh
# Packs the runtime.liblouis metapackage.
#
# This needs no native binaries and no restore of the packages it depends on, because the
# dependencies are declared directly in runtime.liblouis.nuspec. It can therefore run in any CI job,
# before or after the jobs that build the native binaries.

. "$(dirname "$0")/common.sh"

mkdir -p "$PACKAGE_OUTPUT_DIR"

dotnet pack "$REPO_ROOT/runtime.liblouis/runtime.liblouis.csproj" \
    --configuration Release \
    --output "$PACKAGE_OUTPUT_DIR"

echo "==> Metapackage written to $PACKAGE_OUTPUT_DIR"
