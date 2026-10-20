# Pricing audit for 1.70.4

Baseline: published v1.70.3, commit d1d65257e9103d2e0ea029d1c10f3b1818b44768. Audited October 20, 2026.

## Confirmed corrections

- STPASS Double Glass selected DGPAFF ($3,782) instead of DGPAST ($5,294), understating the option by $1,512 per fireplace. Passage feature lookup now positively matches front-facing versus see-through, preserves explicitly shared RGB/burner rows, and refuses to borrow other models when an option is missing. STPASS reflective sides now resolve RBSPAST; both sides variants currently cost $592. See-through Passage cannot receive a reflective-back option.
- Thirty 16-inch outdoor LC/RC/DC reflective-back and screen mappings were unresolved because part numbers omit R and the description fallback cannot distinguish the corner styles. Exact part/SKU matching now supports the regular-height alias and prevents suffix/prefix collisions.
- White and black uniform 2-inch stone balls selected mixed sets ($232) rather than uniform sets ($209). Stone sets now match their complete part identifiers. Media SKU prefix matching also allowed white logs and tan embers to change identity if workbook order changed; exact matches prevent those collisions.

## Validation

- 268 catalog base-model SKU and price mappings.
- 116 standard indoor Double Glass mappings across model style, size, and glass height.
- 253 accessory rows: reflective backs/sides, outdoor screens, outdoor kits, RGB, Summit burners, and Traditional glass/interior/brick options.
- 34 media variants in original and reversed row order (68 checks). Driftwood's separate small/large quantity path is outside this variant matrix.
- Passage aliases, shared rows, reversed order, missing rows, and end-to-end totals for one and two fireplaces.
- Existing quantity regression verifies extended feature/media amounts scale with fireplace count.
- Targeted run: 23 tests passed, 0 failed. Warnings-as-errors build passed.

Read-only comparison of the live 2026 Pricing Google Sheet's Indoor, Outdoor and Large tables (583/197/134 data rows) with the bundled workbook found 27 raw differences, all explainable by whitespace or whole-dollar rounding (maximum 50 cents), with no SKU/part identity changes. No workbook prices were edited.

The local full suite could not be certified: sandbox file move, encrypted storage, temporary-directory and symbolic-link restrictions caused unrelated failures. Repository CI and the existing release gates must pass before publication. This audit verifies the covered application's mappings against its price source, not the commercial correctness of every manufacturer's price or every possible future workbook layout. Parts-only items not exposed as quote features and every possible combined media/feature configuration are not exhaustively tested.

## Source-data discrepancy requiring owner confirmation

SKU 4DFA25 (25-foot, 4-inch DuraFlex) has conflicting prices: $335 in Indoor Price Book row 470 and $273 in Parts Price Book row 118. Both rows describe the same part. This audit did not choose or overwrite a price without an authoritative decision. No cached Excel formula errors were found in the bundled workbook.
