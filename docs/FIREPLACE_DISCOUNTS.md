# Fireplace discounts

Introduced in v1.70.7, based on v1.70.6.

## Entry and scope

Each fireplace configuration can have its own percentage from 0 through 100. Blank or zero means no discount. Decimal percentages are supported. Invalid text or out-of-range values must block adding the fireplace and generating the PDF or draft, including direct command execution.

Discount Terms is an optional multiline field for the whole quote. The terms appear with discounted fireplace pages and in the draft email. No terms or pricing promises are invented automatically.

## Calculation

- MSRP remains the source catalog amount and is never overwritten with a discounted amount.
- Eligible MSRP is the underlying base-fireplace row times the fireplace quantity. Mandatory outdoor kits folded into the displayed base line are excluded from the discount.
- Discount amount = eligible MSRP x percentage / 100, rounded once to two decimals with midpoint rounding away from zero.
- New fireplace cost = displayed extended base MSRP minus the discount; any included outdoor kit remains fully priced.
- Optional features and chargeable media retain their full MSRP.
- Configuration totals add the new fireplace cost and each option amount rounded to cents. This matches the displayed currency lines even when source prices contain fractions of a cent. Each fireplace calculates independently; quote-level totals sum those results.
- The PDF preserves MSRP and shows only the discount percentage and base MSRP after discount in red, with cents where needed. It does not show the dollar amount saved or a separate configuration-total line. Custom discount terms form the final section, with both the heading and text in red. Quotes without a discount retain their existing presentation.

## State and validation

Editing, duplicating, and recalling fireplaces must preserve the percentage. Unfinished-quote recovery preserves both the raw percentage text being edited and the quote terms. Clearing the current fireplace resets its percentage without discarding quote-level terms; clearing the full quote resets both.

Changing a percentage or the terms invalidates any existing priced/PDF snapshot. Draft creation must use the current reviewed quote and terms. Terms are rendered as text in the PDF and HTML-encoded in email.

The price service independently rejects percentages outside 0 through 100. Missing catalog prices remain errors, including when the requested discount is 100%.
