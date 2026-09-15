# Bubble Lab 0.4.1 QA — 2026-09-15

Tested application commit: 7ba25f05e243d819ec2904a7809b6013aeb32f26.
Isolated HTTPS Render preview; synthetic data only. The production ZIP does
not include the QA harness or any user-supplied screenshots.

## Automated result: 49 passing checks

- 320×568, 375×667, 402×874 and 768×1024 preview viewports: uniform scale,
  exact 1206×2622 canvas, no horizontal form overflow, all seven type buttons
  fit in two rows with at least 44-point button heights.
- Add, edit, edited indicator, Tapbacks, move earlier/later, delete, inline
  reply, message-removed flag, sender grouping (4 points) and change (10 points).
- Multiline text with blank lines, long unbroken tokens, emoji-only handling,
  and punctuation remaining normal text.
- Photo upload, video placeholder, link, file, voice and sticker types retained.
- Emoji/photo avatar settings and light/dark themes retained.
- Synthetic long press opens message editing.
- PNG signature and 1206×2622 IHDR dimensions verified.
- Decoded exported pixels equal the visible preview at scroll offset 170:
  **zero differing channels**, with tools open during export. No editor chrome.
- Versioned static cache installed, exactly eight static shell assets,
  no QA/conversation data cached, previous 0.4.0 cache removed.

## Visual review

Reviewed blank and synthetic populated previews, light and dark, and Add
Message/settings sheets. Compared header/composer and bubble geometry directly
with the supplied native Messages references and the prior Bubble Lab images.
Confirmed avatar/pill overlap, round controls, continuous asymmetric tails,
compact attached Tapbacks, and non-stretched bubbles. No native blank-thread
reference was supplied: blank header/composer comparison uses the corresponding
regions of the supplied native populated thread.

## Scope and limitations

- Browser: cloud Chromium, including real iframe viewport sizes. Physical
  iPhone Safari installation, keyboard, and native share-sheet not verified.
- SF and emoji rendering depend on the host OS; no Apple font redistribution.
- Translucent header glass is approximate, not a pixel-perfect native blur.
- Actual video codec thumbnail decoding requires target-device testing; the
  automated video case validates the preview card without an uploaded video.
- Initial export test captured before scroll paint. The harness now waits for
  actual animation frames; the renderer also removes a redundant deferred
  frame. Byte-level PNG serialization was replaced by decoded-pixel comparison.
- Browser-extension metadata errors were observed; those are not application
  runtime errors.
