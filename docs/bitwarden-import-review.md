# Bitwarden import review

The proposed package is `NyssaAI.Configuration.Bitwarden`. Its source and live tests have not been imported into this public repository.

The existing adapter uses `Bitwarden.Secrets.Sdk` 1.0.0, `Microsoft.Extensions.DependencyInjection.Abstractions`, Configuration, and Result. The adapter is separate so other Configuration consumers do not inherit the SDK's native runtime assets or licensing requirements.

## Dependency license finding

The [license shipped with Bitwarden.Secrets.Sdk 1.0.0](https://www.nuget.org/packages/Bitwarden.Secrets.Sdk/1.0.0/License) is the Bitwarden Software Development Kit License Agreement, version 1, dated March 17, 2023.

- Section 3.1 restricts offering, licensing, or selling compatible applications to third parties.
- Section 3.3 restricts developing another SDK.
- Section 3.4 restricts redistribution and derivative works; section 3.5 separately recognizes open-source component licenses.

These provisions raise an unresolved question about distributing this adapter. Publication needs confirmation of applicable rights from Bitwarden or qualified licensing counsel. NyssaAI's PolyForm license does not grant rights to Bitwarden's SDK.

## Remaining import work

Once the dependency terms are resolved, rename the provider's namespaces and project to `NyssaAI.Configuration.Bitwarden`, add package documentation, and verify the packaged adapter against the public Configuration contracts.

The live proof should use an explicitly selected test project, unique secret names, and cleanup that reports failures. It should verify remote reads and updates, local pointer storage, nested DTO round trips, and repeated removal. It must not create persistent projects or update shared marker secrets implicitly. Credentials belong in local user-secrets storage, never in the repository or test output. Ordinary CI must exclude live-service tests.
