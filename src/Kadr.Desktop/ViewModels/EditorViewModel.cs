using System.ComponentModel;

using Microsoft.Extensions.Logging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Export;
using Kadr.Desktop.Services.Ocr;
using Kadr.Desktop.Services.Rendering;
using Kadr.Desktop.Services.Settings;

namespace Kadr.Desktop.ViewModels;

public sealed partial class EditorViewModel : ObservableObject, IDisposable
{
    public DrawingDocument Document
    {
        get;
    }
    public DocumentHistory History
    {
        get;
    }
    public UpdateViewModel? Updates
    {
        get => _updates;
        internal set
        {
            if (_updates is not null)
            {
                _updates.PropertyChanged -= OnUpdateChanged;
            }
            SetProperty(ref _updates, value);
            if (_updates is not null)
            {
                _updates.PropertyChanged += OnUpdateChanged;
            }
            NotifyInputStateChanged();
        }
    }
    public EditorInteraction Interaction
    {
        get;
    }
    public DocumentRenderer Renderer
    {
        get;
    }
    public IReadOnlyList<DrawingTool> Tools => _tools.Tools;
    public ToolbarVisibilityViewModel Toolbar { get; private set; }
    public IReadOnlyList<DrawingTool> LabelTools => Toolbar.LabelTools;
    public IReadOnlyList<DrawingTool> DrawingTools => Toolbar.DrawingTools;
    public int VisibleToolCount => LabelTools.Count + DrawingTools.Count;
    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool ShowOperationProgress => IsLoading;
    public bool IsInputBlocked => IsLoading || Updates?.IsPreparingRestart == true;

    public event EventHandler? CloseRequested;
    public event EventHandler? SettingsRequested;

