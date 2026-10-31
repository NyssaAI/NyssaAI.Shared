// Responsibility: Rewrite named @param placeholders into PostgreSQL positional $1 parameters.
// Public interface:
// `-- rewriteSqlParameters(sql, getParameterValue)

export function rewriteSqlParameters(
  sql: string,
  getParameterValue: (name: string) => unknown
): { readonly sql: string; readonly positionalValues: readonly unknown[] } {
  if (!sql) {
    return { sql: "", positionalValues: [] };
  }

  const parameterIndices = new Map<string, number>();
  const positionalValues: unknown[] = [];
  let result = "";
  const length = sql.length;
  let index = 0;

  while (index < length) {
    const current = sql[index]!;

    // Single-line comment: -- ...
    if (current === "-" && index + 1 < length && sql[index + 1] === "-") {
      const nextNewline = sql.indexOf("\n", index + 2);
      if (nextNewline === -1) {
        result += sql.slice(index);
        break;
      }
      result += sql.slice(index, nextNewline + 1);
      index = nextNewline + 1;
      continue;
    }

    // Multi-line comment: /* ... */
    if (current === "/" && index + 1 < length && sql[index + 1] === "*") {
      const closeComment = sql.indexOf("*/", index + 2);
      if (closeComment === -1) {
        result += sql.slice(index);
        break;
      }
      result += sql.slice(index, closeComment + 2);
      index = closeComment + 2;
      continue;
    }

    // Single-quoted string literal: '...' (escaped by '')
    if (current === "'") {
      let stringEnd = index + 1;
      while (stringEnd < length) {
        if (sql[stringEnd] === "'") {
          if (stringEnd + 1 < length && sql[stringEnd + 1] === "'") {
            stringEnd += 2;
            continue;
          }
          stringEnd += 1;
          break;
        }
        stringEnd++;
      }
      result += sql.slice(index, stringEnd);
      index = stringEnd;
      continue;
    }

    // Double-quoted identifier: "..." (escaped by "")
    if (current === '"') {
      let identEnd = index + 1;
      while (identEnd < length) {
        if (sql[identEnd] === '"') {
          if (identEnd + 1 < length && sql[identEnd + 1] === '"') {
            identEnd += 2;
            continue;
          }
          identEnd += 1;
          break;
        }
        identEnd++;
      }
      result += sql.slice(index, identEnd);
      index = identEnd;
      continue;
    }

    // Dollar-quoted string in PostgreSQL: $tag$...$tag$ or $$...$$
    if (current === "$") {
      const tagEnd = sql.indexOf("$", index + 1);
      if (tagEnd !== -1) {
        const tag = sql.slice(index, tagEnd + 1);
        const validTag = /^\$[a-zA-Z0-9_]*\$$/.test(tag);
        if (validTag) {
          const closeTag = sql.indexOf(tag, tagEnd + 1);
          if (closeTag !== -1) {
            const fullBlock = sql.slice(index, closeTag + tag.length);
            result += fullBlock;
            index = closeTag + tag.length;
            continue;
          }
        }
      }
    }

    // Named parameter: @param
    if (current === "@" && index + 1 < length && /[a-zA-Z_]/.test(sql[index + 1]!)) {
      let nameEnd = index + 1;
      while (nameEnd < length && /[a-zA-Z0-9_]/.test(sql[nameEnd]!)) {
        nameEnd++;
      }
      const paramName = sql.slice(index + 1, nameEnd);
      let paramPos = parameterIndices.get(paramName);
      if (paramPos === undefined) {
        positionalValues.push(getParameterValue(paramName));
        paramPos = positionalValues.length;
        parameterIndices.set(paramName, paramPos);
      }
      result += `$${paramPos}`;
      index = nameEnd;
      continue;
    }

    result += current;
    index++;
  }

  return { sql: result, positionalValues };
}
