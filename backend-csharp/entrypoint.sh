#!/bin/bash
# Entrypoint: starts the .NET app.
# AZURE_CONFIG_DIR is set to /azure-config (mounted from host ~/.azure).
set -e
exec "$@"
