using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTodo
{
    // A frozen copy follows the pointer while the original row is fully hidden.
    internal sealed class TaskDragAdorner : Adorner
    {
        private readonly BitmapSource snapshot;
        internal BitmapSource Snapshot { get { return snapshot; } }
        private const double ContentInset = 8;
        internal static double HorizontalContentInset { get { return ContentInset; } }
        private readonly double x, width, height;
        private readonly Brush surface, outline;
        private readonly DrawingVisual ghost = new DrawingVisual();
        private readonly TranslateTransform position = new TranslateTransform();

        internal TaskDragAdorner(StackPanel panel, Grid row, double x, double y) : base(panel)
        {
            panel.UpdateLayout(); row.UpdateLayout();
            this.x = x; position.Y = y; width = row.ActualWidth; height = row.ActualHeight;
            DpiScale dpi = VisualTreeHelper.GetDpi(row);
            Window window = Window.GetWindow(row); FrameworkElement root = window == null ? null : window.Content as FrameworkElement;
            double scale = 1;
            if (root != null)
            {
                Rect unit = row.TransformToAncestor(root).TransformBounds(new Rect(0, 0, 1, 1));
                scale = Math.Max(1, Math.Max(unit.Width, unit.Height));
            }
            // The row's local DIPs omit CardContent's LayoutTransform. Capture at
            // that ancestor scale too, otherwise enlarged cards upscale a small
            // bitmap while dragging, even though their stationary text is sharp.
            double dpiX = dpi.DpiScaleX * scale, dpiY = dpi.DpiScaleY * scale;
            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(width * dpiX)),
                Math.Max(1, (int)Math.Ceiling(height * dpiY)),
                96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
            DrawingVisual copy = new DrawingVisual();
            using (DrawingContext dc = copy.RenderOpen())
                dc.DrawRectangle(new VisualBrush(row) { Stretch = Stretch.Fill }, null, new Rect(0, 0, width, height));
            bitmap.Render(copy); bitmap.Freeze(); snapshot = bitmap;
            surface = row.TryFindResource("CardBrush") as Brush ?? Brushes.White;
            outline = row.TryFindResource("AccentBrush") as Brush ?? Brushes.SteelBlue;
            IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            AddVisualChild(ghost); ghost.Transform = position;
            using (DrawingContext dc = ghost.RenderOpen()) DrawGhost(dc);
        }

        internal void MoveTo(double top) { position.Y = top; }
        protected override int VisualChildrenCount { get { return 1; } }
        protected override Visual GetVisualChild(int index)
        { if (index == 0) return ghost; throw new ArgumentOutOfRangeException("index"); }
        private void DrawGhost(DrawingContext dc)
        {
            // Padding enlarges the frame, never squeezes the captured text.
            Rect bounds = new Rect(x - ContentInset, 0, width + ContentInset * 2, height);
            dc.DrawRoundedRectangle(surface, null, bounds, 9, 9);
            dc.PushClip(new RectangleGeometry(bounds, 9, 9));
            dc.DrawImage(snapshot, new Rect(x, 0, width, height));
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(outline, 1.5),
                new Rect(bounds.Left + 1.5, 1.5, Math.Max(0, bounds.Width - 3), Math.Max(0, height - 3)), 8, 8);
        }
    }
}
