export class StderrCapture {
  private output = "";
  private originalWrite: typeof process.stderr.write | null = null;

  public constructor() {
    this.originalWrite = process.stderr.write;
    process.stderr.write = ((chunk: string | Uint8Array, encoding?: BufferEncoding | ((err?: Error) => void), callback?: (err?: Error) => void): boolean => {
      const text = typeof chunk === "string" ? chunk : Buffer.from(chunk).toString("utf8");
      this.output += text;
      if (typeof encoding === "function") {
        encoding();
      } else if (callback) {
        callback();
      }
      return true;
    }) as typeof process.stderr.write;
  }

  public get text(): string {
    return this.output;
  }

  public dispose(): void {
    if (this.originalWrite) {
      process.stderr.write = this.originalWrite;
      this.originalWrite = null;
    }
  }
}
