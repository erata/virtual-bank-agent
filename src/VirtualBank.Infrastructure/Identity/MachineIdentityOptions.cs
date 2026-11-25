using System.Collections.Frozen;

namespace VirtualBank.Infrastructure.Identity;

/// <summary>
/// Keycloak client IDs whose tokens may represent a machine identity. A <c>principal_type=machine</c> claim
/// is honored only together with an <c>azp</c> from this list, so a misconfigured mapper on a human-facing
/// client (UI, CLI) is rejected by the application as well. An empty list means: no machine principals.
/// Configured per service (<c>Authorization:MachineClients</c>).
/// </summary>
public sealed class MachineIdentityOptions
{
    public MachineIdentityOptions(IEnumerable<string> trustedClients)
    {
        ArgumentNullException.ThrowIfNull(trustedClients);
        TrustedClients = trustedClients
            .Select(c => c?.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .ToFrozenSet(StringComparer.Ordinal)!;
    }

    public static MachineIdentityOptions None { get; } = new([]);

    public FrozenSet<string> TrustedClients { get; }

    public bool IsTrusted(string? clientId) => clientId is not null && TrustedClients.Contains(clientId);
}
