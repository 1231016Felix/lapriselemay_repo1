using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Cursors = System.Windows.Input.Cursors;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using Canvas = System.Windows.Controls.Canvas;

namespace QuickLauncher.Views;

public partial class AnnotationWindow : Window
{
    private enum AnnotationTool { Pen, Highlight, Rectangle, Arrow, Text, Eraser }

    // ═══ État ═══
    private AnnotationTool _currentTool = AnnotationTool.Pen;
    private Color _currentColor = Color.FromRgb(0xFF, 0x3B, 0x30); // Rouge
    private double _strokeThickness = 3;
    private readonly Bitmap _originalBitmap;
    private bool _isSaved;

    // ═══ Dessin de formes ═══
    private bool _isDrawingShape;
    private Point _shapeStart;
    private System.Windows.Shapes.Shape? _previewShape;

    // ═══ Undo/Redo ═══
    private interface IAnnotationAction { void Undo(); void Redo(); }

    private class StrokeAction(InkCanvas canvas, StrokeCollection strokes) : IAnnotationAction
    {
        public void Undo() => canvas.Strokes.Remove(strokes);
        public void Redo() => canvas.Strokes.Add(strokes);
    }

    private class StrokeEraseAction(InkCanvas canvas, StrokeCollection strokes) : IAnnotationAction
    {
        public void Undo() => canvas.Strokes.Add(strokes);
        public void Redo() => canvas.Strokes.Remove(strokes);
    }

    private class ShapeAction(Canvas canvas, UIElement element) : IAnnotationAction
    {
        public void Undo() => canvas.Children.Remove(element);
        public void Redo() => canvas.Children.Add(element);
    }

    private readonly List<IAnnotationAction> _undoStack = [];
    private readonly List<IAnnotationAction> _redoStack = [];

    // ═══ Constructeur ═══
    public string? SavedFilePath { get; private set; }

    public AnnotationWindow(Bitmap capturedBitmap)
    {
        _originalBitmap = capturedBitmap;
        InitializeComponent();

        // Afficher l'image capturée
        var source = BitmapToSource(capturedBitmap);
        CapturedImage.Source = source;
        CapturedImage.Width = source.PixelWidth;
        CapturedImage.Height = source.PixelHeight;

        // Dimensionner les canvas
        DrawingCanvas.Width = source.PixelWidth;
        DrawingCanvas.Height = source.PixelHeight;
        ShapeCanvas.Width = source.PixelWidth;
        ShapeCanvas.Height = source.PixelHeight;

        // Configurer l'InkCanvas
        UpdateInkCanvasSettings();
        SelectColor(ColorRed);

        Loaded += (_, _) => Activate();
    }

