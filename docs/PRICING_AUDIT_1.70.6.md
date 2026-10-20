# Pricing audit for 1.70.6

## Source and method

- Audit date: October 20, 2026.
- Baseline: published v1.70.5, commit `554b34414622775a3dac73a361cb8ef323b9ae34`.
- Foundation: Kyle's local `2026 - Pricing.xlsx`, supplied October 20, 2026.
- Foundation SHA-256: `bb66e957b6c5d9c01e102c2d7bed8b50ed82490399b92226ae86f65480d7e02d`.
- Comparison uses worksheet, trimmed part name, and trimmed SKU together. The 22 explicit SKU corrections below are mapped separately before comparing their prices. Duplicate part names alone are not product identity.

The foundation contains 1,470 priced rows: 583 Indoor, 209 Outdoor, 134 Large, and 544 Parts. All price cells are numeric and these four sheets contain no formulas. The unchanged Parts row 74, Pilot Housing - LP, has neither a SKU nor a price; no price has been invented for it.

## Accepted MSRP changes

36 existing product rows change MSRP: 10 outdoor screens and 26 venting/adapter parts. The SKU column below shows the imported SKU; changed identities also appear in the next section. Amounts are unit MSRP.

| Part name | SKU | v1.70.5 | v1.70.6 |
|---|---|---:|---:|
| VF-SCREEN-RC-50 | `VSCCNR50` | $307 | $404 |
| VF-SCREEN-RC-60 | `VSCCNR60` | $347 | $444 |
| VF-SCREEN-RC-70 | `VSCCNR70` | $370 | $467 |
| VF-SCREEN-RC-80 | `VSCCNR80` | $377 | $474 |
| VF-SCREEN-RC-100 | `VSCCNR100` | $529 | $626 |
| VF-SCREEN-DC-50 | `VSCDC50` | $307 | $501 |
| VF-SCREEN-DC-60 | `VSCDC60` | $347 | $444 |
| VF-SCREEN-DC-70 | `VSCDC70` | $370 | $467 |
| VF-SCREEN-DC-80 | `VSCDC80` | $377 | $474 |
| VF-SCREEN-DC-100 | `VSCDC100` | $529 | $626 |
| 35CVS-06 | `35CVS-06` | $65 | $70 |
| 35CVS-12 | `35CVS-12` | $100 | $107 |
| 35CVS-12TA | `35CVS-12TA` | $148 | $158 |
| 35CVS-24 | `35CVS-24` | $110 | $118 |
| 35CVS-36 | `35CVS-36` | $137 | $147 |
| 35CVS-18TA | `35CVS-18TA` | $290 | $311 |
| 35CVS-E45 | `35CVS-E45` | $139 | $149 |
| 35CVS-E90 | `35CVS-E90` | $148 | $159 |
| 35CVS-RS | `35CVS-RS` | $44 | $47 |
| 35CVS-CD | `35CVS-CD` | $258 | $276 |
| 35CVS-HC | `35CVS-HC` | $400 | $428 |
| 35CVS-HCR | `35CVS-HCR` | $323 | $345 |
| 35CVS-VC | `35CVS-VC` | $426 | $456 |
| 35CVS-SC | `35CVS-SC` | $16 | $17 |
| 35CVS-TRI | `35CVS-TRI` | $13 | $14 |
| 35CVS-TRO | `35CVS-TRO` | $14 | $15 |
| 35CVS-WP10 | `35CVS-WP10` | $35 | $38 |
| 35CVS-WT | `35CVS-WT` | $144 | $154 |
| 35CVS-WS | `35CVS-WS` | $50 | $54 |
| 35CVS-FS | `35CVS-FS` | $52 | $55 |
| 35CVS-F | `35CVS-F` | $45 | $49 |
| 35CVS-DSA | `35CVS-DSA` | $150 | $160 |
| 35CVS-WTP | `35CVS-WTP` | $170 | $182 |
| 35CVS-IS | `35CVS-IS` | $218 | $233 |
| 58DVA-ADP | `58VENADP` | $71 | $76 |
| 35CVS-ADP | `35CVS-ADP` | $71 | $76 |

There are no accepted base-fireplace MSRP changes and no Large Price Book changes. Thermocouple housing pricing is unchanged: `100PTP` remains $16 and `100PTH` remains $32. Their similar part names must not be collapsed into a single record.

## Accepted SKU corrections

22 source corrections are imported. Indoor FF25EH remains $6,734 and black uniform 4-inch stone balls remain $193 per source set. Outdoor left/right corner screens use shared `VSCCNR` SKUs, while their model-specific part names remain intact.

