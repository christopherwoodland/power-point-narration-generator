#!/usr/bin/env bash
# .devcontainer/on-create.sh
# Runs once when the container image is first created (before postCreate).
# Install system-level tools that aren't available as devcontainer features.
set -euo pipefail

echo ">>> [on-create] Installing system packages..."
sudo apt-get update -qq
sudo apt-get install -y --no-install-recommends \
    ffmpeg \
    curl \
    ca-certificates \
    gnupg \
    lsb-release \
    jq

sudo rm -rf /var/lib/apt/lists/*

echo ">>> [on-create] ffmpeg version: $(ffmpeg -version 2>&1 | head -1)"
echo ">>> [on-create] Done."
