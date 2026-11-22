# Responsibility: Validate namespace registry entries and resolve per-application shared namespace file paths.
# Public interface:
# `-- NamespaceRegistry
#     +-- parse(manifest_path, raw_value)
#     `-- resolve(manifest_path, app_name, shared_namespaces, registry)
"""Namespace registry parsing and application path resolution."""

import os
from collections.abc import Mapping
from pathlib import Path

from nyssa_ai_result import Result, fail, ok


class NamespaceRegistry:
    """Validates registered namespace names and resolves application file selections."""

    @staticmethod
    def parse(manifest_path: Path, raw_value: object) -> Result[dict[str, str]]:
        return _parse_namespace_registry(manifest_path, raw_value)

    @staticmethod
    def resolve(
        manifest_path: Path,
        app_name: str,
        shared_namespaces: object,
        registry: Mapping[str, str],
    ) -> Result[dict[str, str]]:
        return _resolve_namespace_files(manifest_path, app_name, shared_namespaces, registry)


def _parse_namespace_registry(manifest_path: Path, raw_value: object) -> Result[dict[str, str]]:
    """Validate registry entries while preserving their manifest-relative path values."""
    registry_raw = {} if raw_value is None else raw_value
    if not isinstance(registry_raw, dict):
        return fail(f"manifest: '{manifest_path}' 'namespaceRegistry' must be an object", "Validation")

    registry: dict[str, str] = {}
    for namespace_name, raw_path in registry_raw.items():
        if not _is_valid_namespace_name(namespace_name):
            return fail(f"manifest: namespaceRegistry.{namespace_name} has an invalid namespace identifier", "Validation")
        if not isinstance(raw_path, str) or not raw_path.strip() or any(ord(char) < 32 or ord(char) == 127 for char in raw_path):
            return fail(
                f"manifest: namespaceRegistry.{namespace_name} must name a non-blank file path without control characters",
                "Validation",
            )
        path = Path(raw_path)
        resolved_path = path.resolve() if path.is_absolute() else (manifest_path.parent / path).resolve()
        if os.path.normcase(str(resolved_path)) == os.path.normcase(str(manifest_path)):
            return fail(
                f"manifest: namespaceRegistry.{namespace_name} must not point to config-manifest.json",
                "Validation",
            )
        registry[namespace_name] = raw_path

    return ok(registry)


def _resolve_namespace_files(
    manifest_path: Path,
    app_name: str,
    shared_namespaces: object,
    registry: Mapping[str, str],
) -> Result[dict[str, str]]:
    """Resolve the selected registered files relative to the manifest directory."""
    selected = [] if shared_namespaces is None else shared_namespaces
    if not isinstance(selected, list):
        return fail(f"manifest: applications.{app_name}.sharedNamespaces must be an array of namespace names", "Validation")

    files: dict[str, str] = {}
    for index, namespace_name in enumerate(selected):
        if not _is_valid_namespace_name(namespace_name):
            return fail(
                f"manifest: applications.{app_name}.sharedNamespaces[{index}] has an invalid namespace identifier",
                "Validation",
            )
        if namespace_name not in registry:
            return fail(
                f"manifest: applications.{app_name}.sharedNamespaces[{index}] refers to unknown namespaceRegistry entry '{namespace_name}'",
                "Validation",
            )
        registered_path = Path(registry[namespace_name])
        files[namespace_name] = str(
            registered_path.resolve()
            if registered_path.is_absolute()
            else (manifest_path.parent / registered_path).resolve()
        )
    return ok(files)


def _is_valid_namespace_name(namespace_name: object) -> bool:
    """Return whether a namespace identifier is safe as a document filename."""
    if not isinstance(namespace_name, str) or not namespace_name or namespace_name != namespace_name.strip():
        return False
    if (
        namespace_name.startswith(".")
        or namespace_name.endswith(".")
        or ".." in namespace_name
        or namespace_name.lower() in {"config-manifest", "config-manifest.json"}
        or not all(char.isascii() and (char.isalnum() or char in "._-") for char in namespace_name)
    ):
        return False
    stem = namespace_name.split(".", 1)[0].upper()
    return stem not in {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    }
