using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Globalization;
using System.ComponentModel;

namespace DesktopTodo
{
    // Centre scalar button captions by painted ink, not the font's line box.
    // Explicit panel content keeps its own typography and layout.
    public sealed class InkContentPresenter : ContentPresenter
    {
        protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
        {
            base.OnVisualChildrenChanged(visualAdded, visualRemoved);
            TextBlock label = visualAdded as TextBlock;
            if (label != null) UiGlyphs.CenterLabelInk(label);
        }
    }
    internal static class UiGlyphs
    {
        private static readonly DependencyProperty InkAlignedProperty = DependencyProperty.RegisterAttached(
            "InkAligned", typeof(bool), typeof(UiGlyphs), new PropertyMetadata(false));
        private static readonly DependencyPropertyDescriptor TextDescriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        internal const double Stroke = 1.3;
        internal const string OpenShackle = "M 5,7 L 5,4.5 C 5,0.5 11,0.5 11,4.5";
        internal const string ClosedShackle = "M 5,7 L 5,4.5 C 5,0.5 11,0.5 11,4.5 L 11,7";
        internal static readonly Geometry OpenShackleGeometry = FrozenGeometry(OpenShackle);
        internal static readonly Geometry ClosedShackleGeometry = FrozenGeometry(ClosedShackle);
        private static Geometry FrozenGeometry(string data)
        { Geometry geometry = Geometry.Parse(data); geometry.Freeze(); return geometry; }
        internal static FrameworkElement Plus()
        { return Icon("M 8,2 L 8,14 M 2,8 L 14,8"); }
        internal const string TrashData = "M 4,5.25 L 12,5.25 M 6.25,3.5 L 9.75,3.5 M 5.15,6.75 L 5.55,12.2 Q 5.6,12.8 6.25,12.8 L 9.75,12.8 Q 10.4,12.8 10.45,12.2 L 10.85,6.75";
        internal static FrameworkElement Trash()
        { return Icon(TrashData); }
        internal static FrameworkElement Close()
        { return Icon("M 3,3 L 13,13 M 13,3 L 3,13"); }
        // The stroke bounds, not the font's line box, are centred at (8, 8).
        internal const string ChevronRight = "M 5.5,3 L 10.5,8 L 5.5,13";
        internal const string ChevronDown = "M 3,5.5 L 8,10.5 L 13,5.5";
        internal static FrameworkElement Chevron(bool collapsed)
        { return Icon(collapsed ? ChevronRight : ChevronDown); }
        internal static Rect TextInk(TextBlock label)
        {
            FormattedText formatted = new FormattedText(label.Text ?? "", CultureInfo.CurrentUICulture, label.FlowDirection,
                new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch), label.FontSize,
                label.Foreground, VisualTreeHelper.GetDpi(label).PixelsPerDip);
            formatted.MaxTextWidth = System.Math.Max(1, label.ActualWidth);
            formatted.MaxLineCount = 1;
            formatted.Trimming = label.TextTrimming;
            return formatted.BuildGeometry(new Point()).Bounds;
        }
        internal static void CenterLabelInk(TextBlock label)
        {
            if ((bool)label.GetValue(InkAlignedProperty)) return;
            label.SetValue(InkAlignedProperty, true);
            string lastSignature = null;
            // CJK fonts have asymmetric ascent/descent space. Align the actual
            // painted letters with the geometry icons, not the font line box.
            System.Action update = delegate
            {
                if (label.ActualHeight <= 0 || label.ActualWidth <= 0 || label.Text.Length == 0) return;
                string signature = label.Text + ":" + label.ActualWidth + ":" + label.ActualHeight + ":" + label.FontFamily + ":" + label.FontSize + ":" + label.FontStyle + ":" + label.FontWeight + ":" + label.FontStretch + ":" + label.TextTrimming + ":" + label.FlowDirection + ":" + VisualTreeHelper.GetDpi(label).PixelsPerDip;
                if (signature == lastSignature) return;
                Rect ink = TextInk(label); if (ink.IsEmpty) return;
                double delta = label.ActualHeight / 2 - (ink.Top + ink.Height / 2);
                TranslateTransform offset = label.RenderTransform as TranslateTransform;
                if (offset == null || System.Math.Abs(offset.Y - delta) > .01) label.RenderTransform = new TranslateTransform(0, delta);
                lastSignature = signature;
            };
            bool observing = false;
            System.EventHandler changed = delegate { update(); };
            label.Loaded += delegate {
                if (!observing) { TextDescriptor.AddValueChanged(label, changed); observing = true; }
                lastSignature = null; update();
            };
            label.Unloaded += delegate {
                if (observing) { TextDescriptor.RemoveValueChanged(label, changed); observing = false; }
            };
            label.SizeChanged += delegate { update(); };
            if (label.IsLoaded) { TextDescriptor.AddValueChanged(label, changed); observing = true; update(); }
        }
        private static FrameworkElement Icon(string data)
        {
            Grid glyph = new Grid { Width = 16, Height = 16, IsHitTestVisible = false, UseLayoutRounding = false, SnapsToDevicePixels = false };
            Path path = new Path { Data = Geometry.Parse(data), Width = 16, Height = 16, Stretch = Stretch.None,
                StrokeThickness = Stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            path.SetBinding(Path.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1) });
            glyph.Children.Add(path); return glyph;
        }
    }
}
