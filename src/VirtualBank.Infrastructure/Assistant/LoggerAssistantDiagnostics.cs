using Microsoft.Extensions.Logging;
using VirtualBank.Application.Assistant.Abstractions;

namespace VirtualBank.Infrastructure.Assistant;

/// <summary>Logs assistant failures on the "VirtualBank.Assistant" category. Messages and prompts are not logged.</summary>
public sealed class LoggerAssistantDiagnostics(ILoggerFactory loggerFactory) : IAssistantDiagnostics
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("VirtualBank.Assistant");

    public void ToolFailed(string tool, Exception exception) =>
        _logger.LogError(exception, "Assistant tool {Tool} failed", tool);

    public void RuntimeUnavailable(string mode, Exception exception) =>
        _logger.LogWarning(exception, "Assistant runtime {Mode} unavailable", mode);
}
