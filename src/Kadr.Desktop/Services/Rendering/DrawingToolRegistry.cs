using Kadr.Desktop.Services.Editing;

namespace Kadr.Desktop.Services.Rendering;

public sealed class DrawingToolRegistry
{
    public IReadOnlyList<DrawingTool> Tools
    {
        get;
    }

    private readonly IReadOnlyDictionary<DrawingToolId, DrawingTool> _tools;

    public DrawingToolRegistry() : this(Discover())
    {
    }

    public DrawingToolRegistry(IEnumerable<DrawingTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        Tools = tools.OrderBy(tool => tool.Id).ToArray();
        _tools = Tools.ToDictionary(tool => tool.Id);
    }

    public DrawingTool Get(DrawingToolId id)
    {
        return _tools[id];
    }

    private static IEnumerable<DrawingTool> Discover()
    {
        return typeof(DrawingTool).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(DrawingTool)))
            .Select(type => (DrawingTool)(Activator.CreateInstance(type) ?? throw new InvalidOperationException("Drawing tool construction failed.")));
    }
}
