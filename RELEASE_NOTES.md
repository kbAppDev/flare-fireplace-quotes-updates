# 1.70.2

- Completes Traditional Bonfire support for TRA-BON-42 and TRA-BON-46. Auto-fill recognizes the full part name and ordering SKU, infers the correct fireplace size, and preserves the Bonfire identity through pricing, PDFs, email drafts, and specification review.
- Manual model entry recognizes TRA-42/TRA-46 as Traditional Summit and TRA-BON-42/TRA-BON-46 as Traditional Bonfire. The fireplace header shows the burner style, model-field hover help explains the Bonfire codes, and changing a full model code updates its size even within the same style.
- Keeps the same available features as the corresponding TR-42 and TR-46. Resolves the exact TRABON42/TRABON46 base-price rows rather than falling back to the standard Traditional fireplace.
- Offers general premium media for Bonfire, including Gold, Aqua, and Chestnut glass, premium stones and stone balls, Driftwood, and Birchwood, with 45-inch media quantities and price-book package sizes. Keeps the matching complete Traditional oak set available. Standard Traditional media rules remain unchanged.
- Adds the manufacturer’s Bonfire product sheet while retaining the corresponding Traditional framing, CAD, SketchUp, Revit, and three-part specification links.
- Delivered through the existing signed update feed and desktop app update workflow. Adds no paid publisher signing.

# 1.70.1 (expanded 1.70 update)

- Adds an integrated Text message workflow with personalized editable previews, reusable template add/edit/delete, Undo delete, and encrypted template storage. Supports customer, project, model, consultation, and sender-name tokens; copies the message and recipient number and opens Phone Link for review and sending.
- Automatically saves unfinished quotes with Windows encryption, restores them into Review on restart, and flushes on close. Preserves unfinished fireplace and URL edits, link deletions, quantities, media selections, and available photo references. Clear and successful Gmail draft creation remove the unfinished autosave.
- Removes live totals and the background pricing work that maintained them. Pricing occurs when a preview or draft is requested.
- Adds deep-copy fireplace duplication directly into the editing flow and individual photo removal with filenames and attachment sizes.
- Adds inline URL name/address editing, duplicate-label validation within each fireplace, and guards against losing unsaved link edits.
- Replaces permanent instruction paragraphs with contextual rounded help bubbles, including keyboard-focus help. Adds native Windows 11 rounded frames and a restrained Acrylic background with opaque cards, fields, and fallback.
- Reduces product card image assets from 27.5 MB to 3.4 MB while preserving product appearance and transparency.
- Retains the requested indoor email copy and URL deletion behavior from 1.70.0. Adds no paid publisher signing.

# 1.70.0 (1.70)

- Adds a Delete button to every spec URL row, including manually added URLs; removes only the selected row from its own fireplace.
- Preserves manual additions and deletions when moving between preview and link review. Deleting all URLs no longer triggers automatic restoration during Gmail draft creation.
- Keeps fireplace cards available when their link lists are empty, so replacement URLs can be added to the correct fireplace.
- Blocks URL additions and deletions while a Gmail draft is being prepared, keeping the visible link list consistent with the email snapshot.
- Uses the exact requested opening and project consultation link for Indoor, Indoor See Through, Traditional, and Large quotes. Outdoor, hybrid, and mixed quotes retain their existing copy and consultation settings.
- Adds URL deletion, navigation, empty-card, draft, email-copy, and rendered link-review coverage.
- Based on the verified GitHub v1.6.9 tag, commit 781c59739840cb6f0a6b09cc76f7cf169de11929. Packaged numerically as 1.70.0 to remain compatible with the existing updater version format.
- Adds no paid publisher signing, Phone Link feature, or other unapproved optional enhancement.
# 1.6.9

