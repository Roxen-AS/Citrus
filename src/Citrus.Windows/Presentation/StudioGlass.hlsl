// Native HLSL adaptation of Liquid Glass Studio f7b28c36305a862f5cffed3ddd51511cf1204f56.
// Copyright (c) 2024 Charles Yin. MIT; distributed notices retain the original license.
// LCH color helpers derive from Rachmanin0xFF/GLSL-Color-Functions (MIT).
// Pass order: padded source + shadow -> Gaussian X -> Gaussian Y -> STEP9 optics.

cbuffer Parameters : register(b0)
{
    float4 Source;       // valid copied width, height, padded origin relative to copied ROI
    float4 Capture;      // padded width, height, padding pixels, scale
    float4 Body;         // body pixels width, height, DIP width, height
    float4 Refraction;   // shoulder DIP, index, distance, dispersion
    float4 Fresnel;      // range, hardness, intensity, integer blur support
    float4 Glare;        // range, hardness, intensity, convergence
    float4 GlareState;   // opposite intensity, angle, interaction illumination, materialization
    float4 Shadow;       // expand DIP, strength, offset X/Y DIP
    float4 State;        // accent region, pointer X/Y DIP, dark
    float4 LightTint;    // neutral RGB, alpha in light mode
    float4 DarkTint;     // neutral RGB, alpha in dark mode
    float4 Lighting;     // optical reference height DIP, center X/Y DIP, selection tint opacity
    float4 ExteriorShadow; // compact presentation expand DIP, strength, offset X/Y
    float4 SurfaceState; // navigation visibility, backing-only composition, edge feather DIP, reserved
    float4 Shoulders[17]; // region zero's unused slot stores edge-highlight opacity
    float4 Gaussian[51];
};
Texture2D<float4> Original : register(t0);
Texture2D<float4> Blurred : register(t1);
Texture2D<float4> Contour : register(t2);
SamplerState LinearClamp : register(s0);
static const float PI = 3.141592653589793;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
    float2 uv = float2((id << 1) & 2, id & 2);
    return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 FieldAt(float2 bodyPixel)
{
    float2 limited = clamp(bodyPixel, .5, Body.xy - .5);
    float4 field = Contour.Load(int3((int2)limited, 0));
    // Extend only the distance for shadow evaluation outside the finite body field.
    field.z -= length(bodyPixel - limited) / Capture.w;
    return field;
}

float4 Background(float4 position : SV_Position) : SV_Target
{
    float2 sourcePixel = clamp(position.xy + Source.zw, .5, Source.xy - .5);
    float3 rgb = Original.SampleLevel(LinearClamp, sourcePixel / Capture.xy, 0).rgb;
    float distanceDip = FieldAt(position.xy - Capture.z - Shadow.zw * Capture.w).z;
    float shadow = Shadow.x > 0 ? exp(-abs(distanceDip) / max(Shadow.x, .0001)) * .6 * Shadow.y : 0;
    // RGBA16F retains negative shadow/highlight headroom until presentation conversion.
    return float4(rgb - shadow, 1);
}

float4 ClampSource(float4 position : SV_Position) : SV_Target
{
    float2 sourcePixel = clamp(position.xy + Source.zw, .5, Source.xy - .5);
    return float4(Original.SampleLevel(LinearClamp, sourcePixel / Capture.xy, 0).rgb, 1);
}

float Kernel(int offset) { return Gaussian[offset / 4][offset % 4]; }
float4 GaussianPass(float2 position, float2 axis)
{
    float2 uv = position / Capture.xy;
    float4 color = Original.SampleLevel(LinearClamp, uv, 0) * Kernel(0);
    int support = min(200, (int)Fresnel.w);
    [loop] for (int first = 1; first <= support; first += 2)
    {
        float a = Kernel(first);
        float b = first < support ? Kernel(first + 1) : 0;
        float weight = a + b;
        if (weight > 0)
        {
            // Linear filtering combines the two adjacent Gaussian taps with
            // their original weights, including the unpaired last odd tap.
            float2 shift = axis * (first + b / weight) / Capture.xy;
            color += (Original.SampleLevel(LinearClamp, uv + shift, 0) +
                      Original.SampleLevel(LinearClamp, uv - shift, 0)) * weight;
        }
    }
    return color;
}
float4 Horizontal(float4 position : SV_Position) : SV_Target { return GaussianPass(position.xy, float2(1, 0)); }
float4 Vertical(float4 position : SV_Position) : SV_Target { return GaussianPass(position.xy, float2(0, 1)); }

