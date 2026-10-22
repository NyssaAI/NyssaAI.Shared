import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ConfigurationManifestReader } from "../../src/manifest/ConfigurationManifestReader.js";
import { ConfigurationManifestWriter } from "../../src/manifest/ConfigurationManifestWriter.js";
import { TimeSpanParser } from "../../src/manifest/TimeSpanParser.js";

describe("ConfigurationManifestReader and Writer", () => {
  let tempDir: string;
  let manifestPath: string;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-manifest-test-"));
    manifestPath = path.join(tempDir, "config-manifest.json");
  });

  afterEach(() => {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  });

  it("timeSpanParser_dotNetFormat_parsesToMilliseconds", () => {
    const res1 = TimeSpanParser.parse("00:00:10");
    expect(res1.ok).toBe(true);
    if (res1.ok) expect(res1.value).toBe(10000);

    const res2 = TimeSpanParser.parse("00:01:30.500");
    expect(res2.ok).toBe(true);
    if (res2.ok) expect(res2.value).toBe(90500);
  });

  it("manifestWriter_newApplication_provisionsSection", () => {
    const configDir = path.join(tempDir, "app-config");
    const writeRes = ConfigurationManifestWriter.setConfigurationDirectory(
      manifestPath,
      "Cool-CLI",
      configDir,
      5000
    );

    expect(writeRes.ok).toBe(true);
    expect(fs.existsSync(manifestPath)).toBe(true);

    const readRes = ConfigurationManifestReader.readApplication(manifestPath, "Cool-CLI");
    expect(readRes.ok).toBe(true);
    if (readRes.ok) {
      expect(readRes.value.configurationDirectory).toBe(path.resolve(configDir));
      expect(readRes.value.lockTimeoutMs).toBe(5000);
    }
  });

  it("manifestReader_relativeDir_resolvesRelativeToManifest", () => {
    const writeRes = ConfigurationManifestWriter.setConfigurationDirectory(
      manifestPath,
      "RelativeApp",
      "./relative-config"
    );
    expect(writeRes.ok).toBe(true);

    const readRes = ConfigurationManifestReader.readApplication(manifestPath, "RelativeApp");
    expect(readRes.ok).toBe(true);
    if (readRes.ok) {
      const expected = path.resolve(tempDir, "relative-config");
      expect(readRes.value.configurationDirectory).toBe(expected);
    }
  });
});
