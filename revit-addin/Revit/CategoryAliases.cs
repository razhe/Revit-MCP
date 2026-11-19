using Autodesk.Revit.DB;

namespace RevitMcp.Revit;

internal static class CategoryAliases
{
    private static readonly Dictionary<string, BuiltInCategory> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["walls"] = BuiltInCategory.OST_Walls,
        ["floors"] = BuiltInCategory.OST_Floors,
        ["roofs"] = BuiltInCategory.OST_Roofs,
        ["ceilings"] = BuiltInCategory.OST_Ceilings,
        ["doors"] = BuiltInCategory.OST_Doors,
        ["windows"] = BuiltInCategory.OST_Windows,
        ["rooms"] = BuiltInCategory.OST_Rooms,
        ["furniture"] = BuiltInCategory.OST_Furniture,
        ["casework"] = BuiltInCategory.OST_Casework,
        ["plumbing_fixtures"] = BuiltInCategory.OST_PlumbingFixtures,
        ["lighting_fixtures"] = BuiltInCategory.OST_LightingFixtures,
        ["columns"] = BuiltInCategory.OST_Columns,
        ["structural_columns"] = BuiltInCategory.OST_StructuralColumns,
        ["stairs"] = BuiltInCategory.OST_Stairs,
        ["railings"] = BuiltInCategory.OST_StairsRailing,
        ["generic_models"] = BuiltInCategory.OST_GenericModel,
        ["planting"] = BuiltInCategory.OST_Planting
    };

    public static IEnumerable<string> Names => Aliases.Keys;

    public static ICollection<BuiltInCategory> ModelCategories => Aliases.Values;

    public static BuiltInCategory Parse(string value)
    {
        if (Aliases.TryGetValue(value, out var category))
            return category;
        if (Enum.TryParse(value, true, out category))
            return category;

        throw new ArgumentException(
            $"Categoría desconocida '{value}'. Usa una de: {string.Join(", ", Names)}, o un nombre OST_*.");
    }

    public static BuiltInCategory? ParseOptional(string? value) => value is null ? null : Parse(value);
}
