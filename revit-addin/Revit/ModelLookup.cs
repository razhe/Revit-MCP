using Autodesk.Revit.DB;

namespace RevitMcp.Revit;

/// <summary>
/// Búsqueda de niveles, tipos, vistas y elementos por nombre o id, con mensajes
/// de error que listan las opciones disponibles para que el modelo de lenguaje pueda corregirse.
/// </summary>
internal static class ModelLookup
{
    private const int MaxSuggestions = 40;

    private static readonly Dictionary<BuiltInCategory, ElementTypeGroup> SystemTypeGroups = new()
    {
        [BuiltInCategory.OST_Walls] = ElementTypeGroup.WallType,
        [BuiltInCategory.OST_Floors] = ElementTypeGroup.FloorType,
        [BuiltInCategory.OST_Roofs] = ElementTypeGroup.RoofType,
        [BuiltInCategory.OST_Ceilings] = ElementTypeGroup.CeilingType
    };

    public static Element GetElement(Document doc, long id) =>
        doc.GetElement(new ElementId(id)) ?? throw new ArgumentException($"No existe un elemento con id {id}.");

    public static T GetElement<T>(Document doc, long id) where T : Element =>
        GetElement(doc, id) as T ?? throw new ArgumentException($"El elemento {id} no es de tipo {typeof(T).Name}.");

    public static List<ElementId> GetElementIds(Document doc, IEnumerable<long> ids) =>
        ids.Select(id => GetElement(doc, id).Id).ToList();

    public static IEnumerable<Element> GetInstances(Document doc, BuiltInCategory category) =>
        new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .Where(element => element is not FamilyInstance { SuperComponent: not null });

    public static IReadOnlyList<Level> GetLevels(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(level => level.Elevation).ToList();

    public static Level ResolveLevel(Document doc, string? name)
    {
        var levels = GetLevels(doc);
        if (levels.Count == 0)
            throw new InvalidOperationException("El modelo no tiene niveles.");

        return name is null
            ? doc.ActiveView?.GenLevel ?? levels[0]
            : FindByName(levels, name, "nivel", level => level.Name);
    }

    public static Level LevelAbove(Document doc, Level level) =>
        GetLevels(doc).FirstOrDefault(candidate => candidate.Elevation > level.Elevation)
        ?? throw new ArgumentException($"No hay un nivel sobre '{level.Name}'.");

    public static T ResolveType<T>(Document doc, BuiltInCategory category, string? name) where T : ElementType
    {
        var types = new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsElementType()
            .OfType<T>()
            .ToList();
        if (types.Count == 0)
            throw new InvalidOperationException($"No hay tipos cargados en el modelo para la categoría {category}.");

        if (name is not null)
            return FindByName(types, name, "tipo", FullName);

        var defaultId = DefaultTypeId(doc, category);
        return types.FirstOrDefault(type => type.Id == defaultId) ?? types[0];
    }

    public static FamilySymbol ResolveFamilySymbol(Document doc, string name, BuiltInCategory? category)
    {
        var collector = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
        if (category is { } value)
            collector = collector.OfCategory(value);

        return FindByName(collector.Cast<FamilySymbol>().ToList(), name, "familia", FullName);
    }

    public static View ResolveView(Document doc, string? name)
    {
        if (name is null)
            return doc.ActiveView ?? throw new InvalidOperationException("No hay una vista activa.");

        return FindByName(GetViews(doc), name, "vista", view => view.Name);
    }

    public static IReadOnlyList<View> GetViews(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(view => !view.IsTemplate && view.ViewType != ViewType.Internal && view.ViewType != ViewType.ProjectBrowser)
            .OrderBy(view => view.Name)
            .ToList();

    public static ViewFamilyType GetViewFamilyType(Document doc, ViewFamily family) =>
        new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .FirstOrDefault(type => type.ViewFamily == family)
        ?? throw new InvalidOperationException($"El modelo no tiene un tipo de vista {family}.");

    public static void Activate(FamilySymbol symbol)
    {
        if (symbol.IsActive)
            return;

        symbol.Activate();
        symbol.Document.Regenerate();
    }

    public static string FullName(ElementType type) => $"{type.FamilyName}: {type.Name}";

    private static ElementId DefaultTypeId(Document doc, BuiltInCategory category) =>
        SystemTypeGroups.TryGetValue(category, out var group)
            ? doc.GetDefaultElementTypeId(group)
            : doc.GetDefaultFamilyTypeId(new ElementId(category));

    private static T FindByName<T>(IReadOnlyList<T> items, string query, string label, Func<T, string> display)
        where T : Element
    {
        var match = items.FirstOrDefault(item => NameEquals(item.Name, query) || NameEquals(display(item), query));
        if (match is not null)
            return match;

        var options = items.Select(display).Distinct().Take(MaxSuggestions);
        throw new ArgumentException($"No se encontró el {label} '{query}'. Disponibles: {string.Join(" | ", options)}");
    }

    private static bool NameEquals(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
