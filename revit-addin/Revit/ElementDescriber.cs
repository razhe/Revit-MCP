using Autodesk.Revit.DB;

namespace RevitMcp.Revit;

internal static class ElementDescriber
{
    public static object Describe(Element element) => new
    {
        Id = element.Id.Value,
        element.Name,
        Category = element.Category?.Name,
        Type = element.Document.GetElement(element.GetTypeId())?.Name,
        Level = LevelName(element.Document, element.LevelId) ?? ParameterLevel(element, BuiltInParameter.STAIRS_BASE_LEVEL_PARAM),
        TopLevel = ParameterLevel(element, BuiltInParameter.WALL_HEIGHT_TYPE) ?? ParameterLevel(element, BuiltInParameter.STAIRS_TOP_LEVEL_PARAM),
        Location = DescribeLocation(element.Location),
        Bounds = element.Location is LocationPoint or LocationCurve ? null : DescribeBounds(element),
        AreaM2 = element is SpatialElement spatial ? UnitConverter.ToSquareMeters(spatial.Area) : (double?)null
    };

    public static SortedDictionary<string, string?> DescribeParameters(Element element)
    {
        var parameters = new SortedDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (Parameter parameter in element.Parameters)
        {
            if (parameter.HasValue)
                parameters.TryAdd(parameter.Definition.Name, parameter.AsValueString() ?? parameter.AsString());
        }
        return parameters;
    }

    private static object? DescribeLocation(Location? location) => location switch
    {
        LocationPoint point => new { Point = UnitConverter.ToMeters(point.Point), RotationDegrees = TryGetRotation(point) },
        LocationCurve curve => new
        {
            Start = UnitConverter.ToMeters(curve.Curve.GetEndPoint(0)),
            End = UnitConverter.ToMeters(curve.Curve.GetEndPoint(1)),
            LengthM = UnitConverter.ToMeters(curve.Curve.Length)
        },
        _ => null
    };

    private static string? LevelName(Document doc, ElementId levelId) => (doc.GetElement(levelId) as Level)?.Name;

    private static string? ParameterLevel(Element element, BuiltInParameter parameter) =>
        element.get_Parameter(parameter) is { StorageType: StorageType.ElementId } value
            ? LevelName(element.Document, value.AsElementId())
            : null;

    private static object? DescribeBounds(Element element) =>
        element.get_BoundingBox(null) is { } box
            ? new { Min = UnitConverter.ToMeters(box.Min), Max = UnitConverter.ToMeters(box.Max) }
            : null;

    private static double? TryGetRotation(LocationPoint point)
    {
        try
        {
            return UnitConverter.ToDegrees(point.Rotation);
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            return null;
        }
    }
}
