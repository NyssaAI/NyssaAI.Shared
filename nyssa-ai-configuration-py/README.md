# nyssa-ai-configuration (Python)

`nyssa-ai-configuration` provides secure, cross-process locked, AES-256-GCM encrypted configuration and secret storage for Python applications, command-line tools, and services.

It guarantees complete on-disk file, cross-process locking, and cryptographic wire format parity with:
- **.NET**: `NyssaAI.Configuration`
- **Node.js / TypeScript**: `@nyssa-ai/configuration`

## Features

- **Unified Configuration API (`IConfiguration`)**: Read and write plain settings, locally encrypted secrets (`"aesLocal"`), and external cloud vault pointers through a consistent interface.
- **Cross-Platform Parity**: Shares the `~/.nyssa-ai/config-manifest.json` layout, `<namespace>.json` envelope structure, cross-process `.lock` files with backoff, and FormatVersion 2 AES-256-GCM ciphertext authenticated with `(namespace, key)` AAD.
- **Address-Bound Local Encryption**: Encrypts secrets locally with AES-256-GCM using a 256-bit master key managed via platform credential stores (Windows Credential Manager, macOS Keychain, Linux Secret Service, or protected files).
- **Dictionary / DTO & Namespace Operations**: Store objects under single keys or decompose into individual namespace properties.

## Installation

```bash
pip install nyssa-ai-configuration
```

## Quick Start

```python
from nyssa_ai_configuration import ConfigurationClient

# Initialize client (provisions manifest and config directory on first run)
client = ConfigurationClient.initialize(application_name="OrdersService")

# 1. Plaintext setting
client.set("mail", "host", "smtp.example.com")
host = client.get("mail", "host")

# 2. AES-256-GCM encrypted secret
client.set("mail", "password", "SuperSecretP@ss!", encrypted=True, provider="aesLocal")
password = client.get("mail", "password")

# 3. Namespace replacement / dictionary
client.set_namespace("smtp", {
    "host": "smtp.example.com",
    "port": 587,
    "enable_ssl": True
})
smtp_settings = client.get_namespace("smtp")
```

## License

Licensed under the [Apache License, Version 2.0](LICENSE.txt).
