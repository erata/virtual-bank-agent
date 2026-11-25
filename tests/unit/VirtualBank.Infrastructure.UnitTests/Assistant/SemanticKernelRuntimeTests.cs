using Microsoft.SemanticKernel;
using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Infrastructure.Assistant.SemanticKernel;
using Xunit;

namespace VirtualBank.Infrastructure.UnitTests.Assistant;

/// <summary>
/// Runs against the real Semantic Kernel package (CI). Verifies the translation from the provider-neutral tool schema
/// to a kernel function, and that invoking it only forwards the name and raw string arguments to the invoker.
/// No model is called.
/// </summary>
public sealed class SemanticKernelRuntimeTests
{
    private sealed class RecordingInvoker : IToolInvoker
    {
        public string? Tool { get; private set; }

        public IReadOnlyDictionary<string, string?> Arguments { get; private set; } = new Dictionary<string, string?>();

        public Task<string> InvokeAsync(string toolName, IReadOnlyDictionary<string, string?> arguments, CancellationToken cancellationToken)
        {
            Tool = toolName;
            Arguments = arguments;
            return Task.FromResult("{\"status\":\"succeeded\"}");
        }
    }

    private static readonly ToolDefinition GetCustomer = new(
        "get_customer", "Returns one customer.",
        [new("customer_id", "Customer identifier.", ToolParameterKind.Identifier, AssistantToolCatalog.CustomerIdPattern)],
        ToolRisk.Read);

    [Fact]
    public void Kernel_function_exposes_the_declared_schema()
    {
        var function = SemanticKernelAssistantRuntime.ToFunction(GetCustomer, new RecordingInvoker());

        Assert.Equal("get_customer", function.Name);
        var parameter = Assert.Single(function.Metadata.Parameters);
        Assert.Equal("customer_id", parameter.Name);
        Assert.True(parameter.IsRequired);
        Assert.Equal(typeof(string), parameter.ParameterType);
    }

    [Fact]
    public async Task Kernel_function_forwards_the_call_to_the_application_invoker()
    {
        var invoker = new RecordingInvoker();
        var function = SemanticKernelAssistantRuntime.ToFunction(GetCustomer, invoker);

        var result = await function.InvokeAsync(new Kernel(), new KernelArguments { ["customer_id"] = "C-1002" });

        Assert.Equal("{\"status\":\"succeeded\"}", result.GetValue<string>());
        Assert.Equal("get_customer", invoker.Tool);
        Assert.Equal("C-1002", invoker.Arguments["customer_id"]);
    }
}
