# Original image-editor v1 pixel fixtures

These fixtures are generated in `ImageRecipeTests`, with no downloaded images, fonts,
proprietary references or external executables. Magick.NET Q8 15.0.0 is pinned by the
existing project. Tests hash original files before/after every test, except deliberate
external replacement fixtures, whose replacement bytes become the protected original.

The main RGBA marker ramp at integer `(x,y)` is:

- R = `(37x + 19y + 13) mod 256`
- G = `(11x + 43y + 31) mod 256`
- B = `(67x + 7y + 61) mod 256`
- A = 96 when `(x+y) mod 3 == 0`, otherwise 255.

The 7×5 ramp tests all 16 flip/quarter-turn combinations and a non-square fractional
crop. The 19×17 ramp pins every adjustment through SHA-256 of **decoded straight
BGRA8 pixels**, not encoder-dependent file bytes. Expected hashes live beside the
parameter cases in `ImageRecipeTests.Goldens`. These are versioned Citrus regression
goldens, captured from the v1 implementation after formula and independent scalar,
geometry and alpha-reference checks; they are not proprietary product-equivalence
measurements. Full lossless PNG export must equal the same 100% preview bytes.

The scalar goldens independently evaluate the equations below for RGB `(13,31,61)`.
The alpha-weighted blur test independently filters separate opaque numerator/weight
images, catching double alpha association. The transparent-atmosphere test prevents
invisible RGB from choosing dehaze atmospheric light. TIFF carries a real EXIF
orientation; JPEG carries AdobeRGB and Artist metadata. Additional generated PNG
IHDR data tests pre-allocation pixel limits. No binary fixture acquisition is required.

## Coordinate/order contract

Normalize EXIF once and transform embedded ICC to SDR sRGB. Untagged images are
interpreted as sRGB. Apply flips in source plane S, clockwise quarter turns, then
crop in G using floor(left/top), ceil(right/bottom). Reject crop outside actual
source bounds and forbidden upscaling. Native-size previews, explicit source tiles and exports run tone/detail at crop
resolution, followed by exact output resize using Magick Lanczos. Zoomed-out
viewports use the explicitly approximate execution described below.

`PreviewRequest.SourceTile` is an S pixel-edge rectangle. Map its four corners
through the same geometry to final C, intersect output, floor/ceil and crop that
completed image, then fit within request bounds (never upscale). The concrete test
uses S `[4,7]×[5,9]`, source 19×17, flip-X/CW90, G crop `(2,1,12,14)`, producing
C `[6,10]×[11,14]`, a 4×3 detail tile. It is never a magnified viewport bitmap.

## Pinned SDR v1 adjustment equations

RGB values below are encoded sRGB in [0,1], unless stated otherwise. Luminance is
`L = .2126R + .7152G + .0722B`. Floating-point kernels use doubles. Q8 boundaries
clamp to [0,255] and round nearest, ties away from zero. Alpha is unchanged by
adjustments; geometry/resampling may resample it. Order is exactly the following:

1. Exposure: decode sRGB with `v/12.92` for `v≤.04045`, otherwise
   `((v+.055)/1.055)^2.4`; multiply linear value by `2^EV`; re-encode with
   `12.92v` for `v≤.0031308`, otherwise `1.055v^(1/2.4)-.055`.
   Brightness adds its signed value in encoded sRGB. Contrast is
   `clamp((v-.5)*2^(2c)+.5)`; gamma is `v^(1/gamma)`.
2. Shadows/highlights: `delta=.25*(shadows*(1-L)^2 + highlights*L^2)`;
   multiply RGB by `max(0,L+delta)/L`. At zero L, set RGB to `max(0,delta)`.
3. Saturation/vibrance: `chroma=max(R,G,B)-min(R,G,B)`;
   `s=saturation*(1+vibrance*(1-clamp(chroma)))`; `RGB=L+s*(RGB-L)`.
