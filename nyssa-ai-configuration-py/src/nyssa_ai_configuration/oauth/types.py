# Responsibility: Define OAuth 2.0 PKCE configuration options, self-describing sealed token grant records, and held grant records.
# Public interface:
# +-- OAuthHeldGrant
# |   +-- original_ciphertext
# |   +-- sealed_ciphertext
# |   `-- token
# +-- OAuthOptions
# |   +-- authorization_endpoint
# |   +-- client_id
# |   +-- redirect_port
# |   +-- scopes
# |   `-- token_endpoint
# `-- OAuthToken
#     +-- access_token
#     +-- authorization_endpoint
#     +-- client_id
#     +-- expires_at
#     +-- redirect_port
#     +-- refresh_token
#     +-- scopes
#     +-- token_endpoint
#     `-- token_type
"""OAuth 2.0 options, self-describing token, and held-grant data structures."""

from dataclasses import dataclass, field


@dataclass(frozen=True, slots=True)
class OAuthOptions:
    """Parameters required to initiate an interactive OAuth 2.0 PKCE loopback sign-in."""

    authorization_endpoint: str
    token_endpoint: str
    client_id: str
    scopes: tuple[str, ...] = field(default_factory=tuple)
    redirect_port: int | None = None


@dataclass(frozen=True, slots=True)
class OAuthToken:
    """Self-describing OAuth 2.0 grant sealed inside an 'oauth' configuration entry (tokens omitted from repr)."""

    access_token: str = field(repr=False)
    refresh_token: str = field(repr=False)
    expires_at: str
    authorization_endpoint: str
    token_endpoint: str
    client_id: str
    scopes: tuple[str, ...] = field(default_factory=tuple)
    redirect_port: int | None = None
    token_type: str = "Bearer"


@dataclass(frozen=True, slots=True)
class OAuthHeldGrant:
    """In-memory rotated OAuth grant held when local disk publication fails transiently."""

    token: OAuthToken = field(repr=False)
    sealed_ciphertext: str = field(repr=False)
    original_ciphertext: str = field(repr=False)
