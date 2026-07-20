# v1.5.0 Source Package Manifest

The source backup contains:

- WPF App, Core, Infrastructure, and Tests projects.
- Bundled company pricing and resource workbooks.
- Maintained local and GitHub release workflows.
- Installer definition, updater policy, security documentation, and regression tests.
- The verified published installer and manifest when a matching release has completed.

Generated `bin`, `obj`, test results, credentials, OAuth tokens, personal settings, logs, reports, temporary PDFs, and other runtime state are excluded.

The production release is valid only when its tag matches `Directory.Build.props`, CI is green, and the live GitHub manifest matches the published installer byte-for-byte by size and SHA-256.
