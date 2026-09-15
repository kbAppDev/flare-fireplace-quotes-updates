# Bubble Lab

Version 0.4.2 — native-refinement rebuild.

A local-first installable PWA for composing one-to-one iOS-style Messages conversations and exporting the visible iPhone 17 Pro screen at 1206×2622 PNG.

## Rendering and editing

The Messages renderer uses a fixed 402×874 logical canvas rendered at 3×.
Preview fitting applies one uniform scale, including in standalone mode; it
never stretches the axes independently. Editor dialogs and hit targets are
separate DOM layers and are not drawn into the export.

Back opens tools; the contact header edits the avatar/name; the composer opens
Add Message. Tap or hold a bubble to edit, react, delete, or move it. Message
dates determine ordering; moving messages swaps their date positions.

Refinements include continuous mirrored Bézier tails, same-sender grouped
corners, compact Tapbacks, overlapping avatar/name pill, drawn status/lock
icons, and full-width mobile form rows with choice sheets and switches.

Messages and uploaded media stay in memory. Reloading or resetting discards
the conversation. The service worker stores only the static application shell.
No backend, analytics, account, network link preview lookup, or conversation
storage is used. Export before closing the page.

## Run locally

Use any static web server. Example:

```bash
python -m http.server 8080
```

Then open `http://localhost:8080`.

## Deploy

Upload this folder to any HTTPS static host. No build step, backend, database, or environment variables are required.

The existing Render service uses the seven `bubblelab/chunkNN.txt` files.
After changing readable source, run `python3 bubblelab/package.py` from the
repository root. It rebuilds and verifies the existing package, including
icons, and deliberately excludes `bubblelab/qa` and uploaded references.

On iPhone: open the HTTPS URL in Safari → Share → Add to Home Screen.
For an update, export current work, close the app, open the URL in Safari,
and check the version in Back → Bubble Lab. The Check for Update action
checks the network without reloading or discarding a conversation silently.

## QA and limitations

The preview-only `bubblelab/qa/qa.html` harness exercises synthetic messages,
mobile viewport geometry, editor actions, avatars, themes, static cache scope,
and exact PNG dimensions/pixels. It is not shipped in production.

Visual geometry was compared against the supplied Messages screenshots.
This is not claimed to be pixel-perfect: SF/emoji rendering depends on the
device, translucent header glass is an approximation, and native share-sheet,
keyboard, installation and Safari behavior require physical-iPhone testing.
Link/file/voice/video cards remain visual conversation representations, not
a messaging service, fetched link metadata, or audio playback system.
