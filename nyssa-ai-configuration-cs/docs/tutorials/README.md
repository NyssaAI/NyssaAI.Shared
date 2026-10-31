# Configuration tutorials

Run `./scripts/run-tests.ps1` from the repository root to build current source and run all included tests.
The script runs Configuration tests from their build output directories, keeping default-path writes
inside test output. Temporary homes and manifests are isolated under the repository's `.temp`.

| Tutorial | Consumer workflow |
| --- | --- |
| [ManifestTutorials](../../test/use_case/ManifestTutorials.cs) | First-use provisioning, path defaults, overrides, and manifest precedence. |
| [SetupTutorials](../../test/use_case/SetupTutorials.cs) | Application identity, initialization failures, encryption, and provider setup. |
| [ConfigurationTutorials](../../test/use_case/ConfigurationTutorials.cs) | Plain and encrypted values, nested DTOs, fresh scopes, moving values, and source-generated JSON. |
| [NamespaceSettingsTutorials](../../test/use_case/NamespaceSettingsTutorials.cs) | Namespace properties, legacy settings compatibility, updates, isolation, and source-generated JSON. |
| [OAuthTutorials](../../test/use_case/OAuthTutorials.cs) | Authorization, refresh, revoked grants, and provider credentials with a fake authorization server. |
| [ConfigurationIsolationTests](../../test/use_case/ConfigurationIsolationTests.cs) | Verify that a run leaves the user's real manifest unchanged. |

Tutorials consume only the public API under `NyssaAI.Configuration` and `NyssaAI.Results`.
The local keyring, browser, remote vault, and authorization server are fakes; the library's storage,
encryption, registration, and OAuth workflow execute normally. No live Bitwarden integration is
included until its provider package is imported.

The normal suites skip cases for other operating systems and skip live OS keyring access. Enable
`NYSSA_OS_KEYRING_TESTS=1` only when intentionally testing a real credential store: those cases create
and delete test entries under `nyssa.configuration.tests`.

[NamespaceProvidersTutorials](../../test/use_case/NamespaceProvidersTutorials.cs) shows one DTO assembled from plaintext, local encryption, and a cloud vault.
