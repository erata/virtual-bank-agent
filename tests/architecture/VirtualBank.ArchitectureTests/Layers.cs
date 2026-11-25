using System.Reflection;
using System.Xml.Linq;
using VirtualBank.Specifications;

namespace VirtualBank.ArchitectureTests;

/// <summary>One production project: its compiled assembly and its .csproj (relative to the repository root).</summary>
public sealed record Layer(string Name, Assembly Assembly)
{
    public static readonly Layer Model = new("VirtualBank.Model", typeof(Model.Credit.CreditApplication).Assembly);

    public static readonly Layer Application = new("VirtualBank.Application", typeof(Application.Common.UseCaseResult<>).Assembly);

    public static readonly Layer Infrastructure = new("VirtualBank.Infrastructure", typeof(Infrastructure.DependencyInjection).Assembly);

    public static readonly Layer Presentation = new("VirtualBank.Presentation", typeof(Presentation.EndpointMapping).Assembly);

    public static readonly Layer Web = new("VirtualBank.Web", typeof(Web.CompositionRoot).Assembly);

    public static IReadOnlyList<Layer> All { get; } = [Model, Application, Infrastructure, Presentation, Web];

    public string ProjectDirectory => Path.Combine(RepositoryPaths.Root, "src", Name);

    private XDocument Project => XDocument.Load(Path.Combine(ProjectDirectory, Name + ".csproj"));

    /// <summary>Names of the assemblies the compiled IL actually references.</summary>
    public IReadOnlyList<string> ReferencedAssemblies => Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

    public IReadOnlyList<string> DeclaredProjectReferences => Project.Descendants("ProjectReference")
        .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value)).ToList();

    public IReadOnlyList<string> DeclaredPackageReferences => Project.Descendants("PackageReference")
        .Select(e => e.Attribute("Include")!.Value).ToList();

    public IReadOnlyList<string> DeclaredFrameworkReferences => Project.Descendants("FrameworkReference")
        .Select(e => e.Attribute("Include")!.Value).ToList();

    public string Sdk => Project.Root!.Attribute("Sdk")!.Value;

    /// <summary>Everything this layer depends on, declared or compiled, for "must not depend on" rules.</summary>
    public IReadOnlyList<string> AllDependencies =>
        [.. ReferencedAssemblies, .. DeclaredProjectReferences, .. DeclaredPackageReferences, .. DeclaredFrameworkReferences];

    public IEnumerable<string> SourceFiles => Directory.EnumerateFiles(ProjectDirectory, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    public override string ToString() => Name;
}
