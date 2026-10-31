# Cross-Platform Filesystem & Keyring Layout (Hypothetical App Example)

This document illustrates the exact on-disk locations for the **running code**, the **bootstrap manifest** (`config-manifest.json`), the **namespace configuration files** (`<namespace>.json`), and the **OS credential store master keys** across **Linux**, **Windows**, and **macOS**.

---

## Hypothetical Application: `AcmeCommerce`

In this scenario, we have **one application** (`AcmeCommerce`) composed of **two named components** that share the `AcmeCommerce` application configuration store and read/write their own component namespaces as well as shared database settings:

| Application / Component | Role | Component Binary / Entrypoint | Configuration Namespace File |
| :--- | :--- | :--- | :--- |
| **Application**: `AcmeCommerce` | Logical application identity registered in `config-manifest.json` and the OS Keyring (`nyssa-ai-configuration/AcmeCommerce`). | — | Mapped via `applications.AcmeCommerce.configurationDirectory` |
| **Component 1**: `checkout-api` | Customer-facing HTTP API service (.NET / TypeScript / Python) | `checkout-api` (`Checkout.Api.dll` / `dist/checkout-api.js`) | `checkout-api.json` |
| **Component 2**: `fulfillment-worker` | Background order fulfillment worker (.NET / TypeScript / Python) | `fulfillment-worker` (`Fulfillment.Worker.py` / `Fulfillment.Worker.dll`) | `fulfillment-worker.json` |

Both components initialize the configuration library with `applicationName: "AcmeCommerce"`:
- **Component 1 (`checkout-api`)** reads and writes namespace `"checkout-api"` (`checkout-api.json`).
- **Component 2 (`fulfillment-worker`)** reads and writes namespace `"fulfillment-worker"` (`fulfillment-worker.json`).

---

## 1. Linux (`user: svc_acme`, home: `/home/svc_acme`)

### A. Complete Directory Tree (Linux)

```text
/
├── opt/
│   └── acme-commerce/                                  <-- 1. RUNNING CODE (Application Install Root)
│       ├── checkout-api/                               <-- Named Component 1: checkout-api
│       │   ├── Checkout.Api.dll                        <-- Running binary (cwd: /opt/acme-commerce/checkout-api)
│       │   └── Checkout.Api.deps.json
│       └── fulfillment-worker/                         <-- Named Component 2: fulfillment-worker
│           ├── fulfillment_worker.py                   <-- Running worker (cwd: /opt/acme-commerce/fulfillment-worker)
│           └── pyproject.toml
│
└── home/
    └── svc_acme/                                       <-- User Profile Root ($HOME)
        └── .nyssa-ai/                                  <-- [chmod 0700] Component Bootstrap Directory
            ├── config-manifest.json                    <-- 2. MANIFEST [chmod 0600]
            ├── config-manifest.json.lock               <-- Manifest cross-process lock file [chmod 0600]
            │
            └── AcmeCommerce/                           <-- 3. CONFIGURATION FILES [chmod 0700]
                ├── checkout-api.json                   <-- Component 1 settings & secrets [chmod 0600]
                ├── checkout-api.json.lock              <-- Component 1 cross-process lock [chmod 0600]
                ├── fulfillment-worker.json             <-- Component 2 settings & secrets [chmod 0600]
                └── fulfillment-worker.json.lock        <-- Component 2 cross-process lock [chmod 0600]
```

> **Note on First-Run Default (Zero-Ceremony Mode):** If `config-manifest.json` does not exist yet and neither component passes an explicit `configDirectory`, the first component to start automatically provisions `/home/svc_acme/.nyssa-ai/config-manifest.json` with `configurationDirectory` set to `/home/svc_acme/.nyssa-ai/AcmeCommerce` (i.e., `<manifest_dir>/<applicationName>`). Once recorded in `config-manifest.json`, both `checkout-api` and `fulfillment-worker` resolve `/home/svc_acme/.nyssa-ai/AcmeCommerce` regardless of their working directories.

### B. OS Keyring Entry (Linux Secret Service / `libsecret`)
- **Backend**: D-Bus Secret Service (`secret-tool`)
- **Attributes**: `service = "nyssa-ai-configuration"`, `username = "AcmeCommerce"`
- **Label**: `nyssa-ai-configuration/AcmeCommerce`
- **Secret Payload**: 32-byte random master key, Base64-encoded (44 ASCII characters).