    private readonly DrawingToolRegistry _tools;
    private readonly IClipboardWriter _clipboard;
    private readonly IFilePicker _picker;
    private readonly ImageFileWriter _files;
    private readonly IOcrService _ocr;
    private readonly SettingsStore _settings;
    private readonly ILogger<EditorViewModel> _logger;
    private UpdateViewModel? _updates;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasErrorMessage))] private string? _errorMessage;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowOperationProgress))] private bool _isLoading;
    [ObservableProperty] private bool _isPreview;
    [ObservableProperty] private bool _isAdjustingThickness;
    private uint _colorBaseline;
    private double _thicknessBaseline;
    private double _blurBaseline;
    private AppSettings _appliedSettings;

    public EditorViewModel(DrawingDocument document, DrawingToolRegistry tools, IClipboardWriter clipboard,
        IFilePicker picker, ImageFileWriter files, IOcrService ocr, SettingsStore settings, ILogger<EditorViewModel> logger)
    {
        Document = document;
        _tools = tools;
        _clipboard = clipboard;
        _picker = picker;
        _files = files;
        _ocr = ocr;
        _settings = settings;
        _logger = logger;
        _appliedSettings = settings.Current;
        Toolbar = new ToolbarVisibilityViewModel(settings.Current.HiddenToolbarItems, tools.Tools);
        _colorBaseline = settings.Current.Color;
        _thicknessBaseline = settings.Current.Thickness;
        _blurBaseline = settings.Current.BlurStrength;
        History = new DocumentHistory(document);
        History.Changed += OnHistoryChanged;
        Interaction = new EditorInteraction(document, History, tools)
        {
            Color = settings.Current.Color, Thickness = settings.Current.Thickness, BlurStrength = settings.Current.BlurStrength,
            AutoSelectCreatedElements = settings.Current.AutoSelectCreatedElements, OutsideSelectionBehavior = settings.Current.OutsideSelectionBehavior
        };
        Interaction.Changed += OnInteractionChanged;
        Renderer = new DocumentRenderer(document, tools);
    }

    public void SetActiveTool(DrawingToolId? tool)
    {
        Interaction.Cancel();
        if (Interaction.IsTextEditRequested && Interaction.SelectedElement is DrawingElement text)
        {
            Interaction.SetText(text.Text);
        }
        Interaction.DeselectElement();
        Interaction.Tool = tool;
        OnPropertyChanged(nameof(Interaction));
    }

    public void SetDrawingColor(uint color)
    {
        _colorBaseline = _settings.Current.Color;
        Interaction.Color = color;
        OnPropertyChanged(nameof(Interaction));
    }

    public void SetDrawingThickness(double thickness)
    {
        _thicknessBaseline = _settings.Current.Thickness;
        Interaction.Thickness = Math.Clamp(thickness, DrawingDefaults.MinimumThickness, DrawingDefaults.MaximumThickness);
    }

    public void AdjustDrawingSize(double delta, object? wheelGroup = null)
    {
        if (Interaction.SizeTool == DrawingToolId.Blur)
        {
            _blurBaseline = _settings.Current.BlurStrength;
        }
        else
        {
            _thicknessBaseline = _settings.Current.Thickness;
        }
        Interaction.AdjustSize(delta, wheelGroup);
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.HiddenToolbarItems.ToHashSet().SetEquals(_appliedSettings.HiddenToolbarItems))
        {
            Toolbar = new ToolbarVisibilityViewModel(settings.HiddenToolbarItems, _tools.Tools);
            OnPropertyChanged(nameof(Toolbar));
            OnPropertyChanged(nameof(LabelTools));
            OnPropertyChanged(nameof(DrawingTools));
        }
        Interaction.AutoSelectCreatedElements = settings.AutoSelectCreatedElements;
        Interaction.OutsideSelectionBehavior = settings.OutsideSelectionBehavior;
        if (settings.ShowCursor != _appliedSettings.ShowCursor)
        {
            Document.SetCursorVisible(settings.ShowCursor);
        }
        if (settings.Color != _appliedSettings.Color)
        {
            SetDrawingColor(settings.Color);
        }
        if (settings.Thickness != _appliedSettings.Thickness)
        {
            SetDrawingThickness(settings.Thickness);
        }
        if (settings.BlurStrength != _appliedSettings.BlurStrength)
        {
            _blurBaseline = settings.BlurStrength;
            Interaction.BlurStrength = settings.BlurStrength;
        }
        _appliedSettings = settings;
        OnPropertyChanged(nameof(Interaction));
    }

    public async Task RememberPreferencesAsync(CancellationToken ct)
    {
        try
        {
            await _settings.UpdateAsync(current => current with
            {
                Color = current.Color == _colorBaseline ? Interaction.Color : current.Color,
                Thickness = current.Thickness == _thicknessBaseline ? Interaction.Thickness : current.Thickness,
                BlurStrength = current.BlurStrength == _blurBaseline ? Interaction.BlurStrength : current.BlurStrength
            }, ct);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    public async Task RememberColorPickerTabAsync(ColorPickerTab tab, CancellationToken ct)
    {
        if (_settings.Current.ColorPickerTab == tab)
        {
            return;
        }
        try
        {
            await _settings.UpdateAsync(current => current with { ColorPickerTab = tab }, ct);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    public void Dispose()
    {
        if (_updates is not null)
        {
            _updates.PropertyChanged -= OnUpdateChanged;
        }
        History.Changed -= OnHistoryChanged;
        Interaction.Changed -= OnInteractionChanged;
        Renderer.Dispose();
        Document.Dispose();
    }

    [RelayCommand]
    private void Cancel()
    {
        if (Updates?.IsPreparingRestart == true)
        {
            return;
        }
        if (RecognizeCommand.IsRunning)
        {
            RecognizeCommand.Cancel();
        }
        else if (!IsLoading)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Settings()
    {
        if (!IsInputBlocked)
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        Interaction.Cancel();
        if (!IsInputBlocked)
        {
            History.Undo();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        Interaction.Cancel();
        if (!IsInputBlocked)
        {
            History.Redo();
        }
    }

    [RelayCommand]
    private async Task ToggleCursorAsync(CancellationToken ct)
    {
        if (!Document.Desktop.CanToggleCursor)
        {
            return;
        }

        Document.SetCursorVisible(!Document.ShowCursor);

        try
        {
            bool visible = Document.ShowCursor;
            await _settings.UpdateAsync(current => current with { ShowCursor = visible }, ct);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    [RelayCommand]
    private void SelectTool(DrawingTool tool)
    {
        SetActiveTool(Interaction.Tool == tool.Id ? null : tool.Id);
    }

    [RelayCommand]
    private async Task CopyAsync(CancellationToken ct)
    {
        await CompleteAsync(async image =>
        {
            await _clipboard.WriteImageAsync(image, _settings.Current.CopyFormat, ct);
            return EditorActionOutcome.Completed;
        }, ct);
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        if (IsInputBlocked || Document.Selection.IsEmpty)
        {
            return;
        }

        try
        {
            IsLoading = true;
            SaveDestination? destination = await _picker.ChooseSaveDestinationAsync(_settings.Current.SaveDirectory, _settings.Current.SaveFormat, ct);
            IsLoading = false;

            if (destination is not null)
            {
                await CompleteAsync(async image =>
                {
                    await _files.SaveAsync(image, destination, ct);
                    await _settings.UpdateAsync(current => current with
                    {
                        SaveDirectory = Path.GetDirectoryName(destination.Path),
                        SaveFormat = destination.Format
                    }, ct);
                    return EditorActionOutcome.Completed;
                }, ct);
            }
        }
        catch (Exception exception)
        {
            ReportError(exception);
            IsLoading = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RecognizeAsync(CancellationToken ct)
    {
        await CompleteAsync(async image =>
        {
            string text = await _ocr.RecognizeAsync(image, _settings.Current.OcrLanguages, ct);

            if (string.IsNullOrWhiteSpace(text))
            {
                return EditorActionOutcome.EmptyText;
            }

            await _clipboard.WriteTextAsync(text, ct);
            return EditorActionOutcome.Completed;
        }, ct);
    }

    private bool CanUndo()
    {
        return !IsInputBlocked && History.CanUndo;
    }

    private bool CanRedo()
    {
        return !IsInputBlocked && History.CanRedo;
    }

    private async Task CompleteAsync(Func<SKBitmap, Task<EditorActionOutcome>> operation, CancellationToken ct)
    {
        if (IsInputBlocked || Document.Selection.IsEmpty)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            using SKBitmap image = await Renderer.ExportAsync(CancellationToken.None);
            EditorActionOutcome outcome = await operation(image);
            IsLoading = false;

            if (outcome == EditorActionOutcome.Completed)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = UiText.Get("Kadr.NoTextFound");
            }
        }
        catch (OperationCanceledException)
        {
            IsLoading = false;
        }
        catch (Exception exception)
        {
            ReportError(exception);
            IsLoading = false;
        }
    }

    private void ReportError(Exception exception)
    {
        _logger.LogError(exception, "The screenshot action did not complete");
        ErrorMessage = UiText.Get("Kadr.ActionFailed");
    }

    partial void OnIsLoadingChanged(bool value)
    {
        NotifyInputStateChanged();
    }

    private void OnUpdateChanged(object? sender, PropertyChangedEventArgs arguments)
    {
        if (arguments.PropertyName == nameof(UpdateViewModel.IsPreparingRestart))
        {
            NotifyInputStateChanged();
        }
    }

    private void NotifyInputStateChanged()
    {
        OnPropertyChanged(nameof(IsInputBlocked));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void OnHistoryChanged(object? sender, EventArgs arguments)
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void OnInteractionChanged(object? sender, EventArgs arguments)
    {
        OnPropertyChanged(nameof(Interaction));
    }
}
