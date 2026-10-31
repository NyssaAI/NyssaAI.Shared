# Responsibility: Verify that configuration directories, files, and segment names are safe from other accounts and path traversal.
# Public interface:
# `-- DirectorySafety
#     +-- validate_segment(segment, label)
#     +-- verify(target_path)
#     +-- verify_file(file_path)
#     `-- verify_private_file(file_path)
"""Directory and file permission safety checks across POSIX and Windows."""

from pathlib import Path
import re
import subprocess
import sys
from typing import Final

from nyssa_ai_configuration.results.result import Result, fail, ok

_MULTI_USER_IDENTITIES: Final[frozenset[str]] = frozenset(
    s.lower()
    for s in (
        "Everyone",
        "Authenticated Users",
        "NT AUTHORITY\\Authenticated Users",
        "BUILTIN\\Users",
        "Users",
        "BUILTIN\\Guests",
        "Guests",
        "*S-1-1-0",
        "S-1-1-0",
        "*S-1-5-11",
        "S-1-5-11",
        "*S-1-5-32-545",
        "S-1-5-32-545",
        "*S-1-5-32-546",
        "S-1-5-32-546",
    )
)
_WINDOWS_RESERVED: Final[frozenset[str]] = frozenset(
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9",
    }
)
_WRITE_RIGHTS: Final[frozenset[str]] = frozenset({"F", "M", "W", "WD", "AD", "WDAC", "WO", "GW", "GA"})
_READ_RIGHTS: Final[frozenset[str]] = frozenset({"F", "M", "RX", "R", "RD", "GR", "GA"})
_TOKEN_RE: Final[re.Pattern[str]] = re.compile(r"\(([^)]+)\)")


