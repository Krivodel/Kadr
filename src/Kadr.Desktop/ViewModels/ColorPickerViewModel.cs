using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Settings;

namespace Kadr.Desktop.ViewModels;

public sealed partial class ColorPickerViewModel : ObservableObject
{
    public uint OriginalColor { get; }
    public uint ColorValue => _color;
    public bool HasHexError => !ColorMath.TryParseRgbaHex(Hex, out _);
    public double Red
    {
        get => (_color >> 16) & 255;
        set => SetColor((_color & 0xff00ffff) | ((uint)Math.Clamp(Math.Round(value), 0, 255) << 16));
    }
    public double Green
    {
        get => (_color >> 8) & 255;
        set => SetColor((_color & 0xffff00ff) | ((uint)Math.Clamp(Math.Round(value), 0, 255) << 8));
    }
    public double Blue
    {
        get => _color & 255;
        set => SetColor((_color & 0xffffff00) | (uint)Math.Clamp(Math.Round(value), 0, 255));
    }
    public double Alpha
    {
        get => _color >> 24;
        set
        {
            _color = (_color & 0xffffff) | ((uint)Math.Clamp(Math.Round(value), 0, 255) << 24);
            UpdateHex();
        }
    }
    public double Hue
    {
        get => _hue;
        set => SetHsv(value, _saturation, _value);
    }
    public double Saturation
    {
        get => _saturation * 100;
        set => SetHsv(_hue, value / 100, _value);
    }
    public double Brightness => _value;
    public double HslSaturation
    {
        get => ColorMath.ToHsl(_color).Saturation * 100;
        set => SetColor(ColorMath.FromHsl(_hue, value / 100, Lightness / 100, (byte)Alpha));
    }
    public double Lightness
    {
        get => ColorMath.ToHsl(_color).Lightness * 100;
        set => SetColor(ColorMath.FromHsl(_hue, HslSaturation / 100, value / 100, (byte)Alpha));
    }
    public bool IsRgbMode => SelectedTab == 0;
    public bool IsHslMode => SelectedTab == 1;
    public bool IsHexMode => SelectedTab == 2;
    public event EventHandler<ColorChoiceEventArgs>? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRgbMode))]
    [NotifyPropertyChangedFor(nameof(IsHslMode))]
    [NotifyPropertyChangedFor(nameof(IsHexMode))]
    private int _selectedTab;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHexError))]
    private string _hex = string.Empty;
    private uint _color;
    private double _hue;
    private double _saturation;
    private double _value;
    private bool _updating;

    public ColorPickerViewModel(uint color, ColorPickerTab selectedTab = ColorPickerTab.Rgb)
    {
        OriginalColor = color;
        _selectedTab = (int)selectedTab;
        SetColor(color);
    }

    public void SetColor(uint color)
    {
        _color = color;
        (double hue, double saturation, double value) = ColorMath.ToHsv(color);
        if (saturation > 0)
        {
            _hue = hue;
        }
        _saturation = saturation;
        _value = value;
        UpdateHex();
    }

    public void SetHsv(double hue, double saturation, double value)
    {
        _hue = ((hue % 360) + 360) % 360;
        _saturation = Math.Clamp(saturation, 0, 1);
        _value = Math.Clamp(value, 0, 1);
        _color = ColorMath.FromHsv(_hue, _saturation, _value, (byte)Alpha);
        UpdateHex();
    }

    private void UpdateHex()
    {
        _updating = true;
        Hex = ColorMath.ToRgbaHex(_color);
        _updating = false;
        OnPropertyChanged(string.Empty);
    }

    partial void OnHexChanged(string value)
    {
        if (!_updating && ColorMath.TryParseRgbaHex(value, out uint color))
        {
            SetColor(color);
        }
    }

    [RelayCommand]
    private void RestoreOriginal()
    {
        SetColor(OriginalColor);
    }

    [RelayCommand]
    private void SelectTab(string value)
    {
        SelectedTab = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void Accept()
    {
        if (!HasHexError)
        {
            CloseRequested?.Invoke(this, new ColorChoiceEventArgs(_color));
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, new ColorChoiceEventArgs(null));
    }
}
