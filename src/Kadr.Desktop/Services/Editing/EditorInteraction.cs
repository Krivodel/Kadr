using System.Collections.Immutable;

using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Rendering;

namespace Kadr.Desktop.Services.Editing;

public sealed class EditorInteraction
{
    public DrawingToolId? Tool
    {
        get; set;
    }
    public Guid? SelectedId
    {
        get; private set;
    }
    public uint Color { get; set; } = DrawingDefaults.Color;
    public double Thickness { get; set; } = DrawingDefaults.Thickness;
    public bool AutoSelectCreatedElements { get; set; }
    public OutsideSelectionBehavior OutsideSelectionBehavior { get; set; }
    public double BlurStrength { get; set; } = DrawingDefaults.BlurStrength;
    public bool IsGestureActive => _gesture != GestureKind.None;
    public bool IsDrawing => _gesture is GestureKind.Drawing or GestureKind.PendingDrawing;
    public bool IsMovingElement => _gesture == GestureKind.MoveElement;
    public bool IsTextEditRequested
    {
        get; private set;
    }
    public DrawingElement? SelectedElement => _document.Elements.FirstOrDefault(element => element.Id == SelectedId);
    public DrawingToolId? SizeTool => SizeElement?.Tool ?? Tool;
    public object? SizeTarget => SelectedElement is DrawingElement element ? element.Id : Tool;
    public double SizeValue
    {
        get
        {
            DrawingElement? selected = SizeElement;
            return (selected?.Tool ?? Tool) switch
            {
                DrawingToolId.Blur => selected?.BlurStrength ?? BlurStrength,
                DrawingToolId.Number => (selected?.Bounds.Width ?? DrawingDefaults.GetNumberSize(Thickness)) / 2,
                _ => selected?.Thickness ?? Thickness
            };
        }
    }
    public double MinimumSize => SizeTool == DrawingToolId.Number
        ? SizeElement is null ? DrawingDefaults.GetNumberSize(DrawingDefaults.MinimumThickness) / 2 : 0.5
        : DrawingDefaults.MinimumThickness;
    public double MaximumSize => DrawingDefaults.MaximumThickness;

    public event EventHandler? Changed;

    private const double ClickMovementThreshold = 3;
    private DrawingElement? SizeElement => !IsGestureActive ? SelectedElement : null;
    private readonly DrawingDocument _document;
    private readonly DocumentHistory _history;
    private readonly DrawingToolRegistry _tools;
    private ImmutableArray<DrawingElement> _before;
    private ImmutableArray<DrawingElement>? _pendingTextBefore;
    private PixelPoint _start;
    private PixelRectangle _selectionBefore;
    private DrawingElement? _original;
    private GestureKind _gesture;
    private ResizeHandle _handle;
    private ElementControl _elementControl;
    private bool _hasMoved;
    private bool _pendingTextIsNew;
    private object? _wheelGroup;
    private DrawingToolId? _pendingTool;
    private DrawingElement? _clickTarget;
    private Guid? _selectedBefore;

    public EditorInteraction(DrawingDocument document, DocumentHistory history)
        : this(document, history, new DrawingToolRegistry())
    {
    }

    public EditorInteraction(DrawingDocument document, DocumentHistory history, DrawingToolRegistry tools)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    public void Begin(PixelPoint position, double handleRadius)
    {
        _wheelGroup = null;
        _start = position;
        _selectedBefore = SelectedId;
        _hasMoved = false;
        _original = null;
        _pendingTool = null;
        _clickTarget = null;
        _before = _document.Elements;
        _selectionBefore = _document.Selection;
        IsTextEditRequested = false;

        if (_document.Selection.IsEmpty)
        {
            _gesture = GestureKind.Selection;
            return;
        }

        _handle = GetSelectionResizeHandle(position, handleRadius);
        if (_handle != ResizeHandle.None)
        {
            _gesture = GestureKind.ResizeSelection;
            return;
        }

        if (SelectedElement is DrawingElement selected
            && ElementEditing.HitControl(selected, position, handleRadius) is ElementControl control)
        {
            _elementControl = control;
            _original = selected;
            _gesture = GestureKind.ResizeElement;
            return;
        }

        if (SelectedElement is DrawingElement framed
            && ElementEditing.HitFrame(framed, position, handleRadius))
        {
            _original = framed;
            _gesture = GestureKind.MoveElement;
            return;
        }

        DrawingElement? hit = FindElement(position, handleRadius);
        if (Tool is DrawingToolId.Text or DrawingToolId.Number && hit is DrawingElement label)
        {
            SelectedId = label.Id;
            _original = label;
            _gesture = GestureKind.MoveElement;
            return;
        }

        if (Tool is DrawingToolId tool)
        {
            if (hit is not null)
            {
                _clickTarget = hit;
                _pendingTool = tool;
                _gesture = GestureKind.PendingDrawing;
            }
            else
            {
                BeginDrawing(position, tool);
            }
            return;
        }

        SelectedId = hit?.Id;

        if (hit is not null)
        {
            _original = hit;
            _gesture = GestureKind.MoveElement;
            return;
        }

        _gesture = _selectionBefore.Contains(position) ? GestureKind.MoveSelection : GestureKind.Selection;
    }

