using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HospitalSV.Middleware;

/// <summary>
/// Centralized enterprise exception handling middleware.
/// Catches unhandled exceptions, intercepts database errors to prevent SQL/schema leakage,
/// logs issues with correlation context, and returns standard RFC 7807 Problem Details.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items.TryGetValue("X-Correlation-ID", out var cid)
            ? cid?.ToString()
            : context.TraceIdentifier;

        var statusCode = (int)HttpStatusCode.InternalServerError;
        var title = "An internal server error occurred.";
        var message = "An unexpected error occurred while processing your request. Please contact support.";

        switch (exception)
        {
            case BadHttpRequestException badRequestEx:
                statusCode = StatusCodes.Status400BadRequest;
                title = "Bad Request";
                message = badRequestEx.Message;
                _logger.LogWarning("Bad request: {Message} | Correlation: {CorrelationId}", badRequestEx.Message, correlationId);
                break;

            case KeyNotFoundException notFoundEx:
                statusCode = StatusCodes.Status404NotFound;
                title = "Resource Not Found";
                message = notFoundEx.Message;
                _logger.LogInformation("Resource not found: {Message} | Correlation: {CorrelationId}", notFoundEx.Message, correlationId);
                break;

            case UnauthorizedAccessException unauthEx:
                statusCode = StatusCodes.Status401Unauthorized;
                title = "Unauthorized";
                message = string.IsNullOrWhiteSpace(unauthEx.Message) ? "Authentication credentials missing or invalid." : unauthEx.Message;
                _logger.LogWarning("Unauthorized access attempt: {Message} | Correlation: {CorrelationId}", unauthEx.Message, correlationId);
                break;

            case DbUpdateConcurrencyException concurrencyEx:
                statusCode = StatusCodes.Status409Conflict;
                title = "Concurrency Conflict";
                message = "The record was modified or deleted by another operation. Please reload and try again.";
                _logger.LogWarning(concurrencyEx, "Concurrency conflict occurred | Correlation: {CorrelationId}", correlationId);
                break;

            case DbUpdateException dbEx:
                // Intercept database constraint errors to avoid leaking internal DB schema or SQL queries
                if (dbEx.InnerException is PostgresException pgEx)
                {
                    // 23505: unique_violation
                    if (pgEx.SqlState == "23505")
                    {
                        statusCode = StatusCodes.Status409Conflict;
                        title = "Conflict";
                        message = "A record with the specified unique attributes already exists.";
                    }
                    // 23503: foreign_key_violation
                    else if (pgEx.SqlState == "23503")
                    {
                        statusCode = StatusCodes.Status400BadRequest;
                        title = "Invalid Reference";
                        message = "Referenced entity does not exist or cannot be modified.";
                    }
                    else
                    {
                        statusCode = StatusCodes.Status400BadRequest;
                        title = "Database Constraint Violation";
                        message = "The requested operation violates database integrity constraints.";
                    }
                }
                else
                {
                    statusCode = StatusCodes.Status400BadRequest;
                    title = "Database Error";
                    message = "Could not save changes to database. Please verify input data.";
                }

                _logger.LogError(dbEx, "Database exception intercepted. StatusCode: {StatusCode} | Correlation: {CorrelationId}", statusCode, correlationId);
                break;

            default:
                _logger.LogError(exception, "Unhandled exception occurred. TraceId: {TraceId} | Correlation: {CorrelationId}", context.TraceIdentifier, correlationId);
                if (_env.IsDevelopment())
                {
                    message = exception.Message;
                }
                break;
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var problemDetails = new
        {
            status = statusCode,
            title = title,
            message = message,
            correlationId = correlationId,
            traceId = context.TraceIdentifier,
            timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}

public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionMiddleware>();
    }
}
