using VirtualBank.Presentation.Http;
using VirtualBank.Application.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace VirtualBank.Presentation.UnitTests;

/// <summary>The only place where use-case outcomes become HTTP status codes.</summary>
public sealed class UseCaseHttpResultsTests
{
    private static IResult Map(UseCaseResult<string> result) => result.ToHttp(value => TypedResults.Ok(value));

    [Fact]
    public void Success_is_200()
    {
        Assert.Equal("ok", Assert.IsType<Ok<string>>(Map(UseCaseResult<string>.Success("ok"))).Value);
    }

    [Fact]
    public void Not_found_is_404()
    {
        Assert.IsType<NotFound>(Map(UseCaseResult<string>.NotFound()));
    }

    [Fact]
    public void Forbidden_is_403_with_the_notice_code()
    {
        var problem = Assert.IsType<ProblemHttpResult>(Map(UseCaseResult<string>.Forbidden("CREDIT_READ_REQUIRED")));

        Assert.Equal(403, problem.StatusCode);
        Assert.Equal("CREDIT_READ_REQUIRED", problem.ProblemDetails.Extensions["noticeCode"]);
    }

    [Fact]
    public void Invalid_is_a_validation_problem()
    {
        Assert.IsType<ValidationProblem>(Map(UseCaseResult<string>.Invalid("text", "required")));
    }
}
