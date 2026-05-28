#!/bin/bash
# SessionStart hook for Claude Code on the web.
# Provisions the Flowlio dev environment: .NET 10 SDK, the docker daemon plus the
# Postgres / Redis / RabbitMQ backing services, the Playwright Chromium browser,
# and a warm .NET restore + build. Sentry and the rest of the observability stack
# are intentionally left out.
set -euo pipefail

# Resolve the repo root whether invoked by the hook runner or by hand.
CLAUDE_PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"

# This setup only makes sense inside the ephemeral web/remote container.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  echo "session-start: not a remote session, skipping environment setup."
  exit 0
fi

echo "session-start: provisioning Flowlio dev environment..."

# --- .NET 10 SDK ----------------------------------------------------------
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

if "$DOTNET_ROOT/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "  .NET 10 SDK already installed."
else
  echo "  installing .NET 10 SDK..."
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_ROOT"
fi

# Persist the .NET environment for the rest of the session.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$DOTNET_ROOT\""
    echo "export PATH=\"$DOTNET_ROOT:$DOTNET_ROOT/tools:\$PATH\""
    echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
    echo "export DOTNET_NOLOGO=1"
  } >> "$CLAUDE_ENV_FILE"
fi

# --- Docker daemon + backing services -------------------------------------
# Docker-in-docker may not be available everywhere, so treat this block as
# best-effort: warn but don't abort the session if it can't come up.
if docker info >/dev/null 2>&1; then
  echo "  docker daemon already running."
else
  echo "  starting docker daemon..."
  setsid dockerd >/tmp/dockerd.log 2>&1 < /dev/null &
  for _ in $(seq 1 30); do
    docker info >/dev/null 2>&1 && break
    sleep 1
  done
fi

if docker info >/dev/null 2>&1; then
  echo "  starting postgres, redis, rabbitmq..."
  docker compose -f "$CLAUDE_PROJECT_DIR/docker-compose.yml" up -d postgres redis rabbitmq
else
  echo "  WARNING: docker daemon unavailable; skipping postgres/redis/rabbitmq." >&2
  echo "  ($(tail -1 /tmp/dockerd.log 2>/dev/null || echo 'no dockerd log'))" >&2
fi

# --- Playwright browser ---------------------------------------------------
# Chromium is pre-cached at $PLAYWRIGHT_BROWSERS_PATH; this is a fast no-op when present.
if command -v playwright >/dev/null 2>&1; then
  echo "  ensuring Playwright Chromium..."
  playwright install chromium || echo "  WARNING: playwright install chromium failed." >&2
fi

# --- .NET tools, restore & warm build -------------------------------------
cd "$CLAUDE_PROJECT_DIR"
echo "  restoring .NET local tools (dotnet-ef)..."
dotnet tool restore
echo "  building solution (warms the build cache, runs analyzers)..."
dotnet build Flowlio.slnx -c Debug

echo "session-start: environment ready."
