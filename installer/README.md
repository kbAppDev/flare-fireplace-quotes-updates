# Installer output

This directory is intentionally empty in the source candidate.

`Build_Release_Installer.ps1` creates the local installer and update manifest here.
`Build_Publish_Professional_Release.ps1` creates the versioned GitHub release assets here before publication.

Generated `.exe` and `.json` release assets are excluded from source control and source-only backups.
