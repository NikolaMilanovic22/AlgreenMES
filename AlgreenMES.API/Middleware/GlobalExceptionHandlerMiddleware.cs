using System.Net;
using System.Text.Json;
using AlGreenMES.BuildingBlocks.Common.Exceptions;

namespace AlgreenMES.API.Middleware;

public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Not found: {Code} - {Message}", ex.Code, ex.Message);
            await WriteResponseAsync(context, HttpStatusCode.NotFound, new
            {
                error = new { code = ex.Code, message = ex.Message }
            });
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed: {Message}", ex.Message);
            await WriteResponseAsync(context, HttpStatusCode.UnprocessableEntity, new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = ex.Message,
                    errors = ex.Errors.Select(e => new { property = e.Property, message = e.Message })
                }
            });
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Forbidden: {Code} - {Message}", ex.Code, ex.Message);
            await WriteResponseAsync(context, HttpStatusCode.Forbidden, new
            {
                error = new { code = ex.Code, message = ex.Message }
            });
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Domain exception: {Code} - {Message}", ex.Code, ex.Message);
            await WriteResponseAsync(context, HttpStatusCode.BadRequest, new
            {
                error = new { code = ex.Code, message = ex.Message }
            });
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException ex)
        {
            // Client cut the connection mid-request (tab closed, navigation,
            // network drop) — surfaces here as "Unexpected end of request
            // content." or similar. Not a server bug; log at Warning so
            // Sentry doesn't page on every dropped /auth/refresh poll, and
            // return 400 instead of bubbling to 500.
            _logger.LogWarning(ex, "Bad HTTP request: {Message}", ex.Message);
            await WriteResponseAsync(context, HttpStatusCode.BadRequest, new
            {
                error = new { code = "BAD_REQUEST", message = "Malformed or interrupted request." }
            });
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client aborted the request — nothing to log, nothing to return
            // (the connection is already gone). Avoid Sentry noise.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteResponseAsync(context, HttpStatusCode.InternalServerError, new
            {
                error = new { code = "INTERNAL_ERROR", message = "An unexpected error occurred." }
            });
        }
    }

    private static async Task WriteResponseAsync(HttpContext context, HttpStatusCode statusCode, object response)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }
}
