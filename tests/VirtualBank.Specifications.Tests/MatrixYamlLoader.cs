using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VirtualBank.Specifications.Tests;

/// <summary>Reads the independently maintained authorization matrix.</summary>
internal static class MatrixLoader
{
    public static MatrixDocument Load()
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        return deserializer.Deserialize<MatrixDocument>(File.ReadAllText(RepositoryPaths.AuthorizationMatrix));
    }
}