### C. Manifest File: `/home/svc_acme/.nyssa-ai/config-manifest.json`

```json
{
  "version": 1,
  "applications": {
    "AcmeCommerce": {
      "configurationDirectory": "/home/svc_acme/.nyssa-ai/AcmeCommerce",
      "lockTimeoutMs": 10000
    }
  }
}
```

---

## 2. Windows (`user: svc_acme`, profile: `C:\Users\svc_acme`)

### A. Complete Directory Tree (Windows)

```text
C:\
├── Program Files\
│   └── AcmeCommerce\                                   <-- 1. RUNNING CODE (Application Install Root)
│       ├── checkout-api\                               <-- Named Component 1: checkout-api
│       │   ├── Checkout.Api.exe                        <-- Running executable
│       │   └── Checkout.Api.dll
│       └── fulfillment-worker\                         <-- Named Component 2: fulfillment-worker
│           ├── node.exe
│           └── dist\
│               └── fulfillment-worker.js               <-- Running worker script
│
└── Users\
    └── svc_acme\                                       <-- User Profile Root (%USERPROFILE%)
        └── .nyssa-ai\                                  <-- [Owner/SYSTEM/Administrators ACL only]
            ├── config-manifest.json                    <-- 2. MANIFEST [Inheritance disabled, user-private ACL]
            ├── config-manifest.json.lock               <-- Sidecar lock file (FileShare.None / PID lock)
            │
            └── AcmeCommerce\                           <-- 3. CONFIGURATION FILES [Protected directory ACL]
                ├── checkout-api.json                   <-- Component 1 settings & secrets
                ├── checkout-api.json.lock              <-- Component 1 cross-process lock
                ├── fulfillment-worker.json             <-- Component 2 settings & secrets
                └── fulfillment-worker.json.lock        <-- Component 2 cross-process lock
```

### B. OS Keyring Entry (Windows Credential Manager)
- **Backend**: `Advapi32.dll` (`CredReadW` / `CredWriteW`)
- **Credential Type**: `CRED_TYPE_GENERIC` (`1`), `CRED_PERSIST_LOCAL_MACHINE` (`2`)
- **TargetName**: `nyssa-ai-configuration/AcmeCommerce`
- **CredentialBlob**: UTF-16LE encoded 44-character Base64 string representing the 32-byte master key.

### C. Manifest File: `C:\Users\svc_acme\.nyssa-ai\config-manifest.json`

```json
{
  "version": 1,
  "applications": {
    "AcmeCommerce": {
      "configurationDirectory": "C:/Users/svc_acme/.nyssa-ai/AcmeCommerce",
      "lockTimeoutMs": 10000
    }
  }
}
```

---

## 3. macOS (`user: svc_acme`, home: `/Users/svc_acme`)

### A. Complete Directory Tree (macOS)

```text
/
├── Applications/
│   └── AcmeCommerce.app/
│       └── Contents/
│           └── MacOS/                                  <-- 1. RUNNING CODE (Application Bundle Root)
│               ├── checkout-api/                       <-- Named Component 1: checkout-api
│               │   └── checkout-api                    <-- Running binary
│               └── fulfillment-worker/                 <-- Named Component 2: fulfillment-worker
│                   └── fulfillment-worker              <-- Running binary
│
└── Users/
    └── svc_acme/                                       <-- User Profile Root ($HOME)
        └── .nyssa-ai/                                  <-- [chmod 0700] Component Bootstrap Directory
            ├── config-manifest.json                    <-- 2. MANIFEST [chmod 0600]
            ├── config-manifest.json.lock               <-- Manifest cross-process lock file [chmod 0600]
            │
            └── AcmeCommerce/                           <-- 3. CONFIGURATION FILES [chmod 0700]
                ├── checkout-api.json                   <-- Component 1 settings & secrets [chmod 0600]
                ├── checkout-api.json.lock              <-- Component 1 cross-process lock [chmod 0600]
                ├── fulfillment-worker.json             <-- Component 2 settings & secrets [chmod 0600]
                └── fulfillment-worker.json.lock        <-- Component 2 cross-process lock [chmod 0600]
```

### B. OS Keyring Entry (macOS Keychain)
- **Backend**: `Security.framework` / `/usr/bin/security` generic password
- **Service (`-s`)**: `nyssa-ai-configuration`
- **Account (`-a`)**: `AcmeCommerce`
- **Password Data (`-w`)**: 32-byte random master key, Base64-encoded (44 ASCII characters).

