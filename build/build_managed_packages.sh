#!/bin/sh
# Builds, tests and packs the managed projects: LibLouis.NET and LibLouis.NET.Tables.
#
# LibLouis.NET has a PackageReference on runtime.liblouis, so the runtime packages must be
# resolvable before this runs. Point NUGET_LOCAL_FEED at a directory holding the freshly built
# .nupkg files to build against those rather than whatever is already published:
#
#   NUGET_LOCAL_FEED=$PWD/packages sh build/build_managed_packages.sh

. "$(dirname "$0")/common.sh"

stage_tables

mkdir -p "$PACKAGE_OUTPUT_DIR"

# RestoreAdditionalProjectSources is the supported way to add a feed for one invocation, without
# writing to the machine-wide NuGet configuration the way `dotnet nuget add source` does.
restore_args=""
if [ -n "${NUGET_LOCAL_FEED:-}" ]; then
    echo "==> Restoring with additional feed $NUGET_LOCAL_FEED"
    restore_args="-p:RestoreAdditionalProjectSources=$NUGET_LOCAL_FEED"
fi

dotnet build --configuration Release $restore_args "$REPO_ROOT/LibLouis.NET.sln"

if [ -z "${SKIP_TESTS:-}" ]; then
    dotnet test --configuration Release --no-build "$REPO_ROOT/LibLouis.NET.sln"
fi

dotnet pack --configuration Release --no-build \
    --output "$PACKAGE_OUTPUT_DIR" \
    "$REPO_ROOT/LibLouis.NET.Tables/LibLouis.NET.Tables.csproj"

dotnet pack --configuration Release --no-build --include-symbols \
    --output "$PACKAGE_OUTPUT_DIR" \
    "$REPO_ROOT/LibLouis.NET/LibLouis.NET.csproj"

echo "==> Managed packages written to $PACKAGE_OUTPUT_DIR"
