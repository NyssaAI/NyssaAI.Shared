// Responsibility: OS keychain adapter supporting Windows Credential Manager, macOS Keychain, and Linux Secret Service.
// Public interface:
// `-- KeyringOsPlatform
//     +-- delete(service, account, signal)
//     +-- get(service, account, signal)
//     `-- set(service, account, secret, signal)

import { execFileSync } from "node:child_process";
import { ok, fail, type Result } from "../results/Result.js";
import type { IKeyring } from "./IKeyring.js";

/**
 * KeyringOsPlatform interacts with the native OS credential store using identical
 * service/account addressing and base64 encoding as .NET KeyringWindows, KeyringMacOs, and KeyringLinux.
 */
export class KeyringOsPlatform {
  private static readonly WIN_CRED_CSHARP = `
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class NyssaCredMan {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree([In] IntPtr cred);

    public static string Read(string target, out int err) {
        IntPtr ptr;
        if (!CredRead(target, 1, 0, out ptr)) {
            err = Marshal.GetLastWin32Error();
            return null;
        }
        err = 0;
        try {
            CREDENTIAL cred = (CREDENTIAL)Marshal.PtrToStructure(ptr, typeof(CREDENTIAL));
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0) return "";
            byte[] bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, (int)cred.CredentialBlobSize);
            return Encoding.Unicode.GetString(bytes);
        } finally {
            CredFree(ptr);
        }
    }

    public static int Write(string target, string secret) {
        byte[] bytes = Encoding.Unicode.GetBytes(secret);
        IntPtr targetPtr = Marshal.StringToHGlobalUni(target);
        IntPtr blobPtr = Marshal.AllocHGlobal(bytes.Length);
        try {
            Marshal.Copy(bytes, 0, blobPtr, bytes.Length);
            CREDENTIAL cred = new CREDENTIAL();
            cred.Type = 1;
            cred.TargetName = targetPtr;
            cred.CredentialBlobSize = (uint)bytes.Length;
            cred.CredentialBlob = blobPtr;
            cred.Persist = 2;
            if (!CredWrite(ref cred, 0)) return Marshal.GetLastWin32Error();
            return 0;
        } finally {
            Marshal.FreeHGlobal(blobPtr);
            Marshal.FreeHGlobal(targetPtr);
        }
    }

    public static int Delete(string target) {
        if (!CredDelete(target, 1, 0)) return Marshal.GetLastWin32Error();
        return 0;
    }
}
`;

  public async get(service: string, account: string, signal?: AbortSignal): Promise<Result<Buffer | null>> {
    signal?.throwIfAborted();
    const addrRes = this.validateAddress(service, account);
    if (!addrRes.ok) {
      return addrRes;
    }

    let rawRes: Result<string | null>;
    if (process.platform === "win32") {
      rawRes = this.getWindows(addrRes.value);
    } else if (process.platform === "darwin") {
      rawRes = this.getMacOs(service, account);
    } else if (process.platform === "linux") {
      rawRes = this.getLinux(service, account);
    } else {
      return fail(`Unsupported platform '${process.platform}' for OS keyring.`, "Unavailable");
    }

    if (!rawRes.ok) {
      return rawRes;
    }
    if (rawRes.value === null) {
      return ok(null);
    }

    const b64 = rawRes.value.trim();
    if (!/^[A-Za-z0-9+/]+=*$/.test(b64)) {
      return fail(
        `keyring: '${service}/${account}' does not hold 32 bytes of base64 material`,
        "Validation"
      );
    }

    const decoded = Buffer.from(b64, "base64");
    if (decoded.length !== 32) {
      return fail(
        `keyring: '${service}/${account}' does not hold 32 bytes of base64 material`,
        "Validation"
      );
    }

    return ok(decoded);
  }

  public async set(service: string, account: string, secret: Buffer, signal?: AbortSignal): Promise<Result<void>> {
    signal?.throwIfAborted();
    const addrRes = this.validateAddress(service, account);
    if (!addrRes.ok) {
      return addrRes;
    }

    if (secret.length < 16 || secret.length > 64) {
      return fail(
        `keyring: ${secret.length} bytes is outside the supported range of 16 to 64`,
        "Validation"
      );
    }

    const encoded = secret.toString("base64");
    if (process.platform === "win32") {
      return this.setWindows(addrRes.value, encoded);
    }
    if (process.platform === "darwin") {
      return this.setMacOs(service, account, encoded);
    }
    if (process.platform === "linux") {
      return this.setLinux(service, account, addrRes.value, encoded);
    }
    return fail(`Unsupported platform '${process.platform}' for OS keyring.`, "Unavailable");
  }

  public async delete(service: string, account: string, signal?: AbortSignal): Promise<Result<void>> {
    signal?.throwIfAborted();
    const addrRes = this.validateAddress(service, account);
    if (!addrRes.ok) {
      return addrRes;
    }

    if (process.platform === "win32") {
      return this.deleteWindows(addrRes.value);
    }
    if (process.platform === "darwin") {
      return this.deleteMacOs(service, account);
    }
    if (process.platform === "linux") {
      return this.deleteLinux(service, account);
    }
    return fail(`Unsupported platform '${process.platform}' for OS keyring.`, "Unavailable");
  }

  private validateAddress(service: string, account: string): Result<string> {
    if (!service || service.trim().length === 0) {
      return fail("keyring: service name must not be blank", "Validation");
    }
    if (!account || account.trim().length === 0) {
      return fail("keyring: key name must not be blank", "Validation");
    }
    if (/[\x00-\x1F\x7F]/.test(service) || /[\x00-\x1F\x7F]/.test(account)) {
      return fail("keyring: service and key must not contain control characters", "Validation");
    }
    return ok(`${service}/${account}`);
  }

