// Responsibility: Windows Credential Manager P/Invoke helper via PowerShell for generic credentials.
// Public interface:
// `-- KeyringWindowsCredMan
//     +-- delete(target)
//     +-- get(target)
//     `-- set(target, secret)

import { execFileSync } from "node:child_process";
import { ok, fail, type Result } from "../results/Result.js";

/**
 * KeyringWindowsCredMan invokes Advapi32 CredReadW, CredWriteW, and CredDeleteW
 * matching .NET KeyringWindows and Python KeyringWindows generic credential storage.
 */
export class KeyringWindowsCredMan {
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

  public static get(target: string): Result<string | null> {
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
          NYSSA_CRED_CSHARP: this.WIN_CRED_CSHARP,
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
      return fail("keyring: CredReadW returned unexpected output", "Unavailable");
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: CredReadW failed: ${message}`, "Unavailable");
    }
  }

  public static set(target: string, secret: string): Result<void> {
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
          NYSSA_CRED_CSHARP: this.WIN_CRED_CSHARP,
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

  public static delete(target: string): Result<void> {
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
          NYSSA_CRED_CSHARP: this.WIN_CRED_CSHARP,
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
}
