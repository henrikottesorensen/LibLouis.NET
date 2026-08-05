#!/bin/sh
# Local build of the Linux and Windows native runtime packages plus the metapackage, using the same
# container CI uses. Results land in ./packages.
#
# The macOS runtime packages cannot be produced this way; run build/build_runtime_macos_packages.sh
# directly on a Mac for those.
set -eu

docker build --output=packages .
