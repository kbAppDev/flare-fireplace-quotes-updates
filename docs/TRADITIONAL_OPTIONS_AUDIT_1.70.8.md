# Traditional options audit - v1.70.8

Audited October 22, 2026 against the bundled workbook imported from Kyle's supplied 2026 pricing foundation. The workbook itself required no changes. SHA-256: `bc492f153d5b1c844e5034b4d400a9d10bc0f47215386fc822ec5956315ab230`.

Scope: Traditional Summit and Bonfire in both 42 and 46 inch sizes (`DVTRA42`, `DVTRA46`, `TRABON42`, `TRABON46`). Catalog availability, imported-text selection, exact option SKU/price, quantity scaling, saved selections, PDF labels, and shared Free Flow sizing were checked.

## Verified Traditional option matrix

Prices below are per fireplace; louver prices are shown rounded to cents while workbook precision is preserved internally. Every listed option is available for both burner styles.

| Option | 42-inch SKU / MSRP | 46-inch SKU / MSRP |
| --- | --- | --- |
| Base, Summit | DVTRA42 / $11,313 | DVTRA46 / $13,574 |
| Base, Bonfire | TRABON42 / $11,313 | TRABON46 / $13,574 |
| Double Glass | CLGTRA42 / $2,018 | CLGTRA46 / $2,537 |
| Reflective Black Interior | RBTRA42 / $972 | RBTRA46 / $1,168 |
| Offset Red Brick | RDTRA42 / $425 | RDTRA46 / $483 |
| Offset Natural Brick | NTTRA42 / $425 | NTTRA46 / $483 |
| Offset Black Brick | BLTRA42 / $425 | BLTRA46 / $483 |
| Offset Light Stone Brick | LSTRA42 / $425 | LSTRA46 / $483 |
| Herringbone Red Brick | HBRDTRA42 / $425 | HBRDTRA46 / $483 |
| Herringbone Natural Brick | HBNTTRA42 / $425 | HBNTTRA46 / $483 |
| Herringbone Black Brick | HBBLTRA42 / $425 | HBBLTRA46 / $483 |
| Herringbone Light Stone Brick | HBLSTRA42 / $425 | HBLSTRA46 / $483 |
| Free Flow Heat Release Louver | LUV140 / $466.67 | LUV140 / $466.67 |
| Air Intake Louver (Double Glass) | LUV70 / $333.33 | LUV70 / $333.33 |
| Passive Heat Flex, including its louver | HFPK100 / $1,040 | HFPK100 / $1,040 |
| Active Heat Flex, inline | HFAK2570I / $2,477 | HFAK2570I / $2,477 |
| Summer Kit | 100SK / $1,776 | 100SK / $1,776 |
| Power Vent | 100IPV / $3,061 | 100IPV / $3,061 |
| Analog Wall Timer | AWALLTIMER / $89 | AWALLTIMER / $89 |

The interiors are alternative finishes. The quote builder allows multiple explicitly selected alternatives, but importing one Herringbone finish must not silently select an Offset finish too. Matching now prefers the most specific phrase at each occurrence and recognizes sized interior part/SKU aliases.

## Corrections

- Added the missing Offset Light Stone catalog choice. Exact part/SKU lookup resolves the LS rows even though their descriptions omit the word "Brick"; missing requested variants cannot borrow another finish or size.
- Enabled the existing Active/Passive Heat Flex and release/intake louver choices for Traditional, and added exact analog-timer pricing.
- Renamed the release choice to Free Flow Heat Release Louver and retained old labels/keys for saved quotes and imports.
- Separated Free Flow recommendations from Passive Heat Flex sizing. The shared Free Flow matrix now matches the manufacturer's parts list: 30/45/50 release100/intake50; Traditional42/46 and Passage release140/intake70; 60/70 release140/intake70; 80/100 release200/intake100. The manufacturer's FAQ also lists 25 with release100/intake50. Traditional Passive Heat Flex remains HFPK100, whose LUV100 release is already included in that kit; selecting Passive Heat Flex does not automatically add a separately charged Free Flow release.
- Expanded workbook audit coverage to include LS-TRA rows and added independent expected option matrices so omissions cannot validate themselves.

## Included items and remaining limits

RGB lighting, the default mesh safety screen, power supply, wall switch, and remote remain included; they are not added as separate paid options. The Traditional manual explicitly identifies RGB lighting as supplied. Existing Summit and Bonfire media compatibility is preserved: complete small-birch sets remain size-specific; Bonfire oak sets remain complete sets; loose Bonfire media retains its established quantity calculation.

The website also mentions a digital wall timer, but no digital-timer row exists in the supplied foundation. Its price and SKU remain unresolved, so no digital option or guessed price was added. Active Heat Flex keeps the existing inline-kit selection; the foundation also lists a separately orderable end-of-line kit. Neither kit should be described as including separately purchased duct or armored cable. Replacement components and retrofit burner parts were not converted into new-fireplace options solely because they appear in the parts sheet.

This audit validates quote choices and pricing. Installation requirements continue to come from the linked manufacturer manuals. New optional items use the existing fireplace-only discount rules and remain at full MSRP.

## Source references

- Supplied 2026 foundation: Indoor rows430-455 (bases, glass, interiors),459 (Traditional Passive Heat Flex),466 (inline Active Heat Flex),488/492 (Power Vent/Summer Kit),582-583 (small birch); Parts row96 (analog timer),427-433 (louvers).
- [Traditional product and interior choices](https://flarefireplaces.com/traditional-fireplace/)
- [Free Flow official parts matrix](https://flarefireplaces.com/wp-content/uploads/Data/FreeFlow/FRFL-Parts.pdf)
- [Free Flow sizing FAQ](https://flarefireplaces.com/faq-items/q-how-do-i-determine-the-correct-louver-size-for-heat-release-and-air-intake-on-a-flare-fireplace/)
- [Passive Heat Flex](https://flarefireplaces.com/passive-heat-flex/) and [Traditional framing](https://flarefireplaces.com/wp-content/uploads/Data/PassiveHF/Framing/wood-TR.pdf)
- [Active Heat Flex Traditional compatibility](https://flarefireplaces.com/active-heat-flex/) and [installation manual](https://flarefireplaces.com/wp-content/uploads/Data/Install/ActiveHeatFlexManual.pdf). The website's older 3070 kit identifiers are not substituted for the current foundation's 2570 SKUs.
- [Traditional installation manual](https://flarefireplaces.com/wp-content/uploads/Data/Install/Install-Manual-Traditional.pdf), RGB section on PDF page55.

## Verification

Regression coverage includes all nine interiors across four Traditional models, newly available heat-management/timer options, quantities, exact-size missing-row behavior, Free Flow model sizing, separate Passive Heat Flex pricing, imported finish/SKU collisions, UI add/edit persistence, and rendered PDF naming. Existing pricing, discount, and security release gates remain required.
