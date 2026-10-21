using Citrus.Core;

namespace Citrus.Windows.Presentation;

internal readonly record struct GlassRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    internal bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
    internal double SquaredDistance(double x, double y)
    {
        double dx = Math.Max(Math.Max(X - x, 0), x - Right);
        double dy = Math.Max(Math.Max(Y - y, 0), y - Bottom);
        return dx * dx + dy * dy;
    }
}

internal sealed class GlassRegion
{
    public int Id { get; }
    public GlassRect Bounds { get; }
    private readonly int kind;
    private readonly double radius;
    private readonly SegmentNode? polygon;

    private GlassRegion(int id, GlassRect bounds, int kind, double radius = 0, SegmentNode? polygon = null)
    {
        if (id is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(id));
        Id = id;
        Bounds = bounds;
        this.kind = kind;
        this.radius = radius;
        this.polygon = polygon;
    }

    public static GlassRegion Circle(int id, double cx, double cy, double radius)
    {
        Finite(cx, nameof(cx)); Finite(cy, nameof(cy)); Positive(radius, nameof(radius));
        return new(id, new(cx - radius, cy - radius, radius * 2, radius * 2), 0, radius);
    }

    public static GlassRegion RoundedRect(int id, double x, double y, double width, double height, double radius)
    {
        Finite(x, nameof(x)); Finite(y, nameof(y)); Positive(width, nameof(width)); Positive(height, nameof(height));
        if (!double.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        return new(id, new(x, y, width, height), 1, Math.Min(radius, Math.Min(width, height) / 2));
    }

    public static GlassRegion Polygon(int id, IReadOnlyList<WheelPoint> outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        if (outline.Count is < 3 or > 8192) throw new ArgumentOutOfRangeException(nameof(outline));
        // Copy coordinates into immutable segment storage; a caller cannot mutate a
        // contour after its field has been cached or during its background build.
        var points = outline.ToArray();
        foreach (var point in points) { Finite(point.X, nameof(outline)); Finite(point.Y, nameof(outline)); }
        double left = points.Min(p => p.X), top = points.Min(p => p.Y);
        double width = points.Max(p => p.X) - left, height = points.Max(p => p.Y) - top;
        Positive(width, nameof(outline)); Positive(height, nameof(outline));
        double area = 0;
        var segments = new Segment[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Length];
            area += a.X * b.Y - a.Y * b.X;
            segments[i] = new(a.X, a.Y, b.X, b.Y);
        }
        if (Math.Abs(area) < 1e-9) throw new ArgumentException("A polygon must enclose a nonzero area.", nameof(outline));
        return new(id, new(left, top, width, height), 2, polygon: SegmentNode.Build(segments, 0, segments.Length));
    }

    internal bool Contains(double x, double y)
    {
        if (!Bounds.Contains(x, y)) return false;
        if (kind == 2) return polygon!.Crossings(x, y) % 2 != 0;
        return Sample(x, y).Distance >= 0;
    }

