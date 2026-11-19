using System.Reflection;

namespace RevitMcp.Tools;

public static class ToolRegistry
{
    public static IEnumerable<IMcpTool> Discover() =>
        LoadableTypes(typeof(IMcpTool).Assembly)
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IMcpTool).IsAssignableFrom(type))
            .Select(type => (IMcpTool)Activator.CreateInstance(type)!);

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