float Uncompand(float c) { return c > .04045 ? pow(max(0, (c + .055) / 1.055), 2.4) : c / 12.92; }
float Compand(float c) { return c <= .0031308 ? 12.92 * c : 1.055 * pow(max(0, c), 1.0 / 2.4) - .055; }
float LabCurve(float c) { return c > .00885645167 ? pow(max(c, 0), 1.0 / 3.0) : 7.78703703704 * c + .13793103448; }
float LabInverse(float c) { return c > .206897 ? c * c * c : .12841854934 * (c - .137931034); }
float3 ToLch(float3 srgb)
{
    float3 rgb = float3(Uncompand(srgb.r), Uncompand(srgb.g), Uncompand(srgb.b));
    float3 xyz = mul(float3x3(.4124, .3576, .1805, .2126, .7152, .0722, .0193, .1192, .9505), rgb);
    xyz /= float3(.95045592705, 1, 1.08905775076);
    xyz = float3(LabCurve(xyz.x), LabCurve(xyz.y), LabCurve(xyz.z));
    float3 lab = float3(116 * xyz.y - 16, 500 * (xyz.x - xyz.y), 200 * (xyz.y - xyz.z));
    return float3(lab.x, length(lab.yz), atan2(lab.z, lab.y));
}
float3 FromLch(float3 lch)
{
    float3 lab = float3(lch.x, lch.y * cos(lch.z), lch.y * sin(lch.z));
    float w = (lab.x + 16) / 116;
    float3 xyz = float3(LabInverse(w + lab.y / 500), LabInverse(w), LabInverse(w - lab.z / 200));
    xyz *= float3(.95045592705, 1, 1.08905775076);
    float3 rgb = mul(float3x3(3.2406255, -1.537208, -.4986286, -.9689307, 1.8757561, .0415175,
                              .0557101, -.2040211, 1.0569959), xyz);
    return float3(Compand(rgb.r), Compand(rgb.g), Compand(rgb.b));
}
float RangeFactor(float distanceDip, float range, float hardness)
{
    if (range <= 0) return 0;
    float base = max(0, 1 - distanceDip / 1500 * pow(500 / max(range, .0001), 2) + hardness);
    return saturate(base * base * base * base * base);
}
float3 DispersionSample(float2 uv, float2 displacement)
{
    float3 multipliers = float3(1 + .02 * Refraction.w, 1, 1 - .02 * Refraction.w);
    // Pinned Controls.tsx baseline has blurEdge=true. Its STEP9 mix rate is therefore
    // exactly one even at the outside shoulder; retain the diffuse optical sample.
    return float3(Blurred.SampleLevel(LinearClamp, uv + displacement * multipliers.r, 0).r,
                  Blurred.SampleLevel(LinearClamp, uv + displacement * multipliers.g, 0).g,
                  Blurred.SampleLevel(LinearClamp, uv + displacement * multipliers.b, 0).b);
}

