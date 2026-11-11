using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcbInspectionViewer.Core.Api.Dtos;
using PcbInspectionViewer.Core.Geometry;

namespace PcbInspectionViewer.App.Controls;

public sealed class DefectOverlayCanvas : FrameworkElement
{
    private static readonly SolidColorBrush CanvasBackgroundBrush = CreateFrozenBrush(15, 23, 42); // #0F172A
    private static readonly SolidColorBrush PlaceholderTextBrush = CreateFrozenBrush(148, 163, 184); // #94A3B8
    private static readonly Typeface LabelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly ConcurrentDictionary<string, (SolidColorBrush Stroke, SolidColorBrush Fill, Pen Pen)> PaletteCache = new(StringComparer.OrdinalIgnoreCase);

    public static readonly DependencyProperty ImageDataUriProperty =
        DependencyProperty.Register(
            nameof(ImageDataUri),
            typeof(string),
            typeof(DefectOverlayCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnImageDataUriChanged));

    public static readonly DependencyProperty DetectionsProperty =
        DependencyProperty.Register(
            nameof(Detections),
            typeof(IEnumerable<BoundingBoxDetectionDto>),
            typeof(DefectOverlayCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDetectionsChanged));

    public static readonly DependencyProperty ShowBoundingBoxesProperty =
        DependencyProperty.Register(
            nameof(ShowBoundingBoxes),
            typeof(bool),
            typeof(DefectOverlayCanvas),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ZoomFactorProperty =
        DependencyProperty.Register(
            nameof(ZoomFactor),
            typeof(double),
            typeof(DefectOverlayCanvas),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty PanXProperty =
        DependencyProperty.Register(
            nameof(PanX),
            typeof(double),
            typeof(DefectOverlayCanvas),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty PanYProperty =
        DependencyProperty.Register(
            nameof(PanY),
            typeof(double),
            typeof(DefectOverlayCanvas),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private BitmapSource? _cachedBitmap;
    private bool _isDragging;
    private Point _lastMousePosition;

    public DefectOverlayCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public string? ImageDataUri
    {
        get => (string?)GetValue(ImageDataUriProperty);
        set => SetValue(ImageDataUriProperty, value);
    }

    public IEnumerable<BoundingBoxDetectionDto>? Detections
    {
        get => (IEnumerable<BoundingBoxDetectionDto>?)GetValue(DetectionsProperty);
        set => SetValue(DetectionsProperty, value);
    }

    public bool ShowBoundingBoxes
    {
        get => (bool)GetValue(ShowBoundingBoxesProperty);
        set => SetValue(ShowBoundingBoxesProperty, value);
    }

    public double ZoomFactor
    {
        get => (double)GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    public double PanX
    {
        get => (double)GetValue(PanXProperty);
        set => SetValue(PanXProperty, value);
    }

    public double PanY
    {
        get => (double)GetValue(PanYProperty);
        set => SetValue(PanYProperty, value);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        double factor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
        ZoomFactor = Math.Clamp(ZoomFactor * factor, 0.25, 16.0);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _isDragging = true;
        _lastMousePosition = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging)
        {
            return;
        }

        Point current = e.GetPosition(this);
        Vector delta = current - _lastMousePosition;
        _lastMousePosition = current;

        PanX += delta.X;
        PanY += delta.Y;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double viewportW = Math.Max(1.0, ActualWidth);
        double viewportH = Math.Max(1.0, ActualHeight);
        var fullRect = new Rect(0, 0, viewportW, viewportH);
        dc.DrawRectangle(CanvasBackgroundBrush, null, fullRect);

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (_cachedBitmap is null)
        {
            var placeholder = new FormattedText(
                "Select or drop a Golden Template and Test PCB image, then press Inspect (F5)",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                14,
                PlaceholderTextBrush,
                pixelsPerDip);

            dc.DrawText(
                placeholder,
                new Point(
                    Math.Max(12, (viewportW - placeholder.Width) / 2.0),
                    Math.Max(12, (viewportH - placeholder.Height) / 2.0)));
            return;
        }

        OverlayBoardViewport boardViewport = OverlayGeometryCalculator.ProjectBoardViewport(
            viewportW,
            viewportH,
            ZoomFactor,
            PanX,
            PanY);

        var boardRect = new Rect(
            boardViewport.Left,
            boardViewport.Top,
            boardViewport.Width,
            boardViewport.Height);

        dc.DrawImage(_cachedBitmap, boardRect);

        if (!ShowBoundingBoxes || Detections is null)
        {
            return;
        }

        foreach (BoundingBoxDetectionDto box in Detections)
        {
            OverlayBoxProjection proj = OverlayGeometryCalculator.ProjectBox(
                box,
                viewportW,
                viewportH,
                ZoomFactor,
                PanX,
                PanY);

            (SolidColorBrush strokeBrush, SolidColorBrush fillBrush, Pen pen) = GetOrCreateBoxPalette(proj.ColorHex);

            var rect = new Rect(proj.Left, proj.Top, proj.Width, proj.Height);
            dc.DrawRectangle(fillBrush, pen, rect);

            var labelText = new FormattedText(
                proj.LabelText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                11.5,
                Brushes.White,
                pixelsPerDip);

            double labelPadX = 5;
            double labelPadY = 2;
            double labelW = labelText.Width + (labelPadX * 2);
            double labelH = labelText.Height + (labelPadY * 2);
            double labelTop = proj.Top >= labelH ? proj.Top - labelH : proj.Top;

            var labelRect = new Rect(proj.Left, labelTop, labelW, labelH);
            dc.DrawRectangle(strokeBrush, null, labelRect);
            dc.DrawText(labelText, new Point(proj.Left + labelPadX, labelTop + labelPadY));
        }
    }

    private static (SolidColorBrush Stroke, SolidColorBrush Fill, Pen Pen) GetOrCreateBoxPalette(string colorHex) =>
        PaletteCache.GetOrAdd(colorHex, static hex =>
        {
            Color boxColor = (Color)ColorConverter.ConvertFromString(hex);
            var strokeBrush = new SolidColorBrush(boxColor);
            strokeBrush.Freeze();

            var fillBrush = new SolidColorBrush(Color.FromArgb(40, boxColor.R, boxColor.G, boxColor.B));
            fillBrush.Freeze();

            var pen = new Pen(strokeBrush, 2.0);
            pen.Freeze();

            return (strokeBrush, fillBrush, pen);
        });

    private static void OnImageDataUriChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DefectOverlayCanvas canvas)
        {
            canvas._cachedBitmap = DecodeDataUri(e.NewValue as string);
            canvas.InvalidateVisual();
        }
    }

    private static void OnDetectionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DefectOverlayCanvas canvas)
        {
            return;
        }

        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= canvas.OnDetectionsCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += canvas.OnDetectionsCollectionChanged;
        }

        canvas.InvalidateVisual();
    }

    private void OnDetectionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        InvalidateVisual();

    private static BitmapSource? DecodeDataUri(string? dataUri)
    {
        if (string.IsNullOrWhiteSpace(dataUri))
        {
            return null;
        }

        try
        {
            int commaIdx = dataUri.IndexOf(',', StringComparison.Ordinal);
            string base64Payload = commaIdx >= 0 ? dataUri[(commaIdx + 1)..] : dataUri;
            byte[] bytes = Convert.FromBase64String(base64Payload);

            using var ms = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
