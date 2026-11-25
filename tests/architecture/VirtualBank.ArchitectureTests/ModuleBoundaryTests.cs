using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace VirtualBank.ArchitectureTests;

/// <summary>
/// Customers and Credit are peer modules inside every layer. They share the authorization engine but never each
/// other's code: checked on compiled type signatures and on source usings. Assistant is an orchestration module on
/// top of them: it may call their use cases (Application only), and neither peer module may know the assistant.
/// </summary>
public sealed partial class ModuleBoundaryTests
{
    private static readonly string[] Modules = ["Customers", "Credit"];

    [GeneratedRegex(@"\bVirtualBank\.(Model|Application|Infrastructure|Presentation)(\.\w+)*\.(Customers|Credit)\b")]
    private static partial Regex ModuleReference();

    private static string? ModuleOf(string? @namespace) =>
        @namespace is null ? null : Modules.FirstOrDefault(m => @namespace.Split('.').Contains(m));

    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (type.BaseType is not null) yield return type.BaseType;
        foreach (var i in type.GetInterfaces()) yield return i;
        foreach (var f in type.GetFields(all)) yield return f.FieldType;
        foreach (var p in type.GetProperties(all)) yield return p.PropertyType;
        foreach (var m in type.GetMethods(all))
        {
            yield return m.ReturnType;
            foreach (var p in m.GetParameters()) yield return p.ParameterType;
        }

        foreach (var c in type.GetConstructors(all))
        {
            foreach (var p in c.GetParameters()) yield return p.ParameterType;
        }
    }

    private static IEnumerable<Type> Flatten(Type type) =>
        type.IsGenericType ? type.GetGenericArguments().SelectMany(Flatten).Prepend(type) : type.HasElementType ? Flatten(type.GetElementType()!) : [type];

    [Fact]
    public void Module_types_do_not_reference_the_other_module()
    {
        var violations = Layer.All.SelectMany(l => l.Assembly.GetTypes())
            .Where(t => ModuleOf(t.Namespace) is not null)
            .SelectMany(t => SignatureTypes(t).SelectMany(Flatten)
                .Where(dep => dep.Assembly.GetName().Name!.StartsWith("VirtualBank.", StringComparison.Ordinal))
                .Where(dep => ModuleOf(dep.Namespace) is { } other && other != ModuleOf(t.Namespace))
                .Select(dep => $"{t.FullName} -> {dep.FullName}"))
            .Distinct()
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Module_source_files_do_not_import_the_other_module()
    {
        var violations = new List<string>();
        foreach (var layer in Layer.All)
        {
            foreach (var file in layer.SourceFiles)
            {
                var module = Modules.FirstOrDefault(m => file.Split(Path.DirectorySeparatorChar).Contains(m));
                if (module is null) continue;

                violations.AddRange(ModuleReference().Matches(File.ReadAllText(file))
                    .Select(m => m.Groups[3].Value)
                    .Where(referenced => referenced != module)
                    .Select(referenced => $"{Path.GetRelativePath(layer.ProjectDirectory, file)} ({layer}) uses {referenced}"));
            }
        }

        Assert.Empty(violations.Distinct());
    }

    [Fact]
    public void Each_layer_has_both_modules_or_neither()
    {
        foreach (var layer in new[] { Layer.Model, Layer.Application, Layer.Infrastructure, Layer.Presentation })
        {
            var present = Modules.Where(m => layer.Assembly.GetTypes().Any(t => ModuleOf(t.Namespace) == m)).ToList();
            Assert.Equal(Modules, present);
        }
    }

    private const string AssistantModule = "Assistant";

    private static bool IsAssistant(string? @namespace) => @namespace?.Split('.').Contains(AssistantModule) == true;

    private static IEnumerable<(Type Type, Type Dependency)> VirtualBankDependencies(IEnumerable<Type> types) =>
        types.SelectMany(t => SignatureTypes(t).SelectMany(Flatten)
            .Where(dep => dep.Assembly.GetName().Name!.StartsWith("VirtualBank.", StringComparison.Ordinal))
            .Select(dep => (t, dep)));

    [Fact]
    public void Peer_modules_do_not_reference_the_assistant()
    {
        var violations = VirtualBankDependencies(Layer.All.SelectMany(l => l.Assembly.GetTypes()).Where(t => ModuleOf(t.Namespace) is not null))
            .Where(d => IsAssistant(d.Dependency.Namespace))
            .Select(d => $"{d.Type.FullName} -> {d.Dependency.FullName}")
            .Distinct()
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Assistant_reaches_modules_only_through_application_use_cases()
    {
        // Outside Application the assistant must not touch module types at all; inside Application it may use the
        // handlers, commands/queries and views, but never a repository port (Abstractions) or module policy.
        var violations = VirtualBankDependencies(Layer.All.SelectMany(l => l.Assembly.GetTypes()).Where(t => IsAssistant(t.Namespace)))
            .Where(d => ModuleOf(d.Dependency.Namespace) is not null)
            .Where(d => d.Type.Assembly != Layer.Application.Assembly
                     || d.Dependency.Namespace!.Contains(".Abstractions", StringComparison.Ordinal)
                     || d.Dependency.Namespace!.Contains(".Authorization", StringComparison.Ordinal))
            .Select(d => $"{d.Type.FullName} -> {d.Dependency.FullName}")
            .Distinct()
            .ToList();

        Assert.Empty(violations);
    }
}