4. Hue: clamp then rotate HSV hue by degrees, maintaining value and chroma.
   Artistic warmth/tint gains: R `2^(.5warmth-.25tint)`, G `2^(.5tint)`,
   B `2^(-.5warmth-.25tint)`. This is not Kelvin white balance. Quantize RGB.
5. Clarity: quantize luminance; alpha-weighted Gaussian sigma=12 source pixels;
   add `clarity*(L-blurredL)` to all channels; quantize. Clarity is a luminance
   unsharp operation, not a brightness substitute.
6. Dehaze: exact 15×15 local minimum of RGB, clipping neighborhoods at image
   edges, ignoring fully transparent samples. Select the brightest 0.1% of
   visible pixels by dark-channel value (ceil, at least one), with row-major
   ties. Atmospheric light A is the highest-luminance original RGB among those
   pixels, with row-major ties, each component floored to 1/255. Compute the
   second 15×15 dark channel of RGB/A; `t=max(.1,1-.95*dehaze*dark(RGB/A))`;
   `out=(RGB-A)/t+A`. A denotes atmospheric light, not opacity. Fully transparent
   images short-circuit; zero dehaze short-circuits. Quantize.
7. Sharpen: Gaussian sigma=1 source pixel, amount `2*Sharpen`, zero threshold:
   `out=RGB+2*Sharpen*(RGB-blurredRGB)`, quantized.
8. Denoise: per-channel median square, radius `ceil(3*Denoise)` clamped 1..3,
   edge extension from the pinned Magick native kernel; blend by strength,
   preserving original alpha, then quantize.
9. Blur: Gaussian sigma=BlurSigma source pixels. Gaussian helper independently
   filters opaque Q8 premultiplied RGB and Q8 alpha-weight planes, then divides
   once by filtered alpha (zero weight produces zero RGB). Original alpha is
   restored. This avoids invisible RGB bleed and double alpha weighting.
10. Vignette: `nx=2*(x+.5)/width-1`, similarly ny; multiply by
    `1-Vignette*clamp((nx²+ny²)/2)` in cropped-image coordinates.
11. Grain: inverse-map cropped pixel centers to S, floor x/y, then hash
    `h = uint(x)*0x9e3779b9 XOR uint(y)*0x85ebca6b XOR seed` with uint overflow;
    mix `h^=h>>16; h*=0x7feb352d; h^=h>>15; h*=0x846ca68b; h^=h>>16`.
    Add the same `(h/uint.MaxValue*2-1)*Grain*.1` to all channels.
12. Effects blend by amount: grayscale=L; sepia matrix rows
    `(.393,.769,.189),(.349,.686,.168),(.272,.534,.131)`; invert=`1-RGB`;
    posterize rounds clamped RGB to the nearest of four levels 0,1/3,2/3,1.
    Quantize. Effect None/zero amount is identity.

All-default adjustment settings bypass pixel kernels entirely, preserving exact
identity pixels. Schema/ranges are tested in `ImageRecipeValidationTests`. This
pipeline is Q8 SDR and makes no high-bit-depth or HDR-fidelity claim.

JPEG/BMP/GIF require an opaque explicit matte, which is also visible in preview.
Metadata PreserveSupported retains source profiles/attributes supported by the
encoder, normalizes EXIF orientation, removes stale EXIF pixel dimensions and
sets sRGB. StripDescriptive strips descriptive profiles/attributes and restores
sRGB; StripAll omits profiles. PNG explicitly preserves iCCP rather than allowing
its encoder to optimize it away. Metadata field edits remain rejected.

## Remaining native/performance gates

Linux executes the real pinned Magick native kernels. Native Windows codec/pixel
runs and the WPF shared-worker fixture remain required; cross-compilation is not
native evidence. The renderer remains unregistered. Exact full-resolution detail
and export remain expensive; their pixel semantics are unchanged. The bounded fast
viewport and optional idle refinement avoid applying every filter to the original
8/16 MP raster on each adjustment. See the performance report for actual measured
latency, allocation and cold-decode limitations; no hardware timing is a CI gate.

## Independent publication-policy validation

