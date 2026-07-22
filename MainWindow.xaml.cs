using System;
using Microsoft.UI;                 // Win32Interop
using Microsoft.UI.Xaml;            // Window, RoutedEventArgs, UIElement
using Microsoft.UI.Xaml.Input;      // PointerRoutedEventArgs, PointerEventHandler
using Windows.UI;                   // Color  (WPF used System.Windows.Media)
using Gigasoft.ProEssentials;
using Gigasoft.ProEssentials.Enums;

namespace LogLogDragMeasureWinUI
{
    /// <summary>
    /// ProEssentials WinUI — Log-Log Axes &amp; Quick Annotation Drag Measure
    ///
    /// Demonstrates two advanced PesgoWinUI features:
    ///
    /// FEATURE 1 — LOG-LOG AXES (Example 110 pattern):
    ///   Both X and Y axes use ScaleControl.Log. ProEssentials automatically
    ///   selects intelligent logarithmic grid numbers as the user zooms in,
    ///   maintaining clean decade-based tick marks at any zoom level. Data
    ///   spans several decades (100 to ~850M) to make the log behavior visible.
    ///
    /// FEATURE 2 — QUICK ANNOTATION DRAG MEASUREMENT TOOL:
    ///   Left-click and drag to draw a live measurement rectangle over the chart.
    ///   While dragging, six graph annotations are rebuilt each PointerMoved and
    ///   rendered via the Quick Annotation path (CacheBmp2 overlay) — no full
    ///   chart rebuild occurs, making the overlay smooth and responsive.
    ///
    ///   The overlay shows:
    ///     - Semi-transparent filled round-rect covering the selected region
    ///     - White border around the region
    ///     - Green X-delta label centered horizontally at the top edge
    ///     - Green Y-delta label centered vertically at the right edge
    ///
    ///   QUICK ANNOTATION MECHANISM:
    ///   Normal graph annotations are baked into the chart's primary cached
    ///   bitmap (CacheBmp). Changing them triggers a full ResetImage rebuild,
    ///   which is too slow for per-PointerMoved updates.
    ///   Quick annotations use a separate render layer drawn on top of CacheBmp2
    ///   (a secondary double-buffer). Only the quick annotation draw commands
    ///   are re-executed each frame; the underlying chart image is untouched.
    ///
    ///   To mark an annotation as "quick", negate its type with this formula:
    ///     Graph.Type[i] = ((int)GraphAnnotationType.SomeType + 1) * -1;
    ///   Any positive GraphAnnotationType value can be made quick this way.
    ///
    ///   In WinUI, PesgoWinUI automatically manages the CacheBmp2 secondary buffer
    ///   and improved cursor rendering — no explicit setup required for those.
    ///   The only requirement is that MouseWheelFunction.HorizontalVerticalZoom
    ///   needs ScrollingHorzZoom and ScrollingVertZoom set (see Step 6).
    ///
    /// CURSOR SETUP:
    ///   CursorMode.DataCross draws a crosshair snapped to the nearest data point.
    ///   CursorColor and VertLineType/HorzLineType customize its appearance.
    ///
    /// LOG-SCALE GEOMETRY:
    ///   On log axes, the visual midpoint of a region is the geometric mean,
    ///   not the arithmetic mean. Delta labels are positioned at:
    ///     centeredX = 10 ^ ((log10(x1) + log10(x2)) / 2)
    ///   This places the label visually centered within the selected region.
    ///
    /// Controls:
    ///   Mouse wheel       — zoom both axes (log-log aware, smooth)
    ///   Left-click drag   — drag measurement tool
    ///   Right-click       — context menu (export, print, customize)
    ///
    /// WPF -> WinUI plumbing changes (the chart code itself is unchanged):
    ///   System.Windows.Media.Color   -> Windows.UI.Color        (same FromArgb signature)
    ///   System.Windows.Point / Rect  -> Windows.Foundation.Point / Rect
    ///   MouseDown/Move/Up            -> PointerPressed/Moved/Released (PointerRoutedEventArgs)
    ///   Pesgo1.Refresh()             -> Pesgo1.UpdateLayout()
    ///   Window Height/Width in XAML  -> SizeAndCenterWindow() in the constructor
    ///   Window Closing (cancellable) -> Window Closed
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        // ── Drag measurement state ────────────────────────────────────────
        bool   _dragging     = false;
        double _dragStartX   = 0;
        double _dragStartY   = 0;

