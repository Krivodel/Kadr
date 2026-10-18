using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Input;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;

using PixelPoint = Kadr.Desktop.Services.Geometry.PixelPoint;

namespace Kadr.Desktop.Views;

public sealed partial class EditorWindow : Window
{
    internal DesktopMonitor Monitor => _monitor;

    private const double ThicknessSampleLength = 110;
    private readonly Dictionary<StandardCursorType, Cursor> _cursors = [];
    private readonly EditorViewModel _model;
    private readonly DesktopMonitor _monitor;
    private readonly SettingsStore _settings;
    private readonly DispatcherTimer _modifiers;
    private readonly WheelSizeAnimator _wheelSizeAnimator;
    private readonly Func<bool> _isAltPressed;
    private Button[] _toolButtons = Array.Empty<Button>();
    private (PixelRectangle Selection, Size ClientSize, double Scale)? _layoutState;
    private PixelPoint? _thicknessOrigin;
    private PixelPoint? _pointerPosition;
    private double _originalThickness;
    private ThicknessAdjustment? _thicknessAdjustment;
    private bool _isPaletteOpen;
    private bool _pointerGesture;
    private IPointer? _canvasPointer;
    private bool _textEditing;
    private uint? _displayedColor;
    private bool _cursorHidden;
    private bool _chromePending;
    private bool _isClosing;
    private bool _ownsUpdatePopup;

