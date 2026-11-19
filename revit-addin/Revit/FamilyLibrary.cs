using Autodesk.Revit.UI;

namespace RevitMcp.Revit;

/// <summary>
/// Carpetas donde buscar familias (.rfa): las bibliotecas configuradas en Revit más las
/// indicadas en la variable de entorno REVIT_MCP_FAMILY_DIRS (separadas por ';').
/// </summary>
internal static class FamilyLibrary
{
    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive
    };

    public static IReadOnlyList<string> Roots(UIApplication app) =>
        app.Application.GetLibraryPaths().Values
            .Concat((Environment.GetEnvironmentVariable("REVIT_MCP_FAMILY_DIRS") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IEnumerable<string> Search(IEnumerable<string> roots, string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.rfa", Recursive))
            .Where(path => terms.All(term => Path.GetFileNameWithoutExtension(path).Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