        public MainWindow()
        {
            InitializeComponent();

            // WinUI Window has no XAML Height/Width — size it here instead.
            // (The WPF twin also set MinHeight=500 / MinWidth=700; WinUI's Window
            //  exposes no minimum-size property, so that constraint is dropped.)
            SizeAndCenterWindow(1100, 750);

            // WPF's Closing (cancellable) becomes WinUI's Closed.
            this.Closed += Window_Closed;

            // PesgoWinUI forwards pointer input to the native engine in its own
            // PointerPressed/PointerMoved/PointerReleased handlers and then sets
            // e.Handled = true. Attaching with AddHandler(..., handledEventsToo: true)
            // guarantees these handlers still run — and because the control's handler
            // runs first, PeUserInterface.Cursor.LastMouseMove is already up to date
            // by the time we read it, exactly as it was under WPF.
            Pesgo1.AddHandler(UIElement.PointerPressedEvent,
                              new PointerEventHandler(Pesgo1_MouseDown), true);
            Pesgo1.AddHandler(UIElement.PointerMovedEvent,
                              new PointerEventHandler(Pesgo1_MouseMove), true);
            Pesgo1.AddHandler(UIElement.PointerReleasedEvent,
                              new PointerEventHandler(Pesgo1_MouseUp), true);
        }

