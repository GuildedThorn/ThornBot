#!/usr/bin/env bash
# Tear down the ThornBot dev environment container.
set -euo pipefail

if command -v podman >/dev/null 2>&1; then ENGINE=podman; else ENGINE=docker; fi
$ENGINE rm -f thornbot-dev-rabbit >/dev/null 2>&1 || true
echo "Removed thornbot-dev-rabbit ($ENGINE)"
