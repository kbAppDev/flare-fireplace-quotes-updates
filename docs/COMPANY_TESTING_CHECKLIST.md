# Company Testing Checklist — v1.5.0

Use this checklist for each tester.

## 1. Startup and settings

- App opens from `Build_And_Run_Safe.ps1`.
- Build shows `0 Warning(s), 0 Error(s)`.
- Settings opens and each General, Integrations, Data & updates, and Lead times category is reachable.
- Settings saves and reloads values.
- Gmail credentials are imported into `%LOCALAPPDATA%\Flare Fireplace Quotes\Credentials\gmail_credentials.json`.
- Header shows Gmail status accurately after OAuth/draft creation.

## 2. Modern interface

- The Windows title bar, minimize, maximize/restore, and close controls behave normally.
- The Build quote, Preview, Verify links, and Gmail draft stepper remains legible at the minimum supported window size.
- Recent quotes opens a compact menu and recalls the chosen quote.
- Dark and light themes update the main window, menus, Settings, System health, and Update available surfaces consistently.
- Tab and Shift+Tab expose a visible focus indicator on interactive controls.
- Text, status colors, and selection indicators remain readable at 100%, 125%, and 150% Windows scaling.
- No fields, dropdowns, status cards, or action buttons are clipped at 1180 × 760.

## 3. Basic quote flow

- Paste the request into Customer request.
- Auto-fill places name, email, phone, address/postal, model, size, glass height, and feature hints correctly in Review quote details.
- Manual dropdowns work without closing after each multi-select.
- Clear next to Add Fireplace clears current fireplace selections only.
- Add Fireplace stores the current fireplace and clears per-fireplace fields while preserving customer/project fields.

## 4. Feature availability

- Indoor / Indoor See Through / Indoor Outdoor See Through show the full indoor feature set.
- Outdoor and Outdoor See Through use outdoor-appropriate options.
- Traditional shows Traditional-specific features, including Power Vent.
- Room Definer hides Reflective Black Back as an optional feature and includes it in the included-with-purchase paragraph.
- Large detection is based on size 120" and up.

## 5. Premium media

- Premium Media dropdown shows one clean Driftwood option.
- Driftwood PDF description lists calculated small/large quantities.
- Room Definer Driftwood quantities use Room Definer-specific media rules.
- Outdoor Premium Media only offers glass options.
- Traditional Bonfire units show the correct Large Oak option by size.
- DVTRA42 shows only PMDBIRCH and TR42BCH.
- DVTRA46 shows only PMDBIRCH and TR46BCH.

## 6. PDF output

- PDF filename follows: `Name - Model Size Height - Quote.pdf`.
- One fireplace quote renders per page.
- PDF feature names are clean: Summit Burner, Double Glass, Reflective Black Back, Reflective Black Sides, RGB LEDs, Summer Kit, Active Heat Flex, Passive Heat Flex, Heat Release Louver, Air Intake Louver, Power Vent.
- Description column has the informative wording.
- Pricing appears for selected options.
- VFST70 with Reflective Black Sides shows SKU `VFRBSST` and MSRP `$208`.
- Tables are compact and centered.
- Live Preview loads the actual generated PDF.
- Open Generated PDF works even if Live Preview fails.

## 7. Spec URL verification

- Model/glass-height-specific URLs win over generic rows.
- Example: FF-80-H resources are used before FF-80.
- Resource URLs do not fall back to Download Center when a specific row exists.
- VFST70 with 16-inch glass shows the Outdoor Vent Free See Through (`VST`) card and only `/Data/Ventless/ST/` resources, never `ST-OD` resources.

## 8. Gmail draft

- OAuth prompt appears when needed.
- Gmail draft is created, not sent.
- PDF is attached.
- Subject ends with `| Model`.
- First name is bold and italic.
- Model Spec Files label is bold.
- Need More Help? is bold.
- Spacing matches the current email standard.

## 9. Report issues with

- Screenshot.
- Quote request text.
- Model, size, glass height, selected features/media, lead time.
- Build log if the issue is build/startup related.


## 10. Security and update hardening

- Existing `recent_quotes.json` migrates to `recent_quotes.json.dpapi` and the plaintext file is removed.
- Recall Last Quote continues to work after restarting the app.
- The encrypted history file does not contain visible customer names, email addresses, or project text.
- A successful Gmail draft removes its app-owned temporary PDF.
- A failed Gmail draft leaves the PDF available for retry.
- Update checks reject a non-Flare host, mismatched release version, invalid SHA-256, or incorrect installer size.
- The normal GitHub v1.5.0 release manifest downloads and launches only after verification.
