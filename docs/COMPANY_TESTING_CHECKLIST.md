# Company Testing Checklist — v1.4.11

Use this checklist for each tester.

## 1. Startup and settings

- App opens from `Build_And_Run_Safe.ps1`.
- Build shows `0 Warning(s), 0 Error(s)`.
- Settings opens.
- Settings saves and reloads values.
- Gmail credentials are imported into `%LOCALAPPDATA%\Flare Fireplace Quotes\Credentials\gmail_credentials.json`.
- Header shows Gmail status accurately after OAuth/draft creation.

## 2. Basic quote flow

- Paste request into Paste Request.
- Auto-Fill Review places Name, email, phone, address/postal, model, size, glass height, and feature hints correctly.
- Manual dropdowns work without closing after each multi-select.
- Clear next to Add Fireplace clears current fireplace selections only.
- Add Fireplace stores the current fireplace and clears per-fireplace fields while preserving customer/project fields.

## 3. Feature availability

- Indoor / Indoor See Through / Indoor Outdoor See Through show the full indoor feature set.
- Outdoor and Outdoor See Through use outdoor-appropriate options.
- Traditional shows Traditional-specific features, including Power Vent.
- Room Definer hides Reflective Black Back as an optional feature and includes it in the included-with-purchase paragraph.
- Large detection is based on size 120" and up.

## 4. Premium media

- Premium Media dropdown shows one clean Driftwood option.
- Driftwood PDF description lists calculated small/large quantities.
- Room Definer Driftwood quantities use Room Definer-specific media rules.
- Outdoor Premium Media only offers glass options.
- Traditional Bonfire units show the correct Large Oak option by size.
- DVTRA42 shows only PMDBIRCH and TR42BCH.
- DVTRA46 shows only PMDBIRCH and TR46BCH.

## 5. PDF output

- PDF filename follows: `Name - Model Size Height - Quote.pdf`.
- One fireplace quote renders per page.
- PDF feature names are clean: Summit Burner, Double Glass, Reflective Black Back, Reflective Black Sides, RGB LEDs, Summer Kit, Active Heat Flex, Passive Heat Flex, Heat Release Louver, Air Intake Louver, Power Vent.
- Description column has the informative wording.
- Pricing appears for selected options.
- VFST70 with Reflective Black Sides shows SKU `VFRBSST` and MSRP `$208`.
- Tables are compact and centered.
- Live Preview loads the actual generated PDF.
- Open Generated PDF works even if Live Preview fails.

## 6. Spec URL verification

- Model/glass-height-specific URLs win over generic rows.
- Example: FF-80-H resources are used before FF-80.
- Resource URLs do not fall back to Download Center when a specific row exists.
- VFST70 with 16-inch glass shows the Outdoor Vent Free See Through (`VST`) card and only `/Data/Ventless/ST/` resources, never `ST-OD` resources.

## 7. Gmail draft

- OAuth prompt appears when needed.
- Gmail draft is created, not sent.
- PDF is attached.
- Subject ends with `| Model`.
- First name is bold and italic.
- Model Spec Files label is bold.
- Need More Help? is bold.
- Spacing matches the current email standard.

## 8. Report issues with

- Screenshot.
- Quote request text.
- Model, size, glass height, selected features/media, lead time.
- Build log if the issue is build/startup related.


## 9. Security and update hardening

- Existing `recent_quotes.json` migrates to `recent_quotes.json.dpapi` and the plaintext file is removed.
- Recall Last Quote continues to work after restarting the app.
- The encrypted history file does not contain visible customer names, email addresses, or project text.
- A successful Gmail draft removes its app-owned temporary PDF.
- A failed Gmail draft leaves the PDF available for retry.
- Update checks reject a non-Flare host, mismatched release version, invalid SHA-256, or incorrect installer size.
- The normal GitHub v1.4.11 release manifest downloads and launches only after verification.
