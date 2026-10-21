namespace Citrus.Core.Presentation;

/// <summary>
/// A CPU approximation of a rounded glass lens. Geometry, refraction coordinates, and edge
/// normals are cached; each frame samples a new desktop image into caller-owned buffers.
/// This type neither captures the desktop nor retains the source image.
/// </summary>
public sealed class LiquidGlassLens
{
    private const double SurfaceSize = 368, ShellRadius = 176, HubRadius = 60;
    private const double BevelWidth = 8, ExteriorSkirt = 2, MaximumRefraction = 7;
    private readonly PixelMap[] pixels;
    private readonly int rowStride;

    public int PixelSize { get; }
    public int ByteLength { get; }

    public LiquidGlassLens(int pixelSize, int petalCount)
    {
        if (pixelSize is < 128 or > 1536) throw new ArgumentOutOfRangeException(nameof(pixelSize));
        if (petalCount is < 0 or > WheelGeometry.MaxTargetsPerPage) throw new ArgumentOutOfRangeException(nameof(petalCount));
        PixelSize = pixelSize;
        rowStride = pixelSize * 4;
        ByteLength = pixelSize * rowStride;
        pixels = new PixelMap[pixelSize * pixelSize];
        BuildMaps(petalCount);
    }

    /// <summary>
    /// Source is tightly packed BGRA32 covering 368 by 368 logical units; its alpha is ignored.
    /// Both outputs are premultiplied BGRA32 with zero pixels outside the 176-unit shell.
    /// Pointer coordinates are in that same logical surface. Buffers must not overlap.
    /// </summary>
    public void Render(ReadOnlySpan<byte> source, Span<byte> body, Span<byte> lighting,
        double pointerX, double pointerY, bool dark)
    {
        if (source.Length < ByteLength) throw new ArgumentException("The source buffer is too short.", nameof(source));
        if (body.Length < ByteLength) throw new ArgumentException("The body buffer is too short.", nameof(body));
        if (lighting.Length < ByteLength) throw new ArgumentException("The lighting buffer is too short.", nameof(lighting));
        source = source[..ByteLength];
        body = body[..ByteLength];
        lighting = lighting[..ByteLength];
        if (source.Overlaps(body) || source.Overlaps(lighting) || body.Overlaps(lighting))
            throw new ArgumentException("The source, body, and lighting buffers must not overlap.");

        body.Clear();
        lighting.Clear();
        var (lightX, lightY) = LightDirection(pointerX, pointerY);
        float transmission = dark ? .36f : .46f;
        float washBlue = dark ? 32 * .64f : 240 * .54f;
        float washGreen = dark ? 27 * .64f : 247 * .54f;
        float washRed = dark ? 24 * .64f : 250 * .54f;

        for (int i = 0; i < pixels.Length; i++)
        {
            ref readonly var map = ref pixels[i];
            if (map.Alpha == 0) continue;
            int p = i * 4, s = map.SourceIndex;
            float fx = map.FractionX, fy = map.FractionY, coverage = map.Alpha / 255f;
            body[p] = ToByte((Sample(source, s, fx, fy) * transmission + washBlue) * coverage);
            body[p + 1] = ToByte((Sample(source, s + 1, fx, fy) * transmission + washGreen) * coverage);
            body[p + 2] = ToByte((Sample(source, s + 2, fx, fy) * transmission + washRed) * coverage);
            body[p + 3] = map.Alpha;

            if (map.Edge == 0 && map.Caustic == 0) continue;
            // A grazing-angle rim plus a small inset caustic. Only this lighting responds to
            // the pointer; the desktop never swims when the user moves across a label.
            float facing = -(map.NormalX * lightX + map.NormalY * lightY);
            float illumination = map.Edge * (.10f + .42f * facing) + map.Caustic * (.45f + .30f * facing);
            byte alpha = ToByte(MathF.Abs(illumination) * (dark ? .85f : 1f) * map.Alpha);
            bool bright = illumination >= 0;
            lighting[p] = (byte)(alpha * (bright ? 235 : 20) / 255);
            lighting[p + 1] = (byte)(alpha * (bright ? 248 : 14) / 255);
            lighting[p + 2] = (byte)(alpha * (bright ? 255 : 10) / 255);
            lighting[p + 3] = alpha;
        }
    }

