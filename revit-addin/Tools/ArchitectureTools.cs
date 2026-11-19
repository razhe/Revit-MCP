using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitMcp.Revit;

namespace RevitMcp.Tools;

public sealed class CreateLevelTool : IMcpTool
{
    public string Name => "create_level";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var elevation = UnitConverter.FromMeters(parameters.GetDouble("elevation"));
        var name = parameters.GetOptionalString("name");
        var createPlan = parameters.GetBool("create_plan_view", true);

        return ModelTransaction.Run(doc, Name, () =>
        {
            var level = Level.Create(doc, elevation);
            if (name is not null)
                level.Name = name;

            var plan = createPlan
                ? ViewPlan.Create(doc, ModelLookup.GetViewFamilyType(doc, ViewFamily.FloorPlan).Id, level.Id)
                : null;

            return new { LevelId = level.Id.Value, level.Name, PlanView = plan?.Name };
        });
    }
}

public sealed class CreateWallsTool : IMcpTool
{
    public string Name => "create_walls";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var level = ModelLookup.ResolveLevel(doc, parameters.GetOptionalString("level"));
        var wallType = ModelLookup.ResolveType<WallType>(doc, BuiltInCategory.OST_Walls, parameters.GetOptionalString("wall_type"));
        var height = UnitConverter.FromMeters(parameters.GetDouble("height", 2.6));
        var baseOffset = UnitConverter.FromMeters(parameters.GetDouble("base_offset", 0));
        var structural = parameters.GetBool("structural", false);
        var segments = Geometry.Path(
            parameters.GetPoints("points"),
            parameters.GetBool("closed", false),
            level.Elevation,
            parameters.GetOptionalPointSlots("arc_points"));
        var topLevel = parameters.GetOptionalString("top_level") is { } topName ? ModelLookup.ResolveLevel(doc, topName) : null;
        if (topLevel is not null && topLevel.Elevation <= level.Elevation)
            throw new ArgumentException($"El nivel superior '{topLevel.Name}' debe estar sobre '{level.Name}'.");

        Wall CreateWall(Curve segment)
        {
            var wall = Wall.Create(doc, segment, wallType.Id, level.Id, height, baseOffset, false, structural);
            if (topLevel is not null)
                wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).Set(topLevel.Id);
            return wall;
        }

        return ModelTransaction.Run(doc, Name, () => new
        {
            WallType = ModelLookup.FullName(wallType),
            Level = level.Name,
            TopLevel = topLevel?.Name,
            WallIds = segments.Select(segment => CreateWall(segment).Id.Value).ToList()
        });
    }
}

public sealed class CreateRailingTool : IMcpTool
{
    public string Name => "create_railing";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var level = ModelLookup.ResolveLevel(doc, parameters.GetOptionalString("level"));
        var railingType = ModelLookup.ResolveType<RailingType>(doc, BuiltInCategory.OST_StairsRailing, parameters.GetOptionalString("railing_type"));
        var path = CurveLoop.Create(Geometry.Path(
            parameters.GetPoints("points"),
            parameters.GetBool("closed", false),
            level.Elevation,
            parameters.GetOptionalPointSlots("arc_points")).ToList());
        var baseOffset = UnitConverter.FromMeters(parameters.GetDouble("base_offset", 0));

        return ModelTransaction.Run(doc, Name, () =>
        {
            var railing = Railing.Create(doc, path, railingType.Id, level.Id);
            if (baseOffset != 0)
                railing.get_Parameter(BuiltInParameter.STAIRS_RAILING_HEIGHT_OFFSET)?.Set(baseOffset);
            return new { RailingId = railing.Id.Value, RailingType = ModelLookup.FullName(railingType), Level = level.Name };
        });
    }
}

public sealed class CreateFloorTool : IMcpTool
{
    public string Name => "create_floor";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var level = ModelLookup.ResolveLevel(doc, parameters.GetOptionalString("level"));
        var floorType = ModelLookup.ResolveType<FloorType>(doc, BuiltInCategory.OST_Floors, parameters.GetOptionalString("floor_type"));
        var offset = parameters.GetDouble("offset", 0);
        var loops = parameters.GetPointLists("holes")
            .Select(points => Geometry.ClosedLoop(points, level.Elevation))
            .Prepend(Geometry.ClosedLoop(parameters.GetPoints("points"), level.Elevation, parameters.GetOptionalPointSlots("arc_points")))
            .ToList();

