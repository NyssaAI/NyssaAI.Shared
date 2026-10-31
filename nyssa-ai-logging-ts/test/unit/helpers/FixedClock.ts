import type { Clock } from "../../src/fileLogging/Clock.js";

export class FixedClock implements Clock {
  private currentTime: Date;

  public constructor(initialTime: Date = new Date("2026-10-13T12:00:00.000Z")) {
    this.currentTime = new Date(initialTime);
  }

  public now(): Date {
    return new Date(this.currentTime);
  }

  public advance(ms: number): void {
    this.currentTime = new Date(this.currentTime.getTime() + ms);
  }

  public advanceDays(days: number): void {
    this.advance(days * 24 * 60 * 60 * 1000);
  }
}
