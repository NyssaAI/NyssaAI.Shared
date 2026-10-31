// Responsibility: Summarize the results of a schema migration execution.
// Public interface:
// `-- MigrationReport
//     +-- appliedCount
//     +-- fromNumber
//     +-- new(params)
//     `-- toNumber

export class MigrationReport {
  public readonly fromNumber: number;
  public readonly toNumber: number;
  public readonly appliedCount: number;

  public constructor(params: {
    readonly fromNumber: number;
    readonly toNumber: number;
    readonly appliedCount: number;
  }) {
    this.fromNumber = params.fromNumber;
    this.toNumber = params.toNumber;
    this.appliedCount = params.appliedCount;
  }
}
