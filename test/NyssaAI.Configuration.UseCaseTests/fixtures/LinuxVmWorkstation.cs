using System.Diagnostics;
using System.Text;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>
/// Spins up an isolated disposable Linux container acting as a clean VM with a private user home
/// (<c>/home/svc_acme</c>), an unlocked D-Bus Secret Service keyring (<c>gnome-keyring-daemon</c>),
/// and the C#, TypeScript, and Python configuration libraries ready to execute against the real
/// container filesystem and OS keyring.
/// </summary>
public sealed class LinuxVmWorkstation : IDisposable
{
    public const string HomeDirectory = "/home/svc_acme";
    public const string DefaultManifestPath = "/home/svc_acme/.nyssa-ai/config-manifest.json";
    private const string ImageName = "nyssa-config-vm-test:local";

    private readonly string _containerId;
    private bool _disposed;

    public LinuxVmWorkstation()
    {
        EnsureImageReady();
        var repoRoot = LocateRepoRoot();
        var hostBin = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        _containerId = RunHostProcess(
            "docker",
            ["run", "-d", "--rm", "--tmpfs", "/tmp/keyrings:rw,exec,mode=0700", "-v", $"{repoRoot}:/repo:ro", "-v", $"{hostBin}:/host-bin:ro", ImageName, "sleep", "300"],
            stdin: null
        ).Trim();

        try
        {
            InitializeVmEnvironment();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void WriteWithCSharp(string applicationName, string @namespace, string key, string value, string? provider = null, string? manifestPath = null, string? configDirectory = null) =>
        ExecBash($"dotnet /tmp/cs-probe/out/Probe.dll set {Quote(applicationName)} {Quote(@namespace)} {Quote(key)} {Quote(value)} {Quote(provider ?? "")} {Quote(manifestPath ?? "")} {Quote(configDirectory ?? "")}");

    public string ReadWithCSharp(string applicationName, string @namespace, string key, string? manifestPath = null, string? configDirectory = null) =>
        ExecBash($"dotnet /tmp/cs-probe/out/Probe.dll get {Quote(applicationName)} {Quote(@namespace)} {Quote(key)} {Quote(manifestPath ?? "")} {Quote(configDirectory ?? "")}").TrimEnd('\r', '\n');

    public void WriteWithTypeScript(string applicationName, string @namespace, string key, string value, string? provider = null, string? manifestPath = null, string? configDirectory = null) =>
        ExecBash(
            $$"""
            NYSSA_APP={{Quote(applicationName)}} NYSSA_NS={{Quote(@namespace)}} NYSSA_KEY={{Quote(key)}} NYSSA_VAL={{Quote(value)}} NYSSA_PROVIDER={{Quote(provider ?? "")}} NYSSA_MANIFEST={{Quote(manifestPath ?? "")}} NYSSA_DIR={{Quote(configDirectory ?? "")}} \
            node --input-type=module - <<'EOF'
            import { ConfigurationClient } from "/repo/nyssa-ai-configuration/dist/ConfigurationClient.js";
            const opts = { applicationName: process.env.NYSSA_APP };
            if (process.env.NYSSA_MANIFEST) opts.configManifest = process.env.NYSSA_MANIFEST;
            if (process.env.NYSSA_DIR) opts.configDirectory = process.env.NYSSA_DIR;
            const res = await ConfigurationClient.initialize(opts);
            if (!res.ok) throw new Error(res.error);
            const writeRes = await res.value.set(process.env.NYSSA_NS, process.env.NYSSA_KEY, process.env.NYSSA_VAL, process.env.NYSSA_PROVIDER ? { provider: process.env.NYSSA_PROVIDER } : undefined);
            if (!writeRes.ok) throw new Error(writeRes.error);
            EOF
            """
        );

    public string ReadWithTypeScript(string applicationName, string @namespace, string key, string? manifestPath = null, string? configDirectory = null) =>
        ExecBash(
            $$"""
            NYSSA_APP={{Quote(applicationName)}} NYSSA_NS={{Quote(@namespace)}} NYSSA_KEY={{Quote(key)}} NYSSA_MANIFEST={{Quote(manifestPath ?? "")}} NYSSA_DIR={{Quote(configDirectory ?? "")}} \
            node --input-type=module - <<'EOF'
            import { ConfigurationClient } from "/repo/nyssa-ai-configuration/dist/ConfigurationClient.js";
            const opts = { applicationName: process.env.NYSSA_APP };
            if (process.env.NYSSA_MANIFEST) opts.configManifest = process.env.NYSSA_MANIFEST;
            if (process.env.NYSSA_DIR) opts.configDirectory = process.env.NYSSA_DIR;
            const res = await ConfigurationClient.initialize(opts);
            if (!res.ok) throw new Error(res.error);
            const readRes = await res.value.get(process.env.NYSSA_NS, process.env.NYSSA_KEY);
            if (!readRes.ok) throw new Error(readRes.error);
            process.stdout.write(readRes.value);
            EOF
            """
        );

    public void WriteWithPython(string applicationName, string @namespace, string key, string value, string? provider = null, string? manifestPath = null, string? configDirectory = null) =>
        ExecBash(
            $$"""
            PYTHONPATH=/repo/nyssa-ai-configuration-py/src /opt/venv/bin/python - {{Quote(applicationName)}} {{Quote(@namespace)}} {{Quote(key)}} {{Quote(value)}} {{Quote(provider ?? "")}} {{Quote(manifestPath ?? "")}} {{Quote(configDirectory ?? "")}} <<'EOF'
            import sys
            from nyssa_ai_configuration import BootstrapOptions, ConfigurationClient
            _, app_name, ns, key, val, provider, manifest_path, config_dir = sys.argv
            opts = BootstrapOptions(application_name=app_name, manifest_path=manifest_path or None, configuration_directory=config_dir or None)
            res = ConfigurationClient.create(opts)
            assert res.ok, f"Python create failed: {res}"
            write_res = res.value.set(ns, key, val, provider=provider or None)
            assert write_res.ok, f"Python set failed: {write_res}"
            EOF
            """
        );

    public string ReadWithPython(string applicationName, string @namespace, string key, string? manifestPath = null, string? configDirectory = null) =>
        ExecBash(
            $$"""
            PYTHONPATH=/repo/nyssa-ai-configuration-py/src /opt/venv/bin/python - {{Quote(applicationName)}} {{Quote(@namespace)}} {{Quote(key)}} {{Quote(manifestPath ?? "")}} {{Quote(configDirectory ?? "")}} <<'EOF'
            import sys
            from nyssa_ai_configuration import BootstrapOptions, ConfigurationClient
            _, app_name, ns, key, manifest_path, config_dir = sys.argv
            opts = BootstrapOptions(application_name=app_name, manifest_path=manifest_path or None, configuration_directory=config_dir or None)
            res = ConfigurationClient.create(opts)
            assert res.ok, f"Python create failed: {res}"
            read_res = res.value.get(ns, key)
            assert read_res.ok, f"Python get failed: {read_res}"
            sys.stdout.write(read_res.value)
            EOF
            """
        );

    public bool FileExists(string path) =>
        ExecBash($"if [ -f {Quote(path)} ]; then echo yes; else echo no; fi").Trim() == "yes";

    public bool DirectoryExists(string path) =>
        ExecBash($"if [ -d {Quote(path)} ]; then echo yes; else echo no; fi").Trim() == "yes";

    public string ReadFile(string path) => ExecBash($"cat {Quote(path)}");

    public string ModeOctal(string path) => ExecBash($"stat -c '%a' {Quote(path)}").Trim();

    public string ReadKeyringSecret(string service, string account) =>
        ExecBash($"secret-tool lookup service {Quote(service)} username {Quote(account)}").Trim();

    private void InitializeVmEnvironment()
    {
        ExecBash(
            """
            mkdir -p -m 0700 /home/svc_acme /home/svc_acme/.local/share /tmp/runtime /tmp/cs-probe
            ln -sfn /tmp/keyrings /home/svc_acme/.local/share/keyrings
            dbus-daemon --session --address="unix:path=/tmp/runtime/bus" --fork
            export DBUS_SESSION_BUS_ADDRESS="unix:path=/tmp/runtime/bus"
            echo -n "nyssa-vm-unlock" | gnome-keyring-daemon --unlock --components=secrets >/dev/null

            cat > /tmp/cs-probe/Probe.csproj << 'CSPROJ'
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="NyssaAI.Configuration"><HintPath>/host-bin/NyssaAI.Configuration.dll</HintPath></Reference>
                <Reference Include="NyssaAI.Result"><HintPath>/host-bin/NyssaAI.Result.dll</HintPath></Reference>
                <Reference Include="Microsoft.Extensions.DependencyInjection"><HintPath>/host-bin/Microsoft.Extensions.DependencyInjection.dll</HintPath></Reference>
                <Reference Include="Microsoft.Extensions.DependencyInjection.Abstractions"><HintPath>/host-bin/Microsoft.Extensions.DependencyInjection.Abstractions.dll</HintPath></Reference>
                <Reference Include="Microsoft.Extensions.Logging"><HintPath>/host-bin/Microsoft.Extensions.Logging.dll</HintPath></Reference>
                <Reference Include="Microsoft.Extensions.Logging.Abstractions"><HintPath>/host-bin/Microsoft.Extensions.Logging.Abstractions.dll</HintPath></Reference>
                <Reference Include="Microsoft.Extensions.Http"><HintPath>/host-bin/Microsoft.Extensions.Http.dll</HintPath></Reference>
                <Reference Include="Microsoft.Extensions.Options"><HintPath>/host-bin/Microsoft.Extensions.Options.dll</HintPath></Reference>
                <Reference Include="Microsoft.Extensions.Primitives"><HintPath>/host-bin/Microsoft.Extensions.Primitives.dll</HintPath></Reference>
              </ItemGroup>
            </Project>
            CSPROJ

            cat > /tmp/cs-probe/Program.cs << 'CS'
            using Microsoft.Extensions.DependencyInjection;
            using NyssaAI.Configuration;
            using NyssaAI.Configuration.Client;

            var mode = args[0];
            var appName = args[1];
            var ns = args[2];
            var key = args[3];
            var manifestPath = string.IsNullOrEmpty(args[mode == "set" ? 6 : 4]) ? null : args[mode == "set" ? 6 : 4];
            var configDir = string.IsNullOrEmpty(args[mode == "set" ? 7 : 5]) ? null : args[mode == "set" ? 7 : 5];

            var services = new ServiceCollection();
            services.AddNyssaConfiguration(options =>
            {
                options.ApplicationName = appName;
                if (manifestPath is not null) options.Bootstrap.ConfigManifest = manifestPath;
                if (configDir is not null) options.ConfigDirectory = configDir;
            });
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            if (mode == "set")
                await config.SetAsync(ns, key, args[4], provider: string.IsNullOrEmpty(args[5]) ? null : args[5]);
            else
                Console.Write(await config.GetAsync(ns, key));
            CS

            dotnet build /tmp/cs-probe/Probe.csproj -o /tmp/cs-probe/out --nologo -v q >/dev/null
            """
        );
    }

    private string ExecBash(string script)
    {
        var preamble = "set -euo pipefail\nexport HOME=/home/svc_acme\nexport XDG_RUNTIME_DIR=/tmp/runtime\nexport DBUS_SESSION_BUS_ADDRESS=\"unix:path=/tmp/runtime/bus\"\n";
        return RunHostProcess("docker", ["exec", "-i", _containerId, "bash"], (preamble + script).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static void EnsureImageReady()
    {
        try
        {
            RunHostProcess("docker", ["image", "inspect", ImageName], stdin: null);
        }
        catch
        {
            const string dockerfile = """
                FROM nyssa-pilot-warm:2026-10-12
                RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
                    dbus-x11 gnome-keyring libsecret-tools libsecret-1-0 && rm -rf /var/lib/apt/lists/* && \
                    curl -LsSf https://astral.sh/uv/install.sh | env UV_INSTALL_DIR=/usr/local/bin sh && \
                    UV_PYTHON_INSTALL_DIR=/opt/python uv venv /opt/venv --python 3.13 && \
                    uv pip install --python /opt/venv/bin/python cryptography
                """;
            RunHostProcess("docker", ["build", "-t", ImageName, "-"], dockerfile);
        }
    }

    private static string LocateRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NyssaAI.Shared.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root could not be located from " + AppContext.BaseDirectory);
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string RunHostProcess(string fileName, IReadOnlyList<string> arguments, string? stdin)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using (var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {fileName}."))
        {
            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                var details = new StringBuilder()
                    .AppendLine($"Command '{fileName} {string.Join(' ', arguments)}' exited with code {process.ExitCode}.")
                    .AppendLine(stderr)
                    .AppendLine(stdout)
                    .ToString();
                throw new InvalidOperationException(details);
            }

            return stdout;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            RunHostProcess("docker", ["rm", "-f", _containerId], stdin: null);
        }
        catch
        {
            // Container is started with --rm; best-effort forced removal on teardown.
        }
    }
}
