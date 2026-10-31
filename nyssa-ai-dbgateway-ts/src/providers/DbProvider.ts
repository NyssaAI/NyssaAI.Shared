// Responsibility: Factory contract for opening connections and managing engine lifecycles.
// Public interface:
// `-- DbProvider
//     +-- databaseProvider
//     +-- dialect
//     +-- dispose()
//     `-- openConnection(signal)

import type { DbDialect } from "../DbDialect.js";
import type { DbConnectionAdapter } from "./DbConnectionAdapter.js";

export interface DbProvider {
  readonly databaseProvider: string;
  readonly dialect: DbDialect;
  openConnection(signal?: AbortSignal): Promise<DbConnectionAdapter>;
  dispose(): Promise<void>;
}
