"""
Central credential factory for the PowerPoint Narration Generator.

- Production (Azure Container Apps / Managed Identity): DefaultAzureCredential
- Local Docker dev: StaticTokenCredential seeded from AZURE_STATIC_BEARER_TOKEN

The run-docker.ps1 script obtains a token via the host's 'az' CLI and passes
it to the container as an env var so DefaultAzureCredential isn't needed
inside the container (which has no 'az' CLI or managed identity).
"""
import os
import time
from typing import Any

from azure.core.credentials import AccessToken
from azure.identity import DefaultAzureCredential


class _StaticTokenCredential:
    """Wraps a pre-fetched bearer token (local Docker dev only)."""

    def __init__(self, token: str, expires_on: int) -> None:
        self._token = token
        self._expires_on = expires_on

    def get_token(self, *scopes: str, **kwargs: Any) -> AccessToken:
        if time.time() >= self._expires_on:
            raise RuntimeError(
                "AZURE_STATIC_BEARER_TOKEN has expired — "
                "re-run run-docker.ps1 to get a fresh token."
            )
        return AccessToken(self._token, self._expires_on)

    def close(self) -> None:
        pass

    def __enter__(self):
        return self

    def __exit__(self, *args):
        pass


def get_credential():
    """Return the appropriate Azure credential for the current environment."""
    token = os.environ.get("AZURE_STATIC_BEARER_TOKEN", "").strip()
    if token:
        expires_on = int(os.environ.get("AZURE_STATIC_TOKEN_EXPIRES", "0"))
        if not expires_on:
            expires_on = int(time.time()) + 3000  # conservative 50-min window
        return _StaticTokenCredential(token, expires_on)
    return DefaultAzureCredential()
