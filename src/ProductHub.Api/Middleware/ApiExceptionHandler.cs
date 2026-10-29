using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ProductHub.Application.Agents;
using ProductHub.Application.Content;
using ProductHub.Application.Knowledge;
using ProductHub.Application.Products;

namespace ProductHub.Api.Middleware;

internal sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger
) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        var (status, title, extensions) = exception switch
        {
            ValidationException validation => (
                StatusCodes.Status400BadRequest,
                "Validation failed",
                new Dictionary<string, object?> { ["errors"] = validation.Errors }
            ),
            ConflictException => (
                StatusCodes.Status409Conflict,
                "Conflict",
                new Dictionary<string, object?>()
            ),
            NotFoundException => (
                StatusCodes.Status404NotFound,
                "Not found",
                new Dictionary<string, object?>()
            ),
            GenerationUnavailableException generation => (
                StatusCodes.Status503ServiceUnavailable,
                "Content generation failed",
                new Dictionary<string, object?> { ["errorCode"] = generation.ErrorCode }
            ),
            DocumentIngestionException ingestion => (
                StatusCodes.Status503ServiceUnavailable,
                "Document processing failed",
                new Dictionary<string, object?> { ["errorCode"] = ingestion.ErrorCode }
            ),
            AgentWorkflowException agent => (
                StatusCodes.Status422UnprocessableEntity,
                "Agent workflow failed",
                new Dictionary<string, object?> { ["errorCode"] = agent.ErrorCode }
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred",
                new Dictionary<string, object?>()
            ),
        };
        if (status == 500)
            logger.LogError(
                exception,
                "Unhandled request failure. TraceId: {TraceId}",
                context.TraceIdentifier
            );

        context.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = status,
                    Title = title,
                    Detail = status == 500 ? null : exception.Message,
                    Extensions = extensions,
                },
            }
        );
    }
}