    public bool CanMoveElementAt(PixelPoint position, double tolerance)
    {
        return !IsGestureActive && GetSelectionResizeHandle(position, tolerance) == ResizeHandle.None
            && GetElementControl(position, tolerance) is null
            && ((SelectedElement is DrawingElement selected
                    && ElementEditing.HitFrame(selected, position, tolerance))
                || ((Tool is null or DrawingToolId.Text or DrawingToolId.Number)
                    && FindElement(position, tolerance) is not null));
    }

    private ResizeHandle GetSelectionResizeHandle(PixelPoint position, double tolerance)
    {
        if (_document.Selection.IsEmpty)
        {
            return ResizeHandle.None;
        }
        ResizeHandle boundary = SelectionGeometry.HitHandle(_document.Selection, position, tolerance);
        if (boundary != ResizeHandle.None)
        {
            return boundary;
        }
        if (Tool is null && OutsideSelectionBehavior == OutsideSelectionBehavior.Resize
            && GetElementControl(position, tolerance) is null && FindElement(position, tolerance) is null
            && !(SelectedElement is DrawingElement selected && ElementEditing.HitFrame(selected, position, tolerance)))
        {
            return SelectionGeometry.HitSelectionEdge(_document.Selection, position, tolerance);
        }
        return ResizeHandle.None;
    }

    public ElementControl? GetElementControl(PixelPoint position, double radius)
    {
        return !IsDrawing && SelectedElement is DrawingElement selected
            ? ElementEditing.HitControl(selected, position, radius) : null;
    }

    public ResizeHandle GetResizeHandle(PixelPoint position, double handleRadius)
    {
        ResizeHandle selection = GetSelectionResizeHandle(position, handleRadius);
        return selection != ResizeHandle.None ? selection : GetElementControl(position, handleRadius)?.Edges ?? ResizeHandle.None;
    }

    public void Update(PixelPoint position, bool constrain = false)
    {
        if (_gesture == GestureKind.PendingDrawing)
        {
            if (_start.DistanceTo(position) < ClickMovementThreshold || _pendingTool is not DrawingToolId tool)
            {
                return;
            }
            BeginDrawing(_start, tool);
        }
        if (constrain && _gesture == GestureKind.Drawing && _original?.Tool is not DrawingToolId.Number and not DrawingToolId.Text and not DrawingToolId.Blur)
        {
            position = _original?.Tool is DrawingToolId.Rectangle or DrawingToolId.Ellipse
                ? DrawingConstraints.SnapSquare(_start, position) : DrawingConstraints.SnapDirection(_start, position);
        }

        if ((_gesture is GestureKind.ResizeSelection or GestureKind.MoveSelection or GestureKind.ResizeElement or GestureKind.MoveElement)
            && !_hasMoved)
        {
            double threshold = _gesture == GestureKind.MoveElement && _original?.Tool is DrawingToolId.Text or DrawingToolId.Number
                ? ClickMovementThreshold : 1;
            if (_start.DistanceTo(position) < threshold)
            {
                return;
            }

            _hasMoved = true;
        }

        if (_gesture == GestureKind.Selection)
        {
            PixelRectangle rectangle = PixelRectangle.FromPoints(_start, position);
            _document.SetSelection(rectangle with
            {
                Width = Math.Max(1, rectangle.Width),
                Height = Math.Max(1, rectangle.Height)
            });
        }
        else if (_gesture == GestureKind.MoveSelection)
        {
            _document.SetSelection(_selectionBefore with
            {
                X = Math.Clamp(_selectionBefore.X + position.X - _start.X, 0, _document.Bounds.Width - _selectionBefore.Width),
                Y = Math.Clamp(_selectionBefore.Y + position.Y - _start.Y, 0, _document.Bounds.Height - _selectionBefore.Height)
            });
        }
        else if (_gesture == GestureKind.ResizeSelection)
        {
            _document.SetSelection(SelectionGeometry.Resize(_selectionBefore, _handle, position));
        }
        else if (_original is DrawingElement original)
        {
            DrawingElement changed = original;

            if (_gesture == GestureKind.Drawing)
            {
                if (original.Tool is DrawingToolId.Pencil or DrawingToolId.Marker)
                {
                    DrawingElement current = _document.Elements.First(element => element.Id == original.Id);
                    changed = current with
                    {
                        Points = constrain ? ImmutableList.Create(_start, position) : current.Points.Add(position)
                    };
                }
                else if (original.Tool is not DrawingToolId.Number and not DrawingToolId.Text)
                {
                    changed = original with
                    {
                        Points = ImmutableList.Create(_start, position)
                    };
                }
            }
            else if (_gesture == GestureKind.MoveElement)
            {
                changed = original.Translate(position.X - _start.X, position.Y - _start.Y);
            }
            else if (_gesture == GestureKind.ResizeElement)
            {
                changed = ElementEditing.Edit(original, _elementControl, position, constrain ? DrawingConstraint.Strict : DrawingConstraint.Free);
            }

            Replace(changed);
        }
    }

