using System.Globalization;
using Autodesk.Revit.DB;

namespace RevitMcp.Revit;

internal static class HexColor
{
    public static Color Parse(string hex)
    {
        var value = hex.TrimStart('#');
        if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException($"Color inválido '{hex}'. Usa el formato #RRGGBB.");

        return new Color((byte)(rgb >> 16), (byte)(rgb >> 8 & 0xFF), (byte)(rgb & 0xFF));
    }
}
