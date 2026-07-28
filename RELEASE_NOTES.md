Flare Fireplace Quotes v1.6.4 is a focused correctness, resilience, and safe-cleanup release based on the approved v1.6.3 application.

- Reads numeric workbook prices by numeric value instead of locale-formatted display text.
- Preserves the en-US text-price fallback while preventing locale formatting from changing numeric prices.
- Returns a safe empty workbook when a pricing file is corrupt, locked, or partially synchronized instead of crashing the quote workflow.
- Redacts absolute Windows paths and email addresses from update-error messages shown to users.
- Removes seven verified unreferenced members, including a parser method containing a copied PowerShell newline artifact.
- Consolidates two identical resource-key normalizers into the existing shared compact normalizer.
- Adds regression coverage for numeric price loading under a non-US process culture, en-US text-price fallback, and corrupt-workbook handling.
- Keeps the approved interface, pricing workbook, 266-model active inventory, model mappings, PDF generation, Gmail drafting, updater behavior, quote history, settings, and real-flame-and-ash removal animation unchanged.

Manifest signing and Authenticode enforcement remain a separate release-infrastructure project; this update does not enable strict signing before signed artifacts and a protected public trust anchor exist.
- Corrected exact base-SKU, MSRP, and specification matching for Commercial Front Facing, Commercial See Through, Outdoor Left Corner, and Outdoor Double Corner models.
- Added complete-SKU Auto-fill support for Commercial, Outdoor Vent Free, Large, Traditional, and Passage fireplace codes.
- Corrected VFDC/VFLC/VFRC/VFST model normalization so specific Outdoor Vent Free styles cannot fall back to Front Facing.
- Corrected Verify URLs grouping so each quoted fireplace produces exactly one card, including duplicate model instances.
- Added visible Gmail recipient validation and progress/status feedback on the Verify URLs page.
- Removes all 36 discontinued Commercial Front Facing and See Through models from the active catalog, pricing load, parser, automated inventory, and release gates.
- Makes VFDC and VDC use the same Outdoor Vent Free Double Corner optional-feature rules.
- Adds click-and-drag vertical reordering for fireplace cards and preserves that order through PDF, URL verification, and Gmail draft creation.
- Preserves one resource-link set for every quoted fireplace instance, including repeated models.
- Adds a Reconnect Gmail action in Settings that safely archives expired OAuth tokens and starts fresh Google authorization.