### C. Manifest File: `/Users/svc_acme/.nyssa-ai/config-manifest.json`

```json
{
  "version": 1,
  "applications": {
    "AcmeCommerce": {
      "configurationDirectory": "/Users/svc_acme/.nyssa-ai/AcmeCommerce",
      "lockTimeoutMs": 10000
    }
  }
}
```

---

## 4. Component Configuration File Contents (Identical Across All 3 Platforms)

Because the wire format is platform- and language-agnostic, the JSON files inside `configurationDirectory` are byte-for-byte identical in structure on Linux, Windows, and macOS.

### Component 1 File: `<configurationDirectory>/checkout-api.json`

```json
{
  "ListenPort": {
    "value": 8443
  },
  "PublicBaseUrl": {
    "value": "https://checkout.acme.example.com"
  },
  "RateLimit": {
    "value": {
      "RequestsPerMinute": 600,
      "Burst": 50
    }
  },
  "StripeWebhookSigningSecret": {
    "value": "At7xK9mP2vQ8nL4rW1yB5cD6eF7gH8jK9mN0pQ1rS2tU3vW4xY5zA6bC7dE8fG9h",
    "encrypted": true,
    "provider": "aesLocal"
  },
  "GraphMailGrant": {
    "value": "Aj9zM1pR4xS0pN6tY3aD7eF8gH9iJ0kL1mN2oP3qR4sT5uV6wX7yZ8aB9cD0eF1g",
    "encrypted": true,
    "provider": "oauth"
  }
}
```

### Component 2 File: `<configurationDirectory>/fulfillment-worker.json`

```json
{
  "BatchSize": {
    "value": 25
  },
  "PollIntervalMs": {
    "value": 5000
  },
  "RetryPolicy": {
    "value": {
      "MaxAttempts": 5,
      "DeadLetterQueue": "orders-dlq"
    }
  },
  "WarehouseApiKey": {
    "value": "Av3bN5qT8zU2rP0vA4cF9gH0iJ1kL2mN3oP4qR5sT6uV7wX8yZ9aB0cD1eF2gH3i",
    "encrypted": true,
    "provider": "aesLocal"
  }
}
```

---

## 5. Summary Comparison Table

| Artifact | Linux (`svc_acme`) | Windows (`svc_acme`) | macOS (`svc_acme`) |
| :--- | :--- | :--- | :--- |
| **Component 1 Running Code** | `/opt/acme-commerce/checkout-api/` | `C:\Program Files\AcmeCommerce\checkout-api\` | `/Applications/AcmeCommerce.app/Contents/MacOS/checkout-api/` |
| **Component 2 Running Code** | `/opt/acme-commerce/fulfillment-worker/` | `C:\Program Files\AcmeCommerce\fulfillment-worker\` | `/Applications/AcmeCommerce.app/Contents/MacOS/fulfillment-worker/` |
| **Bootstrap Manifest** | `/home/svc_acme/.nyssa-ai/config-manifest.json` | `C:\Users\svc_acme\.nyssa-ai\config-manifest.json` | `/Users/svc_acme/.nyssa-ai/config-manifest.json` |
| **Component 1 Config File** | `/home/svc_acme/.nyssa-ai/AcmeCommerce/checkout-api.json` | `C:\Users\svc_acme\.nyssa-ai\AcmeCommerce\checkout-api.json` | `/Users/svc_acme/.nyssa-ai/AcmeCommerce/checkout-api.json` |
| **Component 2 Config File** | `/home/svc_acme/.nyssa-ai/AcmeCommerce/fulfillment-worker.json` | `C:\Users\svc_acme\.nyssa-ai\AcmeCommerce\fulfillment-worker.json` | `/Users/svc_acme/.nyssa-ai/AcmeCommerce/fulfillment-worker.json` |
| **OS Keyring Master Key** | Secret Service (`service=nyssa-ai-configuration`, `username=AcmeCommerce`) | Credential Manager (`TargetName=nyssa-ai-configuration/AcmeCommerce`) | macOS Keychain (`-s nyssa-ai-configuration -a AcmeCommerce`) |
| **Directory / File Permissions** | `0700` dirs / `0600` files | Inheritance disabled; `Owner`, `SYSTEM`, `Administrators` ACL | `0700` dirs / `0600` files |
