using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMcp.Server;

namespace RevitMcp;

[Transaction(TransactionMode.Manual)]
public sealed class ToggleServerCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (App.Server is null)
        {
            message = "El add-in no se inicializó correctamente.";
            return Result.Failed;
        }

        if (App.Server.IsRunning)
            App.Server.Stop();
        else
            App.TryStartServer();

        var status = App.Server.IsRunning
            ? $"Servidor activo en 127.0.0.1:{BridgeSettings.Port}"
            : "Servidor detenido";
        TaskDialog.Show("Revit MCP", status);
        return Result.Succeeded;
    }
}
