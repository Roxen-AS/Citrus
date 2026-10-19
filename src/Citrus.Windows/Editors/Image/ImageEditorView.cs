using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Citrus.Core.Editing;

namespace Citrus.Windows.Editors.Image;

/// <summary>Supported inspector only; all fields write the immutable recipe used by preview and export.</summary>
public sealed class ImageEditorView : StackPanel
{
    private readonly EditorHostViewModel model;
    private readonly Action refresh;
    private readonly Action<string> error;
    private readonly Action<double?> ratioChanged;
    private readonly Action rebuild;
    private readonly double? initialRatio;
    private EditorDocument Document => model.Selected!.Draft!;
    private ImageRecipe Recipe => (ImageRecipe)Document.Recipe;
    private readonly List<Action> pendingFields = [];
    public bool GestureActive { get; private set; }
    public ImageEditorView(EditorHostViewModel model, string tool, Action refresh, Action<string> error, Action<double?> ratioChanged, Action rebuild, double? initialRatio)
    {
        this.model = model; this.refresh = refresh; this.error = error; this.ratioChanged = ratioChanged; this.rebuild = rebuild; this.initialRatio = initialRatio;
        switch (tool)
        {
            case "Adjust": Adjust(); break;
            case "Crop": Crop(); break;
            case "Resize": Resize(); break;
            case "Rotate": Rotate(); break;
            case "Compress": Compress(); break;
        }
    }
    public void FlushFields() { foreach (var apply in pendingFields) apply(); }
    private void Try(Action action) { try { action(); error(""); } catch (Exception e) when (e is ArgumentException or OverflowException or FormatException) { error(e.Message); } }
    private void Change(ImageRecipe recipe, bool transient = false) { model.Change(Document with { Recipe = recipe }, transient); refresh(); }
    private void Label(string text) => Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0,8,0,4) });
    private Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new(0,4,0,4) }; button.SetResourceReference(StyleProperty,"SecondaryButton"); button.Click += (_, _) => Try(action); Children.Add(button); return button;
    }
    private TextBox TextField(string label, string text, Action<string> apply)
    {
        var box = new TextBox { Text = text, Margin = new(0,0,0,6), Padding = new(6) };
        Label(label); AutomationProperties.SetName(box, label); Children.Add(box);
        string accepted = text;
        void Commit() { if (box.Text == accepted) return; apply(box.Text); accepted = box.Text; }
        box.Tag = new Action<string>(value => { accepted = value; box.Text = value; });
        pendingFields.Add(Commit);
        box.LostKeyboardFocus += (_, _) => Try(Commit);
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Try(Commit); e.Handled = true; } };
        return box;
    }
    private static double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : throw new ArgumentException("Enter a finite number using a decimal point.");
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private void Range(string label, double value, double min, double max, double step, Action<double, bool> apply)
    {
        Slider? slider = null; bool synchronizing = false;
        var box = TextField($"{label} · {Number(min)}–{Number(max)}", Number(value), text =>
        {
            double number = Number(text);
            if (number < min || number > max) throw new ArgumentException(label + " is outside its displayed range.");
            apply(number, false);
            if (slider is not null) { synchronizing = true; slider.Value = number; synchronizing = false; }
        });
        slider = new Slider { Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 10, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, Margin = new(0,0,0,6) };
        AutomationProperties.SetName(slider, label); Children.Add(slider);
        slider.PreviewMouseLeftButtonDown += (_, _) => GestureActive = true;
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => EndGesture()));
        slider.PreviewMouseLeftButtonUp += (_, _) => EndGesture();
        slider.LostMouseCapture += (_, _) => { if (GestureActive) EndGesture(); };
        slider.ValueChanged += (_, _) => { if (!synchronizing) Try(() => { apply(slider.Value, GestureActive); ((Action<string>)box.Tag)(Number(slider.Value)); }); };
        void EndGesture() { if (!GestureActive) return; GestureActive = false; model.CommitGesture(); refresh(); }
    }
    private void Adjust()
    {
        foreach (var field in ImageEditorViewModel.Fields)
            Range(field.Label, field.Read(Recipe.Adjustments), field.Minimum, field.Maximum, field.Step, (value, transient) => Change(Recipe with { Adjustments = field.Write(Recipe.Adjustments, value) }, transient));
        Label("Photo effect");
        var effects = new ComboBox { ItemsSource = Enum.GetValues<PhotoEffect>(), SelectedItem = Recipe.Adjustments.Effect };
        AutomationProperties.SetName(effects,"Photo effect"); Children.Add(effects);
        effects.SelectionChanged += (_, _) => Try(() => Change(Recipe with { Adjustments = Recipe.Adjustments with { Effect = (PhotoEffect)effects.SelectedItem } }));
        TextField("Grain seed · 0–4294967295", Recipe.Adjustments.GrainSeed.ToString(CultureInfo.InvariantCulture), text =>
        {
            if (!uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out uint seed)) throw new ArgumentException("Grain seed must be a whole number from 0 to 4294967295.");
            Change(Recipe with { Adjustments = Recipe.Adjustments with { GrainSeed = seed } });
        });
    }
    private void Crop()
    {
        Label("Drag a corner to resize; drag inside or use arrow keys to move. Coordinates are oriented source pixels.");
        var size = model.Selected!.Size; if (Recipe.Geometry.QuarterTurns % 2 == 1) size = new(size.Height, size.Width);
        var crop = Recipe.Geometry.Crop ?? new RectD(0,0,size.Width,size.Height);
        var boxes = new Dictionary<string,TextBox>();
        void Apply(string key, string text)
        {
            var r = Recipe.Geometry.Crop ?? new RectD(0,0,size.Width,size.Height); double v = Number(text);
            r = key switch { "X" => r with { X = v }, "Y" => r with { Y = v }, "Width" => r with { Width = v }, _ => r with { Height = v } };
            Change(Recipe with { Geometry = Recipe.Geometry with { Crop = r } });
        }
        foreach (var (key,value) in new[] { ("X",crop.X), ("Y",crop.Y), ("Width",crop.Width), ("Height",crop.Height) })
            boxes[key] = TextField(key + " · pixels", Number(value), text => Apply(key,text));
        Label("Aspect ratio"); var ratio = new ComboBox { ItemsSource = new[] { "Free", "Original", "1:1", "4:3", "3:2", "16:9", "9:16" }, SelectedIndex = 0 };
        double?[] ratios = [null, (double)size.Width/size.Height, 1, 4d/3, 3d/2, 16d/9, 9d/16];
        ratio.SelectedIndex = Math.Max(0, Array.FindIndex(ratios, r => r == initialRatio));
        AutomationProperties.SetName(ratio,"Crop aspect ratio"); Children.Add(ratio);
        ratio.SelectionChanged += (_, _) => Try(() =>
        {
            double? value = ratio.SelectedIndex switch { 1 => (double)size.Width / size.Height, 2 => 1, 3 => 4d/3, 4 => 3d/2, 5 => 16d/9, 6 => 9d/16, _ => null };
            ratioChanged(value); if (value is null) return;
            double w = Math.Min(size.Width, size.Height * value.Value), h = w / value.Value;
            var r = new RectD(Math.Floor((size.Width-w)/2), Math.Floor((size.Height-h)/2), Math.Floor(w), Math.Floor(h));
            Change(Recipe with { Geometry = Recipe.Geometry with { Crop = r } });
            ((Action<string>)boxes["X"].Tag)(Number(r.X)); ((Action<string>)boxes["Y"].Tag)(Number(r.Y));
            ((Action<string>)boxes["Width"].Tag)(Number(r.Width)); ((Action<string>)boxes["Height"].Tag)(Number(r.Height));
        });
        Button("Use full image", () => { Change(Recipe with { Geometry = Recipe.Geometry with { Crop = null } }); rebuild(); });
    }
    private void Rotate()
    {
        Label($"Current rotation: {Recipe.Geometry.QuarterTurns * 90}° · Horizontal flip: {(Recipe.Geometry.FlipX ? "on" : "off")} · Vertical flip: {(Recipe.Geometry.FlipY ? "on" : "off")}");
        Label("Quarter turns apply before crop. Rotating resets the crop to the whole image.");
        Button("Rotate 90° clockwise", () => { Change(Recipe with { Geometry = Recipe.Geometry with { QuarterTurns = (Recipe.Geometry.QuarterTurns + 1) % 4, Crop = null } }); rebuild(); });
        Button("Rotate 90° counterclockwise", () => { Change(Recipe with { Geometry = Recipe.Geometry with { QuarterTurns = (Recipe.Geometry.QuarterTurns + 3) % 4, Crop = null } }); rebuild(); });
        Button("Flip horizontally", () => { Change(Recipe with { Geometry = Recipe.Geometry with { FlipX = !Recipe.Geometry.FlipX } }); rebuild(); });
        Button("Flip vertically", () => { Change(Recipe with { Geometry = Recipe.Geometry with { FlipY = !Recipe.Geometry.FlipY } }); rebuild(); });
    }
    private void Resize()
    {
        Label("Set width, height, or both. Preserve aspect fits inside both limits. Blank dimensions leave the source size unchanged.");
        var intent = model.Selected!.ResizeIntent;
        Action commitPair = null!; // Assigned before any input is dispatched to this constructed view.
        var width = TextField("Width · optional pixels", intent?.Width?.ToString() ?? Recipe.Geometry.OutputSize?.Width.ToString() ?? "", _ => commitPair());
        var height = TextField("Height · optional pixels", intent?.Height?.ToString() ?? Recipe.Geometry.OutputSize?.Height.ToString() ?? "", _ => commitPair());
        var aspect = new CheckBox { Content = "Preserve aspect ratio", IsChecked = intent?.PreserveAspectRatio ?? true, Margin = new(0,8,0,8) };
        var upscale = new CheckBox { Content = "Allow upscaling", IsChecked = Recipe.Geometry.AllowUpscale, Margin = new(0,8,0,8) };
        Children.Add(aspect); Children.Add(upscale);
        string appliedWidth = width.Text, appliedHeight = height.Text;
        bool? appliedAspect = aspect.IsChecked, appliedUpscale = upscale.IsChecked;
        void Apply()
        {
            if (width.Text == appliedWidth && height.Text == appliedHeight && aspect.IsChecked == appliedAspect && upscale.IsChecked == appliedUpscale) return;
            int? Integer(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : int.TryParse(box.Text, out int value) && value > 0 ? value : throw new ArgumentException("Dimensions must be positive whole pixels.");
            var resize = new ImageResizeIntent(Integer(width), Integer(height), aspect.IsChecked == true, upscale.IsChecked == true);
            if (resize.Width is null && resize.Height is null) { model.Selected.ResizeIntent = null; Change(Recipe with { Geometry = Recipe.Geometry with { OutputSize = null, AllowUpscale = resize.AllowUpscale } }); Applied(); return; }
            var document = ImageBatchMapper.Map(new(Document, model.Selected.Size, ImageBatchGeometry.Relative, resize, false, true), Document, model.Selected.Size);
            model.Change(document); model.Selected.ResizeIntent = resize; Applied(); refresh();
        }
        void Applied() { appliedWidth = width.Text; appliedHeight = height.Text; appliedAspect = aspect.IsChecked; appliedUpscale = upscale.IsChecked; }
        commitPair = Apply;
        pendingFields.Add(Apply);
        Button("Apply size", Apply);
        Label("Width-only and height-only settings use each image's own aspect ratio when applied to all.");
    }
    private void Compress()
    {
        Label("Quality controls the real exported encoding. Preview shows pixel edits; encoded artifacts and final size are measured on export. Byte and reduction targets are not available yet.");
        Range("Quality", ((QualityGoal)Document.Export.Compression).Value, 1, 100, 1, (value, transient) =>
        {
            if (value != Math.Truncate(value)) throw new ArgumentException("Quality must be a whole number from 1 to 100.");
            model.Change(Document with { Export = Document.Export with { Compression = new QualityGoal((int)value) } }, transient); refresh();
        });
    }
}
