using Xunit.Sdk;
using Xunit.v3;

// Several suites capture log output by redirecting Console.Error, which is
// process-wide state. Running tests in parallel lets one suite capture another
// suite's output, so parallelization is disabled here.
[assembly: Parallelization(Mode = ParallelMode.None)]
