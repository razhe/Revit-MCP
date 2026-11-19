using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitMcp.Tools;

public sealed class ToolContext(UIApplication app)
{
    public UIApplication App { get; } = app;

    public UIDocument UiDocument =>
        App.ActiveUIDocument ?? throw new InvalidOperationException("No hay ningún modelo abierto en Revit.");

    public Document Document => UiDocument.Document;
}
