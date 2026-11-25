namespace VirtualBank.Specifications;

/// <summary>Locates the canonical specification and fixture files relative to the repository root.</summary>
public static class RepositoryPaths
{
    // VIRTUAL_BANK_REPO_ROOT overrides discovery, e.g. when test binaries run outside the repository tree.
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("VIRTUAL_BANK_REPO_ROOT") is { Length: > 0 } root ? root : FindRoot(AppContext.BaseDirectory);

    public static string Realm => Path.Combine(Root, "infra", "keycloak", "realm-virtual-bank.json");

    public static string Fixtures => Path.Combine(Root, "infra", "seed", "fixtures.json");

    public static string Permissions => Path.Combine(Root, "policies", "permissions.json");

    public static string AuthorizationMatrix => Path.Combine(Root, "tests", "specifications", "authorization-matrix.yaml");

    private static string FindRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PROJECT_SPEC.md")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (PROJECT_SPEC.md) not found.");
    }
}
