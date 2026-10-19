# Citrus image wheel candidate

This first native Windows candidate has been cross-compiled on Linux. **Explorer interaction, desktop behavior, tray lifecycle and Windows codecs have not been executed.** It is not a release acceptance result.

Run `Citrus.Windows.exe` on Windows 10 22H2 or Windows 11, x64. The tray menu opens Citrus, pauses gestures and quits. Closing the guide keeps the tray app running. Launch at sign-in is not enabled. The app has no accounts, network conversion, analytics or runtime engine downloads.

Drag a physical image file in Explorer and tap **Shift** for Convert or **Alt** for image tools. Pre-held keys also work after crossing the system drag threshold. Releasing the modifier leaves the wheel latched. Switching mode during the same drag keeps the wheel anchored and retains its confirmed payload; move again deliberately to arm a new target. Glide deliberately to a target, then release the mouse. Drop in the neutral center, outside a target or on a navigation arrow to cancel; Escape also cancels. Conversions report Copy only if the source permits Copy. The wheel first confirms an actual FileDrop payload; unsupported/non-file/offline payloads never start jobs. Unconfirmed surfaces close after 750 ms.

A keyboard-accessible **Choose image** button and format selector offer the same image conversions. The current app presents compact session status; the expanded jobs/settings/actions interface follows in later tasks. Control requests Actions mode, but this image-only candidate has no registered actions and therefore displays no action targets.

## Available image operations

| Operation | Behavior |
| --- | --- |
| PNG, JPEG, WebP, TIFF, BMP, GIF, AVIF | Native Magick.NET 15.0.0 Q8 x64 encoding; all seven generated-fixture round trips pass on Linux. Windows fixtures still need to run. |
| Resize | Optional positive width/height, at least one required; aspect ratio preserved by default. |
| Rotate | 90, 180 or 270 degrees clockwise from the wheel dialog; engine supports integer degrees. |
| Compress | Quality 1–100, default 85; encoder interpretation varies by format. |
| Strip metadata | Removes EXIF, comments and profiles after orienting pixels. |

The adapter accepts single-frame images only. Animated GIF/WebP and multipage TIFF are detected and rejected with an explanation before any output is created; originals remain unchanged. It never silently truncates them to the first frame/page. EXIF orientation is applied to pixels. PNG and other lossless encoders interpret quality as encoding/compression settings, not a promised target byte size.

Input extensions: `.png`, `.jpg`, `.jpeg`, `.webp`, `.tif`, `.tiff`, `.bmp`, `.gif`, `.avif`, plus `.svg` as conversion input only. SVG uses the bundled internal MSVG renderer; a real shapes-to-PNG fixture verifies dimensions and pixels on Linux. Self-contained shapes/text and local paint-server references are supported; external resources, CSS stylesheets, scripts, DTDs and animation are rejected. SVG has no output format or image-tool targets. Decoders are explicitly selected; PDF, HEIC and external/delegate formats remain unavailable. HEIC requires a verified bundled decoder before enabling.

Original files remain unchanged. Outputs go beside each original with collision numbering; tools add an operation suffix. A unique temporary sibling is fully encoded and decoded for dimension validation before a non-replacing commit. Errors and cancellation clean staging. Encoding runs on workers through a queue with at most two active requests. Cancellation is cooperative; native work can finish its current step before observing cancellation.

## Reproduce the Linux verification and Windows candidate

Requires the pinned SDK in `global.json`:

```sh
dotnet restore Citrus.sln --locked-mode
dotnet test -c Release --no-restore
dotnet build src/Citrus.Windows/Citrus.Windows.csproj -c Release -p:EnableWindowsTargeting=true
dotnet publish src/Citrus.Windows/Citrus.Windows.csproj -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -p:RestoreLockedMode=true -o dist/image-wheel-candidate
```

The self-contained output is a portable unsigned candidate. Installer generation belongs to the packaging task. Package locks and `packaging/image-backend.lock.json` pin exact image backend artifacts; `packaging/notices/Magick.NET-15.0.0-Notice.txt` carries the upstream bundled notices. Citrus artwork is original, retained as XAML and SVG; `packaging/generate-artwork.py` regenerates its ICO.
