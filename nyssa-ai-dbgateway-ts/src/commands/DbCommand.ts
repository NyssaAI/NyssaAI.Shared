// Responsibility: Define a typed database command that materializes a single result.
// Public interface:
// `-- DbCommand
//     +-- bindParameters(binder, dialect)
//     +-- getSql(dialect)
//     `-- readResult(reader, signal)

import type { DbDialect } from "../DbDialect.js";
import type { ParameterBinder } from "./ParameterBinder.js";
import type { DataRecordReader } from "./DataRecordReader.js";

export interface DbCommand<TResult> {
  getSql(dialect: DbDialect): string;
  bindParameters(binder: ParameterBinder, dialect: DbDialect): void;
  readResult(reader: DataRecordReader, signal?: AbortSignal): Promise<TResult>;
}
