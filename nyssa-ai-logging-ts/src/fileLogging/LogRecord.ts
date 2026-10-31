// Responsibility: Structured log record matching the canonical Nyssa JSON format.
// Public interface:
// `-- LogRecord
//     +-- AdditionalFields
//     +-- Exception
//     +-- Level
//     +-- Message
//     +-- Module
//     +-- Operation
//     `-- Timestamp

export interface LogRecord {
  readonly Timestamp: string;
  readonly Level: string;
  readonly Message: string;
  readonly Module: string;
  readonly Operation: string;
  readonly Exception: string | null;
  readonly AdditionalFields: Readonly<Record<string, unknown>>;
}
