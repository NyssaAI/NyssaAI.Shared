# Responsibility: Inspect Python declarations and enforce single-responsibility headers, DTO grouping, strict typing, and layer boundaries.
# Public interface:
# (none)
"""Deterministic AST structural checker for Python source roots."""

from __future__ import annotations

import ast
import json
import os
import re
import sys
from pathlib import Path

BANNED_DIRS = {"contracts", "shared", "utils", "helpers", "common", "misc", "types", "models"}
BANNED_SCRATCH = {"tmp", "temp", "scratch"}
EXCLUDED_DIRS = {"__pycache__", "node_modules", "dist", "build", "bin", "obj", "venv", "env"}
DATA_BASES = {"Enum", "StrEnum", "IntEnum", "TypedDict", "NamedTuple"}
ALLOWED_BASES = DATA_BASES | {"Protocol", "Generic", "object"}
HEADER_ENTRY_RE = re.compile(r"^# (?:\(none\)|[| ]*(?:\+-- |`-- ).*)$")


def to_snake_case(name: str) -> str:
    """Convert PascalCase or camelCase identifiers to snake_case."""
    step1 = re.sub(r"([A-Z]+)([A-Z][a-z])", r"\1_\2", name)
    step2 = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", step1)
    return step2.lower()


def base_name(expr: ast.expr) -> str:
    """Extract the simple identifier name of a base class expression."""
    if isinstance(expr, ast.Name):
        return expr.id
    if isinstance(expr, ast.Attribute):
        return expr.attr
    if isinstance(expr, ast.Subscript):
        return base_name(expr.value)
    return ""


def is_dataclass_decorated(node: ast.ClassDef) -> bool:
    """Return True when the class has a @dataclass decorator."""
    for dec in node.decorator_list:
        target = dec.func if isinstance(dec, ast.Call) else dec
        if base_name(target) == "dataclass":
            return True
    return False


def is_property_decorated(node: ast.FunctionDef | ast.AsyncFunctionDef) -> bool:
    """Return True when the method is decorated with @property."""
    return any(base_name(dec) == "property" for dec in node.decorator_list)


def format_params(args: ast.arguments, drop_receiver: bool = False) -> str:
    """Format callable parameter names for the public interface tree."""
    all_args: list[str] = [a.arg for a in (*args.posonlyargs, *args.args)]
    if drop_receiver and all_args and all_args[0] in ("self", "cls"):
        all_args = all_args[1:]
    if args.vararg:
        all_args.append(f"*{args.vararg.arg}")
    all_args.extend(a.arg for a in args.kwonlyargs)
    if args.kwarg:
        all_args.append(f"**{args.kwarg.arg}")
    return ", ".join(all_args)


def union_arm_names(expr: ast.expr) -> set[str]:
    """Collect simple type names referenced in a union expression (A | B)."""
    if isinstance(expr, ast.BinOp) and isinstance(expr.op, ast.BitOr):
        return union_arm_names(expr.left) | union_arm_names(expr.right)
    if isinstance(expr, ast.Subscript) and base_name(expr.value) == "Union":
        if isinstance(expr.slice, ast.Tuple):
            names: set[str] = set()
            for elt in expr.slice.elts:
                names |= union_arm_names(elt)
            return names
        return union_arm_names(expr.slice)
    name = base_name(expr)
    return {name} if name else set()


def updated_header(original: str, tree: list[str]) -> str:
    """Replace only a recognized ASCII public-interface tree while preserving responsibility text and newline style."""
    lines = original.splitlines(keepends=True)
    start = 0
    while start < len(lines):
        stripped = lines[start].strip()
        if stripped and not stripped.startswith("#!"):
            break
        start += 1
    if start + 1 >= len(lines):
        return original
    line0 = lines[start].rstrip("\r\n")
    line1 = lines[start + 1].rstrip("\r\n")
    if (
        not line0.startswith("# Responsibility: ")
        or not line0[len("# Responsibility: ") :].strip()
        or line1 != "# Public interface:"
        or not lines[start + 1].endswith("\n")
    ):
        return original
    end = start + 2
    while end < len(lines) and HEADER_ENTRY_RE.match(lines[end].rstrip("\r\n")):
        end += 1
    if end == start + 2:
        return original
    newline = "\r\n" if lines[start].endswith("\r\n") else "\n"
    rebuilt = (
        "".join(lines[: start + 2])
        + "".join(f"# {entry}{newline}" for entry in tree)
        + "".join(lines[end:])
    )
    return rebuilt


