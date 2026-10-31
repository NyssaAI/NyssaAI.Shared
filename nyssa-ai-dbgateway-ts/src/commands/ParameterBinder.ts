// Responsibility: Define parameter binding contract for typed database commands.
// Public interface:
// `-- ParameterBinder
//     +-- add(name, value)
//     +-- addNull(name)
//     +-- clear()
//     +-- count
//     +-- get(name)
//     +-- has(name)
//     `-- names

export interface ParameterBinder {
  readonly count: number;
  readonly names: readonly string[];
  add(name: string, value: unknown): void;
  addNull(name: string): void;
  get(name: string): unknown;
  has(name: string): boolean;
  clear(): void;
}
