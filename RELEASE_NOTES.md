Flare Fireplace Quotes v1.4.11 is a full reliability and security hardening release.

- Protects Gmail OAuth migration by removing legacy plaintext token remnants after verified DPAPI encryption.
- Rejects malformed, unverifiable, or unsupported signed update manifests and binds signed manifests to installer size.
- Restricts update and shared-workbook redirects to approved HTTPS hosts, ports, and assets.
- Bounds remote workbook, settings, token, manifest, installer, and encrypted-history payloads.
- Verifies downloaded shared pricing files are valid XLSX packages before atomically replacing the cache.
- Makes settings and UI preference writes atomic, normalizes user-configurable values, and removes dead settings controls.
- Strengthens log redaction for OAuth and bearer-token formats and adds bounded log rotation.
- Tracks long-running UI commands to prevent duplicate fire-and-forget workflows and avoids blocking ZIP lookups.
- Adds regression tests, transitive NuGet vulnerability gates, pinned CI actions, Dependabot, and a tag-driven release pipeline.
