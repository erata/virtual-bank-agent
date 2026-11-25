namespace VirtualBank.Application.Authorization.Principals;

/// <summary>Ordered document clearance levels; a higher value satisfies every lower classification.</summary>
public enum Clearance
{
    Public = 0,
    Internal = 1,
    Confidential = 2,
    StrictlyConfidential = 3,
}

public static class ClearanceParser
{
    /// <summary>Parses the token claim value. Unknown or missing values fall back to <see cref="Clearance.Public"/> (least privilege).</summary>
    public static Clearance ParseOrLowest(string? value) => value switch
    {
        "internal" => Clearance.Internal,
        "confidential" => Clearance.Confidential,
        "strictly_confidential" => Clearance.StrictlyConfidential,
        _ => Clearance.Public,
    };
}
