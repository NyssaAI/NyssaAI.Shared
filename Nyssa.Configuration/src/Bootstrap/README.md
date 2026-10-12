# Bootstrap

Owns the manifest: what it says, the reader that settles one application's section, and the
one write the library makes to it. The manifest is `~/.nyssa-ai/config-manifest.json` — one file
per user, one section per application name — or the same schema at a path the developer coded.

The section is the bootstrap. It names what the library needs before it can open the store and
that therefore cannot come from the store: the configuration directory, the key vault (by provider
name and `kind`, with an optional `endpoint`, an optional `credential` the vault authenticates
with, its own `settings`, and its own `secrets`), and the lock timeout. Encryption is never
mentioned because it is never optional. A relative `configurationDirectory` is relative to the
manifest's own folder, so a section can say `"SomeApp-cli"` and mean the folder beside the file.

`ConfigurationManifestReader` reads through a source-generated context, so the read is AOT-safe,
and settles the shape the composer sees: an absent map is an empty map, never null; the directory
is absolute; the timeout is parsed. No manifest, no section, or no directory is `NotFound` with
the path and the section in the message — the library never guesses a directory. A file that
exists but cannot be read is a different failure, because guessing "not configured" from a
permissions error would be exactly the wrong guess.

The manifest decides where every application's secrets are read from, so it may not be writable
by other accounts. When a section's vault carries `secrets`, the file may not be readable by them
either: startup fails naming the bit, who it lets in, and the command that fixes it, and never the
secret. The developer's `BootstrapOptions` are layered under the section — a manifest vault
replaces a same-named code vault whole, and the manifest's lock timeout wins.

`ConfigurationManifestWriter` is what the composer reaches for when an application has no
section yet: it sets one section's `configurationDirectory` under a sidecar lock,
through a temporary file renamed into place, and leaves every other field and every other section
as it found them.

Nothing here reads the process environment. An AI harness hands the environment to callers, so a
variable that decided which providers load would be an attack surface.