| Part name | Previous SKU | Imported SKU |
|---|---|---|
| Flare-FF-25-EH | `DVFF425E` | `DVFF25E` |
| PSB-Bla-U4 | `PSBBU` | `PSBBU4` |
| VF-SCREEN-LC-50 | `VSCLC50` | `VSCCNR50` |
| VF-SCREEN-LC-60 | `VSCLC60` | `VSCCNR60` |
| VF-SCREEN-LC-70 | `VSCLC70` | `VSCCNR70` |
| VF-SCREEN-LC-80 | `VSCLC80` | `VSCCNR80` |
| VF-SCREEN-LC-100 | `VSCLC100` | `VSCCNR100` |
| VF-SCREEN-RC-50 | `VSCRC50` | `VSCCNR50` |
| VF-SCREEN-RC-60 | `VSCRC60` | `VSCCNR60` |
| VF-SCREEN-RC-70 | `VSCRC70` | `VSCCNR70` |
| VF-SCREEN-RC-80 | `VSCRC80` | `VSCCNR80` |
| VF-SCREEN-RC-100 | `VSCRC100` | `VSCCNR100` |
| VF-SCREEN-LC-50-H | `VSCLC50H` | `VSCCNR50H` |
| VF-SCREEN-LC-60-H | `VSCLC60H` | `VSCCNR60H` |
| VF-SCREEN-LC-70-H | `VSCLC70H` | `VSCCNR70H` |
| VF-SCREEN-LC-80-H | `VSCLC80H` | `VSCCNR80H` |
| VF-SCREEN-LC-100-H | `VSCLC100H` | `VSCCNR100H` |
| VF-SCREEN-RC-50-H | `VSCRC50H` | `VSCCNR50H` |
| VF-SCREEN-RC-60-H | `VSCRC60H` | `VSCCNR60H` |
| VF-SCREEN-RC-70-H | `VSCRC70H` | `VSCCNR70H` |
| VF-SCREEN-RC-80-H | `VSCRC80H` | `VSCCNR80H` |
| VF-SCREEN-RC-100-H | `VSCRC100H` | `VSCCNR100H` |

## Added and removed parts

- Add `BN-GRT` / `BNGRT`, Bonfire Grate, at $75.
- Add all 25 `811DVP` venting rows from source Parts rows 522-546: five fixed pipe lengths; adjustable length; two adapters; two elbows; support plate; elbow and wall straps; horizontal and vertical firestops; wall thimble; attic insulation shield; horizontal and vertical caps; two vinyl siding standoffs; three flashing options; and storm collar.
- Remove `BON-GT-42` / `BONGT42` and `BON-GT-46` / `BONGT46`, previously $200 each, as absent from the new foundation.

These 26 additions and two removals update the bundled parts catalog. The application has no dedicated pipe or grate quote feature, so this update does not introduce new selectable features or infer model compatibility for those parts. The workbook loader does not filter these SKU prefixes.

## Preserved corrections and source conflicts

| Source cell/product | Foundation value | Bundled value | Reason |
|---|---|---|---|
| Outdoor D115, `VFRBLC60H` | $799 | $888 | Preserve Kyle's explicit correction shipped in v1.70.5; Indoor `RBLC60H` also remains $888. |
| Parts D118, `4DFA25` | $273 | $335 | Preserve Kyle's explicit confirmation shipped in v1.70.4; Indoor row 470 also lists $335. |
| Parts C378, `FLARE-LC-SS-EH` | `1SCLCSH` | `1SCLCSE` | Retain the distinct EH SKU at $242. Importing `1SCLCSH` would collide with the H side screen at Parts row 336, priced $105. |

The first two exceptions preserve confirmed commercial prices. The third preserves an existing distinct identity because the new source supplies conflicting prices under the same SKU; it is not a new manufacturer SKU confirmation.

## Duplicate and schema audit

- Normalize the Outdoor header from `2026 MSRP` to `MSRP`, which the existing numeric price loader recognizes.
- Preserve source rows rather than deduplicating products by part name. In particular, shared LC/RC corner-screen SKUs retain both model-specific part records.
- The 12 additional Outdoor rows repeat five regular front screens, one regular side screen, five high front screens, and one high side screen. Repeated SKUs agree on price. One repeated `VSSCH` description says 16-inch opening while the other says 24-inch; this source text inconsistency does not change its shared $130 price.
- Existing repeated passive heat flex, gas-system, and gasket rows also agree on price.
- The source's only conflicting duplicate-SKU prices are `4DFA25` and `1SCLCSH`, addressed above. No additional contradictory duplicate-SKU prices were found.

## Quantity data and pricing scope

The foundation contains only four price sheets. Preserve these four existing calculation sheets from v1.70.5:

- `Indoor - Media Calculation -RD`
- `Indoor - Media Calculation`
- `Outdoor Media - Calculation`
- `Large -Media Calculation`

The workbook does not provide replacement size-specific stone-ball schedules. Those existing hardcoded quote schedules and quantity rules remain unchanged; source stone-ball set prices remain $209, $193, and $232. The corrected black uniform 4-inch source SKU is `PSBBU4`. Louver fallback prices agree with the foundation at displayed currency precision.

This audit compares all four supplied price sheets and documents import exceptions. It does not independently establish the manufacturer's commercial correctness for ambiguous source data or introduce compatibility rules for parts-only items.

## Validation

- Independent cell comparison: 8,304 cells checked across the four foundation price sheets and four retained media sheets; no differences beyond the three documented corrections and normalized Outdoor header.
- All 1,470 priced rows retained, representing 1,431 distinct SKUs after correcting the EH conflict; no conflicting duplicate-SKU prices remain. The runtime intentionally filters 36 discontinued commercial fireplace rows, leaving 1,434 active priced rows.
- Warnings-as-errors build passed with zero warnings and errors.
- 41 targeted pricing tests passed: full base/Double Glass/accessory/media mapping coverage, 30 outdoor LC/RC/DC screen combinations through actual quotes and reordered lookups, corrected FF25EH, black 4-inch stone-ball quotes at quantities one/two, new parts, duplicate-price consistency, Passage totals, and prior $335/$888 corrections.
- Source formatting, the full automated suite, dependency audit, UI contract, coverage, and protected-branch checks are enforced by repository CI. Publication additionally requires the existing installer, rendered UI, signed-feed, and release-asset gates.
