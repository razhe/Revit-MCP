using System.Globalization;
using Autodesk.Revit.DB;
using RevitMcp.Revit;

namespace RevitMcp.Tools;

public sealed class MoveElementsTool : IMcpTool
{
    public string Name => "move_elements";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var ids = ModelLookup.GetElementIds(doc, parameters.GetIds("element_ids"));
        var translation = parameters.GetPoint("translation");

        return ModelTransaction.Run(doc, Name, () =>
        {
            ElementTransformUtils.MoveElements(doc, ids, translation);
            return new { Moved = ids.Count };
        });
    }
}

public sealed class CopyElementsTool : IMcpTool
{
    public string Name => "copy_elements";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var ids = ModelLookup.GetElementIds(doc, parameters.GetIds("element_ids"));
        var translation = parameters.GetPoint("translation");

        return ModelTransaction.Run(doc, Name, () =>
        {
            var copies = ElementTransformUtils.CopyElements(doc, ids, translation).Select(doc.GetElement).ToList();
            doc.Regenerate();
            var dependents = copies.OfType<HostObject>()
                .SelectMany(host => host.FindInserts(true, false, true, true))
                .Distinct()
                .Select(doc.GetElement)
                .ExceptBy(copies.Select(copy => copy.Id), element => element.Id)
                .ToList();
            var all = copies.Concat(dependents).ToList();

            return new
            {
                NewElementIds = all.Select(element => element.Id.Value).ToList(),
                ByCategory = all
                    .GroupBy(element => element.Category?.Name ?? "Sin categoría")
                    .ToDictionary(group => group.Key, group => group.Select(element => element.Id.Value).ToList()),
                DependentsCopied = dependents.Count
            };
        });
    }
}

public sealed class RotateElementsTool : IMcpTool
{
    public string Name => "rotate_elements";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var ids = ModelLookup.GetElementIds(doc, parameters.GetIds("element_ids"));
        var angle = UnitConverter.FromDegrees(parameters.GetDouble("angle_degrees"));
        var center = parameters.GetOptionalPoint("center") ?? Geometry.CenterOf(doc, ids);

        return ModelTransaction.Run(doc, Name, () =>
        {
            ElementTransformUtils.RotateElements(doc, ids, Geometry.VerticalAxis(center), angle);
            return new { Rotated = ids.Count, Center = UnitConverter.ToMeters(center) };
        });
    }
}

public sealed class DeleteElementsTool : IMcpTool
{
    public string Name => "delete_elements";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var ids = ModelLookup.GetElementIds(doc, parameters.GetIds("element_ids"));

        return ModelTransaction.Run(doc, Name, () => new { DeletedCount = doc.Delete(ids).Count });
    }
}

public sealed class SetParameterTool : IMcpTool
{
    public string Name => "set_parameter";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var name = parameters.GetString("parameter");
        var value = parameters.GetString("value");
        var targets = parameters.GetIds("element_ids")
            .Select(id => ModelLookup.GetElement(doc, id))
            .Select(element => (element, parameter: WritableParameter(element, name)))
            .ToList();

        return ModelTransaction.Run(doc, Name, () =>
        {
            foreach (var (_, parameter) in targets)
                Apply(parameter, value);

            return new
            {
                Parameter = name,
                Updated = targets.Count,
                Values = targets.Select(target => target.parameter.AsValueString() ?? target.parameter.AsString()).Distinct().ToList()
            };
        });
    }

    private static Parameter WritableParameter(Element element, string name)
    {
        var parameter = element.LookupParameter(name)
            ?? throw new ArgumentException($"El elemento {element.Id.Value} no tiene el parámetro de instancia '{name}'. Usa get_element_info para ver los disponibles.");
        if (parameter.IsReadOnly)
            throw new ArgumentException($"El parámetro '{name}' del elemento {element.Id.Value} es de solo lectura.");
        return parameter;
    }

    private static void Apply(Parameter parameter, string value)
    {
        var accepted = parameter.StorageType switch
        {
            StorageType.String => parameter.Set(value),
            StorageType.Double => parameter.SetValueString(value),
            StorageType.Integer when bool.TryParse(value, out var flag) => parameter.Set(flag ? 1 : 0),
            StorageType.Integer => parameter.Set(int.Parse(value, CultureInfo.InvariantCulture)),
            StorageType.ElementId => parameter.Set(new ElementId(long.Parse(value, CultureInfo.InvariantCulture))),
            _ => false
        };

        if (!accepted)
            throw new ArgumentException($"Revit no aceptó el valor '{value}' para '{parameter.Definition.Name}'.");
    }
}

public sealed class AttachWallsTool : IMcpTool
{
    public string Name => "attach_walls";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var walls = parameters.GetIds("wall_ids").Select(id => ModelLookup.GetElement<Wall>(doc, id)).ToList();
        var target = ModelLookup.GetElement(doc, parameters.GetId("target_id"));
        var location = parameters.GetOptionalString("location") switch
        {
            null or "top" => AttachmentLocation.Top,
            "base" => AttachmentLocation.Base,
            var other => throw new ArgumentException($"location debe ser 'top' o 'base', no '{other}'.")
        };

        return ModelTransaction.Run(doc, Name, () =>
        {
            foreach (var wall in walls)
                wall.AddAttachment(target.Id, location);
            return new { Attached = walls.Count, Target = ElementDescriber.Describe(target), Location = location.ToString() };
        });
    }
}

public sealed class ChangeTypeTool : IMcpTool
{
    public string Name => "change_type";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var element = ModelLookup.GetElement(doc, parameters.GetId("element_id"));
        var category = element.Category?.BuiltInCategory
            ?? throw new ArgumentException("El elemento no tiene categoría.");
        var type = ModelLookup.ResolveType<ElementType>(doc, category, parameters.GetString("type_name"));

        return ModelTransaction.Run(doc, Name, () =>
        {
            element.ChangeTypeId(type.Id);
            return ElementDescriber.Describe(element);
        });
    }
}

public sealed class SetElementColorTool : IMcpTool
{
    public string Name => "set_element_color";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var doc = context.Document;
        var ids = ModelLookup.GetElementIds(doc, parameters.GetIds("element_ids"));
        var view = ModelLookup.ResolveView(doc, parameters.GetOptionalString("view_name"));
        var color = parameters.GetOptionalString("color");
        var settings = color is null ? new OverrideGraphicSettings() : SolidColor(doc, HexColor.Parse(color));

        return ModelTransaction.Run(doc, Name, () =>
        {
            foreach (var id in ids)
                view.SetElementOverrides(id, settings);
            return new { View = view.Name, Updated = ids.Count, Color = color };
        });
    }

    private static OverrideGraphicSettings SolidColor(Document doc, Color color)
    {
        var solidFill = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(pattern => pattern.GetFillPattern().IsSolidFill)
            ?? throw new InvalidOperationException("El modelo no tiene un patrón de relleno sólido.");

        var settings = new OverrideGraphicSettings();
        settings.SetSurfaceForegroundPatternId(solidFill.Id);
        settings.SetSurfaceForegroundPatternColor(color);
        settings.SetCutForegroundPatternId(solidFill.Id);
        settings.SetCutForegroundPatternColor(color);
        return settings;
    }
}
