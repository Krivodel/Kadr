using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;

using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Rendering;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class RenderingPerformanceTests
{
    [Fact]
    public void Update_LongFreehandGesture_AllocationsGrowWithoutCopyingWholeStroke()
    {
        using DrawingDocument document = TestDocuments.Create(3840, 2160);
        document.SetSelection(document.Bounds);
        EditorInteraction interaction = new(document, new DocumentHistory(document))
        {
            Tool = DrawingToolId.Pencil
        };
        interaction.Begin(new PixelPoint(100, 100), 5);
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int index = 1; index <= 10_000; index++)
        {
            interaction.Update(new PixelPoint(100 + (index % 3000), 100 + (index % 1000)));
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(10_002, Assert.Single(document.Elements).Points.Count);
        Assert.True(allocated < 32_000_000, $"The stroke allocated {allocated} bytes.");
    }

    [Fact]
    public void CreateScene_MovingAndResizingOnFourKDocument_RecordsLatencyWithoutRasterizingDesktop()
    {
        using DrawingDocument document = TestDocuments.Create(3840, 2160);
        document.SetSelection(new PixelRectangle(600, 400, 1200, 800));
        ImmutableArray<DrawingElement> baseline = Enumerable.Range(0, 30).Select(index =>
            TestDocuments.Element(DrawingToolId.Rectangle, 100 + (index * 60), 100, 140 + (index * 60), 200)).ToImmutableArray();
        DrawingElement stroke = TestDocuments.Element(DrawingToolId.Pencil, 100, 400, 100, 400) with
        {
            Points = Enumerable.Range(0, 2000).Select(index => new PixelPoint(100 + index, 400 + (index % 40))).ToImmutableList()
        };
        document.SetElements(baseline.Add(stroke));
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry());
        using SKPicture warmup = renderer.CreateScene();
        List<double> latency = [];

        for (int index = 0; index < 240; index++)
        {
            long start = Stopwatch.GetTimestamp();
            DrawingElement edited = index % 2 == 0 ? stroke.Translate(index, index)
                : stroke.Resize(new PixelRectangle(100, 400, 2000 + index, 40 + index));
            document.SetElements(baseline.Add(edited));
            using SKPicture frame = renderer.CreateScene();
            latency.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        latency.Sort();
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
        File.WriteAllText(Path.Combine(directory, "render-recording-benchmark.json"), JsonSerializer.Serialize(new
        {
            Size = "3840x2160",
            Frames = latency.Count,
            Objects = document.Elements.Length,
            StrokePoints = stroke.Points.Count,
            MedianMs = latency[latency.Count / 2],
            P95Ms = latency[(int)(latency.Count * 0.95)],
            MaxMs = latency[^1],
            IncludesGpuPresentation = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(31, document.Elements.Length);
    }
}
