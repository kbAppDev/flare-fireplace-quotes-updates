# v1.6.4 Source Package Manifest

The full backup contains:

- The exact tracked source exported from tag `v1.6.4`.
- WPF App, Core, Infrastructure, and Tests projects.
- Bundled company pricing and resource workbooks.
- Maintained local and GitHub release workflows.
- Installer definition, updater policy, security documentation, and regression tests.
- The matching verified installer, updater manifest, and portable build.
- A machine-readable backup manifest with tag, commit, byte sizes, and SHA-256 hashes.
- Restore instructions.

Generated `bin`, `obj`, test results, credentials, OAuth tokens, personal settings, logs, reports, temporary PDFs, `.git` metadata, and runtime state are excluded.

The production release is valid only when its tag matches `Directory.Build.props`, CI is green, the live GitHub manifest matches the installer byte-for-byte, and the full backup is attached to the same release.
