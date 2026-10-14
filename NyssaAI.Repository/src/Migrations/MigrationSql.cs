namespace NyssaAI.Repository.Migrations;

internal static class MigrationSql
{
    // A defensive check for ordinary transaction-control mistakes, not a sandbox for untrusted SQL.
    internal static bool IsTransactional(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return false;
        var statementStart = true;
        var hasStatement = false;
        var triggerHeader = 0;
        var triggerBody = false;
        for (var index = 0; index < sql.Length;)
        {
            var current = sql[index];
            if (char.IsWhiteSpace(current)) { index++; continue; }
            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                index = sql.IndexOf('\n', index + 2);
                if (index < 0) break;
                continue;
            }
            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                index += 2;
                var closed = false;
                while (index + 1 < sql.Length)
                {
                    // SQLite and PostgreSQL disagree on nested comments; reject this ambiguous authoring form.
                    if (sql[index] == '/' && sql[index + 1] == '*') return false;
                    if (sql[index] == '*' && sql[index + 1] == '/') { closed = true; index += 2; break; }
                    else index++;
                }
                if (!closed) return false;
                continue;
            }
            if (current == ';')
            {
                statementStart = true;
                if (!triggerBody) triggerHeader = 0;
                index++;
                continue;
            }
            if (current is '\'' or '"' or '`' or '[')
            {
                var end = current == '[' ? ']' : current;
                index++;
                var closed = false;
                while (index < sql.Length)
                {
                    if (sql[index++] != end) continue;
                    if (index < sql.Length && sql[index] == end) { index++; continue; }
                    closed = true;
                    break;
                }
                if (!closed || statementStart) return false;
                continue;
            }
            if (current == '$')
            {
                var tagEnd = sql.IndexOf('$', index + 1);
                if (tagEnd < 0) return false;
                var tag = sql[index..(tagEnd + 1)];
                var close = sql.IndexOf(tag, tagEnd + 1, StringComparison.Ordinal);
                if (close < 0 || statementStart) return false;
                index = close + tag.Length;
                continue;
            }
            if (char.IsAsciiLetter(current))
            {
                var start = index++;
                while (index < sql.Length && (char.IsAsciiLetterOrDigit(sql[index]) || sql[index] == '_')) index++;
                var token = sql[start..index];
                var keyword = token.ToUpperInvariant();
                // Escape-string lexing differs from ordinary SQL quotes; use standard or dollar-quoted literals.
                if (keyword == "E" && index < sql.Length && sql[index] == '\'') return false;
                // SQLite trigger bodies end with END after an internal statement terminator.
                // That END belongs to CREATE TRIGGER, not to the migration transaction.
                if (statementStart && triggerBody && keyword == "END") triggerBody = false;
                else if (statementStart && keyword is "BEGIN" or "START" or "COMMIT" or "END" or "ROLLBACK" or "ABORT" or "SAVEPOINT" or "RELEASE" or "VACUUM" or "ATTACH" or "DETACH" or "PRAGMA" or "PREPARE" or "SET" or "RESET" or "DISCARD") return false;
                if (triggerHeader == 0 && statementStart) triggerHeader = keyword == "CREATE" ? 1 : -1;
                else if (triggerHeader == 1) triggerHeader = keyword is "TEMP" or "TEMPORARY" ? 2 : keyword == "TRIGGER" ? 3 : -1;
                else if (triggerHeader == 2) triggerHeader = keyword == "TRIGGER" ? 3 : -1;
                else if (triggerHeader == 3 && keyword == "BEGIN") { triggerBody = true; triggerHeader = -1; }
                statementStart = false;
                hasStatement = true;
                continue;
            }
            if (statementStart) return false;
            index++;
        }
        return hasStatement && !triggerBody;
    }
}
