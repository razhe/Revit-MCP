using Autodesk.Revit.DB;

namespace RevitMcp.Revit;

/// <summary>
/// Orienta la cámara de una vista 3D y recorta su encuadre al modelo. La exportación de imágenes
/// encuadra según la región de recorte, así que se proyectan las esquinas del modelo al plano de la vista.
/// </summary>
internal static class ViewFraming
{
    private static readonly double FrameMargin = UnitConverter.FromMeters(1);
    private static readonly double CameraDistance = UnitConverter.FromMeters(200);

    public static void Orient(View3D view, XYZ fromDirection, double elevationDegrees)
    {
        var horizontal = new XYZ(-fromDirection.X, -fromDirection.Y, 0);
        if (horizontal.IsZeroLength())
            throw new ArgumentException("from_direction no puede ser [0, 0].");

        var elevation = UnitConverter.FromDegrees(elevationDegrees);
        var forward = (horizontal.Normalize() * Math.Cos(elevation) - XYZ.BasisZ * Math.Sin(elevation)).Normalize();
        var up = forward.CrossProduct(XYZ.BasisZ).Normalize().CrossProduct(forward).Normalize();
        view.CropBoxActive = false;
        view.SetOrientation(new ViewOrientation3D(-forward * CameraDistance, up, forward));
    }

    public static void FitToModel(Document doc, View3D view)
    {
        view.CropBoxActive = false;
        doc.Regenerate();
        var ids = new FilteredElementCollector(doc)
            .WherePasses(new ElementMulticategoryFilter(CategoryAliases.ModelCategories))
            .WhereElementIsNotElementType()
            .ToElementIds();
        if (Geometry.BoundsOf(doc, ids, FrameMargin) is not { } bounds)
            return;

        var crop = view.CropBox;
        var toView = crop.Transform.Inverse;
        var corners = (
            from x in new[] { bounds.Min.X, bounds.Max.X }
            from y in new[] { bounds.Min.Y, bounds.Max.Y }
            from z in new[] { bounds.Min.Z, bounds.Max.Z }
            select toView.OfPoint(new XYZ(x, y, z))).ToList();

        crop.Min = new XYZ(corners.Min(p => p.X), corners.Min(p => p.Y), crop.Min.Z);
        crop.Max = new XYZ(corners.Max(p => p.X), corners.Max(p => p.Y), crop.Max.Z);
        view.CropBox = crop;
        view.CropBoxActive = true;
        view.CropBoxVisible = false;
    }
}
