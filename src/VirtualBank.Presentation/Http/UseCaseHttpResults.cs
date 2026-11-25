using VirtualBank.Application.Common;
using Microsoft.AspNetCore.Http;

namespace VirtualBank.Presentation.Http;

/// <summary>The single translation from transport-neutral use-case outcomes to HTTP.</summary>
public static class UseCaseHttpResults
{
    public static IResult ToHttp<T>(this UseCaseResult<T> result, Func<T, IResult> onSuccess) => result.Status switch
    {
        UseCaseStatus.Succeeded => onSuccess(result.Value!),
        UseCaseStatus.NotFound => TypedResults.NotFound(),
        UseCaseStatus.Forbidden => ApiResults.Forbidden(result.NoticeCode),
        UseCaseStatus.Invalid => TypedResults.ValidationProblem(result.Errors),
        _ => throw new InvalidOperationException($"Unmapped use-case status {result.Status}."),
    };
}
