#!/usr/bin/env bash
# Installs the official self-hosted Sentry (getsentry/self-hosted) into ./.sentry-self-hosted.
# This is a heavy, separate stack (Kafka, ClickHouse, Snuba, Relay, Postgres, Redis, web, workers …);
# it is NOT part of Flowlio's docker-compose.yml. Recommended: Docker + Compose v2, >= 16 GB RAM.
#
# Usage:
#   SENTRY_VERSION=25.1.0 ./observability/sentry/install-self-hosted.sh
# Pick a release tag from https://github.com/getsentry/self-hosted/releases
set -euo pipefail

VERSION="${SENTRY_VERSION:-25.1.0}"
TARGET="${SENTRY_DIR:-.sentry-self-hosted}"

command -v docker >/dev/null || { echo "Docker is required."; exit 1; }

if [ ! -d "$TARGET" ]; then
  echo "Cloning getsentry/self-hosted @ $VERSION into $TARGET …"
  git clone --depth 1 --branch "$VERSION" https://github.com/getsentry/self-hosted.git "$TARGET"
fi

cd "$TARGET"
echo "Running install.sh (this takes a while and downloads several GB) …"
# --skip-user-creation: create the admin later with `docker compose run --rm web createuser`
./install.sh

echo
echo "Done. Start Sentry with:   (cd $TARGET && docker compose up -d)"
echo "Sentry UI will be at:      http://localhost:9000"
echo "Then create a project, copy its DSN, and set it for Flowlio (see README.md)."