  private getWindows(target: string): Result<string | null> {
    try {
      const script = `
Add-Type -TypeDefinition $env:NYSSA_CRED_CSHARP
$err = 0
$val = [NyssaCredMan]::Read($env:NYSSA_CRED_TARGET, [ref]$err)
if ($err -eq 1168) { Write-Output "NOT_FOUND" }
elseif ($err -ne 0) { Write-Error "ERR:$err"; exit 1 }
else { Write-Output "FOUND:$val" }
`;
      const output = execFileSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], {
        encoding: "utf8",
        env: {
          ...process.env,
          NYSSA_CRED_CSHARP: KeyringOsPlatform.WIN_CRED_CSHARP,
          NYSSA_CRED_TARGET: target
        },
        stdio: ["ignore", "pipe", "pipe"]
      }).trim();

      if (output === "NOT_FOUND") {
        return ok(null);
      }
      if (output.startsWith("FOUND:")) {
        return ok(output.slice("FOUND:".length));
      }
      return fail(`keyring: CredReadW returned unexpected output`, "Unavailable");
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: CredReadW failed: ${message}`, "Unavailable");
    }
  }

  private setWindows(target: string, secret: string): Result<void> {
    try {
      const script = `
Add-Type -TypeDefinition $env:NYSSA_CRED_CSHARP
$err = [NyssaCredMan]::Write($env:NYSSA_CRED_TARGET, $env:NYSSA_CRED_SECRET)
if ($err -ne 0) { Write-Error "ERR:$err"; exit 1 }
`;
      execFileSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], {
        encoding: "utf8",
        env: {
          ...process.env,
          NYSSA_CRED_CSHARP: KeyringOsPlatform.WIN_CRED_CSHARP,
          NYSSA_CRED_TARGET: target,
          NYSSA_CRED_SECRET: secret
        },
        stdio: ["ignore", "pipe", "pipe"]
      });
      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: CredWriteW failed: ${message}`, "Unavailable");
    }
  }

  private deleteWindows(target: string): Result<void> {
    try {
      const script = `
Add-Type -TypeDefinition $env:NYSSA_CRED_CSHARP
$err = [NyssaCredMan]::Delete($env:NYSSA_CRED_TARGET)
if ($err -ne 0 -and $err -ne 1168) { Write-Error "ERR:$err"; exit 1 }
`;
      execFileSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], {
        encoding: "utf8",
        env: {
          ...process.env,
          NYSSA_CRED_CSHARP: KeyringOsPlatform.WIN_CRED_CSHARP,
          NYSSA_CRED_TARGET: target
        },
        stdio: ["ignore", "pipe", "pipe"]
      });
      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: CredDeleteW failed: ${message}`, "Unavailable");
    }
  }

  private getMacOs(service: string, account: string): Result<string | null> {
    try {
      const output = execFileSync(
        "/usr/bin/security",
        ["find-generic-password", "-s", service, "-a", account, "-w"],
        { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }
      );
      return ok(output.trim());
    } catch (err: unknown) {
      const status = (err as { status?: number }).status;
      if (status === 44) {
        return ok(null);
      }
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: SecItemCopyMatching failed: ${message}`, "Unavailable");
    }
  }

  private setMacOs(service: string, account: string, secret: string): Result<void> {
    try {
      execFileSync(
        "/usr/bin/security",
        ["add-generic-password", "-U", "-s", service, "-a", account, "-w", secret],
        { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }
      );
      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: SecItemAdd failed: ${message}`, "Unavailable");
    }
  }

  private deleteMacOs(service: string, account: string): Result<void> {
    try {
      execFileSync(
        "/usr/bin/security",
        ["delete-generic-password", "-s", service, "-a", account],
        { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }
      );
      return ok(undefined);
    } catch (err: unknown) {
      const status = (err as { status?: number }).status;
      if (status === 44) {
        return ok(undefined);
      }
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: SecItemDelete failed: ${message}`, "Unavailable");
    }
  }

  private getLinux(service: string, account: string): Result<string | null> {
    try {
      const output = execFileSync(
        "secret-tool",
        ["lookup", "service", service, "username", account],
        { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }
      );
      return ok(output.trim());
    } catch (err: unknown) {
      const status = (err as { status?: number; stderr?: Buffer | string }).status;
      const stderr = String((err as { stderr?: Buffer | string }).stderr ?? "").trim();
      if (status === 1 && stderr.length === 0) {
        return ok(null);
      }
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: the Secret Service reported error: ${stderr || message}`, "Unavailable");
    }
  }

  private setLinux(service: string, account: string, label: string, secret: string): Result<void> {
    try {
      execFileSync(
        "secret-tool",
        ["store", `--label=${label}`, "service", service, "username", account],
        { input: secret, encoding: "utf8", stdio: ["pipe", "pipe", "pipe"] }
      );
      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: the Secret Service refused write: ${message}`, "Unavailable");
    }
  }

  private deleteLinux(service: string, account: string): Result<void> {
    try {
      execFileSync(
        "secret-tool",
        ["clear", "service", service, "username", account],
        { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }
      );
      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: the Secret Service refused deletion: ${message}`, "Unavailable");
    }
  }
}

const _typeCheck: IKeyring = null as unknown as KeyringOsPlatform;
void _typeCheck;
