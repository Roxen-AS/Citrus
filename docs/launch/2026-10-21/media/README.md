# Citrus launch media

These assets show the current Citrus preview interface with sample files created for this launch. They are edited product demonstrations, not live Explorer recordings or performance benchmarks.

| Asset | Size and duration | Contents |
| --- | --- | --- |
| [Product overview](product-hero.webp) | 1920 × 1200 | Actual wheel and Edit Photo window |
| [Image editing](image-editor.webp) | 1920 × 1200 | Actual Add BG window and independently reopened saved image |
| [Video editing](video-editor.webp) | 1920 × 1200 | Actual Trim Video window |
| [Social preview](product-social.jpg) | 1200 × 630 | Launch composition with current native UI |
| [Launch film](citrus-launch.mp4) | 1920 × 1080 · 30 seconds | Conversion, photo editing, backgrounds and video trimming |
| [Wheel workflow](wheel-workflow.mp4) | 1600 × 1000 · 8 seconds | Actual wheel states and a verified PNG-to-WEBP conversion |
| [Video trim](video-trim.mp4) | 1600 × 1000 · 8 seconds | Actual trim handles and processed preview playback |
| [README animation](wheel-workflow.gif) | 720 × 450 · 8 seconds | Compact version of the wheel demonstration |

The films are silent and include descriptive English WebVTT companions. Dedicated posters are supplied for each video. [The manifest](manifest.json) records dimensions, durations, byte sizes and SHA-256 hashes; [public provenance](provenance.json) gives concise source and method details.

## What is authentic

Application windows, controls, labels, previews and wheel geometry are exported directly from the current production WPF visual trees. The wheel backing uses the production Direct3D shader over the launch photograph. The native UI pixels are unretouched. The compositor adds only the surrounding stage, typography, diagrammatic pointer, shadows and edited pacing. Native capture exclusion was not bypassed, and no user desktop or customer files were captured.

The harness performs real image conversions, image editing, background export and video trimming through the existing engines. It independently reopens or decodes their outputs and checks that source hashes remain unchanged. The saved trim is 1280 × 720 and 5.3 seconds long, corresponding to the displayed 1.2–6.5-second range. Curated `sample-*` files and `authoring-verification.json` retain the output evidence. The opt-in native harness passed; this is authoring evidence, not comprehensive release, visual acceptance or performance certification.

## Sources and reproduction

The original citrus-garden photograph was created with built-in image generation for this project. Its native dimensions are **1672 × 941**; it is not a native 4K photograph. The exact prompt and source hash are retained in `citrus-garden-generation.json`. The video input is an authored digital camera move over that still, not live camera footage. `motion-source.json` records its creation.

The approved website mark is supplied unchanged as [citrus-mark.png](citrus-mark.png). The actual application's older icon remains unchanged inside the native exports. Inter's [SIL Open Font License](licenses/Inter-OFL.txt) is included with this media package; software licenses remain in the repository. No third-party soundtrack is used.

Authoring sources and reproduction commands are in [launch-media](../../../../launch-media/README.md). [The verification summary](authoring-verification.json) records the opt-in WPF/engine outcome and independently verified output dimensions and hashes. Exact raw logs and tool receipts are retained locally and excluded from the published package. [The contact sheet](film-contact-sheet.png) and inspection frames record representative visual checks; all three final MP4s also pass a complete independent FFmpeg decode.