        // -----------------------------------------------------------------------
        // Pesgo1_Loaded — chart initialization
        //
        // Always initialize ProEssentials in the control's Loaded event.
        // WinUI's Window has no Loaded event at all, so this is the only place
        // to do it — and it is the right one: the control is fully initialized.
        // -----------------------------------------------------------------------
        void Pesgo1_Loaded(object sender, RoutedEventArgs e)
        {
            // =======================================================================
            // Step 1 — Data: 2 subsets × 500 points spanning multiple decades
            //
            // Data is intentionally wide-ranging so both axes require several decades
            // to display. This makes the log scale restructuring during zoom visible:
            // zoom in and watch ProEssentials choose clean decade-based grid lines.
            // =======================================================================
            Pesgo1.PeData.Subsets = 2;
            Pesgo1.PeData.Points  = 500;

            var rand = new Random(42);

            for (int s = 0; s <= 1; s++)
            {
                for (int p = 0; p <= 499; p++)
                {
                    // X spans roughly 1,700 to 850,000 (three decades)
                    Pesgo1.PeData.X[s, p] = (p + 1) * 1700f;

                    // Y spans roughly 100 to ~425M depending on subset and point
                    // The (subset+1) multiplier separates the two series visually
                    Pesgo1.PeData.Y[s, p] = (float)(
                        ((p + 1) * 100)
                        + ((rand.NextDouble() * 850) * (p + 1) * (s + 1)));
                }
            }

            // =======================================================================
            // Step 2 — Log-Log axis scales
            //
            // ScaleControl.Log applies logarithmic scaling to the axis. ProEssentials
            // selects grid numbers intelligently at any zoom level, always landing on
            // clean powers of ten and their subdivisions. Zoom with the mouse wheel
            // to see the grid restructure as you move through decades.
            //
            // ManualMinX/MaxX/MinY/MaxY are NOT set here — ProEssentials auto-scales
            // to the data range on first render and keeps them current as the user
            // zooms. The mouse handlers read these properties at drag time to clamp
            // the cursor position to the visible chart extents.
            // =======================================================================
            Pesgo1.PeGrid.Configure.XAxisScaleControl = ScaleControl.Log;
            Pesgo1.PeGrid.Configure.YAxisScaleControl = ScaleControl.Log;

            // =======================================================================
            // Step 3 — Plotting method and point appearance
            // =======================================================================
            Pesgo1.PePlot.Method = SGraphPlottingMethod.Point;
            Pesgo1.PePlot.MarkDataPoints = false;

            // DotSolid gives clean, small filled circles suited to dense scatter data
            Pesgo1.PeLegend.SubsetPointTypes[0] = PointType.DotSolid;
            Pesgo1.PeLegend.SubsetPointTypes[1] = PointType.DotSolid;

            Pesgo1.PeColor.SubsetColors[0] = Windows.UI.Color.FromArgb(255,   0, 229, 229); // cyan
            Pesgo1.PeColor.SubsetColors[1] = Windows.UI.Color.FromArgb(255, 255, 210,   0); // gold

            // =======================================================================
            // Step 4 — Quick Annotation infrastructure setup
            //
            // In WinUI, PesgoWinUI automatically enables the secondary double-buffer
            // (CacheBmp2) and improved cursor rendering (DrawCursorToCache /
            // ImprovedCursor). CacheBmp2 is not exposed on the WinUI interface at all.
            // Quick annotations work without any explicit setup beyond
            // RenderEngine = Direct2D (set in Step 7 below).
            // =======================================================================

            // =======================================================================
            // Step 5 — DataCross cursor
            //
            // CursorMode.DataCross draws a crosshair that snaps to the nearest data
            // point when MouseCursorControl = true. CursorColor and line types
            // customize its appearance. Red dashes work well against the dark theme.
            // =======================================================================
            Pesgo1.PeUserInterface.Cursor.Mode                   = CursorMode.DataCross;
            Pesgo1.PeUserInterface.Cursor.MouseCursorControl      = true;
            Pesgo1.PeUserInterface.Cursor.MouseCursorControlClosestPoint = false;
            Pesgo1.PeUserInterface.HotSpot.Data                   = true;

            Pesgo1.PeUserInterface.Cursor.CursorColor    = Windows.UI.Color.FromArgb(255, 255, 48, 48); // red
            Pesgo1.PeUserInterface.Cursor.VertLineType   = LineAnnotationType.Dash;
            Pesgo1.PeUserInterface.Cursor.HorzLineType   = LineAnnotationType.Dash;

            Pesgo1.PeUserInterface.Cursor.PromptTracking = true;
            Pesgo1.PeUserInterface.Cursor.PromptLocation = CursorPromptLocation.ToolTip;
            Pesgo1.PeUserInterface.Cursor.PromptStyle    = CursorPromptStyle.XYValues;

            // =======================================================================
            // Step 6 — Zoom configuration
            //
            // AllowZooming.None prevents the standard left-drag zoom box — the left
            // mouse button is reserved for the drag measurement tool instead.
            //
            // MouseWheelFunction.HorizontalVerticalZoom lets the scroll wheel zoom
            // both axes simultaneously. On log axes, zooming feels natural because
            // ProEssentials keeps the grid anchored at decade boundaries.
            //
            // MouseDraggingX/Y = false prevents middle-mouse pan from interfering
            // with the drag measurement tool's PointerMoved logic.
            // =======================================================================
            Pesgo1.PeUserInterface.Allow.Zooming  = AllowZooming.None;
            Pesgo1.PeUserInterface.Allow.ZoomStyle = ZoomStyle.Ro2Not;

            // MouseWheelFunction.HorizontalVerticalZoom requires both scrolling
            // zoom flags to be explicitly set — without them the wheel cannot zoom
            // out and only one axis responds.
            Pesgo1.PeUserInterface.Scrollbar.ScrollingHorzZoom        = true;
            Pesgo1.PeUserInterface.Scrollbar.ScrollingVertZoom        = true;
            Pesgo1.PeUserInterface.Scrollbar.MouseWheelFunction       = MouseWheelFunction.HorizontalVerticalZoom;
            Pesgo1.PeUserInterface.Scrollbar.MouseWheelZoomSmoothness = 8;
            Pesgo1.PeUserInterface.Scrollbar.MouseDraggingX           = false;
            Pesgo1.PeUserInterface.Scrollbar.MouseDraggingY           = false;

            // =======================================================================
            // Step 7 — Style
            // =======================================================================
            Pesgo1.PeColor.BitmapGradientMode = true;
            Pesgo1.PeColor.QuickStyle         = QuickStyle.DarkNoBorder;
            Pesgo1.PeColor.GridBold           = true;

            Pesgo1.PeGrid.InFront     = true;
            Pesgo1.PeGrid.LineControl = GridLineControl.Both;
            Pesgo1.PeGrid.Style       = GridStyle.Dot;

            Pesgo1.PeFont.FontSize       = Gigasoft.ProEssentials.Enums.FontSize.Large;
            Pesgo1.PeFont.Fixed          = true;
            Pesgo1.PeFont.MainTitle.Bold = true;

            Pesgo1.PeConfigure.PrepareImages     = true;
            Pesgo1.PeConfigure.AntiAliasGraphics = true;
            Pesgo1.PeConfigure.ImageAdjustLeft   = 25;

            // =======================================================================
            // Step 8 — Titles
            // =======================================================================
            Pesgo1.PeString.MainTitle  = "Log-Log Axes — Intelligent Scale on Zoom";
            Pesgo1.PeString.SubTitle   = "Mouse wheel zooms both axes  ·  Left-drag draws measurement tool";
            Pesgo1.PeString.XAxisLabel = "Log X";
            Pesgo1.PeString.YAxisLabel = "Log Y";

            // =======================================================================
            // Step 9 — ReinitializeResetImage
            //
            // Always call as the final step. ManualMinX/MaxX/MinY/MaxY are only
            // valid after the chart has rendered its first image — the mouse handlers
            // read them at drag time, never during initialization.
            // =======================================================================
            Pesgo1.PeFunction.ReinitializeResetImage();
            Pesgo1.Invalidate();
        }

