using Autodesk.Revit.DB;
using RevitMcp.Revit;

namespace RevitMcp.Tools;

public sealed class GetModelInfoTool : IMcpTool
{
    public string Name => "get_model_info";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var counts = CategoryAliases.Names
            .Select(name => (name, count: ModelLookup.GetInstances(doc, CategoryAliases.Parse(name)).Count()))
            .Where(entry => entry.count > 0)
            .ToDictionary(entry => entry.name, entry => entry.count);

        return new
        {
            doc.Title,
            doc.PathName,
            ActiveView = doc.ActiveView?.Name,
            Levels = ModelLookup.GetLevels(doc)
                .Select(level => new { Id = level.Id.Value, level.Name, ElevationM = UnitConverter.ToMeters(level.Elevation) })
                .ToList(),
            ElementCounts = counts
        };
    }
}

public sealed class ListTypesTool : IMcpTool
{
    public string Name => "list_types";

    public object? Execute(ToolContext context, ToolParams parameters) =>
        new FilteredElementCollector(context.Document)
            .OfCategory(CategoryAliases.Parse(parameters.GetString("category")))
            .WhereElementIsElementType()
            .Cast<ElementType>()
            .Select(ModelLookup.FullName)
            .Order()
            .ToList();
}

public sealed class GetElementsTool : IMcpTool
{
    public string Name => "get_elements";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var elements = ModelLookup.GetInstances(doc, CategoryAliases.Parse(parameters.GetString("category")));

        if (parameters.GetOptionalString("level") is { } levelName)
        {
            var levelId = ModelLookup.ResolveLevel(doc, levelName).Id;
            elements = elements.Where(element => element.LevelId == levelId);
        }

        var list = elements.ToList();
        return new
        {
            Total = list.Count,
            Elements = list.Take(parameters.GetInt("limit", 100)).Select(ElementDescriber.Describe).ToList()
        };
    }
}

public sealed class GetElementInfoTool : IMcpTool
{
    public string Name => "get_element_info";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var element = ModelLookup.GetElement(doc, parameters.GetId("element_id"));
        var type = doc.GetElement(element.GetTypeId());

        return new
        {
            Element = ElementDescriber.Describe(element),
            Parameters = ElementDescriber.DescribeParameters(element),
            TypeParameters = type is null ? null : ElementDescriber.DescribeParameters(type)
        };
    }
}

public sealed class GetSelectionTool : IMcpTool
{
    public string Name => "get_selection";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        return context.UiDocument.Selection.GetElementIds()
            .Select(id => ElementDescriber.Describe(doc.GetElement(id)))
            .ToList();
    }
}
