Flare Fireplace Quotes v1.5.1 corrects the v1.5.0 interface rollout while preserving the audited quote workflow and security model.

- Replaces the cramped three-column build screen with the approved two-pane Customer request and Quote workspace layout.
- Consolidates customer details, fireplace fields, lead time, features, media, added fireplaces, and the Generate preview action into one coherent workspace.
- Uses compact, content-sized actions and a restrained sticky footer so buttons remain inside the main, Settings, and Update windows.
- Replaces theme, settings, navigation, and caption font glyphs with DPI-independent vector geometry for crisp rendering at Windows scaling levels.
- Removes the automatic system-health popup and its startup code paths; normal startup now opens only the quote workspace and any verified update prompt.
- Adds a Windows-only rendered snapshot gate that captures the real WPF main and Settings windows, validates key control bounds, and publishes the PNGs for review.
- Keeps quote parsing, pricing, PDF generation, URL verification, Gmail drafting, saved settings, recent quotes, feature/media selection, multi-fireplace behavior, and automatic updates unchanged.
- Leaves the pinned updater trust policy, installer hash/size verification, DPAPI storage, logging redaction, pricing workbook, and resource workbooks unchanged.