    internal ContourSample Sample(double x, double y)
    {
        if (kind == 0)
        {
            double dx = x - Bounds.X - radius, dy = y - Bounds.Y - radius;
            double length = Math.Sqrt(dx * dx + dy * dy);
            return new(length == 0 ? 0 : -dx / length, length == 0 ? 0 : -dy / length, radius - length);
        }
        if (kind == 1)
        {
            double dx = x - Bounds.X - Bounds.Width / 2, dy = y - Bounds.Y - Bounds.Height / 2;
            double qx = Math.Abs(dx) - (Bounds.Width / 2 - radius);
            double qy = Math.Abs(dy) - (Bounds.Height / 2 - radius);
            double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0), length = Math.Sqrt(ox * ox + oy * oy);
            double distance = radius - length - Math.Min(Math.Max(qx, qy), 0);
            double nx, ny;
            if (length > 0) { nx = -Math.Sign(dx) * ox / length; ny = -Math.Sign(dy) * oy / length; }
            else if (qx > qy) { nx = -Math.Sign(dx); ny = 0; }
            else { nx = 0; ny = -Math.Sign(dy); }
            return new(nx, ny, distance);
        }
        double nearest = double.PositiveInfinity, boundaryX = 0, boundaryY = 0;
        polygon!.Nearest(x, y, ref nearest, ref boundaryX, ref boundaryY);
        bool inside = Bounds.Contains(x, y) && polygon!.Crossings(x, y) % 2 != 0;
        double d = Math.Sqrt(nearest);
        double sign = inside ? 1 : -1;
        // The gradient of a positive-inside signed distance always points inward,
        // including at exterior texels used for antialiasing and compact shadows.
        return new(d <= 1e-12 ? 0 : sign * (x - boundaryX) / d,
            d <= 1e-12 ? 0 : sign * (y - boundaryY) / d, sign * d);
    }

    private static void Finite(double value, string name)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > 1_000_000) throw new ArgumentOutOfRangeException(name);
    }
    private static void Positive(double value, string name)
    {
        Finite(value, name);
        if (value <= 0) throw new ArgumentOutOfRangeException(name);
    }

    internal readonly record struct ContourSample(double NormalX, double NormalY, double Distance);
    private readonly record struct Segment(double AX, double AY, double BX, double BY)
    {
        public GlassRect Bounds => new(Math.Min(AX, BX), Math.Min(AY, BY), Math.Abs(BX - AX), Math.Abs(BY - AY));
        public void Nearest(double x, double y, ref double minimum, ref double boundaryX, ref double boundaryY)
        {
            double dx = BX - AX, dy = BY - AY, denominator = dx * dx + dy * dy;
            double t = denominator == 0 ? 0 : Math.Clamp(((x - AX) * dx + (y - AY) * dy) / denominator, 0, 1);
            double px = AX + t * dx, py = AY + t * dy;
            double distance = (x - px) * (x - px) + (y - py) * (y - py);
            if (distance >= minimum) return;
            minimum = distance; boundaryX = px; boundaryY = py;
        }
    }

    /// <summary>Exact segment BVH: high-DPI fields do not walk every flattened petal segment per texel.</summary>
    private sealed class SegmentNode
    {
        private readonly GlassRect bounds;
        private readonly SegmentNode? left, right;
        private readonly Segment[] segments;
        private readonly int start, count;
        private SegmentNode(GlassRect bounds, Segment[] segments, int start, int count, SegmentNode? left, SegmentNode? right)
        { this.bounds = bounds; this.segments = segments; this.start = start; this.count = count; this.left = left; this.right = right; }

        public static SegmentNode Build(Segment[] segments, int start, int count)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            for (int i = start; i < start + count; i++)
            {
                var b = segments[i].Bounds;
                minX = Math.Min(minX, b.X); minY = Math.Min(minY, b.Y);
                maxX = Math.Max(maxX, b.Right); maxY = Math.Max(maxY, b.Bottom);
            }
            var bounds = new GlassRect(minX, minY, maxX - minX, maxY - minY);
            if (count <= 8) return new(bounds, segments, start, count, null, null);
            bool byX = bounds.Width >= bounds.Height;
            Array.Sort(segments, start, count, Comparer<Segment>.Create((a, b) =>
                (byX ? a.AX + a.BX : a.AY + a.BY).CompareTo(byX ? b.AX + b.BX : b.AY + b.BY)));
            int half = count / 2;
            return new(bounds, segments, start, count, Build(segments, start, half), Build(segments, start + half, count - half));
        }

        public int Crossings(double x, double y)
        {
            if (y < bounds.Y || y >= bounds.Bottom || x >= bounds.Right) return 0;
            if (left is not null) return left.Crossings(x, y) + right!.Crossings(x, y);
            int crossings = 0;
            for (int i = start; i < start + count; i++)
            {
                var s = segments[i];
                if ((s.AY > y) != (s.BY > y) && x < (s.BX - s.AX) * (y - s.AY) / (s.BY - s.AY) + s.AX) crossings++;
            }
            return crossings;
        }

        public void Nearest(double x, double y, ref double minimum, ref double boundaryX, ref double boundaryY)
        {
            if (bounds.SquaredDistance(x, y) >= minimum) return;
            if (left is null)
            {
                for (int i = start; i < start + count; i++) segments[i].Nearest(x, y, ref minimum, ref boundaryX, ref boundaryY);
                return;
            }
            double dl = left.bounds.SquaredDistance(x, y), dr = right!.bounds.SquaredDistance(x, y);
            var first = dl <= dr ? left : right!; var second = dl <= dr ? right! : left;
            first.Nearest(x, y, ref minimum, ref boundaryX, ref boundaryY);
            second.Nearest(x, y, ref minimum, ref boundaryX, ref boundaryY);
        }
    }
}