    // ═══ Outils ═══
    private void Tool_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        _currentTool = Enum.Parse<AnnotationTool>(tag);
        UpdateInkCanvasSettings();
    }

    private void UpdateInkCanvasSettings()
    {
        if (DrawingCanvas == null || ShapeCanvas == null) return;
        
        switch (_currentTool)
        {
            case AnnotationTool.Pen:
                DrawingCanvas.EditingMode = InkCanvasEditingMode.Ink;
                DrawingCanvas.IsHitTestVisible = true;
                ShapeCanvas.IsHitTestVisible = false;
                DrawingCanvas.DefaultDrawingAttributes = new DrawingAttributes
                {
                    Color = _currentColor,
                    Width = _strokeThickness,
                    Height = _strokeThickness,
                    FitToCurve = true,
                    IsHighlighter = false
                };
                DrawingCanvas.Cursor = Cursors.Cross;
                break;

            case AnnotationTool.Highlight:
                DrawingCanvas.EditingMode = InkCanvasEditingMode.Ink;
                DrawingCanvas.IsHitTestVisible = true;
                ShapeCanvas.IsHitTestVisible = false;
                DrawingCanvas.DefaultDrawingAttributes = new DrawingAttributes
                {
                    Color = Color.FromArgb(100, _currentColor.R, _currentColor.G, _currentColor.B),
                    Width = Math.Max(_strokeThickness * 4, 16),
                    Height = Math.Max(_strokeThickness * 4, 16),
                    FitToCurve = false,
                    IsHighlighter = true
                };
                DrawingCanvas.Cursor = Cursors.Hand;
                break;

            case AnnotationTool.Eraser:
                DrawingCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
                DrawingCanvas.IsHitTestVisible = true;
                ShapeCanvas.IsHitTestVisible = false;
                DrawingCanvas.Cursor = Cursors.Hand;
                break;

            case AnnotationTool.Rectangle:
            case AnnotationTool.Arrow:
            case AnnotationTool.Text:
                DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
                DrawingCanvas.IsHitTestVisible = false;
                ShapeCanvas.IsHitTestVisible = true;
                ShapeCanvas.Cursor = _currentTool == AnnotationTool.Text ? Cursors.IBeam : Cursors.Cross;
                break;
        }
    }

    // ═══ Couleurs & épaisseur ═══
    private void Color_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string hex) return;
        _currentColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        SelectColor(border);
        UpdateInkCanvasSettings();
    }

    private void SelectColor(Border selected)
    {
        // Réinitialiser toutes les bordures
        foreach (var child in ((WrapPanel)selected.Parent).Children)
        {
            if (child is Border b && b.Style == (Style)Resources["ColorSwatch"])
                b.BorderBrush = System.Windows.Media.Brushes.Transparent;
        }
        selected.BorderBrush = System.Windows.Media.Brushes.White;
    }

    private void StrokeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _strokeThickness = Math.Round(e.NewValue);
        if (StrokeLabel != null)
            StrokeLabel.Text = _strokeThickness.ToString("0");
        UpdateInkCanvasSettings();
    }

    // ═══ InkCanvas events ═══
    private void DrawingCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        var action = new StrokeAction(DrawingCanvas, new StrokeCollection { e.Stroke });
        _undoStack.Add(action);
        _redoStack.Clear();
    }

    private void DrawingCanvas_StrokeErased(object sender, RoutedEventArgs e)
    {
        // Quand des strokes sont effacés, on ne peut pas facilement les tracker individuellement
        // On simplifie en ne supportant pas l'undo d'effacement de strokes
    }

    // ═══ Dessin de formes (Rectangle, Flèche) ═══
    private void ShapeCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool == AnnotationTool.Text)
        {
            PlaceTextBox(e.GetPosition(ShapeCanvas));
            return;
        }

        _shapeStart = e.GetPosition(ShapeCanvas);
        _isDrawingShape = true;
        ShapeCanvas.CaptureMouse();

        // Créer la forme de prévisualisation
        var brush = new SolidColorBrush(_currentColor);
        if (_currentTool == AnnotationTool.Rectangle)
        {
            _previewShape = new System.Windows.Shapes.Rectangle
            {
                Stroke = brush,
                StrokeThickness = _strokeThickness,
                Fill = System.Windows.Media.Brushes.Transparent
            };
        }
        else if (_currentTool == AnnotationTool.Arrow)
        {
            _previewShape = new Line
            {
                Stroke = brush,
                StrokeThickness = _strokeThickness,
                X1 = _shapeStart.X,
                Y1 = _shapeStart.Y,
                StrokeEndLineCap = PenLineCap.Triangle
            };
        }

        if (_previewShape != null)
            ShapeCanvas.Children.Add(_previewShape);
    }

    private void ShapeCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawingShape || _previewShape == null) return;
        var pos = e.GetPosition(ShapeCanvas);

        if (_previewShape is System.Windows.Shapes.Rectangle rect)
        {
            var x = Math.Min(_shapeStart.X, pos.X);
            var y = Math.Min(_shapeStart.Y, pos.Y);
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            rect.Width = Math.Abs(pos.X - _shapeStart.X);
            rect.Height = Math.Abs(pos.Y - _shapeStart.Y);
        }
        else if (_previewShape is Line line)
        {
            line.X2 = pos.X;
            line.Y2 = pos.Y;
        }
    }

    private void ShapeCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawingShape || _previewShape == null) return;
        _isDrawingShape = false;
        ShapeCanvas.ReleaseMouseCapture();

        var pos = e.GetPosition(ShapeCanvas);
        var dist = Math.Sqrt(Math.Pow(pos.X - _shapeStart.X, 2) + Math.Pow(pos.Y - _shapeStart.Y, 2));

        if (dist < 3)
        {
            // Trop petit, annuler
            ShapeCanvas.Children.Remove(_previewShape);
        }
        else
        {
            // Ajouter la pointe de flèche
            if (_currentTool == AnnotationTool.Arrow && _previewShape is Line line)
            {
                ShapeCanvas.Children.Remove(_previewShape);
                var arrowGroup = CreateArrow(
                    new Point(line.X1, line.Y1),
                    new Point(line.X2, line.Y2),
                    new SolidColorBrush(_currentColor),
                    _strokeThickness);
                ShapeCanvas.Children.Add(arrowGroup);
                var action = new ShapeAction(ShapeCanvas, arrowGroup);
                _undoStack.Add(action);
                _redoStack.Clear();
                _previewShape = null;
                return;
            }

            var shapeAction = new ShapeAction(ShapeCanvas, _previewShape);
            _undoStack.Add(shapeAction);
            _redoStack.Clear();
        }

        _previewShape = null;
    }

    // ═══ Helpers formes ═══
    private static Canvas CreateArrow(Point from, Point to, SolidColorBrush brush, double thickness)
    {
        var canvas = new Canvas();

        // Ligne principale
        canvas.Children.Add(new Line
        {
            X1 = from.X, Y1 = from.Y,
            X2 = to.X, Y2 = to.Y,
            Stroke = brush,
            StrokeThickness = thickness
        });

        // Pointe de flèche
        var angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        var headLen = Math.Max(thickness * 4, 14);
        var headAngle = Math.PI / 7;

        var p1 = new Point(
            to.X - headLen * Math.Cos(angle - headAngle),
            to.Y - headLen * Math.Sin(angle - headAngle));
        var p2 = new Point(
            to.X - headLen * Math.Cos(angle + headAngle),
            to.Y - headLen * Math.Sin(angle + headAngle));

        var head = new Polygon
        {
            Points = [to, p1, p2],
            Fill = brush
        };
        canvas.Children.Add(head);
        return canvas;
    }

    // ═══ Texte ═══
    private void PlaceTextBox(Point position)
    {
        var textBox = new TextBox
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Foreground = new SolidColorBrush(_currentColor),
            FontSize = Math.Max(_strokeThickness * 5, 16),
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, _currentColor.R, _currentColor.G, _currentColor.B)),
            MinWidth = 100,
            Padding = new Thickness(4, 2, 4, 2),
            AcceptsReturn = true
        };

        Canvas.SetLeft(textBox, position.X);
        Canvas.SetTop(textBox, position.Y);
        ShapeCanvas.Children.Add(textBox);

        // Focus immédiat
        textBox.Loaded += (_, _) => { textBox.Focus(); };

        // Quand on perd le focus, convertir en TextBlock figé
        textBox.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                ShapeCanvas.Children.Remove(textBox);
                return;
            }

            var textBlock = new TextBlock
            {
                Text = textBox.Text,
                Foreground = textBox.Foreground,
                FontSize = textBox.FontSize,
                FontWeight = textBox.FontWeight
            };

            Canvas.SetLeft(textBlock, Canvas.GetLeft(textBox));
            Canvas.SetTop(textBlock, Canvas.GetTop(textBox));

            ShapeCanvas.Children.Remove(textBox);
            ShapeCanvas.Children.Add(textBlock);

            var action = new ShapeAction(ShapeCanvas, textBlock);
            _undoStack.Add(action);
            _redoStack.Clear();
        };
    }

    // ═══ Undo / Redo ═══
    private void Undo_Click(object sender, RoutedEventArgs e) => PerformUndo();
    private void Redo_Click(object sender, RoutedEventArgs e) => PerformRedo();

    private void PerformUndo()
    {
        if (_undoStack.Count == 0) return;
        var action = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        action.Undo();
        _redoStack.Add(action);
    }

    private void PerformRedo()
    {
        if (_redoStack.Count == 0) return;
        var action = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        action.Redo();
        _undoStack.Add(action);
    }

    // ═══ Sauvegarde & Copie ═══
    private void Save_Click(object sender, RoutedEventArgs e) => SaveAnnotated();
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyToClipboard();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private RenderTargetBitmap RenderAnnotated()
    {
        // Rendre tout le CanvasHost (image + annotations) en bitmap
        var width = (int)CanvasHost.ActualWidth;
        var height = (int)CanvasHost.ActualHeight;

        // Utiliser les dimensions en pixels de l'image originale
        var dpi = 96.0;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
            dpi = 96.0 * source.CompositionTarget.TransformToDevice.M11;

        var rtb = new RenderTargetBitmap(
            (int)(width * dpi / 96.0),
            (int)(height * dpi / 96.0),
            dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(CanvasHost);
        return rtb;
    }

    private void SaveAnnotated()
    {
        var screenshotsFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Screenshots");
        Directory.CreateDirectory(screenshotsFolder);

        var fileName = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
        var filePath = System.IO.Path.Combine(screenshotsFolder, fileName);

        var rtb = RenderAnnotated();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using (var stream = File.Create(filePath))
            encoder.Save(stream);

        // Copier aussi dans le presse-papier
        Clipboard.SetImage(rtb);

        SavedFilePath = filePath;
        _isSaved = true;
        Close();
    }

    private void CopyToClipboard()
    {
        var rtb = RenderAnnotated();
        Clipboard.SetImage(rtb);
        
        // Feedback visuel temporaire
        var originalContent = BtnSave.Content;
        BtnSave.Content = "✅ Copié!";
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            BtnSave.Content = originalContent;
            timer.Stop();
        };
        timer.Start();
    }

    // ═══ Raccourcis clavier ═══
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Z:
                    PerformUndo();
                    e.Handled = true;
                    break;
                case Key.Y:
                    PerformRedo();
                    e.Handled = true;
                    break;
                case Key.S:
                    SaveAnnotated();
                    e.Handled = true;
                    break;
                case Key.C:
                    CopyToClipboard();
                    e.Handled = true;
                    break;
            }
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isSaved && (_undoStack.Count > 0 || ShapeCanvas.Children.Count > 0))
        {
            var result = MessageBox.Show(
                "Voulez-vous sauvegarder la capture annotée avant de fermer?",
                "Capture non sauvegardée",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    e.Cancel = true;
                    SaveAnnotated();
                    break;
                case MessageBoxResult.Cancel:
                    e.Cancel = true;
                    break;
            }
        }

        _originalBitmap.Dispose();
    }

    // ═══ Utilitaire ═══
    private static BitmapSource BitmapToSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
    }
}