The payload validator reads encoded bytes without profile/color normalization. Every
non-StripAll output must contain the exact pinned canonical sRGB ICC payload; a
matching profile name alone is insufficient. StripDescriptive permits only this ICC
profile and specifically enumerated decoder technical attributes. StripAll permits
no profiles and the same decoder technical attributes. Unknown/descriptive attributes
(including PNG comments and Title) and EXIF/XMP/IPTC/other profiles fail before
publication. PreserveSupported continues to allow descriptive metadata while enforcing
the sRGB working profile. Validation never rewrites payloads.

A 27-case format × metadata-policy matrix exercises the real publisher. Negative
fixtures re-encode valid PNG payloads with EXIF Artist, comment/Title, AdobeRGB ICC,
or no ICC and update both staged-file and compression byte measurements before
publication. They assert rejection, no destination output and unchanged payload hash.
PNG StripDescriptive clears Magick's `png:exclude-chunk` artifact after Strip() has
removed metadata so the encoder can retain its newly restored sRGB working profile.


## Bounded viewport execution and cache

Approximation applies only when SourceTile is absent and actual final output exceeds
at least one requested Bounds edge. Native-size requests remain exact even if their
Bounds exceed the fast cap. `RenderPreviewAsync` caps each working edge at 512;
concrete `RenderRefinedPreviewAsync` caps at 1024 for optional idle/gesture-end work.
Both share the same session serial lease and global two-preview scheduler. Bounds
are upper limits: no 512-pixel raster is enlarged to pretend it contains 2048-pixel
detail. Every approximate frame reports a scaled-preview warning and truthful Size.

After normalized S geometry and rounded crop, fit the crop aspect inside the minimum
of requested Bounds and the execution cap, flooring dimensions (minimum one). Filter
this unadjusted working raster. Fit the actual final output aspect inside that work
size, never enlarging either edge; thus an intentional nonuniform output resize can
produce a smaller frame. Work scale is the geometric mean
`s=sqrt(workWidth/cropWidth * workHeight/cropHeight)`; this resolves the tiny axis
scale difference caused by integer fitting. Clarity sigma is `12s`, sharpen sigma
`s`, blur sigma `BlurSigma*s`; dark-channel radius is `round(7s)`, and median radius
is `round(clamp(ceil(3*strength),1,3)*s)`. Radius rounding uses ties away from zero;
a zero radius is a 1×1 window. Vignette uses normalized working coordinates. Grain
inverse-maps the actual working pixel center through geometry to S before flooring
and hashing, so it never substitutes viewport coordinates for source coordinates.
All tone/color math and Q8 stage boundaries remain v1. Approximate pixels are not
expected to equal a downsampled exact export.

The renderer caches immutable, unadjusted straight RGBA8 bytes, keyed by verified
source identity, geometry and actual working dimensions. Revision and adjustments
are deliberately absent: each request reconstructs its own native working image
and applies its current recipe. Four-entry LRU and 64 MiB decoded-byte budget both
apply; an over-budget exact crop bypasses retention. No native image handle is
retained by the cache. Verified source staging has a separate four-source LRU;
evicting a source deletes its private scope and associated decoded bases. Session
disposal clears both. Cancellation disposes request-owned native images and never
publishes a frame; a successfully created unadjusted base can safely remain cached.
The budget covers retained decoded bytes, not transient decode/filter/output memory.

Exact optimization evaluates the unchanged Tone function once for each possible Q8
input (256 doubles) and request-constant warmth/tint powers once. The first dark
channel's 256-value histogram determines the top 0.1% cutoff; a row-major scan
retains the same tied cutoff pixels and brightest-luminance/row-major atmosphere as
the original heap. Reusing dark-channel storage does not change float boundaries.
`ImageScaledKernelTests` independently brute-forces multiple-candidate/tied dehaze
windows and compares scaled Gaussian/median results with opaque native-plane
references. `ImagePreviewPerformanceTests` checks real processed dimensions,
truthful fast/refined frames, independent inverse grain geometry, exact tile/export,
cache revision isolation, byte/source LRU eviction, disposal and cancellation.
