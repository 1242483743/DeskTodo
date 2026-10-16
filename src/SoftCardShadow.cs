using System.Windows;
using System.Windows.Media;

namespace DesktopTodo
{
    // Rounded vector falloff avoids a software blur of a whole large bitmap.
    // The parent clips this outside the card so translucent content and text
    // cannot be darkened or blurred by the shadow.
    public sealed class SoftCardShadow : FrameworkElement
    {
        private static readonly double[] alpha = { .024, .014, .010, .007, .005, .004, .003 };
        private static readonly SolidColorBrush[] brushes = CreateBrushes();
        private static SolidColorBrush[] CreateBrushes()
        {
            SolidColorBrush[] result = new SolidColorBrush[alpha.Length];
            for (int i = 0; i < result.Length; i++)
            { result[i] = new SolidColorBrush(Color.FromArgb((byte)System.Math.Round(255 * alpha[i]), 0, 0, 0)); result[i].Freeze(); }
            return result;
        }
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            for (int i = brushes.Length - 1; i >= 0; i--)
            {
                double spread = i + .5;
                drawing.DrawRoundedRectangle(brushes[i], null,
                    new Rect(-spread, 2 - spread, ActualWidth + spread * 2, ActualHeight + spread * 2), 18 + spread, 18 + spread);
            }
        }
    }
}
