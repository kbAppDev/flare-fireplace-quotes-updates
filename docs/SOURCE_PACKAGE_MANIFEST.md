# v1.6.2 Source Package Manifest

The full backup contains:

- WPF App, Core, Infrastructure, and Tests projects.
- Bundled company pricing and resource workbooks.
- Maintained local and GitHub release workflows.
- Installer definition, updater policy, security documentation, and regression tests.
- The verified published installer, updater manifest, and portable build from the matching release.
- A release record identifying the exact tag and commit.

Generated `bin`, `obj`, test results, credentials, OAuth tokens, personal settings, logs, reports, temporary PDFs, `.git` metadata, and other runtime state are excluded.

The production release is valid only when its tag matches `Directory.Build.props`, CI is green, and the live GitHub manifest matches the published installer byte-for-byte by size and SHA-256.
