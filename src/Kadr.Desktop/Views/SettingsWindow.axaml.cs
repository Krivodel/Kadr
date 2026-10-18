using System.ComponentModel;
using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Krivodeling.Localization.Avalonia;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Input;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;

namespace Kadr.Desktop.Views;

public sealed partial class SettingsWindow : Window
{
    internal bool IsRecordingShortcut => _recording is not null;

    private Button? _recording;
    private ShortcutRecorder? _recorder;
    private object? _originalContent;

    public SettingsWindow()
    {
        InitializeComponent();
        AddHandler(Button.ClickEvent, OnContentButtonClick);
        Opened += (sender, arguments) => { WindowSizing.FitToScreen(this); };
        Closed += OnClosed;
        AddHandler(KeyDownEvent, OnSettingsKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnSettingsKeyUp, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnSettingsPointerPressed, RoutingStrategies.Tunnel);
        LocalizationText.Instance.PropertyChanged += OnLocalizationChanged;
        RecordingHintPanel.Hidden += (sender, arguments) =>
        {
            ShortcutRecordingHint.IsOpen = false;
            ShortcutRecordingHint.PlacementTarget = null;
        };
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs arguments)
    {
        if (DataContext is SettingsViewModel model)
        {
            model.RefreshLocalization();
        }
    }

    private void OnContentButtonClick(object? sender, RoutedEventArgs arguments)
    {
        if (arguments.Source is Button button)
        {
            if (button.Classes.Contains("navigation"))
            {
                CancelRecording();
                Controls.Scrolling.SmoothScrollBehavior.ResetOffset(SectionScroll);
            }
            else if (button.DataContext is ShortcutBindingViewModel && button.Command is null)
            {
                OnRecordShortcut(button, arguments);
            }
        }
    }

    private void OnSettingsKeyDown(object? sender, KeyEventArgs arguments)
    {
        if (_recorder is ShortcutRecorder recorder)
        {
            recorder.Session.Press(KeyboardGestures.FromKey(arguments), Stopwatch.GetElapsedTime(0));
            arguments.Handled = true;
            return;
        }
        if (!IsRecordingShortcut && DataContext is SettingsViewModel model
            && (arguments.Key == Key.Escape || model.ExitGestures.Any(gesture => KeyboardGestures.Matches(arguments, gesture))))
        {
            arguments.Handled = true;
            Close();
        }
    }

    private void OnSettingsKeyUp(object? sender, KeyEventArgs arguments)
    {
        if (_recorder is ShortcutRecorder recorder)
        {
            recorder.Session.Release(KeyboardGestures.FromKey(arguments).Key, Stopwatch.GetElapsedTime(0));
            arguments.Handled = true;
        }
    }

    private void OnSettingsPointerPressed(object? sender, PointerPressedEventArgs arguments)
    {
        int key = KeyboardGestures.GetPressedMouseKey(arguments);
        if (!IsRecordingShortcut && DataContext is SettingsViewModel model
            && key != 0 && model.ExitGestures.Any(gesture => KeyboardGestures.MatchesMouse(arguments.KeyModifiers, gesture, key)))
        {
            arguments.Handled = true;
            Close();
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs arguments)
    {
        Close();
    }

    private void ApplyShortcut(ShortcutGesture gesture)
    {
        if ((_recording is not Button button) || (DataContext is not SettingsViewModel model))
        {
            return;
        }

        ShortcutBindingViewModel? binding = button.DataContext as ShortcutBindingViewModel;
        CancelRecording();

        if (binding is null || ((gesture.Key == 0x1b) && (gesture.Modifiers == ShortcutModifiers.None)
            && (binding.Action != Services.Settings.EditorAction.Cancel)))
        {
            return;
        }

        if (button.Tag as string == "alternative")
        {
            binding.AlternativeGesture = gesture;
        }
        else
        {
            binding.Gesture = gesture;
        }
        button.SetCurrentValue(ContentProperty, gesture.DisplayName);
    }

    private void CancelRecording()
    {
        RecordingHintPanel.SetVisible(false);
        if (_recording is Button button)
        {
            button.SetCurrentValue(ContentProperty, _originalContent);
        }

        _recording = null;
        _recorder?.Dispose();
        _recorder = null;

        if (DataContext is SettingsViewModel model)
        {
            model.IsRecordingShortcut = false;
        }
    }

    private void OnRecordShortcut(object? sender, RoutedEventArgs arguments)
    {
        if ((sender is Button button) && (DataContext is SettingsViewModel model))
        {
            bool cancel = ReferenceEquals(_recording, button);
            CancelRecording();

            if (cancel)
            {
                return;
            }

            _recording = button;
            _originalContent = button.Content;
            button.SetCurrentValue(ContentProperty, UiText.Get("Kadr.RecordShortcut"));
            model.IsRecordingShortcut = true;

            try
            {
                ShortcutRecordingMode mode = button.DataContext is ShortcutBindingViewModel { Action: EditorAction.Preview }
                    ? ShortcutRecordingMode.Hold : ShortcutRecordingMode.Command;
                ShortcutRecordingSession session = new(ApplyShortcut, ClearShortcut, CancelRecording, mode);
                _recorder = new ShortcutRecorder(session);
                ShowRecordingHint(button);
                RequestAnimationFrame(timestamp => UpdateRecordingHint(session));
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                CancelRecording();
                model.ReportShortcutRecordingFailure(exception);
            }
        }
    }

    private void ClearShortcut()
    {
        if (_recording is not Button button || button.DataContext is not ShortcutBindingViewModel binding)
        {
            return;
        }
        if (button.Tag as string == "alternative")
        {
            binding.AlternativeGesture = null;
        }
        else
        {
            binding.Gesture = ShortcutGesture.Empty;
        }
        _originalContent = string.Empty;
        button.SetCurrentValue(ContentProperty, string.Empty);
        RecordingHintPanel.SetVisible(false);
    }

    private void ShowRecordingHint(Button button)
    {
        ClearShortcutProgress.IsVisible = false;
        ClearShortcutProgress.Value = 0;
        ShortcutRecordingHint.PlacementTarget = button;
        ShortcutRecordingHint.IsOpen = true;
        RecordingHintPanel.SetVisible(true);
    }

    private void UpdateRecordingHint(ShortcutRecordingSession session)
    {
        if (_recorder?.Session != session)
        {
            return;
        }
        session.Tick(Stopwatch.GetElapsedTime(0));
        ClearShortcutProgress.IsVisible = session.IsHoldingSpace;
        ClearShortcutProgress.Value = session.ClearProgress;
        RequestAnimationFrame(timestamp => UpdateRecordingHint(session));
    }

    private void OnClosed(object? sender, EventArgs arguments)
    {
        LocalizationText.Instance.PropertyChanged -= OnLocalizationChanged;
        if (DataContext is SettingsViewModel model)
        {
            model.SaveCommand.Cancel();
        }

        CancelRecording();
    }
}