class DirectorySafety:
    """Inspects directory and file permissions and validates namespace/key path segments."""

    @staticmethod
    def validate_segment(segment: str, label: str = "namespace") -> Result[str]:
        """Validate that a namespace or filename segment cannot escape the configuration root."""
        if not segment or not segment.strip():
            return fail(f"The {label} cannot be blank.", "Validation")
        if segment != segment.strip() or segment.endswith("."):
            return fail(f"The {label} '{segment}' cannot have leading/trailing spaces or trailing dots.", "Validation")
        if "/" in segment or "\\" in segment or ".." in segment or ":" in segment or "\x00" in segment:
            return fail(f"The {label} '{segment}' contains forbidden path characters.", "Validation")
        stem = segment.split(".", 1)[0].upper()
        if stem in _WINDOWS_RESERVED:
            return fail(f"The {label} '{segment}' is a reserved device name.", "Validation")
        return ok(segment)

    @staticmethod
    def verify(target_path: str | Path) -> Result[None]:
        """Verify that a directory is not writable by other accounts on the machine."""
        text = str(target_path).strip()
        if not text:
            return ok(None)
        resolved = Path(text).resolve()
        if not resolved.exists():
            return ok(None)
        return DirectorySafety._verify_writable(resolved, text)

    @staticmethod
    def verify_file(file_path: str | Path) -> Result[None]:
        """Verify that a file is not writable by other accounts on the machine."""
        text = str(file_path).strip()
        if not text:
            return ok(None)
        resolved = Path(text).resolve()
        if not resolved.exists():
            return ok(None)
        return DirectorySafety._verify_writable(resolved, text)

    @staticmethod
    def verify_private_file(file_path: str | Path) -> Result[None]:
        """Verify that a secret-bearing file is neither writable nor readable by other accounts."""
        writable = DirectorySafety.verify_file(file_path)
        if not writable.ok:
            return writable
        text = str(file_path).strip()
        if not text:
            return ok(None)
        resolved = Path(text).resolve()
        if not resolved.exists():
            return ok(None)
        try:
            if sys.platform == "win32":
                return DirectorySafety._verify_readable_windows(resolved, text)
            return DirectorySafety._verify_readable_unix(resolved, text)
        except Exception as exc:
            return fail(f"configuration: could not read permissions on '{text}': {exc}", "Forbidden", exc)

    @staticmethod
    def _verify_writable(resolved: Path, display_path: str) -> Result[None]:
        try:
            if sys.platform == "win32":
                return DirectorySafety._verify_writable_windows(resolved, display_path)
            return DirectorySafety._verify_writable_unix(resolved, display_path)
        except Exception as exc:
            return fail(f"configuration: could not read permissions on '{display_path}': {exc}", "Forbidden", exc)

    @staticmethod
    def _verify_writable_unix(resolved: Path, display_path: str) -> Result[None]:
        st = resolved.stat()
        mode = st.st_mode & 0o777
        fix_cmd = f"chmod 700 '{display_path}'" if resolved.is_dir() else f"chmod 600 '{display_path}'"
        if (mode & 0o002) != 0:
            return fail(f"configuration: '{display_path}' is writable by other (o+w) — fix with: {fix_cmd}", "Forbidden")
        if (mode & 0o020) != 0:
            return fail(f"configuration: '{display_path}' is writable by group (g+w) — fix with: {fix_cmd}", "Forbidden")
        return ok(None)

    @staticmethod
    def _verify_readable_unix(resolved: Path, display_path: str) -> Result[None]:
        st = resolved.stat()
        mode = st.st_mode & 0o777
        fix_cmd = f"chmod 600 '{display_path}'"
        if (mode & 0o005) != 0:
            return fail(
                f"manifest: '{display_path}' is readable by other (o+r) — fix with: {fix_cmd}",
                "Forbidden",
            )
        if (mode & 0o050) != 0:
            return fail(
                f"manifest: '{display_path}' is readable by group (g+r) — fix with: {fix_cmd}",
                "Forbidden",
            )
        return ok(None)

    @staticmethod
    def _verify_writable_windows(resolved: Path, display_path: str) -> Result[None]:
        for identity, is_deny, rights in DirectorySafety._read_windows_icacls(resolved):
            if is_deny or identity.lower() not in _MULTI_USER_IDENTITIES:
                continue
            if any(r in _WRITE_RIGHTS for r in rights):
                return fail(
                    f'configuration: \'{display_path}\' is writable by {identity} — fix with: icacls "{display_path}" /inheritance:r /grant:r "%USERNAME%:F"',
                    "Forbidden",
                )
        return ok(None)

    @staticmethod
    def _verify_readable_windows(resolved: Path, display_path: str) -> Result[None]:
        for identity, is_deny, rights in DirectorySafety._read_windows_icacls(resolved):
            if is_deny or identity.lower() not in _MULTI_USER_IDENTITIES:
                continue
            if any(r in _READ_RIGHTS for r in rights):
                return fail(
                    f'manifest: \'{display_path}\' is readable by {identity} — fix with: icacls "{display_path}" /inheritance:r /grant:r "%USERNAME%:F"',
                    "Forbidden",
                )
        return ok(None)

    @staticmethod
    def _read_windows_icacls(resolved: Path) -> list[tuple[str, bool, list[str]]]:
        proc = subprocess.run(
            ["icacls.exe", str(resolved)],
            capture_output=True,
            text=True,
            check=False,
        )
        entries: list[tuple[str, bool, list[str]]] = []
        resolved_str = str(resolved)
        for idx, raw_line in enumerate(proc.stdout.splitlines()):
            line = raw_line[len(resolved_str) :] if idx == 0 and raw_line.startswith(resolved_str) else raw_line
            trimmed = line.strip()
            if not trimmed or trimmed.startswith("Successfully processed"):
                continue
            colon_idx = trimmed.find(":(")
            if colon_idx <= 0:
                continue
            identity = trimmed[:colon_idx].strip()
            tokens = _TOKEN_RE.findall(trimmed[colon_idx + 1 :])
            if not tokens:
                continue
            is_deny = any(t.upper() in ("DENY", "N") for t in tokens)
            rights = [r.strip().upper() for r in tokens[-1].split(",") if r.strip()]
            entries.append((identity, is_deny, rights))
        return entries