    private float Sample(ReadOnlySpan<byte> source, int p, float fx, float fy)
    {
        float upper = source[p] + (source[p + 4] - source[p]) * fx;
        float lower = source[p + rowStride] + (source[p + rowStride + 4] - source[p + rowStride]) * fx;
        return upper + (lower - upper) * fy;
    }

    private void BuildMaps(int petalCount)
    {
        // Rasterize only narrow segment neighborhoods. No polygon walk occurs per frame,
        // and constructing a high-DPI map does not scan every polygon for every pixel.
        Boundary[]? petalEdges = petalCount == 0 ? null : BuildPetalEdges(petalCount);
        double scale = PixelSize / SurfaceSize;
        for (int y = 0; y < PixelSize; y++)
        for (int x = 0; x < PixelSize; x++)
        {
            double logicalX = (x + .5) / scale, logicalY = (y + .5) / scale;
            double dx = logicalX - WheelGeometry.CenterX, dy = logicalY - WheelGeometry.CenterY;
            double radius = Math.Sqrt(dx * dx + dy * dy), distance = ShellRadius - radius;
            if (distance <= 0) continue;

            double normalX = radius == 0 ? 0 : -dx / radius, normalY = radius == 0 ? 0 : -dy / radius;
            double strength = BevelProfile(distance), hubDistance = HubRadius - radius;
            double hubStrength = BevelProfile(hubDistance);
            if (hubStrength > strength)
            {
                strength = hubStrength;
                distance = hubDistance;
            }
            int i = y * PixelSize + x;
            if (petalEdges is not null && float.IsFinite(petalEdges[i].SignedDistanceSquared))
            {
                ref readonly var boundary = ref petalEdges[i];
                double petalDistance = Math.CopySign(Math.Sqrt(Math.Abs(boundary.SignedDistanceSquared)), boundary.SignedDistanceSquared);
                double petalStrength = BevelProfile(petalDistance);
                if (petalStrength > strength)
                {
                    strength = petalStrength;
                    distance = petalDistance;
                    normalX = boundary.NormalX;
                    normalY = boundary.NormalY;
                }
            }

            // One thickness field chooses the local shoulder, rather than stacking a
            // second complete glass image over the wheel/petal/hub regions.
            double sourceX = Math.Clamp((logicalX + normalX * strength * MaximumRefraction) * scale - .5, 0, PixelSize - 1);
            double sourceY = Math.Clamp((logicalY + normalY * strength * MaximumRefraction) * scale - .5, 0, PixelSize - 1);
            int left = Math.Min((int)sourceX, PixelSize - 2), top = Math.Min((int)sourceY, PixelSize - 2);
            pixels[i] = new PixelMap
            {
                SourceIndex = (top * PixelSize + left) * 4,
                FractionX = (float)(sourceX - left),
                FractionY = (float)(sourceY - top),
                NormalX = (float)normalX,
                NormalY = (float)normalY,
                Edge = (float)(strength * strength),
                Caustic = strength == 0 || distance < 0 ? 0 : (float)(.16 * Math.Exp(-.5 * Math.Pow((distance - 4.6) / 1.1, 2))),
                Alpha = ToByte((float)Math.Min(1, (ShellRadius - radius) * scale) * 255)
            };
        }
    }

    private Boundary[] BuildPetalEdges(int count)
    {
        var boundaries = new Boundary[pixels.Length];
        for (int i = 0; i < boundaries.Length; i++) boundaries[i].SignedDistanceSquared = float.PositiveInfinity;
        for (int petal = 0; petal < count; petal++)
        {
            var outline = WheelGeometry.Petal(petal, count);
            double area = 0;
            for (int j = 0, k = outline.Count - 1; j < outline.Count; k = j++)
                area += outline[k].X * outline[j].Y - outline[j].X * outline[k].Y;
            double orientation = area >= 0 ? 1 : -1;
            for (int j = 0, k = outline.Count - 1; j < outline.Count; k = j++)
                RasterizeEdge(boundaries, outline[k], outline[j], orientation);
        }
        return boundaries;
    }

