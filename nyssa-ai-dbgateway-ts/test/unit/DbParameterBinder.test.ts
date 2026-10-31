// Responsibility: Unit test parameter binding, name normalization, and SQL parameter rewriting.
// Public interface:
// (none)

import { describe, it, expect } from "vitest";
import { DbParameterBinder } from "../../src/commands/DbParameterBinder.js";
import { rewriteSqlParameters } from "../../src/commands/rewriteSqlParameters.js";

describe("DbParameterBinder", () => {
  it("dbParameterBinder_nameNormalization_stripsLeadingAtAndWhitespace", () => {
    const binder = new DbParameterBinder();
    binder.add("  @title  ", "test-title");
    binder.add("count", 42);

    expect(binder.count).toBe(2);
    expect(binder.has("title")).toBe(true);
    expect(binder.has("@title")).toBe(true);
    expect(binder.get("title")).toBe("test-title");
    expect(binder.get("@title")).toBe("test-title");
    expect(binder.get("count")).toBe(42);
  });

  it("dbParameterBinder_invalidName_throwsError", () => {
    const binder = new DbParameterBinder();
    expect(() => binder.add("", "val")).toThrow();
    expect(() => binder.add("   ", "val")).toThrow();
    expect(() => binder.add("@", "val")).toThrow();
    expect(() => binder.add("  @  ", "val")).toThrow();
  });

  it("dbParameterBinder_toSqliteParameters_coercesTypesProperly", () => {
    const binder = new DbParameterBinder();
    const date = new Date("2026-10-31T12:00:00.000Z");
    const bytes = new Uint8Array([1, 2, 3, 4]);

    binder.add("active", true);
    binder.add("inactive", false);
    binder.add("created", date);
    binder.add("blob", bytes);
    binder.addNull("empty");

    const params = binder.toSqliteParameters();
    expect(params["active"]).toBe(1);
    expect(params["@active"]).toBe(1);
    expect(params["inactive"]).toBe(0);
    expect(params["@inactive"]).toBe(0);
    expect(params["created"]).toBe("2026-10-31T12:00:00.000Z");
    expect(params["blob"]).toBeInstanceOf(Buffer);
    expect(params["empty"]).toBeNull();
  });

  it("dbParameterBinder_toPostgresParameters_rewritesPlaceholdersAndPreservesLiterals", () => {
    const binder = new DbParameterBinder();
    binder.add("id", "item-123");
    binder.add("note", "hello @world in text");
    binder.add("status", 1);

    const sql = `
      -- comment with @fakeParam
      /* block with @ignored */
      SELECT * FROM items
      WHERE id = @id
        AND note = '@note_not_param'
        AND status = @status
        AND alt_id = @id;
    `;

    const rewritten = binder.toPostgresParameters(sql);
    expect(rewritten.sql).toContain("WHERE id = $1");
    expect(rewritten.sql).toContain("AND note = '@note_not_param'");
    expect(rewritten.sql).toContain("AND status = $2");
    expect(rewritten.sql).toContain("AND alt_id = $1");
    expect(rewritten.positionalValues).toEqual(["item-123", 1]);
  });

  it("rewriteSqlParameters_missingParameter_throwsError", () => {
    const binder = new DbParameterBinder();
    binder.add("id", 1);
    expect(() => binder.toPostgresParameters("SELECT * FROM items WHERE id = @id AND missing = @missing")).toThrow(
      "Missing required parameter: @missing"
    );
  });
});
