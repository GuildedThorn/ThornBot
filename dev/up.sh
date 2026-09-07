#!/usr/bin/env bash
# Spin up the ThornBot dev environment: RabbitMQ in a container (for
# GuestBookService), plus a dev .env and application.yml with freshly
# generated secrets so the defaults are never left in place.
# Usage: bash dev/up.sh        (from the repo root)
set -euo pipefail
cd "$(dirname "$0")/.."

RABBIT=thornbot-dev-rabbit

# Prefer podman, fall back to docker
if command -v podman >/dev/null 2>&1; then ENGINE=podman; else ENGINE=docker; fi
echo "==> Using $ENGINE"

$ENGINE rm -f $RABBIT >/dev/null 2>&1 || true
# Start RabbitMQ with a generated management password (15672 UI + 5672 AMQP)
RABBIT_PASS=$(openssl rand -hex 24)
$ENGINE run -d --name $RABBIT \
	-p 5672:5672 \
	-e RABBITMQ_DEFAULT_USER=thornbot \
	-e RABBITMQ_DEFAULT_PASS="$RABBIT_PASS" \
	-p 15672:15672 docker.io/library/rabbitmq:4-management >/dev/null
echo "==> Started $RABBIT (5672 amqp, 15672 mgmt ui)"

LAVALINK_PASS=$(openssl rand -hex 24)

if [ ! -f .env ]; then
	cp example.env .env
	echo "==> Wrote .env from example.env — fill in TOKEN before running the bot"
fi

# Inject declined secrets into .env (idempotent: replace if present, else append)
inject_env() {
	key="$1"
	value="$2"
	if grep -q "^${key}=" .env 2>/dev/null; then
		sed -i "s|^${key}=.*|${key}=${value}|" .env
	else
		printf '\n%s=%s\n' "$key" "$value" >>.env
	fi
}
inject_env 'Lavalink__Authorization' "$LAVALINK_PASS"
inject_env 'RabbitMQ__HostName' 'localhost'
inject_env 'RabbitMQ__UserName' 'thornbot'
inject_env 'RabbitMQ__Password' "$RABBIT_PASS"

if [ ! -f application.yml ]; then
	cp application.yml.example application.yml
elif grep -q 'CHANGE_ME_youshallnotpass' application.yml; then
	sed -i "s|CHANGE_ME_youshallnotpass|${LAVALINK_PASS}|" application.yml
fi
echo "==> application.yml password set (port 2333, loopback only)"

cat <<'EOF'

Dev environment is up. Next steps:
  1. Ensure a Lavalink.jar is next to the built binary (or point
     Lavalink:JarPath in config.json / .env at one).
  2. dotnet run

Tear down with: bash dev/down.sh
EOF
