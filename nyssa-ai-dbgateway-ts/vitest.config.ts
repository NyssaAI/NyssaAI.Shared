// Responsibility: Configure test runner settings and suite discovery.
// Public interface:
// (none)
import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    environment: "node",
    include: ["test/**/*.test.ts"],
    fileParallelism: false,
    testTimeout: 30000,
    passWithNoTests: true
  }
});
