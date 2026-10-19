# Image wheel Windows acceptance — pending

All items below are **unexecuted**. Linux unit tests and cross-builds do not prove these interactions. Run as a normal user on Windows 10 22H2 and Windows 11 x64; retain OS/DPI/monitor details and actual generated output checks.

- [ ] Start/quit/restart; only one tray instance per user session; closing the guide leaves it resident; pause disables hooks' requests and restores ordinary Explorer modifier behaviors.
- [ ] Explorer and desktop physical image drags: Shift held before drag; Shift tapped after movement; key-up leaves the wheel open; left/right Shift, Alt/SYSKEY and Control transitions; repeated keys ignored; last mode wins.
- [ ] The wheel never activates or takes keyboard focus; source retains drag ownership. No click-through in near-transparent capture margins. Ordinary Explorer moves/copies and text selection still work when paused.
- [ ] Real PNG source → JPEG, PNG, WebP, TIFF, BMP, GIF and AVIF outputs; decode actual copies, compare dimensions/pixels, preserve source bytes/location and test repeat-name collisions.
- [ ] Capture appears during an active OLE drag; real FileDrop enters the STA target; Copy returned when allowed even with Shift down; Move-only sources rejected; a confirmed target remains alive when global release is observed before a deliberately delayed OLE Drop; Drop/DragLeave/explicit cancellation owns cleanup.
- [ ] Center, ring gaps, absent sectors, outer area, navigation arrows, Escape, leaving and source cancellation create no output. Stale confirmation/release callbacks cannot close a newer session.
- [ ] Non-file text/window drags, virtual files, folders, mixed folder/file selections, nonexistent files and unsupported files enqueue nothing. Offline/recall cloud placeholders are rejected without hydration.
- [ ] Unconfirmed surface disappears after 750 ms. Confirmed files remain usable beyond 750 ms. An in-session mode change retains the original native anchor, plain payload and confirmation deadline/generation, and begins unselected. A new independent capture gets its own timeout.
- [ ] 100%, 150%, 200% DPI; negative-coordinate monitors; mixed DPI; all screen corners and taskbar positions; WM_DPICHANGED; invocation clamping under a sector does not arm it until deliberate pointer movement.
- [ ] More than eight registered choices: at most eight per page; previous/next change after 400 ms hover; release on an arrow cancels. (Current image-only catalog has seven conversions/four tools; use a later expanded catalog.)
- [ ] Basic self-contained SVG → PNG raster; SVG tools/output absent; lowercase/mixed/uppercase HREF and STYLE external-resource SVG rejected; animated GIF/WebP and multipage TIFF rejected with clear guidance and no output.
- [ ] Resize width-only/height-only/both/aspect-off; rotate; quality boundaries; strip EXIF/comments; invalid dimensions/options stay in dialog; Cancel creates no file. Dialog opens only after OLE Drop returns.
- [ ] Keyboard file picker and format selector are accessible; guide/tray UI readable and unclipped; inspect original Citrus artwork at small tray sizes.
- [ ] Multiple files yield separate jobs and outputs; at most two conversions execute; failures stay item-specific; spaces/Unicode; corrupt rasters; read-only output folder errors; cancellation/quit clean staging where OS permits.
- [ ] Run the full core and conversion test projects on Windows; verify all native encoders load without system ImageMagick/Ghostscript/codecs or runtime downloads. Run portable self-contained candidate offline on a clean VM.
