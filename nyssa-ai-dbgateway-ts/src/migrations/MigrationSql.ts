// Responsibility: Validate that migration SQL contains safe, transactional statements.
// Public interface:
// `-- MigrationSql
//     `-- isTransactional(sql)

const FORBIDDEN_TRANSACTION_KEYWORDS = new Set([
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
  "DISCARD"
]);

export class MigrationSql {
  public static isTransactional(sql?: string | null): boolean {
    if (!sql || !sql.trim()) {
      return false;
    }

    let statementStart = true;
    let hasStatement = false;
    let triggerHeader = 0;
    let triggerBody = false;
    const length = sql.length;
    let index = 0;

    while (index < length) {
      const current = sql[index]!;

      if (/\s/.test(current)) {
        index++;
        continue;
      }

      // Line comment: --
      if (current === "-" && index + 1 < length && sql[index + 1] === "-") {
        const nextNewline = sql.indexOf("\n", index + 2);
        if (nextNewline === -1) {
          break;
        }
        index = nextNewline + 1;
        continue;
      }

      // Block comment: /* ... */
      if (current === "/" && index + 1 < length && sql[index + 1] === "*") {
        index += 2;
        let closed = false;
        while (index + 1 < length) {
          if (sql[index] === "/" && sql[index + 1] === "*") {
            return false;
          }
          if (sql[index] === "*" && sql[index + 1] === "/") {
            closed = true;
            index += 2;
            break;
          }
          index++;
        }
        if (!closed) {
          return false;
        }
        continue;
      }

      // Semicolon separator
      if (current === ";") {
        statementStart = true;
        if (!triggerBody) {
          triggerHeader = 0;
        }
        index++;
        continue;
      }

      // Quoted literals / identifiers: ', ", `, [
      if (current === "'" || current === '"' || current === "`" || current === "[") {
        const end = current === "[" ? "]" : current;
        index++;
        let closed = false;
        while (index < length) {
          if (sql[index++] !== end) {
            continue;
          }
          if (index < length && sql[index] === end) {
            index++;
            continue;
          }
          closed = true;
          break;
        }
        if (!closed || statementStart) {
          return false;
        }
        continue;
      }

      // Dollar quotes: $tag$...$tag$
      if (current === "$") {
        const tagEnd = sql.indexOf("$", index + 1);
        if (tagEnd === -1) {
          return false;
        }
        const tag = sql.slice(index, tagEnd + 1);
        const close = sql.indexOf(tag, tagEnd + 1);
        if (close === -1 || statementStart) {
          return false;
        }
        index = close + tag.length;
        continue;
      }

      // Word tokens
      if (/[a-zA-Z]/.test(current)) {
        const start = index++;
        while (index < length && /[a-zA-Z0-9_]/.test(sql[index]!)) {
          index++;
        }
        const token = sql.slice(start, index);
        const keyword = token.toUpperCase();

        if (keyword === "E" && index < length && sql[index] === "'") {
          return false;
        }

        if (statementStart && triggerBody && keyword === "END") {
          triggerBody = false;
        } else if (statementStart && FORBIDDEN_TRANSACTION_KEYWORDS.has(keyword)) {
          return false;
        }

        if (triggerHeader === 0 && statementStart) {
          triggerHeader = keyword === "CREATE" ? 1 : -1;
        } else if (triggerHeader === 1) {
          triggerHeader = keyword === "TEMP" || keyword === "TEMPORARY" ? 2 : keyword === "TRIGGER" ? 3 : -1;
        } else if (triggerHeader === 2) {
          triggerHeader = keyword === "TRIGGER" ? 3 : -1;
        } else if (triggerHeader === 3 && keyword === "BEGIN") {
          triggerBody = true;
          triggerHeader = -1;
        }

        statementStart = false;
        hasStatement = true;
        continue;
      }

      if (statementStart) {
        return false;
      }
      index++;
    }

    return hasStatement && !triggerBody;
  }
}
