using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitMcp.Revit;

namespace RevitMcp.Tools;

public abstract class WallOpeningTool : IMcpTool
{
    public abstract string Name { get; }

    protected abstract BuiltInCategory Category { get; }

    protected abstract string TypeParameter { get; }

    protected virtual void Configure(FamilyInstance instance, ToolParams parameters)
    {
    }

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var wall = ModelLookup.GetElement<Wall>(doc, parameters.GetId("wall_id"));
        var symbol = ModelLookup.ResolveType<FamilySymbol>(doc, Category, parameters.GetOptionalString(TypeParameter));
        var level = (Level)doc.GetElement(wall.LevelId);
        var curve = ((LocationCurve)wall.Location).Curve;

        var distance = UnitConverter.FromMeters(parameters.GetDouble("distance_along_wall"));
        if (distance < 0 || distance > curve.Length)
            throw new ArgumentException($"distance_along_wall debe estar entre 0 y {UnitConverter.ToMeters(curve.Length)} m.");
        var point = curve.Evaluate(distance / curve.Length, true);

        return ModelTransaction.Run(doc, Name, () =>
        {
            ModelLookup.Activate(symbol);
            var instance = doc.Create.NewFamilyInstance(point, symbol, wall, level, StructuralType.NonStructural);
            Configure(instance, parameters);
            doc.Regenerate();
            return ElementDescriber.Describe(instance);
        });
    }
}

public sealed class PlaceDoorTool : WallOpeningTool
{
    public override string Name => "place_door";

    protected override BuiltInCategory Category => BuiltInCategory.OST_Doors;

    protected override string TypeParameter => "door_type";
}

public sealed class PlaceWindowTool : WallOpeningTool
{
    public override string Name => "place_window";

    protected override BuiltInCategory Category => BuiltInCategory.OST_Windows;

    protected override string TypeParameter => "window_type";

    protected override void Configure(FamilyInstance instance, ToolParams parameters) =>
        instance.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)
            ?.Set(UnitConverter.FromMeters(parameters.GetDouble("sill_height", 0.9)));
}

public sealed class PlaceFamilyInstanceTool : IMcpTool
{
    public string Name => "place_family_instance";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var category = CategoryAliases.ParseOptional(parameters.GetOptionalString("category"));
        var symbol = ModelLookup.ResolveFamilySymbol(doc, parameters.GetString("family_type"), category);
        var level = ModelLookup.ResolveLevel(doc, parameters.GetOptionalString("level"));
        var point = parameters.GetPoint("point");
        var location = new XYZ(point.X, point.Y, level.Elevation + point.Z);
        var rotation = UnitConverter.FromDegrees(parameters.GetDouble("rotation_degrees", 0));

        return ModelTransaction.Run(doc, Name, () =>
        {
            ModelLookup.Activate(symbol);
            var instance = doc.Create.NewFamilyInstance(location, symbol, level, StructuralType.NonStructural);
            if (rotation != 0)
                ElementTransformUtils.RotateElement(doc, instance.Id, Geometry.VerticalAxis(location), rotation);

            doc.Regenerate();
            return ElementDescriber.Describe(instance);
        });
    }
}
