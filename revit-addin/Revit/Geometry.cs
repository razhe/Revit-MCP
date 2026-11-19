using Autodesk.Revit.DB;

namespace RevitMcp.Revit;

internal static class Geometry
{
    /// <summary>
    /// Une los puntos con tramos rectos, salvo los tramos i que tengan un punto en arcPoints[i]:
    /// esos se trazan como arco que pasa por ese punto.
    /// </summary>
    public static IReadOnlyList<Curve> Path(IReadOnlyList<XYZ> points, bool closed, double elevation, IReadOnlyList<XYZ?>? arcPoints = null)
    {
        var flat = points.Select(point => AtElevation(point, elevation)).ToList();
        if (flat.Count < 2)
            throw new ArgumentException("Se necesitan al menos 2 puntos.");
        if (closed && !flat[0].IsAlmostEqualTo(flat[^1]))
            flat.Add(flat[0]);

        return Enumerable.Range(0, flat.Count - 1)
            .Select(i => arcPoints?.ElementAtOrDefault(i) is { } through
                ? (Curve)Arc.Create(flat[i], flat[i + 1], AtElevation(through, elevation))
                : Line.CreateBound(flat[i], flat[i + 1]))
            .ToList();
    }

    public static CurveLoop ClosedLoop(IReadOnlyList<XYZ> points, double elevation, IReadOnlyList<XYZ?>? arcPoints = null)
    {
        if (points.Count < 3)
            throw new ArgumentException("Un contorno cerrado necesita al menos 3 puntos.");

        var loop = new CurveLoop();
        foreach (var curve in Path(points, true, elevation, arcPoints))
            loop.Append(curve);
        return loop;
    }

    public static int NearestIndex(IReadOnlyList<Curve> curves, Curve target)
    {
        var midpoint = target.Evaluate(0.5, true);
        return Enumerable.Range(0, curves.Count).MinBy(i => curves[i].Distance(midpoint));
    }

    private static XYZ AtElevation(XYZ point, double elevation) => new(point.X, point.Y, elevation);

    public static CurveLoop Expand(CurveLoop loop, double distance)
    {
        var positive = CurveLoop.CreateViaOffset(loop, distance, XYZ.BasisZ);
        var negative = CurveLoop.CreateViaOffset(loop, -distance, XYZ.BasisZ);
        return positive.GetExactLength() > negative.GetExactLength() ? positive : negative;
    }

    public static XYZ CenterOf(Document doc, IEnumerable<ElementId> ids)
    {
        var bounds = BoundsOf(doc, ids)
            ?? throw new InvalidOperationException("Los elementos no tienen geometría para calcular un centro.");
        return (bounds.Min + bounds.Max) / 2;
    }

    public static BoundingBoxXYZ? BoundsOf(Document doc, IEnumerable<ElementId> ids, double margin = 0)
    {
        var boxes = ids.Select(id => doc.GetElement(id).get_BoundingBox(null)).OfType<BoundingBoxXYZ>().ToList();
        if (boxes.Count == 0)
            return null;

        var padding = new XYZ(margin, margin, margin);
        return new BoundingBoxXYZ
        {
            Min = new XYZ(boxes.Min(b => b.Min.X), boxes.Min(b => b.Min.Y), boxes.Min(b => b.Min.Z)) - padding,
            Max = new XYZ(boxes.Max(b => b.Max.X), boxes.Max(b => b.Max.Y), boxes.Max(b => b.Max.Z)) + padding
        };
    }

    public static Line VerticalAxis(XYZ point) => Line.CreateBound(point, point + XYZ.BasisZ);
}
