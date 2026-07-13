#!/usr/bin/env bash
# Spin up the ThornBot dev environment: RabbitMQ in a container (for
# GuestBookService), plus a dev .env if one doesn't exist yet.
# Usage: bash dev/up.sh        (from the repo root)
set -euo pipefail
cd "$(dirname "$0")/.."

RABBIT=thornbot-dev-rabbit

# Prefer podman, fall back to docker
if command -v podman >/dev/null 2>&1; then ENGINE=podman; else ENGINE=docker; fi
echo "==> Using $ENGINE"

$ENGINE rm -f $RABBIT >/dev/null 2>&1 || true
$ENGINE run -d --name $RABBIT -p 5672:5672 -p 15672:15672 docker.io/library/rabbitmq:4-management >/dev/null
echo "==> Started $RABBIT (5672, mgmt ui 15672)"

if [ -f .env ]; then
    echo "==> .env already exists, leaving it alone"
else
    cp example.env .env
    echo "==> Wrote .env from example.env — fill in TOKEN before running the bot"
fi

cat <<'EOF'

Dev environment is up. Next steps:
  1. Place a Lavalink.jar next to the built binary (or point
     Lavalink:JarPath in Resources/config.json / .env at one).
  2. dotnet run

Tear down with: bash dev/down.sh
EOF