float4 Optical(float4 position : SV_Position) : SV_Target
{
    float4 field = FieldAt(position.xy);
    int region = clamp((int)round(field.w), 0, 64);
    if (SurfaceState.y > .5 && region == 32 && SurfaceState.x < .5) return 0;
    float feather = max(1 / Capture.w, SurfaceState.z);
    float coverage = SurfaceState.z > 0 ? smoothstep(-feather * .5, feather * .5, field.z)
        : saturate(field.z * Capture.w + .5);
    float alpha = coverage * saturate(GlareState.w);
    // Keep a compact shadow behind the antialiased glass contour. Black is already
    // premultiplied; unlike a captured-background rectangle this leaves far corners clear.
    float shadowDistance = FieldAt(position.xy - ExteriorShadow.zw * Capture.w).z;
    float shadowAlpha = ExteriorShadow.x > 0 && abs(shadowDistance) < ExteriorShadow.x * 8
        ? exp(-abs(shadowDistance) / max(ExteriorShadow.x, .0001)) * .6 * ExteriorShadow.y * saturate(GlareState.w) : 0;
    if (alpha <= 0) return float4(0, 0, 0, shadowAlpha);
    float shoulder = Shoulders[region / 4][region % 4];
    float distanceDip = max(0, field.z);
    float edge = 0;
    if (shoulder > 0 && distanceDip < shoulder)
    {
        float ratio = saturate(1 - distanceDip / max(shoulder, .0001));
        float thetaI = asin(clamp(ratio * ratio, -1, 1));
        float thetaT = asin(clamp(sin(thetaI) / max(Refraction.y, 1), -1, 1));
        edge = max(0, -tan(thetaT - thetaI));
    }
    float2 uv = (position.xy + Capture.z) / Capture.xy;
    float3 tint = float3(1, .314, 0);
    float tintAlpha = abs(State.x - field.w) < .25 && State.x > 0 ? .9 : 0;
    float3 color;
    float2 normal = field.xy;
    float normalLength = length(normal);
    normal = normalLength > .00001 ? normal / normalLength : 0;
    if (edge <= 0)
        color = Blurred.SampleLevel(LinearClamp, uv, 0).rgb;
    else
    {
        // Studio's pixel SDF gradient carries 1414.21356 / canvasHeight. Algebraically
        // cancel that height in UV displacement, so padding and DPI cannot alter lensing.
        float2 displacement = normal * (1414.213562 * edge * Refraction.z * Capture.w) / Capture.xy;
        color = DispersionSample(uv, displacement);
    }
    float3 refracted = color;
    float4 neutral = State.w > .5 ? DarkTint : LightTint;
    color = lerp(color, neutral.rgb, neutral.a);
    color = lerp(color, tint, tintAlpha > 0 ? saturate(Lighting.w) : 0);
    if (edge > 0)
    {
        float3 bodyColor = color;
        // Preserve source normal strength in DIP space (a deliberate DPI-stability adaptation).
        float lightNormal = 1414.213562 / max(Lighting.x, 1);
        float fresnel = RangeFactor(distanceDip, Fresnel.x, Fresnel.y);
        float3 fresnelLch = ToLch(lerp(float3(1, 1, 1), tint, tintAlpha * .5));
        fresnelLch.x = clamp(fresnelLch.x + 20 * fresnel * Fresnel.z, 0, 100);
        color = lerp(color, FromLch(fresnelLch), saturate(fresnel * Fresnel.z * .7 * lightNormal));
        float geometry = RangeFactor(distanceDip, Glare.x, Glare.y);
        // Convert screen-Y-down inward normal to Studio's Y-up outward normal.
        float normalAngle = atan2(normal.y, -normal.x);
        if (normalAngle < 0) normalAngle += 2 * PI;
        float2 pointer = State.yz - Lighting.yz;
        float pointerAngle = dot(pointer, pointer) > .0001 ? atan2(-pointer.y, pointer.x) : 0;
        float angle = (normalAngle - PI / 4 + GlareState.y + saturate(GlareState.z) * (.12 + pointerAngle * .12)) * 2;
        bool farSide = (angle > PI * 1.5 && angle < PI * 3.5) || angle < -PI * .5;
        float angularBase = max(0, (.5 + sin(angle) * .5) * 1.2 * (farSide ? GlareState.x : 1) * Glare.z);
        float angular = saturate(pow(angularBase, max(.0001, .1 + Glare.w * 2)));
        float3 glareLch = ToLch(lerp(refracted, tint, tintAlpha * .5));
        glareLch.x = clamp(glareLch.x + 150 * angular * geometry, 0, 120);
        glareLch.y += 30 * angular * geometry;
        color = lerp(color, FromLch(glareLch), saturate(angular * geometry * lightNormal));
        // Scale the final visible lift, after clipping the optical highlight's gamut.
        // Raw intensity alone can still saturate the Studio LCH glare to full white.
        // Default-one materials keep the original Studio path unchanged.
        if (Shoulders[0].x < 1)
            color = lerp(bodyColor, saturate(color), saturate(Shoulders[0].x));
    }
    // Only this final BGRA8 presentation pass clips display gamut and premultiplies.
    color = all(isfinite(color)) ? saturate(color) : saturate(refracted);
    return float4(color * alpha, alpha + shadowAlpha * (1 - alpha));
}
