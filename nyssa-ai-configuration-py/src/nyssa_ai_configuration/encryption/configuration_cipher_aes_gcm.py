# Responsibility: Seal and open configuration values with AES-256-GCM (FormatVersion 2) and address-bound AAD.
# Public interface:
# `-- ConfigurationCipherAesGcm
#     +-- associated_data(namespace, key)
#     +-- create(key)
#     +-- new(key)
#     +-- open(value, namespace, key)
#     `-- seal(value, namespace, key)
"""AES-256-GCM FormatVersion 2 cipher matching .NET and TypeScript wire formats."""

import base64
import os
import struct
from typing import Final

from cryptography.hazmat.primitives.ciphers.aead import AESGCM

from nyssa_ai_configuration.results.result import Result, fail, ok

_FORMAT_VERSION: Final[int] = 2
_NONCE_LENGTH: Final[int] = 12
_TAG_LENGTH: Final[int] = 16
_KEY_LENGTH: Final[int] = 32
_HEADER_LENGTH: Final[int] = 1 + _NONCE_LENGTH + _TAG_LENGTH


class ConfigurationCipherAesGcm:
    """Seals and opens configuration values with AES-256-GCM and length-prefixed (namespace, key) AAD."""

    def __init__(self, key: bytes) -> None:
        """Initialize with a validated 32-byte key."""
        self._key = bytes(key)
        self._aesgcm = AESGCM(self._key)

    @classmethod
    def create(cls, key: bytes | None) -> Result["ConfigurationCipherAesGcm"]:
        """Create an AES-256-GCM cipher over a 32-byte key, returning Err(Validation) on invalid length."""
        actual = len(key) if key is not None else 0
        if key is None or actual != _KEY_LENGTH:
            return fail(
                f"The encryption key is not usable. AES-256 requires a 32-byte key; {actual} bytes were supplied.",
                "Validation",
            )
        return ok(cls(key))

    @staticmethod
    def associated_data(namespace: str, key: str) -> bytes:
        """Build FormatVersion 2 big-endian length-prefixed AAD: [0x02][len(ns):i32be][ns][len(key):i32be][key]."""
        ns_bytes = namespace.encode("utf-8")
        key_bytes = key.encode("utf-8")
        return struct.pack(
            f">Bi{len(ns_bytes)}si{len(key_bytes)}s",
            _FORMAT_VERSION,
            len(ns_bytes),
            ns_bytes,
            len(key_bytes),
            key_bytes,
        )

    def seal(self, value: str, namespace: str, key: str) -> Result[str]:
        """Encrypt value bound to (namespace, key) and return an unpadded Base64URL string."""
        nonce = os.urandom(_NONCE_LENGTH)
        plaintext = value.encode("utf-8")
        aad = self.associated_data(namespace, key)
        ct_with_tag = self._aesgcm.encrypt(nonce, plaintext, aad)
        ciphertext = ct_with_tag[:-_TAG_LENGTH]
        tag = ct_with_tag[-_TAG_LENGTH:]
        payload = bytes([_FORMAT_VERSION]) + nonce + tag + ciphertext
        encoded = base64.urlsafe_b64encode(payload).rstrip(b"=").decode("ascii")
        return ok(encoded)

    def open(self, value: str, namespace: str, key: str) -> Result[str]:
        """Decrypt and authenticate an unpadded Base64URL string bound to (namespace, key)."""
        if not value or not value.strip():
            return fail(
                "The stored secret could not be read. The sealed value is not a well-formed version 2 AES-256-GCM payload.",
                "Validation",
            )
        try:
            padded = value + "=" * ((4 - len(value) % 4) % 4)
            payload = base64.urlsafe_b64decode(padded.encode("ascii"))
        except Exception as exc:
            return fail(
                "The stored secret could not be read. The sealed value is not a well-formed version 2 AES-256-GCM payload.",
                "Validation",
                exc,
            )

        if len(payload) < _HEADER_LENGTH or payload[0] != _FORMAT_VERSION:
            return fail(
                "The stored secret could not be read. The sealed value is not a well-formed version 2 AES-256-GCM payload.",
                "Validation",
            )

        nonce = payload[1 : 1 + _NONCE_LENGTH]
        tag = payload[1 + _NONCE_LENGTH : _HEADER_LENGTH]
        ciphertext = payload[_HEADER_LENGTH:]
        aad = self.associated_data(namespace, key)
        try:
            plaintext = self._aesgcm.decrypt(nonce, ciphertext + tag, aad)
            return ok(plaintext.decode("utf-8"))
        except Exception as exc:
            return fail(
                "The stored secret could not be read. The sealed value failed authentication: it was altered, or sealed against a different namespace and key.",
                "Validation",
                exc,
            )
