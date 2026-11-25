namespace VirtualBank.Application.Authorization.Resources;

/// <summary>
/// Server-side description of the resource being accessed. Attribute values must come from the
/// owning service's data store, never from the caller or the model.
/// </summary>
public abstract record ResourceDescriptor(string Type, string Id)
{
    public string Reference => $"{Type}:{Id}";
}

/// <summary>Resource identified only by type and id, used for permission-scoped capabilities.</summary>
public sealed record NamedResource(string ResourceType, string ResourceId) : ResourceDescriptor(ResourceType, ResourceId);
