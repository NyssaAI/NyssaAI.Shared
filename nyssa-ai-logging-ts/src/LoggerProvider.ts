// Responsibility: Factory and lifecycle interface for creating category-scoped loggers.
// Public interface:
// `-- LoggerProvider
//     +-- createLogger(categoryName)
//     `-- dispose()

import type { Logger } from "./Logger.js";

export interface LoggerProvider {
  createLogger(categoryName: string): Logger;
  dispose(): void;
}