    public bool Complete(PixelPoint position, bool controlPressed, bool constrain = false)
    {
        if (!IsGestureActive)
        {
            return false;
        }

        Update(position, constrain);
        if (_gesture == GestureKind.PendingDrawing)
        {
            SelectedId = _clickTarget?.Id;
            _gesture = GestureKind.None;
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }
        bool copy = (_gesture == GestureKind.Selection) && controlPressed && !_document.Selection.IsEmpty;
        IsTextEditRequested = (_original?.Tool == DrawingToolId.Text)
            && ((_gesture == GestureKind.Drawing) || ((_gesture == GestureKind.MoveElement) && !_hasMoved));

        if ((_gesture == GestureKind.Drawing) && (_original is DrawingElement created))
        {
            bool degenerateLine = (created.Tool is DrawingToolId.Line or DrawingToolId.Arrow) && (_start.DistanceTo(position) < 1);
            bool degenerateArea = (created.Tool is DrawingToolId.Rectangle or DrawingToolId.Ellipse or DrawingToolId.Blur)
                && ((Math.Abs(position.X - _start.X) < 1) || (Math.Abs(position.Y - _start.Y) < 1));

            if (degenerateLine || degenerateArea)
            {
                _document.SetElements(_before);
            }
        }

        if (IsTextEditRequested)
        {
            _pendingTextBefore = _before;
            _pendingTextIsNew = _gesture == GestureKind.Drawing;
        }
        else
        {
            _history.Commit(_before);
        }
        if (IsDrawing && !IsTextEditRequested && !AutoSelectCreatedElements)
        {
            SelectedId = null;
        }
        _gesture = GestureKind.None;
        Changed?.Invoke(this, EventArgs.Empty);
        return copy;
    }

