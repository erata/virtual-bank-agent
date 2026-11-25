using System.Collections.Frozen;
using System.Text.Json;

namespace VirtualBank.Application.Authorization.Permissions;

/// <summary>
/// Central role-to-permission mapping loaded from <c>policies/permissions.json</c>.
/// Unknown roles map to no permissions (deny by default).
/// </summary>
public sealed class PermissionCatalog
{
    private readonly FrozenDictionary<string, FrozenSet<Permission>> _rolePermissions;

    private PermissionCatalog(FrozenSet<Permission> permissions, FrozenDictionary<string, FrozenSet<Permission>> rolePermissions)
    {
        Permissions = permissions;
        _rolePermissions = rolePermissions;
    }

    public FrozenSet<Permission> Permissions { get; }

    public IEnumerable<string> Roles => _rolePermissions.Keys;

    public static PermissionCatalog FromFile(string path) => FromJson(File.ReadAllText(path));

    public static PermissionCatalog FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var known = root.GetProperty("permissions").EnumerateArray()
            .Select(p => new Permission(p.GetProperty("id").GetString()!))
            .ToFrozenSet();

        var roles = new Dictionary<string, FrozenSet<Permission>>(StringComparer.Ordinal);
        foreach (var role in root.GetProperty("roles").EnumerateObject())
        {
            var granted = role.Value.EnumerateArray().Select(p => new Permission(p.GetString()!)).ToList();
            var unknown = granted.Where(p => !known.Contains(p)).ToList();
            if (unknown.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Role '{role.Name}' references unknown permissions: {string.Join(", ", unknown)}.");
            }

            roles[role.Name] = granted.ToFrozenSet();
        }

        return new PermissionCatalog(known, roles.ToFrozenDictionary(StringComparer.Ordinal));
    }

    public FrozenSet<Permission> PermissionsFor(IEnumerable<string> roles) =>
        roles.SelectMany(r => _rolePermissions.TryGetValue(r, out var p) ? p : Enumerable.Empty<Permission>())
             .ToFrozenSet();
}
