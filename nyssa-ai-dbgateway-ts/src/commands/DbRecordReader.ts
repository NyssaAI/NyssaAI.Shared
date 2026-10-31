// Responsibility: Provide dialect-normalized column reading across SQLite and PostgreSQL records.
// Public interface:
// `-- DbRecordReader
//     +-- columnNames
//     +-- fieldCount
//     +-- getBoolean(ordinalOrName)
//     +-- getBytes(ordinalOrName)
//     +-- getDateTimeOffset(ordinalOrName)
//     +-- getDecimal(ordinalOrName)
//     +-- getDouble(ordinalOrName)
//     +-- getGuid(ordinalOrName)
//     +-- getInt32(ordinalOrName)
//     +-- getInt64(ordinalOrName)
//     +-- getNullableBoolean(ordinalOrName)
//     +-- getNullableBytes(ordinalOrName)
//     +-- getNullableDateTimeOffset(ordinalOrName)
//     +-- getNullableDecimal(ordinalOrName)
//     +-- getNullableDouble(ordinalOrName)
//     +-- getNullableGuid(ordinalOrName)
//     +-- getNullableInt32(ordinalOrName)
//     +-- getNullableInt64(ordinalOrName)
//     +-- getNullableString(ordinalOrName)
//     +-- getString(ordinalOrName)
//     +-- getValue(ordinalOrName)
//     +-- isNull(ordinalOrName)
//     +-- new(rows, columnNames)
//     `-- next(signal)

import { type DataRecordReader } from "./DataRecordReader.js";

export class DbRecordReader {
  private readonly rows: readonly (Record<string, unknown> | readonly unknown[])[];
  private readonly columns: readonly string[];
  private readonly lowerColumnMap: Map<string, number>;
  private currentIndex = -1;

  public constructor(
    rows: readonly (Record<string, unknown> | readonly unknown[])[],
    columnNames?: readonly string[]
  ) {
    this.rows = rows;
    if (columnNames && columnNames.length > 0) {
      this.columns = columnNames;
    } else if (rows.length > 0 && rows[0] && !Array.isArray(rows[0])) {
      this.columns = Object.keys(rows[0]);
    } else {
      this.columns = [];
    }
    this.lowerColumnMap = new Map();
    for (let i = 0; i < this.columns.length; i++) {
      const col = this.columns[i];
      if (col) {
        this.lowerColumnMap.set(col.toLowerCase(), i);
      }
    }
  }

  public get fieldCount(): number {
    return this.columns.length;
  }

  public get columnNames(): readonly string[] {
    return this.columns;
  }

  public async next(signal?: AbortSignal): Promise<boolean> {
    if (signal?.aborted) {
      throw signal.reason ?? new Error("Reading was aborted.");
    }
    this.currentIndex++;
    return this.currentIndex < this.rows.length;
  }

  public isNull(ordinalOrName: number | string): boolean {
    const val = this.getValue(ordinalOrName);
    return val === null || val === undefined;
  }

  public getValue(ordinalOrName: number | string): unknown {
    if (this.currentIndex < 0 || this.currentIndex >= this.rows.length) {
      throw new Error("Reader is not positioned on a valid record. Call next() first.");
    }
    const row = this.rows[this.currentIndex]!;
    if (typeof ordinalOrName === "number") {
      if (ordinalOrName < 0 || ordinalOrName >= this.columns.length) {
        throw new RangeError(`Column index ${ordinalOrName} is out of bounds (0..${this.columns.length - 1}).`);
      }
      if (Array.isArray(row)) {
        return row[ordinalOrName];
      }
      const colName = this.columns[ordinalOrName]!;
      return (row as Record<string, unknown>)[colName];
    }
    const lowerName = ordinalOrName.toLowerCase();
    const ordinal = this.lowerColumnMap.get(lowerName);
    if (ordinal !== undefined) {
      if (Array.isArray(row)) {
        return row[ordinal];
      }
      const actualName = this.columns[ordinal]!;
      return (row as Record<string, unknown>)[actualName];
    }
    if (!Array.isArray(row) && ordinalOrName in (row as Record<string, unknown>)) {
      return (row as Record<string, unknown>)[ordinalOrName];
    }
    throw new Error(`Column "${ordinalOrName}" does not exist in the record.`);
  }

  public getString(ordinalOrName: number | string): string {
    const val = this.getValue(ordinalOrName);
    if (val === null || val === undefined) {
      throw new Error(`Column ${ordinalOrName} is null.`);
    }
    return String(val);
  }

  public getNullableString(ordinalOrName: number | string): string | null {
    return this.isNull(ordinalOrName) ? null : this.getString(ordinalOrName);
  }

  public getInt32(ordinalOrName: number | string): number {
    const val = this.getValue(ordinalOrName);
    if (val === null || val === undefined) {
      throw new Error(`Column ${ordinalOrName} is null.`);
    }
    let num: number;
    if (typeof val === "number") {
      num = Math.trunc(val);
    } else if (typeof val === "bigint") {
      num = Number(val);
    } else if (typeof val === "boolean") {
      num = val ? 1 : 0;
    } else {
      num = parseInt(String(val), 10);
    }
    if (isNaN(num) || num < -2147483648 || num > 2147483647) {
      throw new RangeError(`Column ${ordinalOrName} value ${String(val)} is outside 32-bit integer range.`);
    }
    return num;
  }

