# Responsibility: Validate that migration SQL contains safe, transactional statements.
# Public interface:
# `-- MigrationSql
#     `-- is_transactional(sql)
"""Transactional migration SQL validation."""

_FORBIDDEN_TRANSACTION_KEYWORDS = frozenset(
    {
        "BEGIN",
        "START",
        "COMMIT",
        "END",
        "ROLLBACK",
        "ABORT",
        "SAVEPOINT",
        "RELEASE",
        "VACUUM",
        "ATTACH",
        "DETACH",
        "PRAGMA",
        "PREPARE",
        "SET",
        "RESET",
        "DISCARD",
    }
)


class MigrationSql:
    """Validates that migration SQL contains safe, transactional statements."""

    @staticmethod
    def is_transactional(sql: str | None) -> bool:
        """Return True when sql contains non-empty statements without forbidden transaction or session commands."""
        if sql is None or not sql.strip():
            return False

        statement_start = True
        has_statement = False
        trigger_header = 0
        trigger_body = False
        length = len(sql)
        index = 0

        while index < length:
            current = sql[index]

            if current.isspace():
                index += 1
                continue

            # Line comment: --
            if current == "-" and index + 1 < length and sql[index + 1] == "-":
                next_newline = sql.find("\n", index + 2)
                if next_newline == -1:
                    break
                index = next_newline + 1
                continue

            # Block comment: /* ... */
            if current == "/" and index + 1 < length and sql[index + 1] == "*":
                index += 2
                closed = False
                while index + 1 < length:
                    if sql[index] == "/" and sql[index + 1] == "*":
                        return False
                    if sql[index] == "*" and sql[index + 1] == "/":
                        closed = True
                        index += 2
                        break
                    index += 1
                if not closed:
                    return False
                continue

            # Semicolon separator
            if current == ";":
                statement_start = True
                if not trigger_body:
                    trigger_header = 0
                index += 1
                continue

            # Quoted literals / identifiers: ', ", `, [
            if current in ("'", '"', "`", "["):
                end = "]" if current == "[" else current
                index += 1
                closed = False
                while index < length:
                    ch = sql[index]
                    index += 1
                    if ch != end:
                        continue
                    if index < length and sql[index] == end:
                        index += 1
                        continue
                    closed = True
                    break
                if not closed or statement_start:
                    return False
                continue

            # Dollar quotes: $tag$...$tag$
            if current == "$":
                tag_end = sql.find("$", index + 1)
                if tag_end == -1:
                    return False
                tag = sql[index : tag_end + 1]
                close = sql.find(tag, tag_end + 1)
                if close == -1 or statement_start:
                    return False
                index = close + len(tag)
                continue

            # Word tokens
            if current.isascii() and current.isalpha():
                start = index
                index += 1
                while index < length and (
                    (sql[index].isascii() and sql[index].isalnum()) or sql[index] == "_"
                ):
                    index += 1
                token = sql[start:index]
                keyword = token.upper()

                if keyword == "E" and index < length and sql[index] == "'":
                    return False

                if statement_start and trigger_body and keyword == "END":
                    trigger_body = False
                elif statement_start and keyword in _FORBIDDEN_TRANSACTION_KEYWORDS:
                    return False

                if trigger_header == 0 and statement_start:
                    trigger_header = 1 if keyword == "CREATE" else -1
                elif trigger_header == 1:
                    if keyword in ("TEMP", "TEMPORARY"):
                        trigger_header = 2
                    elif keyword == "TRIGGER":
                        trigger_header = 3
                    else:
                        trigger_header = -1
                elif trigger_header == 2:
                    trigger_header = 3 if keyword == "TRIGGER" else -1
                elif trigger_header == 3 and keyword == "BEGIN":
                    trigger_body = True
                    trigger_header = -1

                statement_start = False
                has_statement = True
                continue

            if statement_start:
                return False
            index += 1

        return has_statement and not trigger_body
