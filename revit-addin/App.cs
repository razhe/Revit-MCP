using System.Net.Sockets;
using Autodesk.Revit.UI;
using RevitMcp.Server;
using RevitMcp.Tools;

namespace RevitMcp;

public sealed class App : IExternalApplication
{
    internal static BridgeServer? Server { get; private set; }

    public Result OnStartup(UIControlledApplication application)
    {
        var dispatcher = new ToolDispatcher(ToolRegistry.Discover());
        Server = new BridgeServer(BridgeSettings.Port, dispatcher);
        CreateRibbon(application);
        TryStartServer();
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        Server?.Dispose();
        return Result.Succeeded;
    }

    internal static void TryStartServer()
    {
        try
        {
            Server?.Start();
        }
        catch (SocketException ex)
        {
            TaskDialog.Show("Revit MCP", $"No se pudo iniciar el servidor en el puerto {BridgeSettings.Port}: {ex.Message}");
        }
    }

    private static void CreateRibbon(UIControlledApplication application)
    {
        var panel = application.CreateRibbonPanel("Revit MCP");
        var button = new PushButtonData(
            "ToggleMcpServer",
            "Servidor\nMCP",
            typeof(App).Assembly.Location,
            typeof(ToggleServerCommand).FullName)
        {
            ToolTip = "Inicia o detiene el servidor que conecta Revit con Claude."
        };
        panel.AddItem(button);
    }
}