/// <summary>Cached pixel-center signed contour field; each texel is inward nx, ny, DIP distance, region ID.</summary>
internal sealed class GlassScene
{
    public double WidthDip { get; }
    public double HeightDip { get; }
    public double LightingReferenceHeightDip { get; }
    public double LightingCenterXDip { get; }
    public double LightingCenterYDip { get; }
    public double Scale { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public int PaddingPixels { get; }
    public int CaptureWidth { get; }
    public int CaptureHeight { get; }
    public GlassMaterial Material { get; }
    public bool IsWheelBacking { get; }
    public IReadOnlyList<GlassRegion> Regions { get; }
    public float[] Field { get; }
    private readonly double[] shoulderThickness = new double[65];
    public double ShoulderThicknessDip(int regionId) => regionId is >= 1 and <= 64 ? shoulderThickness[regionId] : 0;
    public GlassScene(double widthDip, double heightDip, double scale, IReadOnlyList<GlassRegion> regions,
        GlassMaterial? material = null, double? lightingReferenceHeightDip = null,
        double? lightingCenterXDip = null, double? lightingCenterYDip = null, bool isWheelBacking = false)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (!double.IsFinite(widthDip) || widthDip <= 0 || widthDip > 8192) throw new ArgumentOutOfRangeException(nameof(widthDip));
        if (!double.IsFinite(heightDip) || heightDip <= 0 || heightDip > 8192) throw new ArgumentOutOfRangeException(nameof(heightDip));
        if (!double.IsFinite(scale) || scale <= 0 || scale > 8) throw new ArgumentOutOfRangeException(nameof(scale));
        if (regions.Count > 64) throw new ArgumentOutOfRangeException(nameof(regions));
        if (regions.Any(r => r is null)) throw new ArgumentException("Regions cannot contain null.", nameof(regions));
        if (regions.Select(r => r.Id).Distinct().Count() != regions.Count)
            throw new ArgumentException("Region identifiers must be unique within a scene.", nameof(regions));
        double lightingHeight = lightingReferenceHeightDip ?? heightDip;
        double lightingX = lightingCenterXDip ?? widthDip / 2, lightingY = lightingCenterYDip ?? heightDip / 2;
        if (!double.IsFinite(lightingHeight) || lightingHeight <= 0 || lightingHeight > 8192)
            throw new ArgumentOutOfRangeException(nameof(lightingReferenceHeightDip));
        if (!double.IsFinite(lightingX) || Math.Abs(lightingX) > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(lightingCenterXDip));
        if (!double.IsFinite(lightingY) || Math.Abs(lightingY) > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(lightingCenterYDip));
        WidthDip = widthDip; HeightDip = heightDip; Scale = scale;
        LightingReferenceHeightDip = lightingHeight; LightingCenterXDip = lightingX; LightingCenterYDip = lightingY;
        PixelWidth = checked((int)Math.Ceiling(widthDip * scale));
        PixelHeight = checked((int)Math.Ceiling(heightDip * scale));
        Material = material ?? GlassMaterial.Citrus;
        IsWheelBacking = isWheelBacking;
        Material.Validate();
        if (Math.Ceiling(Material.BlurRadiusDip * scale) > 200)
            throw new ArgumentOutOfRangeException(nameof(material), "The physical Gaussian radius cannot exceed the shader's 200-sample limit.");
        PaddingPixels = StudioGlassMath.CapturePaddingPixels(Material, scale);
        CaptureWidth = checked(PixelWidth + 2 * PaddingPixels);
        CaptureHeight = checked(PixelHeight + 2 * PaddingPixels);
        if (CaptureWidth > 8192 || CaptureHeight > 8192 || (long)CaptureWidth * CaptureHeight > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(widthDip), "Padded glass scenes are limited to 16 million pixels and 8192 per side.");
        Regions = Array.AsReadOnly(regions.ToArray());
        foreach (var region in Regions)
            shoulderThickness[region.Id] = Math.Min(Material.ThicknessDip, .45 * Math.Min(region.Bounds.Width, region.Bounds.Height));
        Field = new float[checked(PixelWidth * PixelHeight * 4)];
        BuildField();
    }

    public static GlassScene Wheel(int count, double scale)
    {
        if (count is < 0 or > WheelGeometry.MaxTargetsPerPage) throw new ArgumentOutOfRangeException(nameof(count));
        return new(368, 368, scale, [GlassRegion.Circle(1, 184, 184, 176)], GlassMaterial.Wheel, isWheelBacking: true);
    }

    public static GlassScene WheelPresentation(int count, double scale, bool navigationVisible)
    {
        if (count is < 0 or > WheelGeometry.MaxTargetsPerPage) throw new ArgumentOutOfRangeException(nameof(count));
        GlassRegion[] regions = [GlassRegion.Circle(1, 184, 184, 176), GlassRegion.RoundedRect(32, 86, 372, 196, 36, 18)];
        return new(368, 454, scale, regions, GlassMaterial.Wheel, lightingReferenceHeightDip: 368,
            lightingCenterXDip: 184, lightingCenterYDip: 184, isWheelBacking: true);
    }

    private void BuildField()
    {
        for (int y = 0; y < PixelHeight; y++)
        for (int x = 0; x < PixelWidth; x++)
        {
            double px = (x + .5) / Scale, py = (y + .5) / Scale;
            var selected = new GlassRegion.ContourSample(0, 0, -1_000_000);
            int regionId = 0;
            bool inside = false;
            for (int i = 0; i < Regions.Count; i++)
            {
                var region = Regions[i];
                // Once inside, only a later shape containing this texel can replace
                // the material. Exterior gaps therefore keep the underlying shell.
                if (inside && !region.Contains(px, py)) continue;
                if (!inside && region.Bounds.SquaredDistance(px, py) > selected.Distance * selected.Distance) continue;
                var sample = region.Sample(px, py);
                if (sample.Distance >= 0 || (!inside && sample.Distance > selected.Distance))
                {
                    selected = sample;
                    regionId = region.Id;
                    inside = sample.Distance >= 0;
                }
            }
            int offset = (y * PixelWidth + x) * 4;
            Field[offset] = (float)selected.NormalX;
            Field[offset + 1] = (float)selected.NormalY;
            Field[offset + 2] = (float)selected.Distance;
            Field[offset + 3] = regionId;
        }
    }
}
