using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopTodo
{
    // Draw the complete stroke inside the allocated size. Border's layout-rounded
    // half-DIP margins can put its lower stroke on a clipped fractional pixel when
    // CardContent is scaled. This outline has one geometry for all four edges.
    public sealed class InputOutline : Border
    {
        internal Rect InkBounds { get; private set; }
        protected override void OnRender(DrawingContext dc)
        {
            double stroke = Math.Max(1.25, BorderThickness.Left);
            double inset = stroke / 2 + .75;
            InkBounds = new Rect(inset, inset, Math.Max(0, ActualWidth - inset * 2), Math.Max(0, ActualHeight - inset * 2));
            if (InkBounds.Width <= 0 || InkBounds.Height <= 0 || BorderBrush == null) return;
            dc.DrawRoundedRectangle(null, new Pen(BorderBrush, stroke), InkBounds, CornerRadius.TopLeft, CornerRadius.TopLeft);
        }
    }

    internal static class UiInteraction
    {
        private static bool hintsDisabled;
        internal static void DisableHoverHints(FrameworkElement root)
        {
            // Keep accessible names, but never open an operation tooltip on hover.
            // ToolTipOpening is a DIRECT routed event; a handler on the window alone
            // does not see its children's events (including popup/menu children).
            if (hintsDisabled) return; hintsDisabled = true;
            EventManager.RegisterClassHandler(typeof(FrameworkElement), ToolTipService.ToolTipOpeningEvent, new ToolTipEventHandler(delegate(object sender, ToolTipEventArgs e)
            {
                FrameworkElement element = sender as FrameworkElement;
                if (element != null && String.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(element)) && element.ToolTip is string)
                    System.Windows.Automation.AutomationProperties.SetName(element, (string)element.ToolTip);
                e.Handled = true;
            }), true);
        }
    }

    // Keep only the latest pointer sample until the next compositor frame. Never
    // throttle mouse-up: it must commit the actual final pointer position.
    internal sealed class UiFrameQueue
    {
        private readonly Action<Point> update;
        private Point point;
        internal bool IsQueued { get; private set; }
        internal int FrameUpdates { get; private set; }
        internal UiFrameQueue(Action<Point> update) { this.update = update; }
        internal void Queue(Point value)
        {
            point = value;
            if (IsQueued) return;
            IsQueued = true; CompositionTarget.Rendering += Render;
        }
        private void Render(object sender, EventArgs e)
        { Point value = point; Cancel(); FrameUpdates++; update(value); }
        internal void Flush(Point value) { Cancel(); update(value); }
        internal void Cancel()
        { if (!IsQueued) return; CompositionTarget.Rendering -= Render; IsQueued = false; }
    }

    // Complex pages are rasterized only for their short entrance/exit transform.
    // Restore the previous cache after the animation (or unload), so idle pages,
    // live switches and text stay sharp and do not retain a growing GPU cache.
    internal static class UiAnimation
    {
        private static readonly DependencyProperty LeaseProperty = DependencyProperty.RegisterAttached("AnimationCacheLease", typeof(CacheLease), typeof(UiAnimation));
        private sealed class CacheLease
        {
            internal readonly FrameworkElement Element;
            internal readonly CacheMode Previous;
            internal readonly DispatcherTimer Timer;
            internal bool Released;
            internal CacheLease(FrameworkElement element)
            {
                Element = element; Previous = element.CacheMode;
                Timer = new DispatcherTimer(DispatcherPriority.Background);
                Timer.Tick += delegate { Release(element); };
                element.Unloaded += Unloaded;
            }
            private void Unloaded(object sender, RoutedEventArgs e) { Release(Element); }
            internal void Detach() { Timer.Stop(); Element.Unloaded -= Unloaded; Released = true; }
            internal void Acquire()
            {
                if (Released || !Element.IsVisible || Element.ActualWidth <= 0 || Element.ActualHeight <= 0 || Previous != null) return;
                Window window = Window.GetWindow(Element); FrameworkElement root = window == null ? null : window.Content as FrameworkElement;
                if (root == null) return;
                Rect unit = Element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, 1, 1));
                double scale = Math.Max(1, Math.Max(unit.Width, unit.Height)); DpiScale dpi = VisualTreeHelper.GetDpi(Element);
                if (Element.ActualWidth * Element.ActualHeight * scale * scale * dpi.DpiScaleX * dpi.DpiScaleY > 4000000) return;
                Element.CacheMode = new BitmapCache { RenderAtScale = scale, SnapsToDevicePixels = false };
            }
        }
        internal static void CacheFor(FrameworkElement element, int milliseconds)
        {
            if (element == null || !element.IsVisible) return;
            CacheLease lease = element.GetValue(LeaseProperty) as CacheLease;
            if (lease == null)
            {
                lease = new CacheLease(element); element.SetValue(LeaseProperty, lease);
                lease.Acquire();
                if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
                    element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(lease.Acquire));
            }
            lease.Timer.Stop(); lease.Timer.Interval = TimeSpan.FromMilliseconds(milliseconds); lease.Timer.Start();
        }
        internal static void Release(FrameworkElement element)
        {
            if (element == null) return;
            CacheLease lease = element.GetValue(LeaseProperty) as CacheLease;
            if (lease == null) return;
            lease.Detach(); element.CacheMode = lease.Previous; element.ClearValue(LeaseProperty);
        }
    }

    // Every list uses the home gesture: live layout insertion, FLIP neighbour
    // motion and a same-size snapshot. Preview never mutates the model.
    internal static class UiDrag
    {
        internal sealed class SourceInfo
        { internal FrameworkElement Element, Visual; internal object Payload; internal string Format; internal bool Group; }
        private sealed class TargetInfo
        { internal FrameworkElement Visual; internal string Format; internal Action<object, bool> Drop; internal Func<object, bool> Accept; internal bool? FixedAfter; internal Func<object, bool, Slot> Place; }
        internal sealed class Slot
        {
            internal readonly StackPanel Panel; internal readonly int Index;
            internal Slot(StackPanel panel, int index) { Panel = panel; Index = index; }
        }
        private static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached("DragSource", typeof(SourceInfo), typeof(UiDrag));
        private static readonly DependencyProperty TargetsProperty = DependencyProperty.RegisterAttached("DragTargets", typeof(List<TargetInfo>), typeof(UiDrag));
        private static readonly DependencyProperty BoundaryProperty = DependencyProperty.RegisterAttached("GroupBoundary", typeof(string), typeof(UiDrag));
        private static readonly DependencyProperty ZoneProperty = DependencyProperty.RegisterAttached("ReorderZone", typeof(List<string>), typeof(UiDrag));
        private static Session active;
        private static Gesture pendingGesture;
        internal static bool IsActive { get { return active != null; } }
        internal static void GroupBoundary(FrameworkElement heading, string format) { heading.SetValue(BoundaryProperty, format); }
        internal static void ReorderZone(FrameworkElement viewport, string format)
        {
            List<string> formats = viewport.GetValue(ZoneProperty) as List<string>;
            if (formats == null) { formats = new List<string>(); viewport.SetValue(ZoneProperty, formats); }
            if (!formats.Contains(format)) formats.Add(format);
        }
        internal static bool IsSource(FrameworkElement element) { return element.GetValue(SourceProperty) is SourceInfo; }
        internal static bool IsBoundary(FrameworkElement element, string format)
        { return element != null && (string)element.GetValue(BoundaryProperty) == format; }
        internal static void Cancel(Window owner = null)
        {
            if (pendingGesture != null && (owner == null || pendingGesture.Owner == owner)) pendingGesture.Cancel();
            if (active != null && (owner == null || Window.GetWindow(active.Root) == owner)) active.Complete(false);
        }
        internal static void Source(FrameworkElement element, object payload, string format, FrameworkElement visual = null, bool group = false)
        {
            SourceInfo info = new SourceInfo { Element = element, Visual = visual ?? element, Payload = payload, Format = format, Group = group };
            element.SetValue(SourceProperty, info);
            element.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (active != null || e.ClickCount != 1 || InControl(e.OriginalSource as DependencyObject, element)) return;
                FrameworkElement root = Window.GetWindow(element) == null ? null : Window.GetWindow(element).Content as FrameworkElement;
                if (root != null)
                {
                    if (pendingGesture != null) pendingGesture.Cancel();
                    pendingGesture = new Gesture(root, info, e.GetPosition(root));
                }
            };
        }
        private sealed class Gesture
        {
            private readonly FrameworkElement root;
            private readonly SourceInfo source;
            private readonly Point press;
            private readonly Window window;
            private Session session;
            private bool ended;
            internal Window Owner { get { return window; } }
            internal Gesture(FrameworkElement root, SourceInfo source, Point press)
            {
                this.root = root; this.source = source; this.press = press;
                window = Window.GetWindow(root); if (window != null) window.Deactivated += Deactivated;
                root.PreviewMouseMove += Move; root.PreviewMouseLeftButtonUp += Release;
                root.PreviewKeyDown += OnKey; root.LostMouseCapture += Lost; source.Element.Unloaded += Unloaded;
            }
            private void Move(object sender, MouseEventArgs e)
            {
                if (e.LeftButton != MouseButtonState.Pressed) { End(false); return; }
                Point pointer = e.GetPosition(root);
                if (session == null)
                {
                    if (!TodoWindow.PastTaskDragStart(press, pointer)) return;
                    session = new Session(root, source, press);
                    if (!root.CaptureMouse()) { End(false); return; }
                    if (ended || session == null) return;
                }
                session.Queue(pointer); e.Handled = true;
            }
            private void Release(object sender, MouseButtonEventArgs e) { if (session != null) { session.Flush(e.GetPosition(root)); e.Handled = true; } End(true); }
            private void OnKey(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { End(false); e.Handled = true; } }
            private void Lost(object sender, MouseEventArgs e) { if (!ended) End(false); }
            private void Unloaded(object sender, RoutedEventArgs e)
            { if (!source.Element.IsVisible || Window.GetWindow(source.Element) != window) End(false); }
            private void Deactivated(object sender, EventArgs e) { End(false); }
            internal void Cancel() { End(false); }
            private void End(bool commit)
            {
                if (ended) return; ended = true;
                root.PreviewMouseMove -= Move; root.PreviewMouseLeftButtonUp -= Release;
                root.PreviewKeyDown -= OnKey; root.LostMouseCapture -= Lost; source.Element.Unloaded -= Unloaded;
                if (window != null) window.Deactivated -= Deactivated;
                if (pendingGesture == this) pendingGesture = null;
                if (root.IsMouseCaptured) root.ReleaseMouseCapture();
                if (session != null) session.Complete(commit);
            }
        }
        private static bool InControl(DependencyObject node, DependencyObject limit)
        {
            while (node != null && node != limit)
            {
                if (node is Button || node is CheckBox || node is TextBox || node is ComboBox) return true;
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            return false;
        }
        internal static void Target(FrameworkElement element, string format, Action<object, bool> drop, Func<object, bool> accept = null, bool? fixedAfter = null, Func<object, bool, Slot> place = null)
        {
            element.AllowDrop = true;
            List<TargetInfo> targets = element.GetValue(TargetsProperty) as List<TargetInfo>;
            if (targets == null) { targets = new List<TargetInfo>(); element.SetValue(TargetsProperty, targets); }
            targets.Add(new TargetInfo { Visual = element, Format = format, Drop = drop, Accept = accept, FixedAfter = fixedAfter, Place = place });
        }
        internal static Session BeginForTest(FrameworkElement element, Point press)
        {
            SourceInfo info = element.GetValue(SourceProperty) as SourceInfo;
            if (info == null) throw new ArgumentException("Not a registered drag source.");
            FrameworkElement root = Window.GetWindow(element).Content as FrameworkElement;
            return new Session(root, info, press);
        }
        private static IEnumerable<FrameworkElement> Elements(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                FrameworkElement element = child as FrameworkElement;
                if (element != null && element.IsVisible) yield return element;
                foreach (FrameworkElement descendant in Elements(child)) yield return descendant;
            }
        }
        private static List<FrameworkElement> Block(FrameworkElement visual, string format, bool group)
        {
            List<FrameworkElement> result = new List<FrameworkElement> { visual };
            StackPanel panel = VisualTreeHelper.GetParent(visual) as StackPanel;
            if (!group || panel == null) return result;
            int first = panel.Children.IndexOf(visual);
            for (int i = first + 1; i < panel.Children.Count; i++)
            {
                FrameworkElement next = panel.Children[i] as FrameworkElement;
                if (next == null || (string)next.GetValue(BoundaryProperty) == format) break;
                result.Add(next);
            }
            return result;
        }
        // Copy native glyph/geometry drawings once. Unlike a scaled bitmap, a
        // frozen GlyphRun retains the original font, em size and character shape.
        internal static DrawingGroup CaptureDrawing(Visual visual, bool child = false)
        {
            UIElement element = visual as UIElement;
            if (element != null && element.Visibility != Visibility.Visible) { DrawingGroup empty = new DrawingGroup(); empty.Freeze(); return empty; }
            DrawingGroup result = new DrawingGroup { Opacity = VisualTreeHelper.GetOpacity(visual) };
            Geometry clip = VisualTreeHelper.GetClip(visual);
            if (clip != null) result.ClipGeometry = clip.CloneCurrentValue();
            if (child)
            {
                Transform transform = VisualTreeHelper.GetTransform(visual); Vector offset = VisualTreeHelper.GetOffset(visual);
                Matrix matrix = transform == null ? Matrix.Identity : transform.Value; matrix.Translate(offset.X, offset.Y);
                result.Transform = new MatrixTransform(matrix);
            }
            DrawingGroup content = VisualTreeHelper.GetDrawing(visual);
            if (content != null) result.Children.Add(content.CloneCurrentValue());
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            {
                Visual next = VisualTreeHelper.GetChild(visual, i) as Visual;
                if (next != null) result.Children.Add(CaptureDrawing(next, true));
            }
            result.Freeze(); return result;
        }
        internal sealed class Session : IDisposable
        {
            internal FrameworkElement Root { get { return root; } }
            private readonly FrameworkElement root;
            private readonly SourceInfo source;
            private readonly List<FrameworkElement> sourceBlock;
            private readonly Dictionary<FrameworkElement, double> opacities = new Dictionary<FrameworkElement, double>();
            private readonly Dictionary<FrameworkElement, Transform> originals = new Dictionary<FrameworkElement, Transform>();
            private readonly Dictionary<FrameworkElement, TranslateTransform> offsets = new Dictionary<FrameworkElement, TranslateTransform>();
            private readonly Dictionary<StackPanel, UIElement[]> panelOrders = new Dictionary<StackPanel, UIElement[]>();
            private readonly List<TargetInfo> targets;
            private readonly Dictionary<TargetInfo, Rect> bounds = new Dictionary<TargetInfo, Rect>();
            private readonly Dictionary<TargetInfo, double> hitHeights = new Dictionary<TargetInfo, double>();
            private readonly FrameworkElement zone;
            private readonly ScrollViewer scroll;
            private readonly UiFrameQueue frameQueue;
            private Rect sourceBounds, zoneBounds;
            private readonly AdornerLayer layer;
            private readonly DragFeedback feedback;
            private readonly double ghostLeft, offsetY;
            private readonly DispatcherTimer scrollTimer;
            private readonly Cursor cursor;
            private Point pointer;
            private TargetInfo target;
            private bool after, finished;
            internal bool HasGhost { get { return layer != null && feedback != null; } }
            internal int AnimatedElements { get { return offsets.Values.Count(t => t.HasAnimatedProperties); } }
            internal double PreviewDisplacement { get { return offsets.Values.Sum(t => Math.Abs(t.Y)); } }
            internal Rect Marker { get { return feedback == null ? Rect.Empty : feedback.Marker; } }
            internal BitmapSource Snapshot { get { return feedback == null ? null : feedback.Snapshot; } }
            internal Size SnapshotDisplaySize { get { return feedback == null ? Size.Empty : feedback.ContentSize; } }
            internal DrawingGroup DrawingSnapshot { get { return feedback == null ? null : feedback.DrawingSnapshot; } }
            internal Session(FrameworkElement root, SourceInfo source, Point press)
            {
                if (active != null) active.Complete(false);
                root.UpdateLayout();
                this.root = root; this.source = source; pointer = press; active = this;
                sourceBlock = Block(source.Visual, source.Format, source.Group);
                sourceBounds = BlockBounds(sourceBlock);
                ghostLeft = sourceBounds.Left - 8; offsetY = press.Y - sourceBounds.Top;
                DependencyObject ancestor = source.Visual;
                while (ancestor != null)
                {
                    if (scroll == null && ancestor is ScrollViewer) scroll = (ScrollViewer)ancestor;
                    List<string> formats = ancestor.GetValue(ZoneProperty) as List<string>;
                    if (formats != null && formats.Contains(source.Format)) { zone = ancestor as FrameworkElement; break; }
                    ancestor = VisualTreeHelper.GetParent(ancestor);
                }
                targets = Elements(root).SelectMany(e => (e.GetValue(TargetsProperty) as List<TargetInfo>) ?? Enumerable.Empty<TargetInfo>())
                    .Where(t => t.Format == source.Format && !sourceBlock.Contains(t.Visual) && (t.Accept == null || t.Accept(source.Payload))).ToList();
                ReadBounds();
                frameQueue = new UiFrameQueue(Update);
                layer = AdornerLayer.GetAdornerLayer(root);
                if (layer != null) { feedback = new DragFeedback(root, sourceBlock, sourceBounds); layer.Add(feedback); }
                foreach (FrameworkElement visual in sourceBlock) { opacities[visual] = visual.Opacity; visual.Opacity = 0; }
                cursor = Mouse.OverrideCursor; Mouse.OverrideCursor = Cursors.SizeAll;
                scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
                scrollTimer.Tick += delegate { AutoScroll(); }; scrollTimer.Start();
                Update(press);
            }
            private Rect LayoutBounds(FrameworkElement visual)
            {
                Transform saved = visual.RenderTransform, original;
                if (originals.TryGetValue(visual, out original)) visual.RenderTransform = original;
                Rect result = visual.TransformToAncestor(root).TransformBounds(new Rect(visual.RenderSize));
                visual.RenderTransform = saved; return result;
            }
            private Rect BlockBounds(IList<FrameworkElement> block)
            {
                Rect rect = Rect.Empty;
                foreach (FrameworkElement visual in block) rect.Union(LayoutBounds(visual));
                return rect;
            }
            private void ReadBounds()
            {
                // Hit-test layout slots, not the interpolating FLIP transforms.
                Dictionary<FrameworkElement, Transform> saved = new Dictionary<FrameworkElement, Transform>();
                foreach (FrameworkElement element in originals.Keys) { saved[element] = element.RenderTransform; element.RenderTransform = originals[element]; }
                sourceBounds = BlockBounds(sourceBlock);
                zoneBounds = zone == null ? Rect.Empty : LayoutBounds(zone);
                foreach (TargetInfo info in targets)
                {
                    bool group = (string)info.Visual.GetValue(BoundaryProperty) == info.Format;
                    bounds[info] = BlockBounds(Block(info.Visual, info.Format, group));
                    hitHeights[info] = group ? LayoutBounds(info.Visual).Height : bounds[info].Height;
                }
                foreach (KeyValuePair<FrameworkElement, Transform> pair in saved) pair.Key.RenderTransform = pair.Value;
                targets.Sort(delegate(TargetInfo a, TargetInfo b) { return (bounds[a].Width * bounds[a].Height).CompareTo(bounds[b].Width * bounds[b].Height); });
            }
            internal void Queue(Point point) { if (!finished) { pointer = point; frameQueue.Queue(point); } }
            internal void Flush(Point point) { if (!finished) frameQueue.Flush(point); }
            internal bool HasQueuedFrame { get { return frameQueue.IsQueued; } }
            internal int FrameUpdates { get { return frameQueue.FrameUpdates; } }
            internal void Update(Point point)
            {
                if (finished) return; pointer = point;
                TargetInfo next = null; bool nextAfter = false;
                bool inZone = zone != null ? zoneBounds.Contains(point) : new Rect(0, 0, root.ActualWidth, root.ActualHeight).Contains(point);
                // The hidden row occupies its destination slot. Retain that slot
                // while over it; do not oscillate against a shifted neighbour.
                if (inZone && sourceBounds.Contains(point)) { next = target; nextAfter = after; }
                else if (inZone)
                {
                    foreach (TargetInfo candidate in targets)
                        if (bounds[candidate].Contains(point)) { next = candidate; break; }
                    if (next != null) nextAfter = next.FixedAfter ?? (point.Y > bounds[next].Top + hitHeights[next] / 2);
                    else if (zone != null)
                    {
                        double nearest = Double.PositiveInfinity;
                        foreach (TargetInfo candidate in targets)
                        {
                            Rect rect = bounds[candidate];
                            double top = Math.Abs(point.Y - rect.Top), bottom = Math.Abs(point.Y - rect.Bottom);
                            if (top < nearest) { nearest = top; next = candidate; nextAfter = candidate.FixedAfter ?? false; }
                            if (bottom < nearest) { nearest = bottom; next = candidate; nextAfter = candidate.FixedAfter ?? true; }
                        }
                    }
                }
                if (next != target || nextAfter != after)
                { target = next; after = nextAfter; PreviewInsertion(); }
                if (feedback != null)
                {
                    Rect viewport = zoneBounds.IsEmpty ? new Rect(0, 0, root.ActualWidth, root.ActualHeight) : zoneBounds;
                    double top = Math.Max(2, viewport.Top + 2), bottom = Math.Min(root.ActualHeight - 2, viewport.Bottom - 2);
                    feedback.SetViewport(new Rect(0, viewport.Top, root.ActualWidth, Math.Max(0, viewport.Height)));
                    feedback.Move(new Point(Math.Max(2, Math.Min(root.ActualWidth - feedback.GhostWidth - 2, ghostLeft)),
                        Math.Max(top, Math.Min(Math.Max(top, bottom - Math.Min(feedback.GhostHeight, viewport.Height - 4)), point.Y - offsetY - 4))));
                }
            }
            private Slot DestinationSlot()
            {
                if (target == null) return null;
                if (target.Place != null) return target.Place(source.Payload, after);
                StackPanel panel = VisualTreeHelper.GetParent(target.Visual) as StackPanel;
                if (panel == null) return null;
                List<FrameworkElement> block = Block(target.Visual, target.Format, (string)target.Visual.GetValue(BoundaryProperty) == target.Format);
                return new Slot(panel, panel.Children.IndexOf(after ? block.Last() : block[0]) + (after ? 1 : 0));
            }
            private void Remember(StackPanel panel)
            { if (!panelOrders.ContainsKey(panel)) panelOrders[panel] = panel.Children.Cast<UIElement>().ToArray(); }
            private Dictionary<FrameworkElement, double> Positions(StackPanel from, StackPanel to)
            {
                HashSet<StackPanel> panels = new HashSet<StackPanel>();
                foreach (StackPanel start in new[] { from, to })
                {
                    DependencyObject node = start;
                    while (node != null && node != zone)
                    {
                        StackPanel panel = node as StackPanel; if (panel != null) panels.Add(panel);
                        node = VisualTreeHelper.GetParent(node);
                    }
                }
                Dictionary<FrameworkElement, double> positions = new Dictionary<FrameworkElement, double>();
                foreach (StackPanel panel in panels)
                    foreach (FrameworkElement element in panel.Children.OfType<FrameworkElement>())
                        if (!sourceBlock.Contains(element)) positions[element] = VisualTreeHelper.GetOffset(element).Y + element.RenderTransform.Value.OffsetY;
                return positions;
            }
            private void ResetMotion()
            {
                foreach (TranslateTransform offset in offsets.Values) offset.BeginAnimation(TranslateTransform.YProperty, null);
                foreach (KeyValuePair<FrameworkElement, Transform> pair in originals) pair.Key.RenderTransform = pair.Value;
            }
            private void RestorePanels()
            {
                // Detach all affected panels first (a row may belong to another
                // panel during preview), then restore each exact initial sequence.
                foreach (StackPanel panel in panelOrders.Keys) panel.Children.Clear();
                foreach (KeyValuePair<StackPanel, UIElement[]> pair in panelOrders)
                    foreach (UIElement element in pair.Value) pair.Key.Children.Add(element);
                root.UpdateLayout();
            }
            private bool HasLayoutChange()
            { return panelOrders.Any(pair => !pair.Key.Children.Cast<UIElement>().SequenceEqual(pair.Value)); }
            private void PreviewInsertion()
            {
                Slot slot = DestinationSlot();
                StackPanel from = VisualTreeHelper.GetParent(sourceBlock[0]) as StackPanel;
                if (slot == null || slot.Panel == null || from == null)
                {
                    ResetMotion(); RestorePanels(); ReadBounds();
                    if (feedback != null) feedback.SetMarker(Rect.Empty); return;
                }
                int at = Math.Max(0, Math.Min(slot.Index, slot.Panel.Children.Count));
                if (from == slot.Panel)
                {
                    int current = from.Children.IndexOf(sourceBlock[0]);
                    int removedBefore = sourceBlock.Count(e => from.Children.IndexOf(e) < at);
                    at -= removedBefore;
                    if (at == current) { ShowMarker(); return; }
                }
                Dictionary<FrameworkElement, double> positions = Positions(from, slot.Panel);
                Remember(from); Remember(slot.Panel); ResetMotion();
                foreach (FrameworkElement element in sourceBlock) from.Children.Remove(element);
                for (int i = 0; i < sourceBlock.Count; i++) slot.Panel.Children.Insert(Math.Min(at + i, slot.Panel.Children.Count), sourceBlock[i]);
                root.UpdateLayout();
                foreach (KeyValuePair<FrameworkElement, double> pair in positions)
                {
                    FrameworkElement element = pair.Key;
                    double displacement = pair.Value - VisualTreeHelper.GetOffset(element).Y - element.RenderTransform.Value.OffsetY;
                    if (Math.Abs(displacement) < .1) continue;
                    Transform original; if (!originals.TryGetValue(element, out original)) { original = element.RenderTransform; originals[element] = original; }
                    TranslateTransform offset = new TranslateTransform(0, displacement); offsets[element] = offset;
                    element.RenderTransform = new TransformGroup { Children = { original, offset } };
                    // Keep moving labels as native glyphs; caching a row into a
                    // texture made its text change sharpness for the 170ms move.
                    offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(displacement, 0, TimeSpan.FromMilliseconds(170))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
                ReadBounds(); ShowMarker();
            }
            private void ShowMarker()
            {
                if (feedback == null) return;
                Rect rect = BlockBounds(sourceBlock);
                feedback.SetMarker(target == null ? Rect.Empty : new Rect(rect.Left + 3, rect.Top - 1, Math.Max(1, rect.Width - 6), 2));
            }
            private void AutoScroll()
            {
                if (finished || scroll == null || scroll.ScrollableHeight <= 0) return;
                Point local = root.TranslatePoint(pointer, scroll);
                double step = local.Y < 26 ? -12 : local.Y > scroll.ActualHeight - 26 ? 12 : 0;
                if (step == 0 || local.X < 0 || local.X > scroll.ActualWidth || local.Y < 0 || local.Y > scroll.ActualHeight) return;
                double previous = scroll.VerticalOffset; scroll.ScrollToVerticalOffset(previous + step); scroll.UpdateLayout();
                if (Math.Abs(previous - scroll.VerticalOffset) < .1) return;
                ReadBounds(); Update(pointer); ShowMarker();
            }
            internal void Complete(bool commit)
            {
                if (finished) return; finished = true;
                TargetInfo destination = target; bool placeAfter = after, changed = HasLayoutChange();
                scrollTimer.Stop(); frameQueue.Cancel(); ResetMotion(); RestorePanels();
                foreach (FrameworkElement element in originals.Keys) UiAnimation.Release(element);
                foreach (KeyValuePair<FrameworkElement, double> pair in opacities) pair.Key.Opacity = pair.Value;
                if (feedback != null && layer != null) layer.Remove(feedback);
                Mouse.OverrideCursor = cursor;
                if (active == this) active = null;
                if (commit && changed && destination != null) destination.Drop(source.Payload, placeAfter);
            }
            public void Dispose() { Complete(false); }
        }
        private sealed class DragFeedback : Adorner
        {
            private BitmapSource snapshot;
            internal DrawingGroup DrawingSnapshot { get; private set; }
            private readonly DpiScale dpi;
            private readonly Brush surface, accent;
            private readonly DrawingVisual ghost = new DrawingVisual(), insertion = new DrawingVisual();
            private readonly TranslateTransform position = new TranslateTransform();
            private readonly double height;
            private Rect viewport = Rect.Empty;
            internal BitmapSource Snapshot
            {
                get
                {
                    if (snapshot == null)
                    {
                        RenderTargetBitmap copy = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(ContentSize.Width * dpi.DpiScaleX)),
                            Math.Max(1, (int)Math.Ceiling(ContentSize.Height * dpi.DpiScaleY)), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                        DrawingVisual drawing = new DrawingVisual();
                        using (DrawingContext dc = drawing.RenderOpen()) dc.DrawDrawing(DrawingSnapshot);
                        copy.Render(drawing); copy.Freeze(); snapshot = copy;
                    }
                    return snapshot;
                }
            }
            internal Size ContentSize { get; private set; }
            internal double GhostWidth { get; private set; }
            internal double GhostHeight { get { return height; } }
            internal Rect Marker { get; private set; }
            internal DragFeedback(FrameworkElement root, IEnumerable<FrameworkElement> block, Rect bounds) : base(root)
            {
                ContentSize = bounds.Size;
                GhostWidth = bounds.Width + 16; height = bounds.Height + 8;
                position.X = bounds.X; position.Y = bounds.Y; Marker = Rect.Empty; IsHitTestVisible = false;
                AddVisualChild(insertion); AddVisualChild(ghost); ghost.Transform = position;
                FrameworkElement first = block.First();
                surface = first.TryFindResource("CardBrush") as Brush ?? first.TryFindResource("AdminCard") as Brush ?? Brushes.White;
                accent = first.TryFindResource("AccentBrush") as Brush ?? first.TryFindResource("AdminAccent") as Brush ?? Brushes.SteelBlue;
                dpi = VisualTreeHelper.GetDpi(root);
                DrawingSnapshot = new DrawingGroup();
                foreach (FrameworkElement visual in block)
                {
                    Matrix matrix = ((MatrixTransform)visual.TransformToAncestor(root)).Matrix;
                    matrix.Translate(-bounds.X, -bounds.Y);
                    DrawingSnapshot.Children.Add(new DrawingGroup { Transform = new MatrixTransform(matrix), Children = { CaptureDrawing(visual) } });
                }
                DrawingSnapshot.Freeze();
                // Record the frozen copy once; pointer movement updates only the
                // retained visual's transform, never redraws the full-root adorner.
                using (DrawingContext dc = ghost.RenderOpen())
                {
                    Rect rect = new Rect(0, 0, GhostWidth, height);
                    dc.DrawRoundedRectangle(surface, null, rect, 9, 9);
                    dc.PushClip(new RectangleGeometry(rect, 9, 9));
                    dc.PushTransform(new TranslateTransform(8, 4)); dc.DrawDrawing(DrawingSnapshot); dc.Pop(); dc.Pop();
                    Rect outline = rect; outline.Inflate(-1.5, -1.5);
                    dc.DrawRoundedRectangle(null, new Pen(accent, 1.5), outline, 8, 8);
                }
            }
            internal void Move(Point point) { position.X = point.X; position.Y = point.Y; }
            internal void SetViewport(Rect rect)
            { if (rect == viewport) return; viewport = rect; Clip = new RectangleGeometry(rect); }
            internal void SetMarker(Rect marker)
            {
                if (marker == Marker) return; Marker = marker;
                using (DrawingContext dc = insertion.RenderOpen())
                    if (!marker.IsEmpty) dc.DrawRoundedRectangle(accent, null, marker, 1, 1);
            }
            protected override int VisualChildrenCount { get { return 2; } }
            protected override Visual GetVisualChild(int index)
            { if (index == 0) return insertion; if (index == 1) return ghost; throw new ArgumentOutOfRangeException("index"); }
        }
    }

    // Text is measured independently of the clipped viewport. Animate only actual
    // overflow, and detach clocks when unloaded so closed menus consume no resources.
    public sealed class MarqueeLabel : Border
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(MarqueeLabel),
            new FrameworkPropertyMetadata("", delegate(DependencyObject d, DependencyPropertyChangedEventArgs e) { ((MarqueeLabel)d).UpdateText(); }));
        private readonly TextBlock label;
        private readonly TranslateTransform offset = new TranslateTransform();
        public string Text { get { return (string)GetValue(TextProperty); } set { SetValue(TextProperty, value); } }
        public MarqueeLabel()
        {
            ClipToBounds = true; VerticalAlignment = VerticalAlignment.Center;
            label = new TextBlock { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = offset, TextWrapping = TextWrapping.NoWrap };
            Child = label;
            Loaded += delegate { Restart(); }; SizeChanged += delegate { Restart(); };
            Unloaded += delegate { offset.BeginAnimation(TranslateTransform.XProperty, null); };
        }
        private void UpdateText() { if (label == null) return; label.Text = Text ?? ""; ToolTip = label.Text; Restart(); }
        private void Restart()
        {
            offset.BeginAnimation(TranslateTransform.XProperty, null); offset.X = 0;
            label.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            double overflow = label.DesiredSize.Width - ActualWidth;
            if (!IsLoaded || ActualWidth <= 0 || overflow <= 1) return;
            double travel = Math.Max(1.2, overflow / 26);
            DoubleAnimationUsingKeyFrames motion = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            motion.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            motion.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.8))));
            motion.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.8 + travel))));
            motion.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.3 + travel))));
            motion.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.3 + travel * 2))));
            offset.BeginAnimation(TranslateTransform.XProperty, motion);
        }
    }
}
