#!/bin/sh
# Packs a runtime package for every RID that has a native binary staged.
#
# Compiling and packing happen in different container stages: the toolchain images have no .NET SDK
# and the SDK image has no cross compilers. The build stages leave binaries staged under
# runtime.<rid>.liblouis/runtimes/, and this packs whatever it finds.
#
# Packing whatever is present, rather than a list kept here, means the list cannot fall out of step
# with the RIDs the build stages actually produce. Finding nothing is an error: a stage that copied
# no binaries would otherwise produce an empty set of packages and look like a success.

. "$(dirname "$0")/common.sh"

packed=0

for project in "$REPO_ROOT"/runtime.*.liblouis; do
    rid=$(basename "$project" | sed 's/^runtime\.\(.*\)\.liblouis$/\1/')

    # The metapackage has no RID of its own and no native payload.
    if [ "$rid" = "liblouis" ]; then
        continue
    fi

    if [ -z "$(find "$project/runtimes" -type f 2>/dev/null | head -n 1)" ]; then
        continue
    fi

    pack_runtime_package "$rid"
    packed=$((packed + 1))
done

if [ "$packed" -eq 0 ]; then
    echo "pack_runtime_packages: no staged native binaries found under runtime.*.liblouis/runtimes/" >&2
    exit 1
fi

echo "==> Packed $packed runtime package(s) into $PACKAGE_OUTPUT_DIR"
