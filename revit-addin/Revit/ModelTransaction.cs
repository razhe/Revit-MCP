using Autodesk.Revit.DB;

namespace RevitMcp.Revit;

/// <summary>
/// Ejecuta una modificación en una transacción con nombre "MCP: ..." (visible en Deshacer).
/// Las advertencias se descartan para que Revit no bloquee con diálogos; si hay errores
/// la transacción se revierte y se lanza una excepción con los mensajes de Revit.
/// </summary>
internal static class ModelTransaction
{
    public static T Run<T>(Document doc, string name, Func<T> action)
    {
        using var transaction = new Transaction(doc, $"MCP: {name}");
        transaction.Start();

        var failures = new FailureCollector();
        var options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(failures);
        options.SetClearAfterRollback(true);
        transaction.SetFailureHandlingOptions(options);

        var result = action();
        if (transaction.Commit() != TransactionStatus.Committed)
            throw failures.ToException();

        return result;
    }
}

internal sealed class FailureCollector : IFailuresPreprocessor
{
    private readonly List<string> _errors = [];

    public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
    {
        foreach (var failure in accessor.GetFailureMessages())
        {
            if (failure.GetSeverity() == FailureSeverity.Warning)
                accessor.DeleteWarning(failure);
            else
                _errors.Add(failure.GetDescriptionText());
        }

        return _errors.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
    }

    public InvalidOperationException ToException() =>
        new($"Revit revirtió la operación: {string.Join(" | ", _errors)}");
}
