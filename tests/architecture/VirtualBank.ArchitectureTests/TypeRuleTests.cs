using System.Reflection;
using VirtualBank.Application.Authorization.Policies;
using VirtualBank.Application.Authorization.Principals;
using Xunit;

namespace VirtualBank.ArchitectureTests;

/// <summary>Rules on types: where concepts may live and how the layers are shaped.</summary>
public sealed class TypeRuleTests
{
    private static IEnumerable<Type> PublicTypes(Layer layer) => layer.Assembly.GetExportedTypes();

    private static bool InNamespace(Type t, string ns) =>
        t.Namespace is not null && (t.Namespace == ns || t.Namespace.StartsWith(ns + ".", StringComparison.Ordinal));

    [Fact]
    public void Authorization_engine_contains_no_business_types()
    {
        var business = PublicTypes(Layer.Application)
            .Where(t => InNamespace(t, "VirtualBank.Application.Authorization"))
            .Where(t => t.Name.Contains("Customer", StringComparison.Ordinal) || t.Name.Contains("Credit", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToList();

        Assert.Empty(business);
    }

    [Fact]
    public void Authorization_engine_ships_mechanisms_and_modules_ship_the_rules()
    {
        var engine = Layer.Application.Assembly.GetTypes()
            .Where(t => InNamespace(t, "VirtualBank.Application.Authorization"))
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IResourcePolicy).IsAssignableFrom(t))
            .Select(t => t.Name);

        Assert.Equal(new[] { "PermissionPolicy" }, engine);
    }

    [Fact]
    public void Application_ports_are_interfaces_implemented_only_in_infrastructure()
    {
        var ports = PublicTypes(Layer.Application)
            .Where(t => t.Namespace!.EndsWith(".Abstractions", StringComparison.Ordinal))
            .Append(typeof(IPrincipalResolver))
            .ToList();

        Assert.Empty(ports.Where(t => !t.IsInterface).Select(t => t.FullName!));
        foreach (var port in ports)
        {
            var implementations = Layer.All.SelectMany(l => l.Assembly.GetTypes())
                .Where(t => t is { IsClass: true, IsAbstract: false } && port.IsAssignableFrom(t))
                .ToList();
            Assert.NotEmpty(implementations);
            Assert.All(implementations, t => Assert.Equal(Layer.Infrastructure.Assembly, t.Assembly));
        }
    }

    [Fact]
    public void Use_case_handlers_follow_the_cqrs_naming_and_folder_convention()
    {
        var handlers = PublicTypes(Layer.Application).Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal)).ToList();

        Assert.Equal(11, handlers.Count); // + ListCreditNotesHandler
        foreach (var handler in handlers)
        {
            var isQuery = handler.Namespace!.Contains(".Queries.", StringComparison.Ordinal);
            var isCommand = handler.Namespace.Contains(".Commands.", StringComparison.Ordinal);
            Assert.True(isQuery ^ isCommand, $"{handler.FullName} must live under Queries or Commands.");

            var message = handler.GetMethod("HandleAsync")!.GetParameters()[0].ParameterType;
            Assert.True(message.Name.EndsWith(isQuery ? "Query" : "Command", StringComparison.Ordinal),
                $"{handler.FullName} handles {message.Name}; expected a {(isQuery ? "query" : "command")} message.");
        }
    }

    [Fact]
    public void Model_exposes_no_public_setters()
    {
        var setters = PublicTypes(Layer.Model)
            .Where(t => !typeof(Exception).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is { IsPublic: true } && !IsInitOnly(p))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.Empty(setters);
    }

    private static bool IsInitOnly(PropertyInfo property) =>
        property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    [Fact]
    public void Presentation_returns_http_results_only_through_the_single_mapping()
    {
        // Endpoints call ToHttp(); no use-case status is translated to HTTP anywhere else.
        var offenders = Layer.Presentation.SourceFiles
            .Where(f => !f.EndsWith("UseCaseHttpResults.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("UseCaseStatus.", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .ToList();

        Assert.Empty(offenders);
    }
}