  public getNullableInt32(ordinalOrName: number | string): number | null {
    return this.isNull(ordinalOrName) ? null : this.getInt32(ordinalOrName);
  }

  public getInt64(ordinalOrName: number | string): bigint {
    const val = this.getValue(ordinalOrName);
    if (val === null || val === undefined) {
      throw new Error(`Column ${ordinalOrName} is null.`);
    }
    if (typeof val === "bigint") {
      return val;
    }
    if (typeof val === "number") {
      return BigInt(Math.trunc(val));
    }
    if (typeof val === "boolean") {
      return val ? 1n : 0n;
    }
    return BigInt(String(val));
  }

  public getNullableInt64(ordinalOrName: number | string): bigint | null {
    return this.isNull(ordinalOrName) ? null : this.getInt64(ordinalOrName);
  }

  public getDouble(ordinalOrName: number | string): number {
    const val = this.getValue(ordinalOrName);
    if (val === null || val === undefined) {
      throw new Error(`Column ${ordinalOrName} is null.`);
    }
    if (typeof val === "number") {
      return val;
    }
    const num = parseFloat(String(val));
    if (isNaN(num)) {
      throw new TypeError(`Column ${ordinalOrName} value ${String(val)} cannot be coerced to Double.`);
    }
    return num;
  }

  public getNullableDouble(ordinalOrName: number | string): number | null {
    return this.isNull(ordinalOrName) ? null : this.getDouble(ordinalOrName);
  }

  public getDecimal(ordinalOrName: number | string): string {
    const val = this.getValue(ordinalOrName);
    if (val === null || val === undefined) {
      throw new Error(`Column ${ordinalOrName} is null.`);
    }
    if (typeof val === "string") {
      return val;
    }
    return String(val);
  }

  public getNullableDecimal(ordinalOrName: number | string): string | null {
    return this.isNull(ordinalOrName) ? null : this.getDecimal(ordinalOrName);
  }

  public getBoolean(ordinalOrName: number | string): boolean {
    const val = this.getValue(ordinalOrName);
    if (val === null || val === undefined) {
      throw new Error(`Column ${ordinalOrName} is null.`);
    }
    if (typeof val === "boolean") {
      return val;
    }
    if (typeof val === "number" || typeof val === "bigint") {
      return val !== 0 && val !== 0n;
    }
    if (typeof val === "string") {
      const lower = val.trim().toLowerCase();
      if (lower === "true" || lower === "1" || lower === "t") {
        return true;
      }
      if (lower === "false" || lower === "0" || lower === "f") {
        return false;
      }
    }
    throw new TypeError(`Column ${ordinalOrName} cannot be coerced to Boolean.`);
  }

  public getNullableBoolean(ordinalOrName: number | string): boolean | null {
    return this.isNull(ordinalOrName) ? null : this.getBoolean(ordinalOrName);
  }

  public getGuid(ordinalOrName: number | string): string {
    const val = this.getValue(ordinalOrName);
    if (val === null || val === undefined) {
      throw new Error(`Column ${ordinalOrName} is null.`);
    }
    if (typeof val === "string") {
      const trimmed = val.trim().toLowerCase();
      if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(trimmed)) {
        return trimmed;
      }
      if (/^[0-9a-f]{32}$/.test(trimmed)) {
        return `${trimmed.slice(0, 8)}-${trimmed.slice(8, 12)}-${trimmed.slice(12, 16)}-${trimmed.slice(16, 20)}-${trimmed.slice(20, 32)}`;
      }
    }
    if (val instanceof Uint8Array && val.length === 16) {
      const hex = Buffer.from(val).toString("hex").toLowerCase();
      return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20, 32)}`;
    }
    throw new TypeError(`Column ${ordinalOrName} cannot be coerced to Guid.`);
  }

  public getNullableGuid(ordinalOrName: number | string): string | null {
    return this.isNull(ordinalOrName) ? null : this.getGuid(ordinalOrName);
  }

  public getDateTimeOffset(ordinalOrName: number | string): Date {
    const val = this.getValue(ordinalOrName);
    if (val === null || val === undefined) {
      throw new Error(`Column ${ordinalOrName} is null.`);
    }
    if (val instanceof Date) {
      return new Date(val.getTime());
    }
    if (typeof val === "string") {
      const d = new Date(val);
      if (!isNaN(d.getTime())) {
        return d;
      }
    }
    if (typeof val === "number" || typeof val === "bigint") {
      const num = Number(val);
      const ms = num < 10000000000 ? num * 1000 : num;
      return new Date(ms);
    }
    throw new TypeError(`Column ${ordinalOrName} cannot be coerced to DateTimeOffset.`);
  }

  public getNullableDateTimeOffset(ordinalOrName: number | string): Date | null {
    return this.isNull(ordinalOrName) ? null : this.getDateTimeOffset(ordinalOrName);
  }

  public getBytes(ordinalOrName: number | string): Uint8Array {
    const val = this.getValue(ordinalOrName);
    if (val === null || val === undefined) {
      throw new Error(`Column ${ordinalOrName} is null.`);
    }
    if (val instanceof Uint8Array) {
      return val;
    }
    throw new TypeError(`Column ${ordinalOrName} is not a byte array.`);
  }

  public getNullableBytes(ordinalOrName: number | string): Uint8Array | null {
    return this.isNull(ordinalOrName) ? null : this.getBytes(ordinalOrName);
  }
}
