using System.Collections.Immutable;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using Citrus.Conversion.Editing.Media;

namespace Citrus.Windows.Editors.Media;

public readonly record struct MediaInterval(Guid Id,double Start,double End);

/// <summary>Direct waveform trim handles and a separate draggable bleep lane. Coordinates are timeline seconds.</summary>
public sealed class MediaRangeView : FrameworkElement
{
    public ImmutableArray<WaveformBucket> Buckets {get;set;}=[];
    public ImmutableArray<MediaInterval> Regions {get;set;}=[];
    public double Duration {get;set;}=1;
    public double SelectionStart {get;set;}
    public double SelectionEnd {get;set;}=1;
    public bool BleepLane {get;set;}
    public Guid? SelectedRegion {get;private set;}
    public event Action<double,double,bool>? TrimChanged;
    public event Action<ImmutableArray<MediaInterval>,bool>? RegionsChanged;
    public event Action<Guid?>? SelectionChanged;
    private bool dragging;
    private int handle;
    private double origin,start,end;
    private Guid regionId;
    private bool creating;
    public MediaRangeView() {Focusable=true;MinHeight=120;Cursor=Cursors.Hand;}
    private double X(double time)=>Math.Clamp(time/Math.Max(.000001,Duration),0,1)*Math.Max(1,ActualWidth-16)+8;
    private double Time(Point point)=>Math.Clamp((point.X-8)/Math.Max(1,ActualWidth-16),0,1)*Duration;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var orange=SystemParameters.HighContrast?SystemColors.HighlightBrush:new SolidColorBrush(Color.FromRgb(255,85,0));
        double top=BleepLane?48:12,bottom=Math.Max(top+1,ActualHeight-24),mid=(top+bottom)/2,half=(bottom-top)/2;
        dc.DrawRoundedRectangle(SystemParameters.HighContrast?SystemColors.WindowBrush:new SolidColorBrush(Color.FromRgb(247,248,248)),null,new(0,0,ActualWidth,ActualHeight),10,10);
        if(BleepLane)
        {
            Text(dc,"Bleep",new(8,6));Text(dc,"Audio",new(8,46));
            foreach(var region in Regions)
            {
                double left=X(region.Start),right=X(region.End);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(80,255,85,0)),new(orange,SelectedRegion==region.Id?2:1),new(left,22,Math.Max(2,right-left),20),4,4);
                dc.DrawRectangle(orange,null,new(left-3,20,6,24));dc.DrawRectangle(orange,null,new(right-3,20,6,24));
            }
        }
        if(!Buckets.IsDefaultOrEmpty)
        {
            var pen=new Pen(orange,Math.Max(1,(ActualWidth-16)/Buckets.Length));
            for(int i=0;i<Buckets.Length;i++)
            {
                double x=8+(ActualWidth-16)*(i+.5)/Buckets.Length;
                dc.DrawLine(pen,new(x,mid-Buckets[i].Maximum*half),new(x,mid-Buckets[i].Minimum*half));
            }
        }
        if(!BleepLane)
        {
            double left=X(SelectionStart),right=X(SelectionEnd);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120,255,255,255)),null,new(8,top,Math.Max(0,left-8),bottom-top));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120,255,255,255)),null,new(right,top,Math.Max(0,ActualWidth-8-right),bottom-top));
            dc.DrawRectangle(null,new Pen(orange,2),new(left,top,Math.Max(1,right-left),bottom-top));
            foreach(double edge in new[]{left,right}) {dc.DrawRoundedRectangle(orange,null,new(edge-5,top,10,bottom-top),3,3);dc.DrawLine(new(Brushes.White,2),new(edge,mid-9),new(edge,mid+9));}
        }
        Text(dc,"0:00.00",new(8,ActualHeight-20));Text(dc,Timecode(Duration),new(Math.Max(8,ActualWidth-64),ActualHeight-20));
    }
    private void Text(DrawingContext dc,string text,Point at)=>dc.DrawText(new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,SystemParameters.HighContrast?SystemColors.WindowTextBrush:Brushes.DimGray,VisualTreeHelper.GetDpi(this).PixelsPerDip),at);
    public static string Timecode(double seconds)=>TimeSpan.FromSeconds(Math.Max(0,seconds)).ToString(seconds>=3600?@"h\:mm\:ss\.ff":@"m\:ss\.ff",CultureInfo.InvariantCulture);
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);if(Duration<=0)return;
        Focus();Point point=e.GetPosition(this);origin=Time(point);
        if(BleepLane)
        {
            if(point.Y>46)return;
            var selected=Regions.LastOrDefault(r=>point.X>=X(r.Start)-8&&point.X<=X(r.End)+8);
            if(selected.Id==Guid.Empty)
            {
                creating=true;regionId=Guid.NewGuid();start=Math.Min(origin,Math.Max(0,Duration-Math.Min(.001,Duration)));end=Math.Min(Duration,start+.01);handle=1;
                Regions=Regions.Add(new(regionId,start,end));
            }
            else {creating=false;regionId=selected.Id;start=selected.Start;end=selected.End;handle=Math.Abs(point.X-X(start))<9?0:Math.Abs(point.X-X(end))<9?1:2;}
            SelectRegion(regionId);
        }
        else {start=SelectionStart;end=SelectionEnd;handle=Math.Abs(point.X-X(start))<=Math.Abs(point.X-X(end))?0:1;}
        dragging=CaptureMouse();e.Handled=true;InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);if(!dragging)return;
        double point=Time(e.GetPosition(this)),minimum=Math.Min(.001,Duration);
        double a=handle==0?Math.Min(point,end-minimum):start,b=handle==1?Math.Max(point,start+minimum):end;
        if(BleepLane&&creating){a=Math.Min(origin,point);b=Math.Min(Duration,Math.Max(a+minimum,Math.Max(origin,point)));}
        if(handle==2) {double delta=Math.Clamp(point-origin,-start,Duration-end);a=start+delta;b=end+delta;}
        if(BleepLane) {Regions=Regions.Select(r=>r.Id==regionId?new MediaInterval(r.Id,a,b):r).ToImmutableArray();RegionsChanged?.Invoke(Regions,false);}
        else {SelectionStart=a;SelectionEnd=b;TrimChanged?.Invoke(a,b,false);}
        InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if(!dragging)return;dragging=false;ReleaseMouseCapture();
        if(BleepLane) RegionsChanged?.Invoke(Regions,true);else TrimChanged?.Invoke(SelectionStart,SelectionEnd,true);
        e.Handled=true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) {base.OnLostMouseCapture(e);if(!dragging)return;dragging=false;if(BleepLane) RegionsChanged?.Invoke(Regions,true);else TrimChanged?.Invoke(SelectionStart,SelectionEnd,true);}
    public void RemoveSelected()
    {
        if(SelectedRegion is not {} id)return;Regions=Regions.Where(r=>r.Id!=id).ToImmutableArray();SelectRegion(Regions.IsEmpty?null:Regions[0].Id);RegionsChanged?.Invoke(Regions,true);InvalidateVisual();
    }
    public void SelectRegion(Guid? id)
    {
        if(id is {} value&&!Regions.Any(r=>r.Id==value))id=null;
        if(SelectedRegion==id)return;SelectedRegion=id;SelectionChanged?.Invoke(id);InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(BleepLane&&e.Key is Key.Delete or Key.Back) {RemoveSelected();e.Handled=true;}
        else if(!BleepLane&&e.Key is Key.Left or Key.Right)
        {
            double delta=e.Key==Key.Left?-.01:.01;
            if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))SelectionEnd=Math.Clamp(SelectionEnd+delta,SelectionStart+.001,Duration);
            else SelectionStart=Math.Clamp(SelectionStart+delta,0,SelectionEnd-.001);
            TrimChanged?.Invoke(SelectionStart,SelectionEnd,true);InvalidateVisual();e.Handled=true;
        }
    }
}