- Made pricing fail closed when a selected fireplace, required outdoor kit, optional feature, or chargeable media item cannot be matched exactly; unsuccessful pricing can no longer reach PDF or Gmail output.
- Added mandatory RS256-signed v2 update manifests with an immutable in-app public key, exact release URL/size/hash verification, and an isolated sign-verify-publish workflow. The legacy unsigned feed remains only to upgrade installed v1.6.8 clients.
- Hardened pricing and resource workbooks with file, archive, worksheet, dimension, relationship, expansion-ratio, macro, external-link, embedded-object, and cancellation limits before parsing.
- Made PDF creation cancellable, off the UI thread, atomically published, and directly tied to the quote's captured branding rather than mutable global settings.
- Added unique quote numbers and kept pricing, quantities, feature quantities, hyperlinks, customer-specific filenames, PDF content, and Gmail attachments consistent across add, edit, save, recall, and duplicate configurations.
- Removed the external ZIP-code lookup, restricted clickable quote links to approved HTTPS Flare resource domains and the exact HubSpot consultation host, tightened log redaction and retention, and made Gmail reconnection transactional.
- Added a user-run system check for workbook/configuration health, keyboard fireplace reordering, accessibility labels, and clearer link-review language.
- Sanitized the shipped outdoor-resource workbook and added security, pricing, updater, PDF, privacy, workbook, health, and UI regression coverage.
- Pinned the SDK, installer, dependencies, package locks, analyzers, test runner, SBOM generator, and third-party notices used by the release lane.

# 1.6.8

- Added a quantity selector for each fireplace configuration and preserved it through add, edit, recall, preview, PDF generation, and Gmail draft creation.
- Extended base-fireplace and optional-feature quantities and MSRP totals by the selected fireplace quantity, including media quantities such as glass sets and Driftwood piece counts.
- Replaced standard Free Flow wood/metal framing links with the exact model- and height-specific Passive Heat Flex framing guide whenever Passive Heat Flex is selected.
- Kept specification resources paired by fireplace position so duplicate models with different feature selections receive the correct guide on each PDF page.
- Added quantity columns and clearly labeled total MSRP columns to generated quote PDFs.
- Appended the sanitized customer name to generated PDF filenames.
- Added regression coverage for the complete Passive Heat Flex guide matrix, quantity pricing, UI quantity persistence, and PDF filename generation.

# 1.6.7

- Made the existing per-fireplace location field explicit in the quote builder and easier to scan on saved fireplace cards.
- Preserved each fireplace location through preview, URL verification, and Gmail resource-link review so repeated models remain distinguishable.
- Added a dedicated `Fireplace Location` row to every applicable PDF page while retaining the shared project name and address.
- Included fireplace locations in blank-project fallback headings without changing quotes that leave the location blank.
- Removed archived build output and prior packaged releases from the 1.6.7 deliverables; only the current updater installer, portable application, and verified update manifest are published.

# 1.6.6

- Reverted the unshipped Front Facing Outdoor Kit option; Front Facing fireplaces no longer expose an Outdoor Kit optional feature.
- Fixed selected-feature/media chip remove buttons so clicking the `×` is not swallowed by the drag/reorder mouse handler.
- Added Zircon Black Diamonds and Rain Drop Diamonds to the Classic Media lists for Outdoor and Outdoor See Through fireplaces.
- Reworked the `Est. total` card to price the current quote before PDF preview and refresh automatically when pricing inputs change.
- Added regression coverage for the Front Facing Outdoor Kit revert, feature removal, Outdoor diamond media availability, and pre-preview estimated totals.

# 1.6.5

- Fixed auto-fill so an empty labeled field cannot consume the following line.
- Preserved blank Project Name values while correctly detecting an unlabeled customer name.
- Added clean blank-project PDF headings in the format `Application Style Width" x Height"`.
- Multi-fireplace quotes now list every fireplace in quote order in the blank-project fallback heading.
- Outdoor headings use opening dimensions and never label the height as glass.
- Confirmed email greetings continue to use the first name parsed into Customer.

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
