namespace CosmoNet.App.Models;

public sealed class CoreDiagnosticResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string Details { get; init; } = "";

    public static CoreDiagnosticResult Ok(string message, string details = "")
    {
        return new CoreDiagnosticResult
        {
            Success = true,
            Message = message,
            Details = details
        };
    }

    public static CoreDiagnosticResult Fail(string message, string details = "")
    {
        return new CoreDiagnosticResult
        {
            Success = false,
            Message = message,
            Details = details
        };
    }
}