        // -----------------------------------------------------------------------
        // ConvPixelToGraphClamped — helper: pixel → data coordinates, clamped
        //
        // Converts a WinUI pixel point to data-unit graph coordinates using
        // ConvPixelToGraph, then clamps the result to the current visible axis
        // extents (ManualMinX/MaxX/MinY/MaxY).
        //
        // IMPORTANT: ManualMinX/MaxX/MinY/MaxY are only valid after the chart
        // has rendered at least one frame. Never call this during initialization.
        //
        // The clamping prevents drag coordinates from escaping the chart area
        // when the cursor is near or beyond the axis boundaries.
        // -----------------------------------------------------------------------
        void ConvPixelToGraphClamped(Windows.Foundation.Point pt,
                                     out double fX, out double fY)
        {
            int nA = 0;
            int nX = (int)pt.X;
            int nY = (int)pt.Y;
            fX = 0; fY = 0;

            Pesgo1.PeFunction.ConvPixelToGraph(ref nA, ref nX, ref nY,
                                                ref fX, ref fY,
                                                false, false, false);

            // Clamp to visible axis extents
            fX = Math.Max(Pesgo1.PeGrid.Configure.ManualMinX,
                 Math.Min(Pesgo1.PeGrid.Configure.ManualMaxX, fX));
            fY = Math.Max(Pesgo1.PeGrid.Configure.ManualMinY,
                 Math.Min(Pesgo1.PeGrid.Configure.ManualMaxY, fY));
        }

        // -----------------------------------------------------------------------
        // Pesgo1_MouseDown — capture drag start point  (WinUI: PointerPressed)
        //
        // Records the drag start position in data coordinates and suppresses
        // the default XY tooltip so it doesn't compete with the measurement overlay.
        // -----------------------------------------------------------------------
        private void Pesgo1_MouseDown(object sender, PointerRoutedEventArgs e)
        {
            Windows.Foundation.Point pt = Pesgo1.PeUserInterface.Cursor.LastMouseMove;
            Windows.Foundation.Rect  r  = Pesgo1.PeFunction.GetRectGraph();

            // Only start a drag if the click is inside the chart grid area
            if (!r.Contains(pt))
                return;

            _dragging = true;

            ConvPixelToGraphClamped(pt, out _dragStartX, out _dragStartY);

            // Suppress the XY tooltip while the measurement overlay is active
            Pesgo1.PeUserInterface.Cursor.PromptStyle = CursorPromptStyle.None;
        }

