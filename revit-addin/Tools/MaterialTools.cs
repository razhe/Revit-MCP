using Autodesk.Revit.DB;
using RevitMcp.Revit;

namespace RevitMcp.Tools;

/// <summary>
/// Pinta caras de elementos con un material (como la herramienta Pintar de Revit), sin cambiar
/// su tipo. Si el material no existe y se indica un color, lo crea con ese color de sombreado.
/// </summary>
public sealed class SetMaterialTool : IMcpTool
{
    private const double AxisTolerance = 0.7;

    private static readonly Dictionary<string, Func<XYZ, bool>> FaceSelectors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["all"] = _ => true,
        ["top"] = normal => normal.Z > AxisTolerance,
        ["bottom"] = normal => normal.Z < -AxisTolerance,
        ["sides"] = normal => Math.Abs(normal.Z) < 1 - AxisTolerance,
        ["+x"] = normal => normal.X > AxisTolerance,
        ["-x"] = normal => normal.X < -AxisTolerance,
        ["+y"] = normal => normal.Y > AxisTolerance,
        ["-y"] = normal => normal.Y < -AxisTolerance
    };

    public string Name => "set_material";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var ids = ModelLookup.GetElementIds(doc, parameters.GetIds("element_ids"));
        var materialName = parameters.GetString("material");
        var color = parameters.GetOptionalString("color") is { } hex ? HexColor.Parse(hex) : null;
        var facesName = parameters.GetOptionalString("faces") ?? "all";
        if (!FaceSelectors.TryGetValue(facesName, out var selector))
            throw new ArgumentException($"faces debe ser uno de: {string.Join(", ", FaceSelectors.Keys)}.");

        return ModelTransaction.Run(doc, Name, () =>
        {
            var material = ResolveMaterial(doc, materialName, color);
            var painted = ids.Sum(id => Paint(doc, id, material.Id, selector));
            if (painted == 0)
                throw new InvalidOperationException(
                    "No se pudo pintar ninguna cara. Pintar funciona con muros, losas, techos y cielos rasos; revisa también 'faces'.");

            return new { Material = material.Name, PaintedFaces = painted };
        });
    }

    private static Material ResolveMaterial(Document doc, string name, Color? color)
    {
        var materials = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().ToList();
        var material = materials.FirstOrDefault(item => string.Equals(item.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (material is null)
        {
            if (color is null)
                throw new ArgumentException(
                    $"No existe el material '{name}'. Indica color para crearlo, o usa uno existente, por ejemplo: " +
                    string.Join(" | ", materials.Select(item => item.Name).OrderBy(item => item).Take(40)));
            material = (Material)doc.GetElement(Material.Create(doc, name.Trim()));
        }

        if (color is not null)
            material.Color = color;
        return material;
    }

    private static int Paint(Document doc, ElementId elementId, ElementId materialId, Func<XYZ, bool> selector)
    {
        var geometry = doc.GetElement(elementId).get_Geometry(new Options { ComputeReferences = true });
        if (geometry is null)
            return 0;

        var painted = 0;
        foreach (var face in geometry.OfType<Solid>().SelectMany(solid => solid.Faces.Cast<Face>()))
        {
            var box = face.GetBoundingBox();
            if (!selector(face.ComputeNormal((box.Min + box.Max) / 2)))
                continue;

            try
            {
                doc.Paint(elementId, face, materialId);
                painted++;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
            }
        }
        return painted;
    }
}