        return ModelTransaction.Run(doc, Name, () =>
        {
            var floor = Floor.Create(doc, loops, floorType.Id, level.Id);
            if (offset != 0)
                floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(UnitConverter.FromMeters(offset));

            return new { FloorId = floor.Id.Value, FloorType = ModelLookup.FullName(floorType), Level = level.Name };
        });
    }
}

public sealed class CreateRoofTool : IMcpTool
{
    public string Name => "create_roof";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var level = ModelLookup.ResolveLevel(doc, parameters.GetOptionalString("level"));
        var roofType = ModelLookup.ResolveType<RoofType>(doc, BuiltInCategory.OST_Roofs, parameters.GetOptionalString("roof_type"));
        var slopeDegrees = parameters.GetDouble("slope_degrees", 30);
        var overhang = UnitConverter.FromMeters(parameters.GetDouble("overhang", 0));
        var baseOffset = parameters.GetDouble("base_offset", 0);
        var slopedEdges = parameters.GetOptionalIndexes("sloped_edges");
        var outline = Geometry.ClosedLoop(parameters.GetPoints("points"), level.Elevation, parameters.GetOptionalPointSlots("arc_points"));
        var originalEdges = outline.ToList();
        var footprintEdges = overhang > 0 ? Geometry.Expand(outline, overhang) : outline;

        return ModelTransaction.Run(doc, Name, () =>
        {
            var footprint = new CurveArray();
            foreach (var edge in footprintEdges)
                footprint.Append(edge);

            var modelCurves = new ModelCurveArray();
            var roof = doc.Create.NewFootPrintRoof(footprint, level, roofType, out modelCurves);
            foreach (ModelCurve curve in modelCurves)
            {
                var edgeIndex = Geometry.NearestIndex(originalEdges, curve.GeometryCurve);
                var sloped = slopeDegrees > 0 && (slopedEdges.Count == 0 || slopedEdges.Contains(edgeIndex));
                roof.set_DefinesSlope(curve, sloped);
                if (sloped)
                    roof.set_SlopeAngle(curve, Math.Tan(UnitConverter.FromDegrees(slopeDegrees)));
            }

            if (baseOffset != 0)
                roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM)?.Set(UnitConverter.FromMeters(baseOffset));

            return new { RoofId = roof.Id.Value, RoofType = ModelLookup.FullName(roofType), Level = level.Name };
        });
    }
}

public sealed class CreateCeilingTool : IMcpTool
{
    public string Name => "create_ceiling";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var level = ModelLookup.ResolveLevel(doc, parameters.GetOptionalString("level"));
        var ceilingType = ModelLookup.ResolveType<CeilingType>(doc, BuiltInCategory.OST_Ceilings, parameters.GetOptionalString("ceiling_type"));
        var height = UnitConverter.FromMeters(parameters.GetDouble("height", 2.4));
        var outline = Geometry.ClosedLoop(parameters.GetPoints("points"), level.Elevation, parameters.GetOptionalPointSlots("arc_points"));

        return ModelTransaction.Run(doc, Name, () =>
        {
            var ceiling = Ceiling.Create(doc, [outline], ceilingType.Id, level.Id);
            ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM)?.Set(height);
            return new { CeilingId = ceiling.Id.Value, CeilingType = ModelLookup.FullName(ceilingType), Level = level.Name };
        });
    }
}

public sealed class CreateRoomTool : IMcpTool
{
    public string Name => "create_room";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var level = ModelLookup.ResolveLevel(doc, parameters.GetOptionalString("level"));
        var point = parameters.GetPoint("point");
        var name = parameters.GetOptionalString("name");
        var number = parameters.GetOptionalString("number");

        return ModelTransaction.Run(doc, Name, () =>
        {
            var room = doc.Create.NewRoom(level, new UV(point.X, point.Y));
            if (name is not null)
                room.Name = name;
            if (number is not null)
                room.Number = number;

            doc.Regenerate();
            return ElementDescriber.Describe(room);
        });
    }
}