    private void RasterizeEdge(Boundary[] boundaries, WheelPoint a, WheelPoint b, double orientation)
    {
        double vx = b.X - a.X, vy = b.Y - a.Y, lengthSquared = vx * vx + vy * vy;
        if (lengthSquared < 1e-12) return;
        double scale = PixelSize / SurfaceSize, step = 1 / scale, inverseLength = 1 / Math.Sqrt(lengthSquared);
        double inwardX = -vy * inverseLength * orientation, inwardY = vx * inverseLength * orientation;
        int minX = Math.Clamp((int)Math.Floor((Math.Min(a.X, b.X) - BevelWidth) * scale - .5), 0, PixelSize - 1);
        int maxX = Math.Clamp((int)Math.Ceiling((Math.Max(a.X, b.X) + BevelWidth) * scale - .5), 0, PixelSize - 1);
        int minY = Math.Clamp((int)Math.Floor((Math.Min(a.Y, b.Y) - BevelWidth) * scale - .5), 0, PixelSize - 1);
        int maxY = Math.Clamp((int)Math.Ceiling((Math.Max(a.Y, b.Y) + BevelWidth) * scale - .5), 0, PixelSize - 1);
        for (int y = minY; y <= maxY; y++)
        {
            double py = (y + .5) * step;
            for (int x = minX; x <= maxX; x++)
            {
                double px = (x + .5) * step;
                double t = Math.Clamp(((px - a.X) * vx + (py - a.Y) * vy) / lengthSquared, 0, 1);
                double offsetX = px - (a.X + vx * t), offsetY = py - (a.Y + vy * t);
                double squared = offsetX * offsetX + offsetY * offsetY;
                ref var boundary = ref boundaries[y * PixelSize + x];
                if (squared > BevelWidth * BevelWidth || squared >= Math.Abs(boundary.SignedDistanceSquared)) continue;
                bool inside = offsetX * inwardX + offsetY * inwardY >= 0;
                double normalScale = squared < 1e-12 ? 0 : (inside ? 1 : -1) / Math.Sqrt(squared);
                boundary.SignedDistanceSquared = (float)(inside ? squared : -squared);
                boundary.NormalX = (float)(normalScale == 0 ? inwardX : offsetX * normalScale);
                boundary.NormalY = (float)(normalScale == 0 ? inwardY : offsetY * normalScale);
            }
        }
    }

    private static double BevelProfile(double distance)
    {
        if (distance <= -ExteriorSkirt || distance >= BevelWidth) return 0;
        if (distance >= 0) return Math.Pow(1 - distance / BevelWidth, 1.4);
        // A narrow smooth skirt joins raised rounded petals to the surrounding shell.
        double t = (distance + ExteriorSkirt) / ExteriorSkirt;
        return t * t * (3 - 2 * t);
    }

    private static (float X, float Y) LightDirection(double pointerX, double pointerY)
    {
        const double keyX = -.6, keyY = -.8;
        if (!double.IsFinite(pointerX) || !double.IsFinite(pointerY)) return ((float)keyX, (float)keyY);
        double x = Math.Clamp(pointerX, -SurfaceSize, 2 * SurfaceSize) - WheelGeometry.CenterX;
        double y = Math.Clamp(pointerY, -SurfaceSize, 2 * SurfaceSize) - WheelGeometry.CenterY;
        double length = Math.Sqrt(x * x + y * y);
        if (length < .001) return ((float)keyX, (float)keyY);
        x = .35 * keyX + .65 * x / length;
        y = .35 * keyY + .65 * y / length;
        length = Math.Sqrt(x * x + y * y);
        return ((float)(x / length), (float)(y / length));
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)(value + .5f), 0, 255);

    private struct PixelMap
    {
        public int SourceIndex;
        public float FractionX, FractionY, NormalX, NormalY, Edge, Caustic;
        public byte Alpha;
    }

    private struct Boundary
    {
        public float SignedDistanceSquared, NormalX, NormalY;
    }
}
