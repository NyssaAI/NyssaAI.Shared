# Responsibility: Verify DirectorySafety segment validation, permission checks, and ProtectedFileSystem atomic writes.
# Public interface:
# +-- test_protected_file_system_creates_private_file_atomically(tmp_path)
# +-- test_validate_segment_accepts_safe_names()
# `-- test_validate_segment_rejects_traversal_and_reserved_names(segment)
"""Unit tests for DirectorySafety and ProtectedFileSystem."""

from pathlib import Path
import pytest

from nyssa_ai_configuration.file_security.directory_safety import DirectorySafety
from nyssa_ai_configuration.file_security.protected_file_system import ProtectedFileSystem


@pytest.mark.parametrize(
    "segment",
    [
        "",
        "   ",
        ".",
        "..",
        "a/b",
        "a\\b",
        "CON",
        "nul.txt",
        "LPT1",
        "trailing.",
        "trailing ",
    ],
)
def test_validate_segment_rejects_traversal_and_reserved_names(segment: str) -> None:
    """Verify DirectorySafety.validate_segment rejects unsafe or Windows-reserved names."""
    result = DirectorySafety.validate_segment(segment, "namespace")

    assert not result.ok
    assert result.category == "Validation"


def test_validate_segment_accepts_safe_names() -> None:
    """Verify DirectorySafety.validate_segment accepts valid segments."""
    result = DirectorySafety.validate_segment("billing-service_01", "namespace")

    assert result.ok
    assert result.value == "billing-service_01"


def test_protected_file_system_creates_private_file_atomically(tmp_path: Path) -> None:
    """Verify ProtectedFileSystem creates a private directory and atomically writes a private file."""
    target_dir = tmp_path / "secure_store"
    target_file = target_dir / "settings.json"

    dir_res = ProtectedFileSystem.create_directory(target_dir)
    assert dir_res.ok

    write_res = ProtectedFileSystem.create_file(target_file, '{"hello": "world"}')
    assert write_res.ok
    assert target_file.read_text(encoding="utf-8") == '{"hello": "world"}'

    verify_dir = DirectorySafety.verify(target_dir)
    assert verify_dir.ok

    verify_file = DirectorySafety.verify_file(target_file)
    assert verify_file.ok

    verify_private = DirectorySafety.verify_private_file(target_file)
    assert verify_private.ok
