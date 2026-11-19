using System.Text.Json;
using Autodesk.Revit.DB;
using RevitMcp.Revit;

namespace RevitMcp.Tools;

/// <summary>
/// Acceso tipado a los parámetros JSON de una herramienta. Puntos y vectores
/// llegan en metros como [x, y] o [x, y, z] y se convierten a unidades internas de Revit.
/// </summary>
public sealed class ToolParams(JsonElement root)
{
    public string GetString(string name) => AsText(Require(name));

    public string? GetOptionalString(string name) => TryGet(name, out var value) ? AsText(value) : null;

    public double GetDouble(string name) => Require(name).GetDouble();

    public double GetDouble(string name, double fallback) => TryGet(name, out var value) ? value.GetDouble() : fallback;

    public int GetInt(string name, int fallback) => TryGet(name, out var value) ? value.GetInt32() : fallback;

    public bool GetBool(string name, bool fallback) => TryGet(name, out var value) ? value.GetBoolean() : fallback;

    public long GetId(string name) => Require(name).GetInt64();

    public IReadOnlyList<long> GetIds(string name) =>
        Require(name).EnumerateArray().Select(value => value.GetInt64()).ToList();

    public XYZ GetPoint(string name) => ToPoint(Require(name), name);

    public XYZ? GetOptionalPoint(string name) => TryGet(name, out var value) ? ToPoint(value, name) : null;

    public IReadOnlyList<XYZ> GetPoints(string name) =>
        Require(name).EnumerateArray().Select(value => ToPoint(value, name)).ToList();

    public IReadOnlyList<XYZ?> GetOptionalPointSlots(string name) =>
        TryGet(name, out var value)
            ? value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.Null ? null : ToPoint(item, name))
                .ToList()
            : [];

    public IReadOnlyList<int> GetOptionalIndexes(string name) =>
        TryGet(name, out var value) ? value.EnumerateArray().Select(item => item.GetInt32()).ToList() : [];

    public IReadOnlyList<IReadOnlyList<XYZ>> GetPointLists(string name) =>
        TryGet(name, out var value)
            ? value.EnumerateArray()
                .Select(list => (IReadOnlyList<XYZ>)list.EnumerateArray().Select(point => ToPoint(point, name)).ToList())
                .ToList()
            : [];

    private JsonElement Require(string name) =>
        TryGet(name, out var value) ? value : throw new ArgumentException($"Falta el parámetro obligatorio '{name}'.");

    private bool TryGet(string name, out JsonElement value)
    {
        value = default;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out value)
            && value.ValueKind != JsonValueKind.Null;
    }

    private static string AsText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    private static XYZ ToPoint(JsonElement value, string name)
    {
        var coordinates = value.EnumerateArray().Select(item => item.GetDouble()).ToArray();
        if (coordinates.Length is < 2 or > 3)
            throw new ArgumentException($"'{name}' debe ser [x, y] o [x, y, z] en metros.");

        return new XYZ(
            UnitConverter.FromMeters(coordinates[0]),
            UnitConverter.FromMeters(coordinates[1]),
            coordinates.Length == 3 ? UnitConverter.FromMeters(coordinates[2]) : 0);
    }
}
