// Responsibility: Provide dialect-normalized column reading across SQLite and PostgreSQL records.
// Public interface:
// `-- DataRecordReader
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
//     `-- next(signal)

export interface DataRecordReader {
  readonly fieldCount: number;
  readonly columnNames: readonly string[];
  next(signal?: AbortSignal): Promise<boolean>;
  isNull(ordinalOrName: number | string): boolean;
  getString(ordinalOrName: number | string): string;
  getNullableString(ordinalOrName: number | string): string | null;
  getInt32(ordinalOrName: number | string): number;
  getNullableInt32(ordinalOrName: number | string): number | null;
  getInt64(ordinalOrName: number | string): bigint;
  getNullableInt64(ordinalOrName: number | string): bigint | null;
  getDouble(ordinalOrName: number | string): number;
  getNullableDouble(ordinalOrName: number | string): number | null;
  getDecimal(ordinalOrName: number | string): string;
  getNullableDecimal(ordinalOrName: number | string): string | null;
  getBoolean(ordinalOrName: number | string): boolean;
  getNullableBoolean(ordinalOrName: number | string): boolean | null;
  getGuid(ordinalOrName: number | string): string;
  getNullableGuid(ordinalOrName: number | string): string | null;
  getDateTimeOffset(ordinalOrName: number | string): Date;
  getNullableDateTimeOffset(ordinalOrName: number | string): Date | null;
  getBytes(ordinalOrName: number | string): Uint8Array;
  getNullableBytes(ordinalOrName: number | string): Uint8Array | null;
  getValue(ordinalOrName: number | string): unknown;
}
