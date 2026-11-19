using Autodesk.Revit.DB;

namespace RevitMcp.Revit;

internal static class UnitConverter
{
    private const int Decimals = 4;

    public static double FromMeters(double meters) => UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);

    public static double ToMeters(double feet) =>
        Math.Round(UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters), Decimals);

    public static double[] ToMeters(XYZ point) => [ToMeters(point.X), ToMeters(point.Y), ToMeters(point.Z)];

    public static double ToSquareMeters(double squareFeet) =>
        Math.Round(UnitUtils.ConvertFromInternalUnits(squareFeet, UnitTypeId.SquareMeters), Decimals);

    public static double FromDegrees(double degrees) => degrees * Math.PI / 180.0;

    public static double ToDegrees(double radians) => Math.Round(radians * 180.0 / Math.PI, Decimals);
}
