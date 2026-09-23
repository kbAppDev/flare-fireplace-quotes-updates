# Security and Release Notes

## Sensitive files excluded

- `LocalData/gmail_credentials.json`
- OAuth token files
- personal settings
- generated quote PDFs
- debug logs
- build output and repository history

## Implemented hardening

- Gmail OAuth tokens use Windows DPAPI CurrentUser protection.
- Recall quote history uses DPAPI CurrentUser encryption in `recent_quotes.json.dpapi`.
- Existing plaintext recall history is migrated once, verified, overwritten, and deleted.
- Gmail credentials are consumed only from `%LOCALAPPDATA%\Flare Fireplace Quotes\Credentials\gmail_credentials.json` after an explicit one-time import.
- Email headers are sanitized and recipients are validated.
- Logs and user-facing errors redact local paths, email addresses, and sensitive values where practical.
- Quote PDFs are created only under the app-owned Temp directory.
- The generated PDF is deleted immediately after a Gmail draft is confirmed.
- Failed or abandoned temporary PDFs are retained briefly for retry and cleaned after two hours.
- Update checks are pinned to the Flare-managed GitHub repository and exact release asset naming.
- Update manifests must provide a valid semantic version, 64-character SHA-256, and expected installer size.
- Update manifests must carry a valid RS256 signature from the release key compiled into the application.
- Installer downloads are rejected when the host, path, size, or SHA-256 does not match the trusted manifest data.
- Release builds run without a write-capable GitHub token. A separate job signs the manifest, creates a draft,
  re-downloads and verifies every asset, and only then publishes the release.
- The .NET SDK, NuGet dependency graph, Inno Setup, and SBOM generator are version-pinned.
- Every release includes an SPDX SBOM and `THIRD-PARTY-NOTICES.md`.

## Free update-manifest signing

Manifest signing is fully automated and does not require a paid certificate or signing account. It authenticates
the manifest that supplies the installer URL, size, and SHA-256. The private key lives only in the tag-restricted
`release` environment as the `UPDATE_MANIFEST_SIGNING_KEY_PEM` Actions secret; ordinary branches and pull requests
cannot access it.

```powershell
Get-Content -LiteralPath "C:\secure\flare-quotes-manifest-signing-private.pem" -Raw |
  gh secret set UPDATE_MANIFEST_SIGNING_KEY_PEM --env release --repo kbAppDev/flare-fireplace-quotes-updates
```

The configured key must match the embedded public-key SHA-256 fingerprint
`098c6b505a5cecbe6c060c80633f4283cc12597291113eb266ef3293bfff2066`. The workflow fails before creating a
release if the secret is absent or does not match. A Windows DPAPI CurrentUser recovery copy is stored outside the
repository under the app's local ReleaseKeys directory; plaintext private keys must never be retained. Do not
regenerate it casually: safe key rotation requires shipping support for the replacement key before signing solely
with it.

Version 1.6.9 and newer read only the mandatory-signed `flare-quotes-v2-latest.json` feed. Releases also carry an
unsigned `flare-quotes-v1-latest.json` compatibility feed so already-installed v1.6.8 clients can receive the
v1.6.9 upgrade through their existing updater. The hardened app never reads that legacy feed.

## Intentional Authenticode decision

Paid Windows Authenticode publisher signing is intentionally not required for this two-person internal deployment
lane. Windows may therefore show an unknown-publisher warning on a new machine. This does not disable the app's
mandatory manifest signature, repository pinning, expected-size check, or installer SHA-256 verification.

## Team cautions

- Distribute installers only through the approved GitHub release/update lane.
- Do not email or upload `gmail_credentials.json` to chat, tickets, or public storage.
- Do not commit `bin`, `obj`, logs, tokens, credentials, or generated PDFs.
- Close the app before rebuilding to avoid locked DLL errors.
