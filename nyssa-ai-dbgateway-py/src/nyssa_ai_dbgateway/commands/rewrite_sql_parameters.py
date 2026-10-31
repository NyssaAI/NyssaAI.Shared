# Responsibility: Rewrite SQL statements with @param placeholders to PostgreSQL positional $1, $2 placeholders.
# Public interface:
# `-- rewrite_sql_parameters(sql)
"""Parser and rewriter for database parameters across SQLite and PostgreSQL."""


def rewrite_sql_parameters(sql: str) -> tuple[str, list[str]]:
    """Rewrite '@param' placeholders to '$1, $2...' while preserving string literals and comments."""
    output: list[str] = []
    param_names: list[str] = []
    param_index_map: dict[str, int] = {}
    i = 0
    length = len(sql)

    while i < length:
        ch = sql[i]

        # Single-quote string literal
        if ch == "'":
            output.append(ch)
            i += 1
            while i < length:
                curr = sql[i]
                output.append(curr)
                if curr == "'":
                    if i + 1 < length and sql[i + 1] == "'":
                        i += 1
                        output.append(sql[i])
                    else:
                        i += 1
                        break
                i += 1
            continue

        # Double-quote identifier
        if ch == '"':
            output.append(ch)
            i += 1
            while i < length:
                curr = sql[i]
                output.append(curr)
                if curr == '"':
                    if i + 1 < length and sql[i + 1] == '"':
                        i += 1
                        output.append(sql[i])
                    else:
                        i += 1
                        break
                i += 1
            continue

        # Line comment --
        if ch == "-" and i + 1 < length and sql[i + 1] == "-":
            while i < length and sql[i] != "\n":
                output.append(sql[i])
                i += 1
            continue

        # Block comment /* ... */
        if ch == "/" and i + 1 < length and sql[i + 1] == "*":
            output.append("/*")
            i += 2
            while i < length:
                if sql[i] == "*" and i + 1 < length and sql[i + 1] == "/":
                    output.append("*/")
                    i += 2
                    break
                output.append(sql[i])
                i += 1
            continue

        # Parameter @identifier
        if ch == "@":
            j = i + 1
            while j < length and (sql[j].isalnum() or sql[j] == "_"):
                j += 1
            if j > i + 1:
                param_name = sql[i + 1 : j]
                pos = param_index_map.get(param_name)
                if pos is None:
                    pos = len(param_names) + 1
                    param_index_map[param_name] = pos
                    param_names.append(param_name)
                output.append(f"${pos}")
                i = j
                continue

        output.append(ch)
        i += 1

    return "".join(output), param_names