        // -----------------------------------------------------------------------
        // Pesgo1_MouseMove — rebuild and display the quick annotation overlay
        //                    (WinUI: PointerMoved)
        //
        // Called on every pointer move. When dragging, builds six quick annotations
        // that form the measurement rectangle and delta labels, then triggers a
        // fast overlay redraw via ShowingQuickAnnotations = true.
        //
        // QUICK ANNOTATION TYPE FORMULA:
        //   Quick (overlay-only) version of any type:
        //     Graph.Type[i] = ((int)GraphAnnotationType.SomeType + 1) * -1;
        //   The negative sign signals the quick-draw path. The chart renders
        //   only negative-type annotations on the CacheBmp2 layer — the primary
        //   cached image (CacheBmp) is never touched.
        //
        // ANNOTATION LAYOUT (6 entries, indices 0–5):
        //   [0] TopLeft corner — defines the top-left bound of the rectangle
        //   [1] BottomRight corner — defines the bottom-right bound
        //   [2] RoundRectFill — semi-transparent filled background
        //   [3] RoundRectMedium — white border outline
        //   [4] NoSymbol + "|c" text — X-delta label, centered horizontally
        //   [5] NoSymbol + "|D" text — Y-delta label, centered vertically (right edge)
        //
        // LOG-SCALE CENTERING:
        //   On log axes the visual center of a span is the geometric mean, not the
        //   arithmetic mean. Positioning labels at the arithmetic midpoint would
        //   place them visually off-center. The geometric mean is computed as:
        //     center = 10 ^ ((log10(a) + log10(b)) / 2)
        //
        // TEXT JUSTIFICATION CODES:
        //   |c  — centered horizontally, bottom anchor (text above the point)
        //   |D  — centered vertically on right side (rotated 90°, right edge)
        // -----------------------------------------------------------------------
        private void Pesgo1_MouseMove(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging)
                return;

            Windows.Foundation.Point pt = Pesgo1.PeUserInterface.Cursor.LastMouseMove;
            Windows.Foundation.Rect  r  = Pesgo1.PeFunction.GetRectGraph();

            if (!r.Contains(pt))
                return;

            ConvPixelToGraphClamped(pt, out double fX, out double fY);

            // Normalise so dLeft < dRight and dTop > dBottom (Y increases upward)
            double dLeft   = Math.Min(_dragStartX, fX);
            double dRight  = Math.Max(_dragStartX, fX);
            double dTop    = Math.Max(_dragStartY, fY);
            double dBottom = Math.Min(_dragStartY, fY);

            // ── [0] TopLeft corner of bounding rectangle ──────────────────────
            Pesgo1.PeAnnotation.Graph.X[0]    = dLeft;
            Pesgo1.PeAnnotation.Graph.Y[0]    = dTop;
            Pesgo1.PeAnnotation.Graph.Type[0] = ((int)GraphAnnotationType.TopLeft + 1) * -1;

            // ── [1] BottomRight corner ────────────────────────────────────────
            Pesgo1.PeAnnotation.Graph.X[1]    = dRight;
            Pesgo1.PeAnnotation.Graph.Y[1]    = dBottom;
            Pesgo1.PeAnnotation.Graph.Type[1] = ((int)GraphAnnotationType.BottomRight + 1) * -1;

            // ── [2] Semi-transparent filled background ────────────────────────
            // X and Y repeat the BottomRight corner — the rectangle is defined
            // by the preceding TopLeft/BottomRight pair.
            Pesgo1.PeAnnotation.Graph.X[2]             = dRight;
            Pesgo1.PeAnnotation.Graph.Y[2]             = dBottom;
            Pesgo1.PeAnnotation.Graph.Type[2]          = ((int)GraphAnnotationType.RoundRectFill + 1) * -1;
            Pesgo1.PeAnnotation.Graph.Color[2]         = Windows.UI.Color.FromArgb( 70, 198, 198, 198);
            Pesgo1.PeAnnotation.Graph.Text[2]          = "";
            Pesgo1.PeAnnotation.Graph.GradientStyle[2] = (int)PlotGradientStyle.RadialBottomRight;
            Pesgo1.PeAnnotation.Graph.GradientColor[2] = Windows.UI.Color.FromArgb(170, 255, 255, 255);

            // ── [3] White border outline ──────────────────────────────────────
            Pesgo1.PeAnnotation.Graph.X[3]    = dRight;
            Pesgo1.PeAnnotation.Graph.Y[3]    = dBottom;
            Pesgo1.PeAnnotation.Graph.Type[3] = ((int)GraphAnnotationType.RoundRectMedium + 1) * -1;
            Pesgo1.PeAnnotation.Graph.Color[3] = Windows.UI.Color.FromArgb(255, 255, 255, 255);
            Pesgo1.PeAnnotation.Graph.Text[3]  = "";

