using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DesktopTodo
{
    internal static class UiGlyphs
    {
        internal const double Stroke = 1.8;
        internal const string OpenShackle = "M 5,7 L 5,4.5 C 5,0.5 11,0.5 11,4.5";
        internal const string ClosedShackle = "M 5,7 L 5,4.5 C 5,0.5 11,0.5 11,4.5 L 11,7";
        internal const string TrashData = "M 2.5,4.5 L 13.5,4.5 M 6,4.5 L 6,2 L 10,2 L 10,4.5 M 4,4.5 L 4.5,14 L 11.5,14 L 12,4.5 M 6.5,7 L 6.5,11.5 M 9.5,7 L 9.5,11.5";
        internal static FrameworkElement Trash()
        { return Icon(TrashData); }
        internal static FrameworkElement Close()
        { return Icon("M 3,3 L 13,13 M 13,3 L 3,13"); }
        private static FrameworkElement Icon(string data)
        {
            Grid glyph = new Grid { Width = 16, Height = 16, IsHitTestVisible = false, UseLayoutRounding = false, SnapsToDevicePixels = false };
            Path path = new Path { Data = Geometry.Parse(data), Width = 16, Height = 16, Stretch = Stretch.None,
                StrokeThickness = Stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            path.SetBinding(Path.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
            glyph.Children.Add(path); return glyph;
        }
    }
}
