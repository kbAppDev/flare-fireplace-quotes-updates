# Flare Fireplace Quotes - Professional Hardening Notes

Implemented foundation:

1. Code-signing-ready release script:
   - `Build_Publish_Professional_Release.ps1`
   - Supports optional certificate thumbprint or PFX signing.
   - Keeps one installer EXE: `Flare.Fireplace.Quotes.exe`.

2. Permanent self-contained validation:
   - Professional release script publishes with `--self-contained true`.
   - Fails if `runtimeOptions.frameworks` exists in runtimeconfig.
   - `Validate_SelfContained_Release.ps1` can be run independently.

3. DPAPI/security audit:
   - Gmail OAuth token store was already DPAPI-backed through `ProtectedFileDataStore`.
   - New `SecurityAuditService` checks for plain token JSON and obvious token secrets in settings.

4. Manifest signature validation foundation:
   - Manifest now supports `signature` and `signatureAlgorithm`.
   - Updater validates the signature when configured.
   - Unsigned manifests remain allowed unless strict validation is enabled.

5. Redacted logging:
   - `RedactingFileLogger` writes to AppData.
   - Emails, tokens, auth-header strings, and local user paths are redacted.

6. First-run health check:
   - `SystemHealthWindow` runs once per user profile.
   - Checks local data files, update feed, runtime packaging, Gmail token protection, settings, and logging.

7. Cleaner error handling:
   - Global unhandled UI exceptions are logged and shown with safer language.
   - Update check errors are logged without blocking app startup.