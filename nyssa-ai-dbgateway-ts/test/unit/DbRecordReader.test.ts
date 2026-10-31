// Responsibility: Unit test dialect-normalized column reading and type coercions.
// Public interface:
// (none)

import { describe, it, expect } from "vitest";
import { DbRecordReader } from "../../src/commands/DbRecordReader.js";

describe("DbRecordReader", () => {
  it("dbRecordReader_iteration_advancesThroughRows", async () => {
    const rows = [
      { id: "1", val: 10 },
      { id: "2", val: 20 }
    ];
    const reader = new DbRecordReader(rows);

    expect(reader.fieldCount).toBe(2);
    expect(reader.columnNames).toEqual(["id", "val"]);

    expect(await reader.next()).toBe(true);
    expect(reader.getString("id")).toBe("1");
    expect(reader.getInt32("val")).toBe(10);

    expect(await reader.next()).toBe(true);
    expect(reader.getString("id")).toBe("2");
    expect(reader.getInt32("val")).toBe(20);

    expect(await reader.next()).toBe(false);
  });

  it("dbRecordReader_typeCoercions_matchesExpectedPrimitives", async () => {
    const rows = [
      {
        boolIntTrue: 1,
        boolIntFalse: 0,
        boolStrTrue: "true",
        boolNative: true,
        guidStr: "12345678-1234-1234-1234-123456789abc",
        guidRaw: "12345678123412341234123456789abc",
        int64Big: 9007199254740993n,
        int64Num: 42,
        doubleVal: 3.14159,
        decimalVal: "12345678901234567890.123456789",
        dateIso: "2026-10-31T12:00:00.000Z",
        dateObj: new Date("2026-10-31T12:00:00.000Z"),
        bytesVal: new Uint8Array([1, 2, 3]),
        nullVal: null
      }
    ];
    const reader = new DbRecordReader(rows);
    await reader.next();

    // Booleans
    expect(reader.getBoolean("boolIntTrue")).toBe(true);
    expect(reader.getBoolean("boolIntFalse")).toBe(false);
    expect(reader.getBoolean("boolStrTrue")).toBe(true);
    expect(reader.getBoolean("boolNative")).toBe(true);

    // GUIDs
    expect(reader.getGuid("guidStr")).toBe("12345678-1234-1234-1234-123456789abc");
    expect(reader.getGuid("guidRaw")).toBe("12345678-1234-1234-1234-123456789abc");

    // Integers
    expect(reader.getInt64("int64Big")).toBe(9007199254740993n);
    expect(reader.getInt64("int64Num")).toBe(42n);
    expect(reader.getInt32("int64Num")).toBe(42);

    // Double & Decimal
    expect(reader.getDouble("doubleVal")).toBeCloseTo(3.14159);
    expect(reader.getDecimal("decimalVal")).toBe("12345678901234567890.123456789");

    // Dates
    expect(reader.getDateTimeOffset("dateIso").toISOString()).toBe("2026-10-31T12:00:00.000Z");
    expect(reader.getDateTimeOffset("dateObj").toISOString()).toBe("2026-10-31T12:00:00.000Z");

    // Bytes
    expect(reader.getBytes("bytesVal")).toEqual(new Uint8Array([1, 2, 3]));

    // Nulls
    expect(reader.isNull("nullVal")).toBe(true);
    expect(reader.getNullableString("nullVal")).toBeNull();
    expect(reader.getNullableInt32("nullVal")).toBeNull();
    expect(reader.getNullableInt64("nullVal")).toBeNull();
    expect(reader.getNullableBoolean("nullVal")).toBeNull();
    expect(reader.getNullableGuid("nullVal")).toBeNull();
    expect(reader.getNullableDateTimeOffset("nullVal")).toBeNull();
  });

  it("dbRecordReader_caseInsensitiveNamesAndOrdinals_retrievesValues", async () => {
    const rows = [{ FirstName: "Alice", AGE: 30 }];
    const reader = new DbRecordReader(rows);
    await reader.next();

    expect(reader.getString(0)).toBe("Alice");
    expect(reader.getString("firstname")).toBe("Alice");
    expect(reader.getString("FIRSTNAME")).toBe("Alice");
    expect(reader.getInt32(1)).toBe(30);
    expect(reader.getInt32("age")).toBe(30);
    expect(reader.getInt32("Age")).toBe(30);
  });
});
