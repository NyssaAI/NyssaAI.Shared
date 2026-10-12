# Preparing libraries for publication

Add one library at a time, bringing its dependencies first. Each import includes the current source, unit tests, consumer tutorials, and documentation needed to use it.

Before an import is published:

1. Review the public API, dependencies, source notices, and examples. Remove machine-specific paths, credentials, obsolete instructions, and internal working records.
2. Give the package its own version and accurate package metadata, including the repository, README, and chosen license.
3. Build from the new repository with no dependency on the original checkout. Run unit tests and tutorials using current binaries.
4. Inspect the NuGet package contents and compile a consumer against the package, rather than a project reference.
5. Review the exact files and diff entering public Git history. Record unresolved limitations rather than calling unverified behavior complete.

The initial imports are `NyssaAI.Result` and `NyssaAI.Configuration`, using the `NyssaAI.*` namespaces. The separately packaged Bitwarden provider and its live integration tests await clarification of SDK distribution rights; see the [Bitwarden import review](bitwarden-import-review.md). Configuration's vault contracts and isolated fake-vault tests are included. Other libraries are selected individually once their dependencies are present.

The build workflow verifies source and tests. Creating a repository or pushing code does not publish a NuGet package. Package publication is a separate release action.
