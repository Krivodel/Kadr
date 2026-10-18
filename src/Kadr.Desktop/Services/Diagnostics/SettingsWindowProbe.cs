using System.Runtime.InteropServices;
using System.Text.Json;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Input;

using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Services.Capture;
using PixelPoint = Kadr.Desktop.Services.Geometry.PixelPoint;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.Views;

namespace Kadr.Desktop.Services.Diagnostics;

internal static class SettingsWindowProbe
{
    internal static async Task RunAsync(SettingsViewModel model, string path, CancellationToken ct)
    {
        SettingsWindow window = new() { DataContext = model, ShowActivated = true, Topmost = true };
        PixelPoint originalPointer = NativeDesktop.GetPointerPosition();
        try
        {
            window.Show();
            await Task.Delay(250, ct);
            nint handle = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("Settings HWND is missing.");
            long style = GetWindowLongPtrW(handle, -16).ToInt64();
            bool nativeFrame = (style & 0x00cf0000) == 0x00cf0000;
            int[] bounds = new int[4];
            GetWindowRect(handle, bounds);
            int point = ((bounds[0] + 1) & 0xffff) | (((bounds[1] + bounds[3]) / 2) << 16);
            long edge = SendMessageW(handle, 0x84, 0, point).ToInt64();
            int captionPoint = ((bounds[0] + 220) & 0xffff) | ((bounds[1] + 24) << 16);
            long caption = SendMessageW(handle, 0x84, 0, captionPoint).ToInt64();
            Size before = window.ClientSize;
            window.Width += 40;
            await Task.Delay(150, ct);
            bool resize = window.ClientSize.Width > before.Width;
            window.WindowState = WindowState.Maximized;
            await Task.Delay(300, ct);
            bool maximized = window.WindowState == WindowState.Maximized;
            window.WindowState = WindowState.Normal;
            await Task.Delay(300, ct);
            window.Activate();
            window.WindowState = WindowState.Minimized;
            await Task.Delay(250, ct);
            bool minimized = window.WindowState == WindowState.Minimized;
            window.WindowState = WindowState.Normal;
            await Task.Delay(300, ct);
            window.Activate();
            await Task.Delay(80, ct);
            Visual root = window.GetVisualAncestors().LastOrDefault() ?? window;
            Button[] captionButtons = root.GetVisualDescendants().OfType<Button>().Where(button => button.Name is "PART_CloseButton" or "PART_MinimizeButton" or "PART_MaximizeButton").ToArray();
            ComboBox combo = window.GetVisualDescendants().OfType<ComboBox>().First(box => box.Name == "CaptureModeBox");
            ScrollViewer settingsScroll = window.FindControl<ScrollViewer>("SectionScroll") ?? throw new InvalidOperationException();
            settingsScroll.Offset = new Vector(0, Math.Min(30, settingsScroll.Extent.Height - settingsScroll.Viewport.Height));
            await Task.Delay(100, ct);
            Vector offsetBefore = settingsScroll.Offset;
            combo.IsDropDownOpen = true;
            await Task.Delay(150, ct);
            Popup popup = combo.GetTemplateDescendants().OfType<Popup>().Single();
            bool overlay = popup.IsUsingOverlayLayer;
            Border? popupBorder = popup.Child as Border ?? popup.Child?.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Name == "PopupBorder");
            bool popupAppearance = popupBorder?.Background is Avalonia.Media.ISolidColorBrush background
                && background.Color == Avalonia.Media.Color.Parse("#26354E") && popupBorder.CornerRadius.TopLeft == 10;
            bool scrollStable = settingsScroll.Offset == offsetBefore;
            bool hover = true;
            List<object> hoverStates = [];
            for (int index = 0; index < 2; index++)
            {
                ComboBoxItem item = (ComboBoxItem)(combo.ContainerFromIndex(index) ?? throw new InvalidOperationException());
                Avalonia.PixelPoint position = item.PointToScreen(new Point(20, item.Bounds.Height / 2));
                MovePointer(position);
                await Task.Delay(150, ct);
                ContentPresenter presenter = item.GetVisualDescendants().OfType<ContentPresenter>().First(control => control.Name == "PART_ContentPresenter");
                Color expected = Color.Parse(index == combo.SelectedIndex ? "#605184" : "#3E5273");
                hoverStates.Add(new { Index = index, Over = item.IsPointerOver, Color = (presenter.Background as ISolidColorBrush)?.Color.ToString(),
                    Expected = expected.ToString(), Screen = position.ToString(), ActualPointer = NativeDesktop.GetPointerPosition().ToString(),
                    Window = handle.ToInt64(), HitWindow = WindowFromPoint(((long)(uint)position.Y << 32) | (uint)position.X).ToInt64(),
                    Hit = window.InputHitTest(window.PointToClient(position))?.GetType().Name });
                hover &= item.IsPointerOver && presenter.Background is ISolidColorBrush brush && brush.Color == expected;
            }
            string imagePath = Path.ChangeExtension(path, ".png");
            using (RenderTargetBitmap bitmap = new(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96)))
            {
                bitmap.Render(root);
                bitmap.Save(imagePath, PngBitmapEncoderOptions.Default);
            }
            combo.IsDropDownOpen = false;
            Button minimize = captionButtons.Single(button => button.Name == "PART_MinimizeButton");
            Avalonia.PixelPoint buttonPoint = minimize.PointToScreen(new Point(minimize.Bounds.Width / 2, minimize.Bounds.Height / 2));
            MovePointer(buttonPoint);
            await Task.Delay(120, ct);
            nint hand = LoadCursorW(0, 32649);
            bool handStable = true;
            List<long> cursorHandles = [];
            for (int frame = 0; frame < 12; frame++)
            {
                nint actualCursor = NativeDesktop.GetSystemCursorHandle();
                cursorHandles.Add(actualCursor.ToInt64());
                handStable &= actualCursor == hand;
                await Task.Delay(8, ct);
            }
            bool squareButtons = captionButtons.All(button => button.CornerRadius.TopLeft == 0);
            NativeDesktop.RestorePointer(originalPointer);
            bool colorPickersVerified = true;
            List<object> colorPickerStates = [];
            foreach (ColorPickerTab pickerTab in Enum.GetValues<ColorPickerTab>())
            {
                ColorPickerWindow picker = new(0xff3b82f6, pickerTab) { ShowActivated = false };
                try
                {
                    picker.Show();
                    await Task.Delay(150, ct);
                    Visual pickerRoot = picker.GetVisualAncestors().LastOrDefault() ?? picker;
                    using RenderTargetBitmap bitmap = new(new PixelSize((int)pickerRoot.Bounds.Width, (int)pickerRoot.Bounds.Height), new Vector(96, 96));
                    bitmap.Render(pickerRoot);
                    bitmap.Save(Path.ChangeExtension(path, $"{pickerTab}.png"), PngBitmapEncoderOptions.Default);
                    Button[] pickerCaption = pickerRoot.GetVisualDescendants().OfType<Button>().Where(button => button.Name is "PART_CloseButton" or "PART_MinimizeButton" or "PART_MaximizeButton").ToArray();
                    ColorPickerViewModel colorModel = (ColorPickerViewModel)(picker.DataContext ?? throw new InvalidOperationException());
                    int footerButtons = picker.GetVisualDescendants().OfType<Button>().Count(button => ReferenceEquals(button.Command, colorModel.AcceptCommand) || ReferenceEquals(button.Command, colorModel.CancelCommand));
                    bool channels = picker.FindControl<NumericUpDown>("RedInput")?.Value == 59 && picker.FindControl<NumericUpDown>("AlphaInput")?.Value == 255;
                    if (pickerCaption.Length == 1)
                    {
                        pickerCaption[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    }
                    await Task.Delay(30, ct);
                    bool closes = !picker.IsVisible;
                    colorPickersVerified &= pickerCaption.Length == 1 && footerButtons == 2 && channels && closes;
                    colorPickerStates.Add(new { Tab = pickerTab.ToString(), CaptionButtons = pickerCaption.Length, FooterButtons = footerButtons, ChannelsCorrect = channels, CloseWorks = closes });
                }
                finally
                {
                    picker.Close();
                }
            }
            Button save = window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, model.SaveCommand));
            ScrollBar verticalBar = settingsScroll.GetVisualDescendants().OfType<ScrollBar>().Single(control => control.Orientation == Avalonia.Layout.Orientation.Vertical);
            double footerRight = save.TranslatePoint(new Point(save.Bounds.Width, 0), window)?.X ?? throw new InvalidOperationException();
            double scrollbarLeft = verticalBar.TranslatePoint(default, window)?.X ?? throw new InvalidOperationException();
            bool footerAligned = footerRight <= scrollbarLeft;
            bool passed = nativeFrame && edge == 10 && caption == 2 && resize && maximized && minimized && overlay && popupAppearance
                && footerAligned && captionButtons.Length == 3 && hover && scrollStable && handStable && squareButtons && colorPickersVerified;
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                Passed = passed, FooterLeftOfScrollbar = footerAligned, FooterRight = footerRight, ScrollbarLeft = scrollbarLeft, NativeStyle = $"0x{style:X}", NativeFrame = nativeFrame, ResizeHitTest = edge, CaptionHitTest = caption,
                Resize = resize, Maximized = maximized, Minimized = minimized, OverlayPopup = overlay,
                PopupAppearance = popupAppearance, PopupBorderName = popupBorder?.Name, PopupCornerRadius = popupBorder?.CornerRadius.TopLeft,
                CaptionButtons = captionButtons.Length, ViewportHeight = window.FindControl<ScrollViewer>("SectionScroll")?.Viewport.Height,
                HoverVerified = hover, ScrollStable = scrollStable, CaptionHandStable = handStable, SquareCaptionButtons = squareButtons,
                HoverStates = hoverStates,
                CaptionPoint = buttonPoint.ToString(), CaptionHitWindow = WindowFromPoint(((long)(uint)buttonPoint.Y << 32) | (uint)buttonPoint.X).ToInt64(),
                CaptionButtonHit = SendMessageW(handle, 0x84, 0, (buttonPoint.X & 0xffff) | (buttonPoint.Y << 16)).ToInt64(),
                CaptionButtonOver = minimize.IsPointerOver, CaptionRole = WindowDecorationProperties.GetElementRole(minimize).ToString(),
                HandHandle = hand.ToInt64(), CursorHandles = cursorHandles,
                WindowActive = window.IsActive, ColorPickersVerified = colorPickersVerified, ColorPickerStates = colorPickerStates,
                Image = imagePath
            }, new JsonSerializerOptions { WriteIndented = true }), ct);
            if (!passed)
            {
                throw new InvalidOperationException($"Native settings verification failed; see {path}.");
            }

        }
        finally
        {
            NativeDesktop.RestorePointer(originalPointer);
            window.Close();
        }
    }

    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, [Out] int[] rectangle);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nint parameter, nint data);
    [DllImport("user32.dll")] private static extern nint LoadCursorW(nint instance, nint name);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(long position);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, NativeMouseInput[] inputs, int size);

    private static void MovePointer(Avalonia.PixelPoint position)
    {
        NativeDesktop.RestorePointer(new PixelPoint(position.X, position.Y));
        NativeMouseInput[] inputs = new NativeMouseInput[] { new NativeMouseInput { X = 1, Flags = 1 } };
        if (SendInput(1, inputs, Marshal.SizeOf<NativeMouseInput>()) != 1)
        {
            throw new InvalidOperationException("The native pointer verification input could not be submitted.");
        }
    }
}
