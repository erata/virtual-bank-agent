using Xunit;

namespace VirtualBank.ArchitectureTests;

/// <summary>
/// Layer rules (ADR-0009): Web → Presentation → Application → Model, and Infrastructure → Application.
/// Arrows point inwards only; a failure names the offending dependency.
/// </summary>
public sealed class DependencyRuleTests
{
    /// <summary>Technologies of the outer rings: web, ORM, drivers, identity libraries, DI/hosting/logging frameworks.</summary>
    private static readonly string[] OuterTechnology =
    [
        "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.Extensions", "Microsoft.IdentityModel",
        "System.IdentityModel", "Microsoft.Agents", "Microsoft.SemanticKernel",
    ];

    private static string[] Matching(Layer layer, params string[] prefixes) =>
        layer.AllDependencies
            .Where(d => prefixes.Any(p => d.Equals(p, StringComparison.Ordinal) || d.StartsWith(p + ".", StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] OwnLayers(Layer layer) =>
        layer.AllDependencies.Where(d => d.StartsWith("VirtualBank.", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void Model_depends_on_nothing_but_the_base_class_library()
    {
        Assert.Empty(OwnLayers(Layer.Model));
        Assert.Empty(Matching(Layer.Model, OuterTechnology));
        Assert.Empty(Layer.Model.DeclaredPackageReferences);
    }

    [Fact]
    public void Application_depends_only_on_model()
    {
        Assert.Equal(new[] { "VirtualBank.Model" }, OwnLayers(Layer.Application));
        Assert.Empty(Matching(Layer.Application, OuterTechnology));
        Assert.Empty(Layer.Application.DeclaredPackageReferences);
    }

    [Fact]
    public void Infrastructure_implements_application_and_knows_no_outer_layer()
    {
        Assert.Empty(Matching(Layer.Infrastructure, "VirtualBank.Presentation", "VirtualBank.Web"));
        Assert.Contains("VirtualBank.Application", OwnLayers(Layer.Infrastructure));
    }

    [Fact]
    public void Presentation_knows_application_but_not_infrastructure_the_driver_or_the_identity_library()
    {
        Assert.Empty(Matching(Layer.Presentation,
            "VirtualBank.Infrastructure", "VirtualBank.Web", "Npgsql", "Microsoft.AspNetCore.Authentication.JwtBearer", "Microsoft.IdentityModel"));
        Assert.Contains("VirtualBank.Application", OwnLayers(Layer.Presentation));
    }

    [Fact]
    public void Web_is_the_only_layer_that_references_both_presentation_and_infrastructure()
    {
        Assert.Equal(new[] { "VirtualBank.Infrastructure", "VirtualBank.Presentation" }, Layer.Web.DeclaredProjectReferences.Order(StringComparer.Ordinal));
        foreach (var layer in Layer.All.Where(l => l != Layer.Web))
        {
            var both = OwnLayers(layer).Contains("VirtualBank.Infrastructure") && OwnLayers(layer).Contains("VirtualBank.Presentation");
            Assert.False(both, $"{layer} references both outer layers; only the composition root may.");
        }
    }

    [Fact]
    public void Inner_layers_are_plain_class_libraries()
    {
        foreach (var layer in new[] { Layer.Model, Layer.Application })
        {
            Assert.Equal("Microsoft.NET.Sdk", layer.Sdk);
            Assert.Empty(layer.DeclaredFrameworkReferences);
        }
    }
}
