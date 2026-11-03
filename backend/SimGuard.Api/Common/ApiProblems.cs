namespace SimGuard.Api.Common;

public static class ApiProblems
{
    public static IResult NotFound(string detail) =>
        TypedResults.Problem(
            title: "Resource not found",
            detail: detail,
            statusCode: StatusCodes.Status404NotFound);

    public static IResult Unauthorized(string detail) =>
        TypedResults.Problem(
            title: "Unauthorized",
            detail: detail,
            statusCode: StatusCodes.Status401Unauthorized);

    public static IResult Conflict(string detail) =>
        TypedResults.Problem(
            title: "Conflict",
            detail: detail,
            statusCode: StatusCodes.Status409Conflict);

    // A conflict with extra members next to title and detail, for example
    // errors keyed by field (like a 400) or a count the client shows.
    public static IResult Conflict(string detail, IDictionary<string, object?> extensions) =>
        TypedResults.Problem(
            title: "Conflict",
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            extensions: extensions);
}
