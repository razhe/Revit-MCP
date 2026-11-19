using Autodesk.Revit.DB;
using RevitMcp.Revit;

namespace RevitMcp.Tools;

public sealed class SearchFamilyLibraryTool : IMcpTool
{
    public string Name => "search_family_library";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var roots = FamilyLibrary.Roots(context.App);
        return new
        {
            Folders = roots,
            Files = FamilyLibrary.Search(roots, parameters.GetString("query")).Take(parameters.GetInt("limit", 30)).ToList()
        };
    }
}

public sealed class LoadFamilyTool : IMcpTool
{
    public string Name => "load_family";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var path = parameters.GetString("path");
        if (!File.Exists(path))
            throw new ArgumentException($"No existe el archivo '{path}'. Usa search_family_library para encontrar familias.");

        var family = ModelTransaction.Run(doc, Name, () =>
            doc.LoadFamily(path, out Family loaded) ? loaded : FindLoaded(doc, Path.GetFileNameWithoutExtension(path)));

        return new
        {
            Family = family.Name,
            Category = family.FamilyCategory?.Name,
            Types = family.GetFamilySymbolIds().Select(id => doc.GetElement(id).Name).OrderBy(name => name).ToList()
        };
    }

    private static Family FindLoaded(Document doc, string name) =>
        new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
            .FirstOrDefault(family => string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Revit no cargó la familia '{name}'.");
}
