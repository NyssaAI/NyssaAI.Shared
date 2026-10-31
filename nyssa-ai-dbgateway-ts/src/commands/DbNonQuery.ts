// Responsibility: Define a typed write command returning the number of affected rows.
// Public interface:
// `-- DbNonQuery
//     +-- bindParameters(binder, dialect)
//     `-- getSql(dialect)

import type { DbDialect } from "../DbDialect.js";
import type { ParameterBinder } from "./ParameterBinder.js";

export interface DbNonQuery {
  getSql(dialect: DbDialect): string;
  bindParameters(binder: ParameterBinder, dialect: DbDialect): void;
}
