using System.Diagnostics;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;

using SkiaSharp;

using Kadr.Desktop.Controls;
using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;

namespace Kadr.Desktop.Services.Diagnostics;

internal static class EditorPerformanceProbe
{
    internal static async Task RunAsync(Func<CapturedDesktop, EditorViewModel> createEditor,
        SettingsStore settings, string path, CancellationToken ct)
    {
        DesktopMonitor monitor = NativeDesktop.GetMonitors()[0];
        List<object> sessions = [];

        for (int trial = 0; trial < 4; trial++)
        {
            SKBitmap image = new((int)monitor.Bounds.Width, (int)monitor.Bounds.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (SKCanvas canvas = new(image))
            {
                canvas.Clear(new SKColor(35, 48, 68));
            }

            using EditorViewModel model = createEditor(new CapturedDesktop(image, new DesktopMonitor[] { monitor }, monitor.Bounds,
                null, true, new CaptureMetrics(0, 0, 0, Array.Empty<long>())));
            EditorWindow window = new(model, monitor, settings)
            {
                ShowActivated = false
            };
            List<(double At, double Interval)> frames = [];
            List<double> presented = [];
            List<double> updates = [];
            object renderSync = new();
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            long start = Stopwatch.GetTimestamp();
            double previous = 0;
            bool drawing = trial > 0;
            if (drawing)
            {
                model.Document.SetSelection(model.Document.Bounds);
                model.Interaction.Tool = trial == 2 ? DrawingToolId.Arrow : DrawingToolId.Pencil;
                model.Interaction.Begin(new PixelPoint(100, 100), 5);
            }
            ScreenshotCanvas surface = window.FindControl<ScreenshotCanvas>("Surface") ?? throw new InvalidOperationException("Editor canvas is missing.");
            surface.FrameRendered = () =>
            {
                lock (renderSync)
                {
                    presented.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
            };

            void Frame(TimeSpan timestamp)
            {
                try
                {
                    double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    frames.Add((elapsed, elapsed - previous));
                    previous = elapsed;
                    if (elapsed >= 3000)
                    {
                        completion.TrySetResult();
                        return;
                    }

                    long updateStart = Stopwatch.GetTimestamp();
                    if (drawing)
                    {
                        for (int sample = 0; sample < 8; sample++)
                        {
                            double progress = Math.Clamp(elapsed / 3000, 0, 1);
                            model.Interaction.Update(new PixelPoint(120 + progress * (model.Document.Bounds.Width - 240),
                                200 + Math.Sin((frames.Count * 8 + sample) / 80d) * 80));
                        }
                    }
                    else
                    {
                        double width = Math.Min(model.Document.Bounds.Width - 200, 400 + (frames.Count % 400));
                        double height = Math.Min(model.Document.Bounds.Height - 200, 300);
                        model.Document.SetSelection(new PixelRectangle(100, 100, width, height));
                    }
                    updates.Add(Stopwatch.GetElapsedTime(updateStart).TotalMilliseconds);
                    window.RequestAnimationFrame(Frame);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }

            try
            {
                window.Show();
                window.RequestAnimationFrame(Frame);
                await completion.Task.WaitAsync(ct);
                double[] early = frames.Where(frame => (frame.At < 2000) && (frame.At != frames[0].At))
                    .Select(frame => frame.Interval).Order().ToArray();
                double[] late = frames.Where(frame => frame.At >= 2000).Select(frame => frame.Interval).Order().ToArray();
                double[] renderIntervals;
                lock (renderSync)
                {
                    renderIntervals = presented.Zip(presented.Skip(1), (first, second) => second - first).Order().ToArray();
                }
                updates.Sort();
                bool paletteVerified = false;
                if (trial == 3)
                {
                    model.Interaction.Complete(model.Document.Elements[^1].Points[^1], false);
                    await Task.Delay(20, ct);
                    Button color = window.FindControl<Button>("ColorButton") ?? throw new InvalidOperationException("Color button is missing.");
                    PalettePanel palette = window.FindControl<PalettePanel>("Palette") ?? throw new InvalidOperationException("Palette is missing.");
                    color.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(300, ct);
                    bool opened = palette.Reveal == 1 && palette.Opacity == 1 && palette.IsHitTestVisible;
                    color.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(300, ct);
                    paletteVerified = opened && palette.Reveal == 0 && palette.Opacity == 0 && !palette.IsHitTestVisible;
                    if (!paletteVerified)
                    {
                        throw new InvalidOperationException("The native palette animation did not complete.");
                    }
                }
                sessions.Add(new
                {
                    Trial = trial + 1,
                    Scenario = drawing ? model.Interaction.Tool.ToString() : "Selection",
                    PaletteAnimationVerified = paletteVerified,
                    RenderedFrames = renderIntervals.Length + 1,
                    RenderMedianMs = renderIntervals.Length == 0 ? 0 : renderIntervals[renderIntervals.Length / 2],
                    RenderP95Ms = renderIntervals.Length == 0 ? 0 : renderIntervals[(int)((renderIntervals.Length - 1) * 0.95)],
                    UpdateP95Ms = updates[(int)((updates.Count - 1) * 0.95)],
                    FrameCount = frames.Count,
                    FirstFrameMs = frames[0].At,
                    EarlyP95Ms = early.Length == 0 ? 0 : early[(int)((early.Length - 1) * 0.95)],
                    LateP95Ms = late.Length == 0 ? 0 : late[(int)((late.Length - 1) * 0.95)],
                    MaximumIntervalMs = frames.Skip(1).Max(frame => frame.Interval)
                });
            }
            finally
            {
                window.Close();
            }
        }

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(sessions, new JsonSerializerOptions { WriteIndented = true }), ct);
    }
}
