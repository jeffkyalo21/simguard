namespace SimGuard.Api.Common;

public static class ApiProblems
{
    public static IResult NotFound(string detail) =>
        TypedResults.Problem(
            title: "Resource not found",
            detail: detail,
            statusCode: StatusCodes.Status404NotFound);

    public static IResult Conflict(string detail) =>
        TypedResults.Problem(
            title: "Conflict",
            detail: detail,
            statusCode: StatusCodes.Status409Conflict);
}
