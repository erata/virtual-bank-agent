namespace VirtualBank.Application.Assistant.Tools;

/// <summary>Arguments after gate validation. Only declared parameters are present; undeclared ones are dropped.</summary>
public sealed class ToolArguments(IReadOnlyDictionary<string, string> values)
{
    public string Get(string name) => values[name];

    public string? Find(string name) => values.TryGetValue(name, out var value) ? value : null;
}
