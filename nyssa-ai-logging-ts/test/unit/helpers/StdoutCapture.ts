export class StdoutCapture {
  private output = "";
  private originalWrite: typeof process.stdout.write | null = null;

  public constructor() {
    this.originalWrite = process.stdout.write;
    process.stdout.write = ((chunk: string | Uint8Array, encoding?: BufferEncoding | ((err?: Error) => void), callback?: (err?: Error) => void): boolean => {
      const text = typeof chunk === "string" ? chunk : Buffer.from(chunk).toString("utf8");
      this.output += text;
      if (typeof encoding === "function") {
        encoding();
      } else if (callback) {
        callback();
      }
      return true;
    }) as typeof process.stdout.write;
  }

  public get text(): string {
    return this.output;
  }

  public dispose(): void {
    if (this.originalWrite) {
      process.stdout.write = this.originalWrite;
      this.originalWrite = null;
    }
  }
}
