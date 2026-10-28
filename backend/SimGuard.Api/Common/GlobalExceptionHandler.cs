using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace SimGuard.Api.Common;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException
            && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer. 499 ("client closed
            // request") keeps logs and metrics from counting this as a success.
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        var (status, title, detail) = Classify(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception");
        }
        else
        {
            logger.LogWarning("Request failed with {StatusCode}: {Message}", status, exception.Message);
        }

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail
            }
        });
    }

    private static (int Status, string Title, string Detail) Classify(Exception exception)
    {
        if (exception is BadHttpRequestException badRequest)
        {
            return (
                badRequest.StatusCode,
                "Bad request",
                "The request could not be read. Check the JSON body and parameter types.");
        }

        var postgresException = FindPostgresException(exception);

        return postgresException?.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation => (
                StatusCodes.Status409Conflict,
                "Conflict",
                "A record with the same unique value already exists."),
            PostgresErrorCodes.ForeignKeyViolation => (
                StatusCodes.Status409Conflict,
                "Conflict",
                "The operation conflicts with related records."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                "An unexpected error occurred.")
        };
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }
        }

        return null;
    }
}
