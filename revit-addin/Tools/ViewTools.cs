using Autodesk.Revit.DB;
using RevitMcp.Revit;

namespace RevitMcp.Tools;

public sealed class ListViewsTool : IMcpTool
{
    public string Name => "list_views";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var activeId = doc.ActiveView?.Id;
        return ModelLookup.GetViews(doc)
            .Select(view => new
            {
                Id = view.Id.Value,
                view.Name,
                ViewType = view.ViewType.ToString(),
                IsActive = view.Id == activeId
            })
            .ToList();
    }
}

public sealed class OpenViewTool : IMcpTool
{
    public string Name => "open_view";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var view = ModelLookup.ResolveView(context.Document, parameters.GetString("view_name"));
        context.UiDocument.RequestViewChange(view);
        return new { Opened = view.Name };
    }
}

public sealed class Create3DViewTool : IMcpTool
{
    private static readonly BuiltInCategory[] HiddenCategories =
        [BuiltInCategory.OST_Levels, BuiltInCategory.OST_Grids, BuiltInCategory.OST_CLines];

    public string Name => "create_3d_view";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var name = parameters.GetOptionalString("name");
        var fromDirection = parameters.GetOptionalPoint("from_direction");
        var elevation = parameters.GetDouble("elevation_degrees", 30);

        var view = ModelTransaction.Run(doc, Name, () =>
        {
            var created = View3D.CreateIsometric(doc, ModelLookup.GetViewFamilyType(doc, ViewFamily.ThreeDimensional).Id);
            if (name is not null)
                created.Name = name;
            created.DisplayStyle = DisplayStyle.ShadingWithEdges;
            HideCategories(created);
            if (fromDirection is not null)
                ViewFraming.Orient(created, fromDirection, elevation);
            ViewFraming.FitToModel(doc, created);
            return created;
        });

        if (parameters.GetBool("open", true))
            context.UiDocument.RequestViewChange(view);

        return new { ViewId = view.Id.Value, view.Name };
    }

    private static void HideCategories(View view)
    {
        foreach (var category in HiddenCategories)
        {
            var categoryId = new ElementId(category);
            if (view.CanCategoryBeHidden(categoryId))
                view.SetCategoryHidden(categoryId, true);
        }
    }
}

public sealed class Orient3DViewTool : IMcpTool
{
    public string Name => "orient_3d_view";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var view = ModelLookup.ResolveView(doc, parameters.GetOptionalString("view_name")) as View3D
            ?? throw new ArgumentException("La vista indicada no es una vista 3D.");
        var fromDirection = parameters.GetOptionalPoint("from_direction");
        var elevation = parameters.GetDouble("elevation_degrees", 30);

        return ModelTransaction.Run(doc, Name, () =>
        {
            if (fromDirection is not null)
                ViewFraming.Orient(view, fromDirection, elevation);
            ViewFraming.FitToModel(doc, view);
            return new { View = view.Name };
        });
    }
}

public sealed class ExportViewImageTool : IMcpTool
{
    public string Name => "export_view_image";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var view = ModelLookup.ResolveView(doc, parameters.GetOptionalString("view_name"));
        var folder = Directory.CreateTempSubdirectory("revit-mcp-").FullName;

        var options = new ImageExportOptions
        {
            ExportRange = ExportRange.SetOfViews,
            FilePath = Path.Combine(folder, "view"),
            HLRandWFViewsFileType = ImageFileType.PNG,
            ShadowViewsFileType = ImageFileType.PNG,
            ImageResolution = ImageResolution.DPI_150,
            ZoomType = ZoomFitType.FitToPage,
            FitDirection = FitDirectionType.Horizontal,
            PixelSize = parameters.GetInt("width_px", 1600)
        };
        options.SetViewsAndSheets([view.Id]);
        doc.ExportImage(options);

        var file = Directory.GetFiles(folder, "*.png").FirstOrDefault()
            ?? throw new InvalidOperationException($"Revit no generó una imagen para la vista '{view.Name}'.");
        return new { Path = file, View = view.Name };
    }
}
