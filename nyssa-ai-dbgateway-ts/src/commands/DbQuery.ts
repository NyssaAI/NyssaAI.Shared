// Responsibility: Define a typed database query mapping rows into typed records.
// Public interface:
// `-- DbQuery
//     +-- bindParameters(binder, dialect)
//     +-- getSql(dialect)
//     `-- mapRow(reader)

import type { DbDialect } from "../DbDialect.js";
import type { ParameterBinder } from "./ParameterBinder.js";
import type { DataRecordReader } from "./DataRecordReader.js";

export interface DbQuery<TRow> {
  getSql(dialect: DbDialect): string;
  bindParameters(binder: ParameterBinder, dialect: DbDialect): void;
  mapRow(reader: DataRecordReader): TRow;
}