def main() -> int:
    args = sys.argv[1:]
    if args and args[0] == "--":
        args = args[1:]
    if not args or len(args) > 2 or (len(args) == 2 and args[1] not in ("--describe", "--update-headers")):
        print("usage: python check.py SOURCE_ROOT [--describe | --update-headers]", file=sys.stderr)
        return 2
    raw_root = Path(args[0])
    describe = len(args) == 2 and args[1] == "--describe"
    update_headers = len(args) == 2 and args[1] == "--update-headers"
    try:
        if raw_root.is_symlink():
            raise OSError("source root must not be a symlink")
        root = raw_root.resolve()
        if not root.is_dir():
            raise OSError(f"missing source directory: {raw_root}")
    except OSError as exc:
        print(str(exc), file=sys.stderr)
        return 2

    failed = False
    count = 0
    unsafe_files: set[Path] = set()
    inventory: dict[str, list[str]] = {}

    def diagnostic(file: str | Path, line: int, rule_id: str, message: str) -> None:
        nonlocal failed
        if rule_id in ("PY-COVERAGE", "PY-PARSE"):
            unsafe_files.add(Path(file).resolve())
        print(f"{file}:{line} {rule_id} {message}", file=sys.stderr)
        failed = True

    def walk(directory: Path) -> None:
        nonlocal count
        dir_lower = directory.name.lower()
        if dir_lower in BANNED_DIRS or (directory != root and dir_lower in BANNED_SCRATCH):
            diagnostic(directory, 1, "PY-STRUCTURE", f"forbidden catch-all or scratch folder: {directory.name}")
        for entry in sorted(os.scandir(directory), key=lambda e: e.name):
            entry_path = Path(entry.path)
            if entry.is_symlink():
                diagnostic(entry_path, 1, "PY-COVERAGE", "symlinks are not scanned")
                continue
            if entry.is_dir(follow_symlinks=False):
                if entry.name.startswith(".") or entry.name in EXCLUDED_DIRS:
                    continue
                walk(entry_path)
                continue
            if not entry.name.endswith(".py"):
                continue
            count += 1
            check_file(entry_path)

    def check_file(file: Path) -> None:
        original = file.read_bytes().decode("utf-8")
        source = original.replace("\r\n", "\n")
        line_count = len(source.splitlines())
        if line_count > 350:
            diagnostic(file, 351, "PY-FILE", f"file has {line_count} lines; maximum allowed is 350 (files over 100 lines are discouraged)")
        try:
            tree_ast = ast.parse(source, filename=str(file))
        except SyntaxError as exc:
            diagnostic(file, exc.lineno or 1, "PY-PARSE", exc.msg)
            return

        classes: list[ast.ClassDef] = []
        functions: list[ast.FunctionDef | ast.AsyncFunctionDef] = []
        type_aliases: dict[str, ast.expr] = {}
        declarations: dict[str, list[str]] = {}

        for node in tree_ast.body:
            if isinstance(node, ast.ImportFrom):
                if any(alias.name == "*" for alias in node.names):
                    diagnostic(file, node.lineno, "PY-COVERAGE", "star imports require explicit public-surface review")
            elif isinstance(node, ast.ClassDef):
                classes.append(node)
                base_names = [base_name(b) for b in node.bases]
                if "Protocol" in base_names and re.match(r"^I[A-Z]", node.name):
                    diagnostic(
                        file,
                        node.lineno,
                        "PY-NAME",
                        "Protocol names do not use an I prefix in Python; only C# uses IInterface",
                    )
                if node.name.startswith("_"):
                    continue
                if any(b not in ALLOWED_BASES for b in base_names if b):
                    diagnostic(file, node.lineno, "PY-COVERAGE", "inherited public members require explicit review")
                members: list[str] = []
                for item in node.body:
                    if isinstance(item, ast.AnnAssign) and isinstance(item.target, ast.Name):
                        if not item.target.id.startswith("_"):
                            members.append(item.target.id)
                    elif isinstance(item, ast.Assign):
                        for target in item.targets:
                            if isinstance(target, ast.Name) and not target.id.startswith("_"):
                                members.append(target.id)
                    elif isinstance(item, (ast.FunctionDef, ast.AsyncFunctionDef)):
                        if item.name == "__init__":
                            members.append(f"new({format_params(item.args, drop_receiver=True)})")
                        elif not item.name.startswith("_"):
                            if is_property_decorated(item):
                                members.append(item.name)
                            else:
                                members.append(f"{item.name}({format_params(item.args, drop_receiver=True)})")
                declarations[node.name] = members
            elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                functions.append(node)
                if not node.name.startswith("_"):
                    declarations[f"{node.name}({format_params(node.args)})"] = []
            elif isinstance(node, ast.TypeAlias):
                alias_name = node.name.id
                type_aliases[alias_name] = node.value
                if not alias_name.startswith("_"):
                    declarations[alias_name] = []
            elif isinstance(node, ast.AnnAssign) and isinstance(node.target, ast.Name):
                name = node.target.id
                if name == "__all__":
                    if not isinstance(node.value, (ast.List, ast.Tuple)):
                        diagnostic(file, node.lineno, "PY-COVERAGE", "dynamic __all__ requires explicit review")
                elif not name.startswith("_"):
                    if node.value is not None and base_name(node.annotation) == "TypeAlias":
                        type_aliases[name] = node.value
                    declarations[name] = []
            elif isinstance(node, ast.Assign):
                for target in node.targets:
                    if not isinstance(target, ast.Name):
                        if not any(
                            isinstance(n, ast.Name) and n.id.startswith("_") for n in ast.walk(target)
                        ):
                            diagnostic(file, node.lineno, "PY-COVERAGE", "destructured public declarations require explicit review")
                        continue
                    name = target.id
                    if name == "__all__":
                        if not isinstance(node.value, (ast.List, ast.Tuple)):
                            diagnostic(file, node.lineno, "PY-COVERAGE", "dynamic __all__ requires explicit review")
                        continue
                    if name.startswith("_"):
                        continue
                    if isinstance(node.value, ast.Call) and base_name(node.value.func) == "TypeVar":
                        continue
                    declarations[name] = []

        stem = file.stem
        is_test_or_init = stem == "__init__" or stem.startswith("test_") or stem.endswith("_test")
        if stem == "types":
            if functions:
                diagnostic(file, functions[0].lineno, "PY-FILE", "types.py may only group pure data types/DTOs without top-level functions")
            for cls in classes:
                cls_bases = {base_name(b) for b in cls.bases}
                is_pure_data = is_dataclass_decorated(cls) or bool(cls_bases & DATA_BASES)
                has_behavior = any(
                    isinstance(m, (ast.FunctionDef, ast.AsyncFunctionDef)) and not is_property_decorated(m)
                    for m in cls.body
                )
                if "Protocol" in cls_bases or not is_pure_data or has_behavior:
                    diagnostic(
                        file,
                        cls.lineno,
                        "PY-FILE",
                        f"types.py may only group pure data types/DTOs without behavior; move {cls.name} to {to_snake_case(cls.name)}.py",
                    )
        elif not is_test_or_init:
            matching_union_arms: set[str] = set()
            for alias_name, alias_expr in type_aliases.items():
                if to_snake_case(alias_name) == stem:
                    arms = union_arm_names(alias_expr)
                    if len(arms) >= 2:
                        matching_union_arms |= arms
            primary_classes = [
                cls for cls in classes if not (cls.name in matching_union_arms and is_dataclass_decorated(cls))
            ]
            if len(primary_classes) > 1:
                diagnostic(
                    file,
                    primary_classes[1].lineno,
                    "PY-FILE",
                    "one concept/type per file; pure DTOs may be grouped in types.py",
                )
            elif len(primary_classes) == 1:
                expected_stem = to_snake_case(primary_classes[0].name)
                if expected_stem != stem:
                    diagnostic(
                        file,
                        primary_classes[0].lineno,
                        "PY-FILE",
                        f"rename file for {primary_classes[0].name} (expected {expected_stem}.py)",
                    )
            elif not classes and declarations:
                export_stems = {to_snake_case(k.split("(", 1)[0]) for k in declarations}
                if stem not in export_stems:
                    diagnostic(file, 1, "PY-FILE", "filename must name a primary export")

        sorted_decls = sorted(declarations.items(), key=lambda kv: kv[0])
        tree: list[str] = []
        for idx, (name, members) in enumerate(sorted_decls):
            last = idx == len(sorted_decls) - 1
            tree.append(f"{'`-- ' if last else '+-- '}{name}")
            children = sorted(set(members))
            for child_idx, member in enumerate(children):
                child_last = child_idx == len(children) - 1
                prefix = "    " if last else "|   "
                branch = "`-- " if child_last else "+-- "
                tree.append(f"{prefix}{branch}{member}")
        if not tree:
            tree.append("(none)")
        inventory[str(file)] = tree

        if update_headers and file.resolve() not in unsafe_files:
            updated = updated_header(original, tree)
            if updated != original:
                file.write_bytes(updated.encode("utf-8"))
                print(f"UPDATED: {file}")
                source = updated.replace("\r\n", "\n")

        if not describe:
            header: list[str] = []
            for line in source.split("\n"):
                stripped = line.strip()
                if not stripped or stripped.startswith("#!"):
                    continue
                if not stripped.startswith("#"):
                    break
                header.append(line.lstrip()[1:].removeprefix(" "))
            if (
                len(header) < 3
                or not header[0].startswith("Responsibility: ")
                or not header[0][len("Responsibility: ") :].strip()
                or header[1] != "Public interface:"
                or "\n".join(header[2:]) != "\n".join(tree)
            ):
                formatted_tree = "\n# ".join(tree)
                diagnostic(
                    file,
                    1,
                    "PY-HEADER",
                    f"require Responsibility and exact public tree:\n# {formatted_tree}",
                )

        rel_parts = [root.name, *file.relative_to(root).parts[:-1]]
        role: str | None = None
        for part in rel_parts:
            if part in ("domain", "application"):
                role = part

        for node in ast.walk(tree_ast):
            if isinstance(node, ast.ImportFrom):
                if any(alias.name == "Any" for alias in node.names):
                    diagnostic(file, node.lineno, "PY-STRICT", "Any is forbidden; use object and narrow at boundaries")
                if role and node.module:
                    segments = node.module.split(".")
                    check_segments = segments[1:] if len(segments) > 1 and segments[0] == root.name else segments
                    if any(
                        seg in ("infrastructure", "configuration", "api", "ui")
                        or (role == "domain" and seg == "application")
                        for seg in check_segments
                    ):
                        diagnostic(file, node.lineno, "PY-BOUNDARY", f"{role} imports outer layer {node.module}")
            elif isinstance(node, ast.Import) and role:
                for alias in node.names:
                    segments = alias.name.split(".")
                    check_segments = segments[1:] if len(segments) > 1 and segments[0] == root.name else segments
                    if any(
                        seg in ("infrastructure", "configuration", "api", "ui")
                        or (role == "domain" and seg == "application")
                        for seg in check_segments
                    ):
                        diagnostic(file, node.lineno, "PY-BOUNDARY", f"{role} imports outer layer {alias.name}")
            elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                if node.returns is None:
                    diagnostic(file, node.lineno, "PY-STRICT", f"{node.name} requires an explicit return type annotation")
                all_params = [*node.args.posonlyargs, *node.args.args]
                for idx, param in enumerate(all_params):
                    if idx == 0 and param.arg in ("self", "cls"):
                        continue
                    if param.annotation is None:
                        diagnostic(file, node.lineno, "PY-STRICT", f"parameter {param.arg} in {node.name} requires a type annotation")
                for param in (*node.args.kwonlyargs, node.args.vararg, node.args.kwarg):
                    if param is not None and param.annotation is None:
                        diagnostic(file, node.lineno, "PY-STRICT", f"parameter {param.arg} in {node.name} requires a type annotation")
            elif (isinstance(node, ast.Name) and node.id == "Any") or (
                isinstance(node, ast.Attribute) and node.attr == "Any"
            ):
                diagnostic(file, node.lineno, "PY-STRICT", "Any is forbidden; use object and narrow at boundaries")

    try:
        walk(root)
    except OSError as exc:
        print(str(exc), file=sys.stderr)
        return 2

    if count == 0:
        print("no Python source files found", file=sys.stderr)
        return 2
    if describe:
        print(json.dumps(inventory))
    elif not failed:
        print(
            f"PASS: {count} Python files; parsed placement, public trees, no-Any, folders and named-layer imports. Full conformance requires review."
        )
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
