Flare Fireplace Quotes v1.6.4 is a focused correctness, resilience, and safe-cleanup release based on the approved v1.6.3 application.

- Reads numeric workbook prices by numeric value instead of locale-formatted display text.
- Preserves the en-US text-price fallback while preventing locale formatting from changing numeric prices.
- Returns a safe empty workbook when a pricing file is corrupt, locked, or partially synchronized instead of crashing the quote workflow.
- Redacts absolute Windows paths and email addresses from update-error messages shown to users.
- Removes seven verified unreferenced members, including a parser method containing a copied PowerShell newline artifact.
- Consolidates two identical resource-key normalizers into the existing shared compact normalizer.
- Adds regression coverage for numeric price loading under a non-US process culture, en-US text-price fallback, and corrupt-workbook handling.
- Keeps the approved interface, pricing workbook, 302-model inventory, model mappings, PDF generation, Gmail drafting, updater behavior, quote history, settings, and real-flame-and-ash removal animation unchanged.

Manifest signing and Authenticode enforcement remain a separate release-infrastructure project; this update does not enable strict signing before signed artifacts and a protected public trust anchor exist.
- Corrected exact base-SKU, MSRP, and specification matching for Commercial Front Facing, Commercial See Through, Outdoor Left Corner, and Outdoor Double Corner models.
- Corrected Commercial High and Extra High pricing so shared Commercial part names cannot fall back to the Regular-height SKU.
