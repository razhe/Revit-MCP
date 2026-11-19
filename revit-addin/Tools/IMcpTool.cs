namespace RevitMcp.Tools;

public interface IMcpTool
{
    string Name { get; }

    object? Execute(ToolContext context, ToolParams parameters);
}