    public EditorWindow(EditorViewModel model, DesktopMonitor monitor, SettingsStore settings, Func<bool>? isAltPressed = null)
    {
        _model = model;
        _monitor = monitor;
        _settings = settings;
        _isAltPressed = isAltPressed ?? (() => NativeDesktop.IsKeyDown(0x12));
        InitializeComponent();
        _wheelSizeAnimator = new WheelSizeAnimator(this, model, RefreshWheelSize);
        DataContext = model;
        Surface.Editor = model;
        Surface.Monitor = monitor;
        Surface.PointerPressed += OnCanvasPressed;
        Surface.PointerMoved += OnCanvasMoved;
        Surface.PointerReleased += OnCanvasReleased;
        Surface.PointerCaptureLost += OnCanvasCaptureLost;
        Surface.PointerExited += (_, _) => { Surface.ShowDrawingPointer = false; Surface.InvalidateVisual(); };
        AddHandler(PointerWheelChangedEvent, OnCanvasWheel, RoutingStrategies.Tunnel);
        TextEntry.TextChanged += OnTextChanged;
        ThicknessButton.AddHandler(PointerPressedEvent, OnThicknessPressed, RoutingStrategies.Tunnel);
        ThicknessButton.AddHandler(PointerReleasedEvent, OnThicknessReleased, RoutingStrategies.Tunnel);
        ThicknessButton.PointerMoved += OnThicknessMoved;
        ThicknessButton.PointerCaptureLost += OnThicknessLost;
        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnEditorPointerPressed, RoutingStrategies.Tunnel);
        TextEntry.LostFocus += OnTextLostFocus;
        _model.Document.Changed += OnDocumentChanged;
        _model.PropertyChanged += OnModelChanged;
        if (_model.Updates is not null)
        {
            _model.Updates.PropertyChanged += OnUpdateChanged;
        }
        _modifiers = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, OnModifierTick);
        _modifiers.Start();
        Opened += OnOpened;
        Closed += OnClosed;
        ScalingChanged += OnScalingChanged;
        uint[] colors = new uint[] { 0xff000000, 0xffffffff, 0xffff0000, 0xffff8000, 0xffffff00, 0xff00ff00, 0xffa000ff, 0xff00ffff, 0xff0000ff };

        foreach (uint color in colors)
        {
            Button button = new()
            {
                Tag = color,
                Width = 36,
                Height = 32,
                Margin = new Thickness(3),
                Background = new SolidColorBrush(Color.FromUInt32(color))
            };
            button.Classes.Add("swatch");
            button.Click += (sender, arguments) => { SetDrawingColor(color); SetPaletteVisible(false); };
            PresetColors.Children.Add(button);
        }
        Palette.InitializeItems(PresetColors.Children.Append(CustomColorButton));

        UpdateDrawingStyle();
    }

    internal async Task FadeOutAsync()
    {
        _wheelSizeAnimator.Finish();
        _isClosing = true;
        _modifiers.Stop();
        IsHitTestVisible = false;
        FinishThickness();
        Surface.DimAmount = 0;
        FloatingPanelAnimator.SetVisible(Actions, false);
        FloatingPanelAnimator.SetVisible(ToolsPanel, false);
        SetPaletteVisible(false);
        await Task.Delay(Controls.UiAnimationDuration.PanelFade);
    }

    internal void PrepareForUpdate()
    {
        _wheelSizeAnimator.Finish();
        CommitText();
        CancelCanvasGesture();
        FinishThickness();
        SetPaletteVisible(false);
        _model.IsPreview = false;
    }

    private void OnUpdateChanged(object? sender, PropertyChangedEventArgs arguments)
    {
        if (arguments.PropertyName == nameof(UpdateViewModel.IsAvailable))
        {
            _layoutState = null;
        }
        RequestChromeUpdate();
    }

    private void UpdateUpdatePopup()
    {
        bool visible = _ownsUpdatePopup && _model.Updates is { IsOpen: true } && !_model.IsPreview && !_isClosing;
        FloatingPanelAnimator.SetVisible(UpdatePopup, visible);
        Surface.IsHitTestVisible = _model.Updates?.IsLoading != true;
        if (!visible)
        {
            return;
        }
        SetPaletteVisible(false);
        UpdatePopup.Measure(new Size(350, double.PositiveInfinity));
        double width = UpdatePopup.DesiredSize.Width;
        double height = UpdatePopup.DesiredSize.Height;
        double left = Canvas.GetLeft(Actions) - width - 12;
        if (left < 6 && Canvas.GetLeft(Actions) + Actions.Bounds.Width + 12 + width <= ClientSize.Width - 6)
        {
            left = Canvas.GetLeft(Actions) + Actions.Bounds.Width + 12;
        }
        Canvas.SetLeft(UpdatePopup, Math.Clamp(left, 6, Math.Max(6, ClientSize.Width - width - 6)));
        Canvas.SetTop(UpdatePopup, Math.Clamp(Canvas.GetTop(Actions), 6, Math.Max(6, ClientSize.Height - height - 6)));
    }

    private PixelPoint GetDocumentPointer(PointerEventArgs arguments)
    {
        Point local = arguments.GetPosition(Surface);
        PixelPoint position = new(_monitor.Bounds.X + (local.X * RenderScaling), _monitor.Bounds.Y + (local.Y * RenderScaling));
        DesktopMonitor? monitor = _model.Document.Desktop.Monitors.FirstOrDefault(candidate => candidate.Bounds.Contains(position));

        if (monitor is not null)
        {
            position = SelectionGeometry.SnapPointerToBoundary(position, monitor.Bounds);
        }

        return new PixelPoint(position.X - _model.Document.Desktop.Bounds.X, position.Y - _model.Document.Desktop.Bounds.Y);
    }

    private Point ToLocal(PixelPoint position)
    {
        return new Point((position.X + _model.Document.Desktop.Bounds.X - _monitor.Bounds.X) / RenderScaling,
            (position.Y + _model.Document.Desktop.Bounds.Y - _monitor.Bounds.Y) / RenderScaling);
    }

    private void UpdateChrome()
    {
        if (_isClosing)
        {
            return;
        }

        UpdateUpdatePopup();

        if (_cursorHidden != _model.IsAdjustingThickness)
        {
            _cursorHidden = _model.IsAdjustingThickness;
            Classes.Set("thickness-drag", _cursorHidden);
            Cursor = GetCursor(_cursorHidden ? StandardCursorType.None : StandardCursorType.Arrow);
        }

        UpdateDrawingStyle();
        Surface.InvalidateVisual();
        Surface.PreviewAmount = _model.IsPreview ? 1 : 0;
        UpdatePointerCursor(_pointerPosition ?? new PixelPoint(0, 0));

        if (_toolButtons.Length != _model.VisibleToolCount)
        {
            _toolButtons = ToolsPanel.GetVisualDescendants().OfType<Button>()
                .Where(button => button.CommandParameter is Services.Rendering.DrawingTool).ToArray();
            _layoutState = null;
        }

        foreach (Button button in _toolButtons)
        {
            if (button.CommandParameter is Services.Rendering.DrawingTool tool)
            {
                button.Classes.Set("active", _model.Interaction.Tool == tool.Id);
            }
        }

        bool show = !_model.Document.Selection.IsEmpty && !_model.IsPreview && !_model.Interaction.IsGestureActive && !_model.IsLoading;

        FloatingPanelAnimator.SetVisible(Actions, show && _model.Toolbar.HasActions);
        FloatingPanelAnimator.SetVisible(ToolsPanel, show && _model.Toolbar.HasTools);
        CursorButton.IsEnabled = _model.Document.Desktop.CanToggleCursor;
        CursorButton.Classes.Set("active", _model.Document.ShowCursor);

        if (!show)
        {
            SetPaletteVisible(false);
            return;
        }
        if (!_model.Toolbar.Color)
        {
            SetPaletteVisible(false);
        }

        PixelRectangle selection = _model.Document.Selection;
        (PixelRectangle Selection, Size ClientSize, double Scale) layout = (selection, ClientSize, RenderScaling);
        if (_layoutState == layout)
        {
            return;
        }
        _layoutState = layout;
        double anchorGlobalX = selection.Right + _model.Document.Desktop.Bounds.X;
        double anchorGlobalY = selection.Bottom + _model.Document.Desktop.Bounds.Y;
        DesktopMonitor anchorMonitor = _model.Document.Desktop.Monitors.MinBy(monitor =>
            Math.Pow(Math.Clamp(anchorGlobalX, monitor.Bounds.X, monitor.Bounds.Right) - anchorGlobalX, 2)
            + Math.Pow(Math.Clamp(anchorGlobalY, monitor.Bounds.Y, monitor.Bounds.Bottom) - anchorGlobalY, 2)) ?? _monitor;

        if (anchorMonitor.Handle != _monitor.Handle)
        {
            _ownsUpdatePopup = false;
            UpdateUpdatePopup();
            FloatingPanelAnimator.SetVisible(Actions, false);
            FloatingPanelAnimator.SetVisible(ToolsPanel, false);
            return;
        }

        Point topLeft = ToLocal(new PixelPoint(selection.X, selection.Y));
        PixelRectangle localSelection = new(topLeft.X, topLeft.Y, selection.Width / RenderScaling, selection.Height / RenderScaling);
        PixelRectangle viewport = new(6, 6, Math.Max(1, ClientSize.Width - 12), Math.Max(1, ClientSize.Height - 12));
        double width = Math.Min(720, Math.Max(44, viewport.Width - 64));
        ToolsPanel.MaxWidth = width;
        ToolsPanel.Measure(new Size(width, double.PositiveInfinity));
        Actions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        (PixelRectangle actions, PixelRectangle tools) = ToolbarPlacement.PlacePair(localSelection,
            _model.Toolbar.HasActions ? (Actions.DesiredSize.Width, Actions.DesiredSize.Height) : (0, 0),
            _model.Toolbar.HasTools ? (Math.Min(width, ToolsPanel.DesiredSize.Width), ToolsPanel.DesiredSize.Height) : (0, 0),
            viewport, 10);
        Canvas.SetLeft(ToolsPanel, tools.X);
        Canvas.SetTop(ToolsPanel, tools.Y);
        Canvas.SetLeft(Actions, actions.X);
        Canvas.SetTop(Actions, actions.Y);
        _ownsUpdatePopup = true;
        UpdateUpdatePopup();
    }

    private void UpdatePointerCursor(PixelPoint position)
    {
        _pointerPosition = position;
        ResizeHandle handle = _model.Interaction.GetResizeHandle(position, 7 * RenderScaling);
        StandardCursorType cursor = handle switch
        {
            ResizeHandle.Left or ResizeHandle.Right => StandardCursorType.SizeWestEast,
            ResizeHandle.Top or ResizeHandle.Bottom => StandardCursorType.SizeNorthSouth,
            ResizeHandle.Left | ResizeHandle.Top => StandardCursorType.TopLeftCorner,
            ResizeHandle.Right | ResizeHandle.Bottom => StandardCursorType.BottomRightCorner,
            ResizeHandle.Right | ResizeHandle.Top => StandardCursorType.TopRightCorner,
            ResizeHandle.Left | ResizeHandle.Bottom => StandardCursorType.BottomLeftCorner,
            _ => _model.Document.Selection.IsEmpty
                || (_model.Interaction.OutsideSelectionBehavior == OutsideSelectionBehavior.Reselect
                    && !_model.Document.Selection.Contains(position))
                ? StandardCursorType.Cross : StandardCursorType.Arrow
        };
        bool control = handle == ResizeHandle.None && _model.Interaction.GetElementControl(position, 7 * RenderScaling) is not null;
        bool move = _model.Interaction.IsMovingElement || _model.Interaction.CanMoveElementAt(position, 7 * RenderScaling);
        bool drawing = !_model.Document.Selection.IsEmpty && _model.Interaction.Tool is not null && !control && !move && handle == ResizeHandle.None && !_textEditing;
        bool hidden = drawing && _settings.Current.HideCursorWhileDrawing;
        bool creating = _model.Interaction.IsDrawing;
        Surface.DrawingPointer = position;
        Surface.ShowDrawingPointer = drawing && !creating && Surface.IsPointerOver && !_model.IsAdjustingThickness && !_isPaletteOpen;
        Surface.Cursor = GetCursor(_model.IsAdjustingThickness || (hidden && (creating || _model.Interaction.Tool != DrawingToolId.Blur))
            ? StandardCursorType.None : control ? StandardCursorType.Hand
            : move ? StandardCursorType.SizeAll
            : drawing ? _model.Interaction.Tool == DrawingToolId.Text ? StandardCursorType.Ibeam : StandardCursorType.Cross : cursor);
        Surface.InvalidateVisual();
    }

    private Cursor GetCursor(StandardCursorType type)
    {
        if (!_cursors.TryGetValue(type, out Cursor? cursor))
        {
            cursor = new Cursor(type);
            _cursors.Add(type, cursor);
        }
        return cursor;
    }

    private void UpdateThicknessPreview()
    {
        double thickness = _model.Interaction.Thickness / RenderScaling;
        ThicknessLine.Width = thickness;
        ThicknessLine.Height = ThicknessSampleLength + thickness;
        ThicknessPreview.Height = ThicknessLine.Height + 40;
        PositionPopup(ThicknessPreview, ThicknessButton, new Size(ThicknessPreview.Width, ThicknessPreview.Height));
    }

    private void PositionPopup(Control popup, Button anchor, Size size)
    {
        Point top = anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, 0), Chrome) ?? new Point();
        Point bottom = anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height), Chrome) ?? top;
        double left = Math.Clamp(top.X - (size.Width / 2), 0, Math.Max(0, ClientSize.Width - size.Width));
        double y = top.Y >= size.Height + 10 ? top.Y - size.Height - 10 : bottom.Y + 10;
        Canvas.SetLeft(popup, left);
        Canvas.SetTop(popup, Math.Clamp(y, 0, Math.Max(0, ClientSize.Height - size.Height)));
    }

    private void SetPaletteVisible(bool visible)
    {
        _isPaletteOpen = visible;
        Palette.SetVisible(visible);
    }

    private void SetDrawingColor(uint color)
    {
        _model.SetDrawingColor(color);
        UpdateDrawingStyle();
    }

    private void UpdateDrawingStyle()
    {
        if (_displayedColor != _model.Interaction.Color)
        {
            _displayedColor = _model.Interaction.Color;
            ColorSwatch.Background = new SolidColorBrush(Color.FromUInt32(_model.Interaction.Color));
            ThicknessLine.Background = ColorSwatch.Background;
            TextEntry.CaretBrush = ColorSwatch.Background;
            foreach (Button button in PresetColors.Children.OfType<Button>())
            {
                button.Classes.Set("chosen", button.Tag is uint color && color == _model.Interaction.Color);
            }
        }
    }

    private void CommitText()
    {
        if (_textEditing)
        {
            _textEditing = false;
            _model.Interaction.SetText(TextEntry.Text ?? string.Empty);
            TextEntry.IsVisible = false;
        }
    }

    private void FinishThickness()
    {
        if (_thicknessOrigin is PixelPoint origin)
        {
            _thicknessOrigin = null;
            _model.IsAdjustingThickness = false;
            FloatingPanelAnimator.SetVisible(ThicknessPreview, false);
            _thicknessAdjustment = null;
            ThicknessButton.ClearValue(CursorProperty);
            Cursor = GetCursor(StandardCursorType.Arrow);
            NativeDesktop.RestorePointer(origin);
        }
    }

    private void UpdateMonitorSize()
    {
        Width = _monitor.Bounds.Width / RenderScaling;
        Height = _monitor.Bounds.Height / RenderScaling;
    }

    private void RequestChromeUpdate()
    {
        if (_chromePending)
        {
            return;
        }

        _chromePending = true;
        Dispatcher.Post(() =>
        {
            _chromePending = false;
            UpdateChrome();
        }, DispatcherPriority.Render);
    }

    private void OnOpened(object? sender, EventArgs arguments)
    {
        Position = new Avalonia.PixelPoint((int)_monitor.Bounds.X, (int)_monitor.Bounds.Y);
        UpdateMonitorSize();
        Surface.Focus();
        UpdateChrome();
        Dispatcher.Post(() => Surface.DimAmount = 1, DispatcherPriority.Render);
    }

    private void OnScalingChanged(object? sender, EventArgs arguments)
    {
        UpdateMonitorSize();
        UpdateChrome();
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs arguments)
    {
        _wheelSizeAnimator.Finish();
        int key = KeyboardGestures.GetPressedMouseKey(arguments);
        if (!_isClosing && !_model.IsLoading && key != 0
            && KeyboardGestures.FindMouse(_settings.Current, arguments.KeyModifiers, key) is ShortcutDefinition shortcut)
        {
            if (shortcut.Action == EditorAction.Delete && !IsCanvasSource(arguments.Source))
            {
                return;
            }
            CommitText();
            ExecuteShortcut(shortcut, GetDocumentPointer(arguments));
            arguments.Handled = true;
        }
    }

    private void OnCanvasPressed(object? sender, PointerPressedEventArgs arguments)
    {
        if (_isClosing || _model.IsLoading)
        {
            return;
        }
        if (!arguments.GetCurrentPoint(Surface).Properties.IsLeftButtonPressed)
        {
            return;
        }

        CommitText();
        SetPaletteVisible(false);
        _pointerGesture = true;
        _model.Interaction.Begin(GetDocumentPointer(arguments), 7 * RenderScaling);
        _canvasPointer = arguments.Pointer;
        arguments.Pointer.Capture(Surface);
        UpdateChrome();
        arguments.Handled = true;
    }

    private static bool IsConstrained(KeyModifiers modifiers)
    {
        return (modifiers & (KeyModifiers.Shift | KeyModifiers.Control)) != 0;
    }

    private void OnCanvasMoved(object? sender, PointerEventArgs arguments)
    {
        PixelPoint position = GetDocumentPointer(arguments);

        if (_pointerGesture)
        {
            _model.Interaction.Update(position, IsConstrained(arguments.KeyModifiers));
        }

        UpdatePointerCursor(position);
    }

    private void OnCanvasReleased(object? sender, PointerReleasedEventArgs arguments)
    {
        if (!_pointerGesture)
        {
            return;
        }

        bool copy = _model.Interaction.Complete(GetDocumentPointer(arguments), arguments.KeyModifiers.HasFlag(KeyModifiers.Control), IsConstrained(arguments.KeyModifiers));
        ReleaseCanvasPointer();
        UpdateChrome();

        if (copy)
        {
            _model.CopyCommand.Execute(null);
        }
        else if (_model.Interaction.IsTextEditRequested && (_model.Interaction.SelectedElement is { Tool: DrawingToolId.Text } element))
        {
            Point local = ToLocal(new PixelPoint(element.Bounds.X, element.Bounds.Y));
            Canvas.SetLeft(TextEntry, local.X);
            Canvas.SetTop(TextEntry, local.Y);
            TextEntry.Width = Math.Max(1, ClientSize.Width - local.X);
            TextEntry.FontSize = DrawingDefaults.GetLabelSize(element.Thickness) / RenderScaling;
            TextEntry.LineHeight = TextEntry.FontSize * DrawingDefaults.TextLineSpacing;
            TextEntry.CaretBrush = new SolidColorBrush(Color.FromUInt32(element.Color));
            TextEntry.Text = element.Text;
            TextEntry.IsVisible = true;
            _textEditing = true;
            TextEntry.Focus();
            TextEntry.CaretIndex = element.Text.Length;
        }

        arguments.Handled = true;
    }

    private void OnCanvasCaptureLost(object? sender, PointerCaptureLostEventArgs arguments)
    {
        if (_pointerGesture)
        {
            CancelCanvasGesture();
            UpdateChrome();
        }
    }

    private void ReleaseCanvasPointer()
    {
        _pointerGesture = false;
        IPointer? pointer = _canvasPointer;
        _canvasPointer = null;
        pointer?.Capture(null);
    }

    private void CancelCanvasGesture()
    {
        _model.Interaction.Cancel();
        ReleaseCanvasPointer();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs arguments)
    {
        _wheelSizeAnimator.Finish();
        if (_isClosing)
        {
            arguments.Handled = true;
            return;
        }

        if (_textEditing)
        {
            if (arguments.Key == Key.Escape)
            {
                CommitText();
                if (_settings.Current.ExitImmediately)
                {
                    _model.CancelCommand.Execute(null);
                }
                else
                {
                    _model.Interaction.DeselectElement();
                }
                UpdateChrome();
                arguments.Handled = true;
            }

            return;
        }

        if (_model.IsLoading)
        {
            return;
        }

        if ((arguments.Key == Key.Escape) || KeyboardGestures.Matches(arguments, _settings.Current, EditorAction.Cancel))
        {
            CancelOrClose();
        }
        else
        {
            ShortcutDefinition? shortcut = ShortcutCatalog.Definitions.FirstOrDefault(definition =>
                definition.Action != EditorAction.Capture && KeyboardGestures.Matches(arguments, _settings.Current, definition.Action));
            if (shortcut is null)
            {
                return;
            }

            ExecuteShortcut(shortcut);
        }
        arguments.Handled = true;
        UpdateChrome();
    }

    private void CancelOrClose()
    {
        if (_model.Updates is { IsOpen: true } updates)
        {
            updates.DismissCommand.Execute(null);
        }
        else if (_thicknessOrigin is not null)
        {
            _model.Interaction.Thickness = _originalThickness;
            FinishThickness();
        }
        else if (_isPaletteOpen)
        {
            SetPaletteVisible(false);
        }
        else if (_model.Interaction.IsGestureActive)
        {
            CancelCanvasGesture();
        }
        else if (!_settings.Current.ExitImmediately && _model.Interaction.SelectedElement is not null)
        {
            _model.Interaction.DeselectElement();
        }
        else
        {
            _model.CancelCommand.Execute(null);
        }
        UpdateChrome();
    }

    private void ExecuteShortcut(ShortcutDefinition shortcut, PixelPoint? target = null)
    {
        if (shortcut.Tool is DrawingToolId tool)
        {
            _model.SelectToolCommand.Execute(_model.Tools.Single(item => item.Id == tool));
        }
        else
        {
            switch (shortcut.Action)
            {
                case EditorAction.Delete:
                    CancelCanvasGesture();
                    if (target is PixelPoint point)
                    {
                        _model.Interaction.DeleteAt(point, 7 * RenderScaling);
                    }
                    else
                    {
                        _model.Interaction.DeleteSelected();
                    }
                    break;
                case EditorAction.Cancel:
                    CancelOrClose();
                    break;
                case EditorAction.Cursor:
                    _model.ToggleCursorCommand.Execute(null);
                    break;
                case EditorAction.Undo:
                    _model.UndoCommand.Execute(null);
                    break;
                case EditorAction.Redo:
                    _model.RedoCommand.Execute(null);
                    break;
                case EditorAction.Copy:
                    _model.CopyCommand.Execute(null);
                    break;
                case EditorAction.Save:
                    _model.SaveCommand.Execute(null);
                    break;
                case EditorAction.Recognize:
                    _model.RecognizeCommand.Execute(null);
                    break;
                case EditorAction.Select:
                    CancelCanvasGesture();
                    FinishThickness();
                    SetPaletteVisible(false);
                    _model.SetActiveTool(null);
                    break;
                case EditorAction.SelectAll:
                    CancelCanvasGesture();
                    _model.Document.SetSelection(_model.Document.Bounds);
                    break;
            }
        }
        UpdateChrome();
    }

    private void OnColorClick(object? sender, RoutedEventArgs arguments)
    {
        Palette.Measure(new Size(180, double.PositiveInfinity));
        PositionPopup(Palette, ColorButton, Palette.DesiredSize);
        SetPaletteVisible(!_isPaletteOpen);
    }

    private async void OnCustomColorClick(object? sender, RoutedEventArgs arguments)
    {
        SetPaletteVisible(false);
        ColorPickerWindow picker = new(_model.Interaction.Color, _settings.Current.ColorPickerTab);
        uint? result = await picker.ShowDialog<uint?>(this);
        await _model.RememberColorPickerTabAsync(picker.SelectedTab, CancellationToken.None);

        if (result is uint color)
        {
            SetDrawingColor(color);
        }
    }

    private void OnThicknessPressed(object? sender, PointerPressedEventArgs arguments)
    {
        if (!arguments.GetCurrentPoint(ThicknessButton).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _thicknessOrigin = NativeDesktop.GetPointerPosition();
        _model.IsAdjustingThickness = true;
        _originalThickness = _model.Interaction.Thickness;
        _thicknessAdjustment = new ThicknessAdjustment(_originalThickness, arguments.GetPosition(this).X * RenderScaling);
        Cursor = GetCursor(StandardCursorType.None);
        ThicknessButton.Cursor = GetCursor(StandardCursorType.None);
        arguments.Pointer.Capture(ThicknessButton);
        UpdateThicknessPreview();
        FloatingPanelAnimator.SetVisible(ThicknessPreview, true);
        arguments.Handled = true;
    }

    private void OnThicknessMoved(object? sender, PointerEventArgs arguments)
    {
        if (_thicknessAdjustment is ThicknessAdjustment adjustment)
        {
            bool precise = (arguments.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt)) != 0;
            adjustment.Update(arguments.GetPosition(this).X * RenderScaling, precise);
            _model.SetDrawingThickness(adjustment.Value);
            double physicalX = _monitor.Bounds.X + arguments.GetPosition(this).X * RenderScaling;
            if (_thicknessOrigin is PixelPoint origin && (physicalX <= _monitor.Bounds.X + 1 || physicalX >= _monitor.Bounds.Right - 2))
            {
                NativeDesktop.RestorePointer(origin);
                adjustment.Rebase(origin.X - _monitor.Bounds.X);
            }
            UpdateThicknessPreview();
        }
    }

    private void OnThicknessReleased(object? sender, PointerReleasedEventArgs arguments)
    {
        FinishThickness();
        arguments.Pointer.Capture(null);
        arguments.Handled = true;
    }

    private void OnThicknessLost(object? sender, PointerCaptureLostEventArgs arguments)
    {
        FinishThickness();
    }

    private void OnModifierTick(object? sender, EventArgs arguments)
    {
        bool preview = IsActive && !_textEditing && !_isPaletteOpen && _model.Updates?.IsOpen != true && (_thicknessOrigin is null)
            && !_model.Interaction.IsGestureActive && _isAltPressed();

        if (_model.IsPreview != preview)
        {
            _model.IsPreview = preview;
        }
    }

    private void OnCanvasWheel(object? sender, PointerWheelEventArgs arguments)
    {
        if (_isClosing || _model.IsLoading || arguments.Delta.Y == 0 || (_model.Interaction.Tool is null && _model.Interaction.SelectedElement is null)
            || !IsCanvasSource(arguments.Source))
        {
            return;
        }
        _wheelSizeAnimator.Add(arguments.Delta.Y);
        UpdatePointerCursor(GetDocumentPointer(arguments));
        arguments.Handled = true;
    }

    private bool IsCanvasSource(object? source)
    {
        return source is Visual visual && (ReferenceEquals(visual, Surface) || ReferenceEquals(visual, TextEntry)
            || visual.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, Surface) || ReferenceEquals(ancestor, TextEntry)));
    }

    private void RefreshWheelSize()
    {
        if (_textEditing && _model.Interaction.SelectedElement is DrawingElement text)
        {
            TextEntry.FontSize = DrawingDefaults.GetLabelSize(text.Thickness) / RenderScaling;
            TextEntry.LineHeight = TextEntry.FontSize * DrawingDefaults.TextLineSpacing;
        }
        UpdateChrome();
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs arguments)
    {
        if (_textEditing)
        {
            _model.Interaction.UpdateText(TextEntry.Text ?? string.Empty);
        }
    }

    private void OnTextLostFocus(object? sender, RoutedEventArgs arguments)
    {
        CommitText();
    }

    private void OnDocumentChanged(object? sender, EventArgs arguments)
    {
        Surface.InvalidateVisual();

        if (!_pointerGesture)
        {
            RequestChromeUpdate();
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs arguments)
    {
        if (arguments.PropertyName == nameof(EditorViewModel.Toolbar))
        {
            _layoutState = null;
            _toolButtons = Array.Empty<Button>();
            if (!_model.Toolbar.Thickness)
            {
                FinishThickness();
            }
        }
        if (arguments.PropertyName == nameof(EditorViewModel.IsLoading))
        {
            UpdateChrome();
        }
        else
        {
            RequestChromeUpdate();
        }
    }

    private void OnClosed(object? sender, EventArgs arguments)
    {
        _isClosing = true;
        _wheelSizeAnimator.Dispose();
        ReleaseCanvasPointer();
        FinishThickness();
        _modifiers.Stop();
        _model.Document.Changed -= OnDocumentChanged;
        _model.PropertyChanged -= OnModelChanged;
        if (_model.Updates is not null)
        {
            _model.Updates.PropertyChanged -= OnUpdateChanged;
        }
        Surface.ClearValue(CursorProperty);
        ClearValue(CursorProperty);
        foreach (Cursor cursor in _cursors.Values)
        {
            cursor.Dispose();
        }
        _cursors.Clear();
    }
}
