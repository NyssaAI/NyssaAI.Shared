// Responsibility: Define the process execution callback contract for invoking the Bitwarden Secrets Manager CLI (bws).
// Public interface:
// `-- BwsCommandRunner
//     `-- call(binaryPath, args, env, signal)

import type { Result } from "@nyssa-ai/result";

/**
 * Process execution delegate for running the bws CLI.
 */
export type BwsCommandRunner = (
  binaryPath: string,
  args: readonly string[],
  env: Readonly<Record<string, string>>,
  signal?: AbortSignal
) => Promise<Result<string>>;
