#!/bin/sh
# Copies the braille tables from the upstream liblouis release into the LibLouis.NET.Tables project.
#
# build_managed_packages.sh does this on its own. It is exposed separately because the tests need
# the tables too: the da-dk tables under LibLouis.NET.Test/tables `include` general tables such as
# braille-patterns.cti that only exist in the upstream release.

. "$(dirname "$0")/common.sh"

stage_tables

echo "==> Staged $(ls "$REPO_ROOT/LibLouis.NET.Tables/tables" | wc -l | tr -d ' ') tables"
