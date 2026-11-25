namespace VirtualBank.Application.Authorization.Decisions;

/// <summary>How much an ordinary user may learn about a denied resource.</summary>
public enum DisclosureMode
{
    /// <summary>Do not reveal that the resource exists (APIs answer 404, no notice is produced).</summary>
    None,

    /// <summary>Reveal only that part of the request could not be completed.</summary>
    Generic,

    /// <summary>Reveal which kind of permission would be required.</summary>
    PermissionHint,
}
