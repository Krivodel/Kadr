using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using Kadr.Desktop.Controls;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;

namespace Kadr.Desktop.Views;

public sealed partial class ColorPickerWindow : Window
{
    public ColorPickerTab SelectedTab => (ColorPickerTab)_model.SelectedTab;

    private readonly ColorPickerViewModel _model;

    public ColorPickerWindow(uint initial, ColorPickerTab selectedTab = ColorPickerTab.Rgb)
    {
        InitializeComponent();
        _model = new ColorPickerViewModel(initial, selectedTab);
        DataContext = _model;
        OldColor.ColorValue = initial;
        HslHueSlider.TrackBrush = new LinearGradientBrush { GradientStops = ColorBrushes.CreateHueStops(), StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative) };
        Opened += (sender, arguments) => WindowSizing.FitToScreen(this);
        Spectrum.ColorChanged += (sender, arguments) => _model.SetHsv(Spectrum.Hue, Spectrum.Saturation, Spectrum.Value);
        _model.PropertyChanged += OnColorChanged;
        _model.CloseRequested += OnCloseRequested;
        KeyDown += (sender, arguments) =>
        {
            if (arguments.Key == Key.Escape)
            {
                _model.CancelCommand.Execute(null);
                arguments.Handled = true;
            }
        };
        UpdateAppearance();
    }

    private void OnColorChanged(object? sender, PropertyChangedEventArgs arguments)
    {
        if (string.IsNullOrEmpty(arguments.PropertyName))
        {
            UpdateAppearance();
        }
    }

    private void OnCloseRequested(object? sender, ColorChoiceEventArgs arguments)
    {
        Close(arguments.Color);
    }

    private void UpdateAppearance()
    {
        uint color = _model.ColorValue;
        Spectrum.SetHsv(_model.Hue, _model.Saturation / 100, _model.Brightness);
        NewColor.ColorValue = color;
        HslHueSlider.MarkerColor = Color.FromUInt32(ColorMath.FromHsv(_model.Hue, 1, 1, 255));
        HslSaturationSlider.MarkerColor = LightnessSlider.MarkerColor = Color.FromUInt32(color | 0xff000000);
        AlphaSlider.MarkerColor = Color.FromUInt32(color | 0xff000000);
        RedSlider.MarkerColor = Color.Parse("#FF4C5E");
        GreenSlider.MarkerColor = Color.Parse("#00DD6F");
        BlueSlider.MarkerColor = Color.Parse("#1677FF");
        SetGradient(RedSlider, Color.FromUInt32(color & 0xff00ffff | 0xff000000), Color.FromUInt32(color | 0xffff0000));
        SetGradient(GreenSlider, Color.FromUInt32(color & 0xffff00ff | 0xff000000), Color.FromUInt32(color | 0xff00ff00));
        SetGradient(BlueSlider, Color.FromUInt32(color & 0xffffff00 | 0xff000000), Color.FromUInt32(color | 0xff0000ff));
        SetGradient(AlphaSlider, Color.FromUInt32(color & 0x00ffffff), Color.FromUInt32(color | 0xff000000));
        SetGradient(HslSaturationSlider, Color.FromUInt32(ColorMath.FromHsl(_model.Hue, 0, _model.Lightness / 100, 255)), Color.FromUInt32(ColorMath.FromHsl(_model.Hue, 1, _model.Lightness / 100, 255)));
        LinearGradientBrush lightness = ColorBrushes.CreateGradient(Colors.Black, Colors.White);
        lightness.GradientStops.Insert(1, new GradientStop(Color.FromUInt32(ColorMath.FromHsl(_model.Hue, _model.HslSaturation / 100, 0.5, 255)), 0.5));
        LightnessSlider.TrackBrush = lightness;
        LightnessSlider.InvalidateVisual();
    }

    private static void SetGradient(ColorSlider slider, Color start, Color end)
    {
        slider.TrackBrush = ColorBrushes.CreateGradient(start, end);
        slider.InvalidateVisual();
    }
}