            // ── [4] X-delta label — centered horizontally at the top edge ─────
            // Geometric mean places the label at the visual center on a log axis.
            double centeredXLog = (Math.Log10(fX) + Math.Log10(_dragStartX)) / 2.0;
            double centeredX    = Math.Pow(10.0, centeredXLog);
            double deltaX       = Math.Abs(fX - _dragStartX);

            Pesgo1.PeAnnotation.Graph.X[4]     = centeredX;
            Pesgo1.PeAnnotation.Graph.Y[4]      = dTop;
            Pesgo1.PeAnnotation.Graph.Type[4]   = ((int)GraphAnnotationType.NoSymbol + 1) * -1;
            Pesgo1.PeAnnotation.Graph.Color[4]  = Windows.UI.Color.FromArgb(255, 0, 255, 0);
            Pesgo1.PeAnnotation.Graph.Text[4]   = $"|c<~ {deltaX:0.#} ~>"; // |c = centered horizontal

            // ── [5] Y-delta label — centered vertically at the right edge ─────
            // |D = "Centered on right side, vertical text" — rotated 90°, right edge.
            double centeredYLog = (Math.Log10(fY) + Math.Log10(_dragStartY)) / 2.0;
            double centeredY    = Math.Pow(10.0, centeredYLog);
            double deltaY       = Math.Abs(fY - _dragStartY);

            Pesgo1.PeAnnotation.Graph.X[5]    = dRight;
            Pesgo1.PeAnnotation.Graph.Y[5]    = centeredY;
            Pesgo1.PeAnnotation.Graph.Type[5] = ((int)GraphAnnotationType.NoSymbol + 1) * -1;
            Pesgo1.PeAnnotation.Graph.Color[5] = Windows.UI.Color.FromArgb(255, 0, 255, 0);
            Pesgo1.PeAnnotation.Graph.Text[5]  = $"|D<~ {deltaY:0.#} ~>"; // |D = centered vertical right

            Pesgo1.PeAnnotation.Graph.TextSize = 120;

            // Enable annotation visibility switches
            Pesgo1.PeAnnotation.Graph.Show = true;
            Pesgo1.PeAnnotation.Show       = true;

            // ShowingQuickAnnotations = true signals the engine to:
            //   1. Blit the primary cached image (CacheBmp) to screen
            //   2. Render ONLY the negative-type annotations on top (CacheBmp2)
            // This avoids any rebuild of the underlying chart image.
            Pesgo1.PeAnnotation.ShowingQuickAnnotations = true;
            Pesgo1.Invalidate();
            Pesgo1.UpdateLayout();   // WPF called Refresh() here
        }

        // -----------------------------------------------------------------------
        // Pesgo1_MouseUp — clear the quick annotation overlay
        //                  (WinUI: PointerReleased)
        //
        // HidingQuickAnnotations = true signals the engine to remove the overlay
        // and revert to normal rendering. CacheBmp2 remains allocated — it is
        // reused the next time the user starts a drag.
        // -----------------------------------------------------------------------
        private void Pesgo1_MouseUp(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging)
                return;

            _dragging = false;

            // Clear the quick annotation overlay
            Pesgo1.PeAnnotation.ShowingQuickAnnotations = false;
            Pesgo1.PeAnnotation.HidingQuickAnnotations  = true;

            // Restore the XY tooltip
            Pesgo1.PeUserInterface.Cursor.PromptStyle = CursorPromptStyle.XYValues;

            Pesgo1.Invalidate();
            Pesgo1.UpdateLayout();   // WPF called Refresh() here
        }

        // -----------------------------------------------------------------------
        // Window_Closed  (WPF: Window_Closing with CancelEventArgs)
        // -----------------------------------------------------------------------
        private void Window_Closed(object sender, WindowEventArgs e)
        {
        }

        // -----------------------------------------------------------------------
        // Window sizing. WinUI has no XAML Height/Width on Window, and AppWindow.Resize
        // is unreliable on this SDK, so size and centre with raw Win32 MoveWindow.
        // -----------------------------------------------------------------------
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        private void SizeAndCenterWindow(int w, int h)
        {
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            int x = area.WorkArea.X + (area.WorkArea.Width - w) / 2;
            int y = area.WorkArea.Y + (area.WorkArea.Height - h) / 2;
            MoveWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id), x, y, w, h, true);
        }
    }
}
