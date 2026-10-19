# Citrus interaction and interface redesign

The user requests Tangerine's intuitive wheel and editing experience on Windows, supplies six visual references, and reports unstable file selection when pressing Shift or Control. This specification makes the interaction and presentation foundation concrete. The approved first-release data safety and local processing contracts remain binding. Full tool parity is tracked separately in `docs/tangerine-parity.md`; this interface project does not erase the remaining editing and document work.

## Interaction contract

- Show a full wheel only after a genuine physical OLE FileDrop is confirmed. Mouse motion, marquee selection, dragging a window, and ordinary Shift/Control selection alone must never display a full wheel or create a submission.
- Low-level input observation remains non-suppressing. Do not synthesize Explorer selections, inspect an Explorer selection to invent a payload, or globally consume modifiers. Windows retains ordinary selection behavior outside a real file drag.
- Freeze a defensive copy of the first genuine payload for the whole drag. Mode changes and DragOver cannot replace, expand, reorder, or shrink it. Validate the actual drop against that snapshot before submission. Copy-only source permission is required; Citrus conversions preserve original files.
- A drag epoch invalidates queued modifier activation after button release, Escape, disabling gestures, or disposal. A valid confirmed OLE Drop retains ownership when the low-level button-up arrives first.
- Preserve the wheel's screen anchor, payload and generation across mode changes. Re-arm selection after changing modes. A folder can stay in Actions while Convert/Tools show no eligible operations without terminating the drag.
- Retain the 750 ms unconfirmed timeout, 8 DIP deliberate selection movement, 400 ms page hover and at most nine targets per page. These values are interaction policy, not animation durations.
- Obtain FileDrop data on entry and final drop, not every pointer event. Cache eligibility and operation lists once per confirmed payload; keep filesystem work, COM data extraction, decoding and catalog scans out of the hot DragOver path.

## Wheel presentation

Match the supplied references: translucent pale circular shell, separated rounded petals, warm orange selection, simple black/white line icons, a real central file thumbnail or recognizable file glyph, a batch badge and a short operation caption. No opaque square flash, stale content flash, dark heavy outlines or text-only wedges.

- Window: 368 × 414 DIP; ring center (184,184), outer radius 166 DIP, neutral center radius 78 DIP. Use the actual visible target count N (1–9), equally spaced at 360/N degrees with a 4-degree neutral gutter; this matches the six-action, seven-format and nine-tool reference wheels. Petal corners approximately 8 DIP. Drawn and hit geometry must agree, including gutters and rounded corners; absent slots remain neutral.
- Target geometry stays fixed during hover and opening. Keep retained visual objects; a hover change updates only the old and new target state. Rebuild only on changed page/content or theme, not per pointer movement.
- Prepare clean content before presentation. Animate opacity and presentation transforms only: opening 160 ms ease-out from scale .94 to 1; hover color 90 ms; dismissal 100 ms. Hit testing uses final layout throughout. Reduced motion disables scale/fades; high contrast uses system brushes and clear boundaries.
- Keep a nonactivating tool window with a real OLE hit surface; never use click-through or fake hook-only drops. All post-drop work begins after the OLE callback unwinds.
- Thumbnails load asynchronously from copied paths with bounded decode size (maximum 192 physical pixels), cancellation and generation checks. A stale preview must not replace a later drag. Use a generic glyph immediately; preview failure does not block the drop.
- Provide quiet original open/selection/success/error cues, preloaded and optional. Selection cues are throttled (at least 45 ms apart). Never block input on audio or decode, and do not modify global system volume. Sound is configurable separately from reduced motion and is enabled by default, as requested, with an easy-to-find mute preference.

## Desktop workspace

The home window must be a useful file workspace rather than a numeric form. Use the screenshots' clean light surfaces, orange emphasis, rounded cards, restrained separators and legible hierarchy. Keep complete light, dark and system theme support.

- Default size 1180 × 820 DIP, minimum 860 × 640. Header 64, category rail 48, input list approximately 240, inspector approximately 304, gaps 16, outer padding 24 DIP. Adapt at small sizes without clipped controls; scroll operation content.
- Categories use real icons and clear names. Show only functional available routes, with an explanatory empty state for a selected unsupported input. Do not invent enabled Office/EPUB tools before their backend exists.
- Provide an obvious drop area, Add files and Add folder, file count and clear list. Separate files included in the submission from the single file currently previewed. Shift/Control clicks must never silently change batch inclusion. Inclusion has explicit checkboxes/remove controls.
- Group Convert, Tools and File actions visibly. Show a readable operation label, helper text, output format, file count, output location and safe-copy behavior before submission. Preserve keyboard navigation, focus indicators, accessible names and predictable Enter/Escape behavior.
- Show image previews, a file summary fallback, selection details and task progress. Inspector controls are labeled, validated and disable while submitting. Preserve the existing SubmissionDraft → SubmissionChoice → queue/output writer path.
- Wheel conversions that need no options run quietly: do not unconditionally open/activate Home. Show a compact nonactivating status toast with progress/result, reveal and open-Citrus actions. Tools may open their focused editor; errors remain discoverable with recovery actions. Do not steal focus on completion.
- First launch teaches “Start dragging, then tap a key”, with remapped key labels and a safe in-app wheel demonstration. Demo mode cannot mutate files.

## Shared visual system

Resources are split into Tokens.xaml, Controls.xaml, Icons.xaml and Motion.xaml. Use Segoe UI Variable with Segoe UI fallback, 28 DIP window titles, 22 editor titles, 16 section headings, 14 body and 12 metadata. Spacing scale: 4, 8, 12, 16, 24, 32, 40. Inputs/buttons minimum 40 DIP high; radii 10/12/16/24 DIP. Focus rings and text must remain visible in high contrast.

Light palette: canvas #F7F5F2, panel #FFFFFF, inset #F0EFEC, border #E4E1DC, main text #202020, secondary #62605D, decorative orange #FF7900, accessible action fill #C14D00, action ink #B84900. Selected petal gradient #FF870A → #F36A00. Do not copy proprietary assets: ship original vector glyphs, audio and Citrus branding.

## Evidence and delivery

Meaningful host tests pin immutable payloads, stale activation, mode switching, deliberate arming, cancellation and output safety. Windows integration tests must instantiate real WPF resources/windows and record rendered wheel/workspace PNGs plus repeated-hover timing/allocation evidence. CI must upload these artifacts even on failure. Native input, Explorer, mixed DPI, sound, tray and accessibility still require Windows evidence; a Linux cross-build is not proof of those properties. Do not claim “100% parity” or “no visual bugs” without that evidence.
