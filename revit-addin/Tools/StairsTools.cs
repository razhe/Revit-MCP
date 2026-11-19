using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitMcp.Revit;

namespace RevitMcp.Tools;

/// <summary>
/// Crea una escalera nativa recta o en U entre dos niveles. El número de contrahuellas
/// se calcula para no superar la altura máxima indicada; en U se reparte entre dos tramos
/// paralelos unidos por un descanso.
/// </summary>
public sealed class CreateStairsTool : IMcpTool
{
    public string Name => "create_stairs";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var baseLevel = ModelLookup.ResolveLevel(doc, parameters.GetOptionalString("base_level"));
        var topLevel = parameters.GetOptionalString("top_level") is { } topName
            ? ModelLookup.ResolveLevel(doc, topName)
            : ModelLookup.LevelAbove(doc, baseLevel);
        var height = topLevel.Elevation - baseLevel.Elevation;
        if (height <= 0)
            throw new ArgumentException($"El nivel superior '{topLevel.Name}' debe estar sobre '{baseLevel.Name}'.");

        var shape = parameters.GetOptionalString("shape") ?? "straight";
        if (shape is not ("straight" or "u"))
            throw new ArgumentException($"shape debe ser 'straight' o 'u', no '{shape}'.");

        var start = parameters.GetPoint("start");
        var angle = UnitConverter.FromDegrees(parameters.GetDouble("direction_degrees", 0));
        var direction = new XYZ(Math.Cos(angle), Math.Sin(angle), 0);
        var side = parameters.GetOptionalString("turn") == "right"
            ? new XYZ(direction.Y, -direction.X, 0)
            : new XYZ(-direction.Y, direction.X, 0);
        var width = UnitConverter.FromMeters(parameters.GetDouble("width", 1.0));
        var tread = UnitConverter.FromMeters(parameters.GetDouble("tread_depth", 0.28));
        var risers = (int)Math.Ceiling(height / UnitConverter.FromMeters(parameters.GetDouble("max_riser_height", 0.18)) - 1e-9);

        ElementId stairsId;
        using (var scope = new StairsEditScope(doc, $"MCP: {Name}"))
        {
            stairsId = scope.Start(baseLevel.Id, topLevel.Id);
            ModelTransaction.Run(doc, Name, () =>
            {
                var stairs = (Stairs)doc.GetElement(stairsId);
                stairs.DesiredRisersNumber = risers;
                stairs.ActualTreadDepth = tread;

                var origin = new XYZ(start.X, start.Y, 0);
                if (shape == "straight")
                    CreateRun(doc, stairsId, origin, direction, risers, tread, width, 0);
                else
                    CreateUShape(doc, stairsId, origin, direction, side, risers, tread, width);
                return stairsId;
            });
            scope.Commit(new FailureCollector());
        }

        return new
        {
            Stairs = ElementDescriber.Describe(doc.GetElement(stairsId)),
            Risers = risers,
            RiserHeightM = UnitConverter.ToMeters(height / risers),
            BaseLevel = baseLevel.Name,
            TopLevel = topLevel.Name
        };
    }

    private static StairsRun CreateRun(
        Document doc, ElementId stairsId, XYZ start, XYZ direction, int risers, double tread, double width, double elevation)
    {
        var from = new XYZ(start.X, start.Y, elevation);
        var to = from + direction * ((risers - 1) * tread);
        var run = StairsRun.CreateStraightRun(doc, stairsId, Line.CreateBound(from, to), StairsRunJustification.Center);
        run.ActualRunWidth = width;
        return run;
    }

    private static void CreateUShape(
        Document doc, ElementId stairsId, XYZ start, XYZ direction, XYZ side, int risers, double tread, double width)
    {
        var gap = UnitConverter.FromMeters(0.1);
        var firstRisers = (risers + 1) / 2;

        var firstRun = CreateRun(doc, stairsId, start, direction, firstRisers, tread, width, 0);
        var landingElevation = firstRun.TopElevation;
        var firstEnd = start + direction * ((firstRisers - 1) * tread);
        CreateRun(doc, stairsId, firstEnd + side * (width + gap), -direction, risers - firstRisers, tread, width, landingElevation);

        var corner = new XYZ(firstEnd.X, firstEnd.Y, landingElevation) - side * (width / 2);
        var landing = CurveLoop.Create(
        [
            Line.CreateBound(corner, corner + side * (2 * width + gap)),
            Line.CreateBound(corner + side * (2 * width + gap), corner + side * (2 * width + gap) + direction * width),
            Line.CreateBound(corner + side * (2 * width + gap) + direction * width, corner + direction * width),
            Line.CreateBound(corner + direction * width, corner)
        ]);
        StairsLanding.CreateSketchedLanding(doc, stairsId, landing, landingElevation);
    }
}
