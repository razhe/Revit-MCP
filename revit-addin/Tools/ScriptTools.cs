using System.Collections;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using RevitMcp.Revit;
using Document = Autodesk.Revit.DB.Document;

namespace RevitMcp.Tools;

public sealed class ScriptGlobals(UIApplication app, UIDocument uiDoc, Document doc)
{
    public UIApplication App { get; } = app;

    public UIDocument UiDoc { get; } = uiDoc;

    public Document Doc { get; } = doc;
}

/// <summary>
/// Compila y ejecuta código C# (Roslyn scripting) en el hilo de Revit, por defecto dentro de
/// una transacción abierta. Las variables globales disponibles son App, UiDoc y Doc.
/// </summary>
public sealed class ExecuteCSharpTool : IMcpTool
{
    private static readonly ScriptOptions Options = ScriptOptions.Default
        .AddReferences(typeof(Document).Assembly, typeof(UIApplication).Assembly, typeof(Enumerable).Assembly)
        .AddImports(
            "System",
            "System.Linq",
            "System.Collections.Generic",
            "Autodesk.Revit.DB",
            "Autodesk.Revit.DB.Architecture",
            "Autodesk.Revit.UI");

    public string Name => "execute_csharp";

    public object? Execute(ToolContext context, ToolParams parameters)
    {
        var script = CSharpScript.Create<object>(parameters.GetString("code"), Options, typeof(ScriptGlobals));
        var errors = script.Compile().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
            throw new ArgumentException($"Error de compilación:\n{string.Join("\n", errors)}");

        var run = script.CreateDelegate();
        var doc = context.Document;
        var globals = new ScriptGlobals(context.App, context.UiDocument, doc);
        object? Evaluate() => run(globals).GetAwaiter().GetResult();
        var result = parameters.GetBool("use_transaction", true) ? ModelTransaction.Run(doc, Name, Evaluate) : Evaluate();

        return new { Result = Format(result) };
    }

    private static object? Format(object? value) => value switch
    {
        null => null,
        string text => text,
        IEnumerable items => items.Cast<object?>().Select(item => item?.ToString()).ToList(),
        _ => value.ToString()
    };
}
