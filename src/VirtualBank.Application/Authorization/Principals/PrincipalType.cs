namespace VirtualBank.Application.Authorization.Principals;

/// <summary>Identity category. A category never grants business permissions by itself.</summary>
public enum PrincipalType
{
    Human,
    Administrative,
    Machine,
}
