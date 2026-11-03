// Responsibility: Disposable container holding a captured plaintext secret and chosen destination provider.
// Public interface:
// `-- CapturedSecret
//     +-- [Symbol.dispose]()
//     +-- dispose()
//     +-- new(secret, selectedProvider)
//     +-- secret
//     `-- selectedProvider
export class CapturedSecret {
  private _buffer: Buffer | null;
  private readonly _selectedProvider: string;
  private _disposed: boolean = false;

  public constructor(secret: string, selectedProvider: string) {
    this._buffer = Buffer.from(secret, "utf8");
    this._selectedProvider = selectedProvider;
  }

  public get secret(): string {
    if (this._disposed || !this._buffer) {
      throw new Error("CapturedSecret has been disposed.");
    }
    return this._buffer.toString("utf8");
  }

  public get selectedProvider(): string {
    return this._selectedProvider;
  }

  public dispose(): void {
    if (!this._disposed && this._buffer) {
      this._buffer.fill(0);
      this._buffer = null;
      this._disposed = true;
    }
  }

  public [Symbol.dispose](): void {
    this.dispose();
  }
}
