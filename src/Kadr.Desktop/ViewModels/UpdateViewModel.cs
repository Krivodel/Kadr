using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Errors;
using Kadr.Desktop.Services.Updates;

namespace Kadr.Desktop.ViewModels;

public sealed partial class UpdateViewModel : ObservableObject, IDisposable
{
    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool ShowConfirmation => !IsLoading;
    public string Title => UiText.Format("Kadr.UpdateAvailable", Version);

    private readonly IUpdateService _service;
    private readonly Func<CancellationToken, Task<string?>> _saveSession;
    private readonly Func<CancellationToken, Task>? _waitForEditor;
    private readonly Action _restart;
    private readonly IViewModelErrorHandler _errors;
    private readonly Action<string>? _discardSession;
    private readonly CancellationTokenSource _stop = new();
    private bool _applying;
    private bool _disposed;
    private int _operation;
    private string? _statusKey;
    private Exception? _lastError;

    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(Title))] private string _version = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowConfirmation))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isLoading;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(DismissCommand))] private bool _isPreparingRestart;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private double _percentage;
    [ObservableProperty] private string _status = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasErrorMessage))] private string? _errorMessage;

    public UpdateViewModel(IUpdateService service, Func<CancellationToken, Task<string?>> saveSession,
        Action restart, IViewModelErrorHandler errors, Action<string>? discardSession = null,
        Func<CancellationToken, Task>? waitForEditor = null)
    {
        _service = service;
        _saveSession = saveSession;
        _restart = restart;
        _errors = errors;
        _discardSession = discardSession;
        _waitForEditor = waitForEditor;
    }

    public void SetRelease(UpdateRelease? release)
    {
        if (!IsLoading)
        {
            Version = release?.Version ?? "";
            IsAvailable = release is not null;
        }
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Title));
        if (_statusKey is not null)
        {
            Status = UiText.Get(_statusKey);
        }
        if (_lastError is not null)
        {
            ErrorMessage = _errors.GetUserMessage(_lastError);
        }
    }

    private void SetStatus(string key)
    {
        _statusKey = key;
        Status = UiText.Get(key);
    }

    [RelayCommand]
    private void Open()
    {
        if (IsAvailable)
        {
            RefreshLocalization();
            IsOpen = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDismiss))]
    private void Dismiss()
    {
        if (!IsPreparingRestart)
        {
            IsOpen = false;
        }
    }

    private bool CanDismiss()
    {
        return !IsPreparingRestart;
    }

    private bool CanApply()
    {
        return IsAvailable && !IsLoading;
    }

    partial void OnIsAvailableChanged(bool value)
    {
        ApplyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (!CanApply())
        {
            return;
        }
        IsLoading = true;
        ErrorMessage = null;
        _lastError = null;
        IsIndeterminate = false;
        Percentage = 0;
        SetStatus("Kadr.UpdateDownloading");
        int operation = ++_operation;
        string? sessionId = null;
        bool installStarted = false;
        try
        {
            Progress<UpdateProgress> progress = new(value =>
            {
                if (!_applying && IsLoading && operation == _operation)
                {
                    Percentage = value.Percentage;
                    IsIndeterminate = value.IsVerifying;
                    SetStatus(value.IsVerifying ? "Kadr.UpdateVerifying" : "Kadr.UpdateDownloading");
                }
            });
            await _service.DownloadAsync(progress, _stop.Token);
            if (_waitForEditor is not null)
            {
                await _waitForEditor(_stop.Token);
            }
            _stop.Token.ThrowIfCancellationRequested();
            _applying = true;
            IsPreparingRestart = true;
            IsIndeterminate = true;
            SetStatus("Kadr.UpdateSavingSession");
            sessionId = await _saveSession(_stop.Token);
            SetStatus("Kadr.UpdateRestarting");
            await _service.InstallAsync(sessionId, _stop.Token);
            installStarted = true;
            _restart();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _errors.Log(exception, "Apply");
            _lastError = exception;
            ErrorMessage = _errors.GetUserMessage(exception);
            IsOpen = true;
        }
        finally
        {
            if (sessionId is not null && !installStarted)
            {
                try
                {
                    _discardSession?.Invoke(sessionId);
                }
                catch (Exception exception)
                {
                    _errors.Log(exception, "Discard session");
                }
            }
            IsPreparingRestart = false;
            IsLoading = false;
            _applying = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _stop.Cancel();
        _stop.Dispose();
    }
}
