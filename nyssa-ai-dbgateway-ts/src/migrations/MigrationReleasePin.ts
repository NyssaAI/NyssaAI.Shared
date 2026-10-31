// Responsibility: Map an exact semantic version release to a target migration step number.
// Public interface:
// `-- MigrationReleasePin
//     +-- new(release, number)
//     +-- number
//     `-- release

export class MigrationReleasePin {
  public readonly release: string;
  public readonly number: number;

  public constructor(release: string, number: number) {
    this.release = release;
    this.number = number;
  }
}
