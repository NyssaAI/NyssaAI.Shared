// Responsibility: Store named command parameters and produce dialect-coerced bindings.
// Public interface:
// `-- DbParameterBinder
//     +-- add(name, value)
//     +-- addNull(name)
//     +-- clear()
//     +-- count
//     +-- get(name)
//     +-- has(name)
//     +-- names
//     +-- normalizeName(name)
//     +-- toPostgresParameters(sql)
//     `-- toSqliteParameters()

import { type ParameterBinder } from "./ParameterBinder.js";
import { rewriteSqlParameters } from "./rewriteSqlParameters.js";

export class DbParameterBinder {
  private readonly parameters = new Map<string, unknown>();

  public get count(): number {
    return this.parameters.size;
  }

  public get names(): readonly string[] {
    return Array.from(this.parameters.keys());
  }

  public add(name: string, value: unknown): void {
    const normalized = DbParameterBinder.normalizeName(name);
    this.parameters.set(normalized, value);
  }

  public addNull(name: string): void {
    const normalized = DbParameterBinder.normalizeName(name);
    this.parameters.set(normalized, null);
  }

  public get(name: string): unknown {
    const normalized = DbParameterBinder.normalizeName(name);
    return this.parameters.get(normalized);
  }

  public has(name: string): boolean {
    const normalized = DbParameterBinder.normalizeName(name);
    return this.parameters.has(normalized);
  }

  public clear(): void {
    this.parameters.clear();
  }

  public toSqliteParameters(): Record<string, unknown> {
    const result: Record<string, unknown> = {};
    for (const [key, value] of this.parameters) {
      const coerced = DbParameterBinder.coerceSqliteValue(value);
      result[key] = coerced;
      result[`@${key}`] = coerced;
    }
    return result;
  }

  public toPostgresParameters(
    sql: string
  ): { readonly sql: string; readonly positionalValues: readonly unknown[] } {
    return rewriteSqlParameters(sql, (name: string) => {
      const normalized = DbParameterBinder.normalizeName(name);
      if (!this.parameters.has(normalized)) {
        throw new Error(`Missing required parameter: @${name}`);
      }
      return DbParameterBinder.coercePostgresValue(this.parameters.get(normalized));
    });
  }

  public static normalizeName(name: string): string {
    if (!name || typeof name !== "string") {
      throw new Error("Parameter name must be a non-empty string.");
    }
    const trimmed = name.trim();
    if (!trimmed || trimmed === "@") {
      throw new Error("Parameter name cannot be empty or only '@'.");
    }
    return trimmed.startsWith("@") ? trimmed.slice(1) : trimmed;
  }

  private static coerceSqliteValue(value: unknown): unknown {
    if (value === null || value === undefined) {
      return null;
    }
    if (typeof value === "boolean") {
      return value ? 1 : 0;
    }
    if (value instanceof Date) {
      return value.toISOString();
    }
    if (value instanceof Uint8Array) {
      return Buffer.from(value.buffer, value.byteOffset, value.byteLength);
    }
    if (typeof value === "bigint" || typeof value === "number" || typeof value === "string") {
      return value;
    }
    return String(value);
  }

  private static coercePostgresValue(value: unknown): unknown {
    if (value === null || value === undefined) {
      return null;
    }
    if (value instanceof Uint8Array && !(value instanceof Buffer)) {
      return Buffer.from(value.buffer, value.byteOffset, value.byteLength);
    }
    return value;
  }
}