    public void Cancel()
    {
        _wheelGroup = null;
        if (IsGestureActive)
        {
            _document.SetElements(_before);
            _document.SetSelection(_selectionBefore);
            SelectedId = _selectedBefore;
            _gesture = GestureKind.None;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void DeleteAt(PixelPoint position, double tolerance)
    {
        Cancel();
        DrawingElement? hit = FindElement(position, tolerance, false);
        if (hit is not null)
        {
            SelectedId = hit.Id;
            DeleteSelected();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void DeleteSelected()
    {
        _wheelGroup = null;
        ImmutableArray<DrawingElement> before = _document.Elements;
        _document.SetElements(before.RemoveAll(element => element.Id == SelectedId));
        _history.Commit(before);
        SelectedId = null;
    }

    public void DeselectElement()
    {
        _wheelGroup = null;
        SelectedId = null;
        IsTextEditRequested = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreSelection(Guid? selectedId)
    {
        SelectedId = _document.Elements.Any(element => element.Id == selectedId) ? selectedId : null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (SelectedElement is { Tool: DrawingToolId.Text } selected)
        {
            Replace(selected with { Text = text });
        }
    }

    public void AdjustSize(double delta, object? wheelGroup = null)
    {
        if (_pendingTextBefore is not null && SelectedElement is { Tool: DrawingToolId.Text } editingText)
        {
            Replace(editingText with
            {
                Thickness = Math.Clamp(editingText.Thickness + delta, DrawingDefaults.MinimumThickness, DrawingDefaults.MaximumThickness)
            });
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (SelectedElement is DrawingElement selectedElement && !IsGestureActive)
        {
            ImmutableArray<DrawingElement> before = _document.Elements;
            DrawingElement resized;
            if (selectedElement.Tool == DrawingToolId.Blur)
            {
                resized = selectedElement with { BlurStrength = Math.Clamp(selectedElement.BlurStrength + delta,
                    DrawingDefaults.MinimumBlurStrength, DrawingDefaults.MaximumBlurStrength) };
                BlurStrength = resized.BlurStrength;
            }
            else if (selectedElement.Tool == DrawingToolId.Number)
            {
                double size = Math.Clamp(selectedElement.Bounds.Width + delta * 2, 1, DrawingDefaults.GetNumberSize(DrawingDefaults.MaximumThickness));
                double scale = size / selectedElement.Bounds.Width;
                resized = selectedElement.Translate((selectedElement.Bounds.Width - size) / 2, (selectedElement.Bounds.Height - size) / 2) with
                {
                    LabelSize = size,
                    Thickness = Math.Clamp(selectedElement.Thickness * scale, DrawingDefaults.MinimumThickness, DrawingDefaults.MaximumThickness)
                };
            }
            else
            {
                resized = selectedElement with { Thickness = Math.Clamp(selectedElement.Thickness + delta,
                    DrawingDefaults.MinimumThickness, DrawingDefaults.MaximumThickness) };
            }
            Replace(resized);
            if (!before.SequenceEqual(_document.Elements))
            {
                if (wheelGroup is null || !ReferenceEquals(_wheelGroup, wheelGroup))
                {
                    _history.Commit(before);
                }
                _wheelGroup = wheelGroup;
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (Tool == DrawingToolId.Blur)
        {
            BlurStrength = Math.Clamp(BlurStrength + delta, DrawingDefaults.MinimumBlurStrength, DrawingDefaults.MaximumBlurStrength);
        }
        else if (Tool == DrawingToolId.Number && !IsDrawing)
        {
            Thickness = Math.Clamp(SizeValue + delta, MinimumSize, MaximumSize);
        }
        else
        {
            Thickness = Math.Clamp(Thickness + delta, DrawingDefaults.MinimumThickness, DrawingDefaults.MaximumThickness);
        }

        if (IsDrawing && _original is DrawingElement original && SelectedElement is DrawingElement selected)
        {
            _original = original with { Thickness = Thickness, BlurStrength = BlurStrength };
            Replace(selected with { Thickness = Thickness, BlurStrength = BlurStrength });
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (SelectedElement is DrawingElement selected)
        {
            ImmutableArray<DrawingElement> before = _pendingTextBefore ?? _document.Elements;

            if (string.IsNullOrWhiteSpace(text))
            {
                _document.SetElements(_document.Elements.Remove(selected));
            }
            else
            {
                Replace(selected with
                {
                    Text = text
                });
            }

            _history.Commit(before);
            _pendingTextBefore = null;
            IsTextEditRequested = false;
            if (_pendingTextIsNew && !AutoSelectCreatedElements)
            {
                SelectedId = null;
            }
            _pendingTextIsNew = false;
        }
    }

    private void Replace(DrawingElement element)
    {
        DrawingElement? existing = _document.Elements.FirstOrDefault(value => value.Id == element.Id);
        int index = existing is null ? -1 : _document.Elements.IndexOf(existing);

        if (index >= 0)
        {
            _document.SetElements(_document.Elements.SetItem(index, element));
        }
    }

    private bool CanEditWithActiveTool(DrawingElement element)
    {
        return Tool is not (DrawingToolId.Text or DrawingToolId.Number) || element.Tool == Tool;
    }

    private DrawingElement? FindElement(PixelPoint position, double tolerance, bool filterByTool = true)
    {
        bool Matches(DrawingElement element)
        {
            return !filterByTool || CanEditWithActiveTool(element);
        }
        return _document.Elements.LastOrDefault(element => Matches(element) && _tools.Get(element.Tool).HitTest(element, position, tolerance))
            ?? _document.Elements.LastOrDefault(element => Matches(element) && _tools.Get(element.Tool).HitInterior(element, position, tolerance));
    }

    private void BeginDrawing(PixelPoint position, DrawingToolId tool)
    {
        if (tool == DrawingToolId.Number)
        {
            double radius = DrawingDefaults.GetNumberSize(Thickness) / 2;
            position = new PixelPoint(position.X - radius, position.Y - radius);
        }
        else if (tool == DrawingToolId.Text)
        {
            position = new PixelPoint(position.X, position.Y - DrawingDefaults.GetTextLineHeight(Thickness) / 2);
        }
        _original = new DrawingElement(Guid.NewGuid(), tool, ImmutableList.Create(position, position), Color, Thickness, BlurStrength: BlurStrength);
        SelectedId = _original.Id;
        _gesture = GestureKind.Drawing;
        _document.SetElements(_before.Add(_original));
    }
}
