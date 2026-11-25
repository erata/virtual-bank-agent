using System.Text.Json;
using VirtualBank.Infrastructure.Identity;

namespace VirtualBank.Specifications;

/// <summary>The machine-client allowlist exactly as the services configure it (appsettings.json).</summary>
public static class TrustedMachineClients
{
    public static readonly string[] ServiceSettings =
    [
        "src/VirtualBank.Web/appsettings.json",
    ];

    public static IReadOnlyList<string> Read(string settingsPath)
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.Root, settingsPath)));
        return settings.RootElement.TryGetProperty("Authorization", out var authorization)
               && authorization.TryGetProperty("MachineClients", out var clients)
            ? clients.EnumerateArray().Select(c => c.GetString()!).ToList()
            : [];
    }

    /// <summary>Options as configured for the API host.</summary>
    public static MachineIdentityOptions FromServiceSettings() => new(Read(ServiceSettings[0]));
}
