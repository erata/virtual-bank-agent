using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace VirtualBank.Presentation.Http;

/// <summary>Uniform, non-revealing error responses (RFC 9457 problem details).</summary>
public static class ApiResults
{
    /// <summary>403 with an optional deterministic notice code; never includes policy internals.</summary>
    public static ProblemHttpResult Forbidden(string? noticeCode = null)
    {
        var extensions = noticeCode is null ? null : new Dictionary<string, object?> { ["noticeCode"] = noticeCode };
        return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", extensions: extensions);
    }
}
