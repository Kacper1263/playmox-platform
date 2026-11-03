using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace PlaymoxAPI.Extensions.Errors;

public class PrettyErrorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PrettyErrorMiddleware> _logger;

    public PrettyErrorMiddleware(RequestDelegate next, ILogger<PrettyErrorMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var originalBodyStream = context.Response.Body;

        using var newBodyStream = new MemoryStream();
        context.Response.Body = newBodyStream;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";

            var error = new ApiError
            {
                Status = 500,
                Error = "Internal Server Error",
                Message = "An unexpected server error occurred.",
                Path = context.Request.Path
            };

            var json = JsonSerializer.Serialize(error);
            context.Response.Body = originalBodyStream;
            await context.Response.WriteAsync(json);
            return;
        }

        // If status indicates an error and the body is a plain string, wrap it in ApiError
        if (context.Response.StatusCode >= 400 && newBodyStream.Length > 0)
        {
            newBodyStream.Seek(0, SeekOrigin.Begin);
            var reader = new StreamReader(newBodyStream);
            var bodyText = await reader.ReadToEndAsync();

            // If the response is raw text (e.g., "User data is required.")
            if (!IsJson(bodyText))
            {
                var error = new ApiError
                {
                    Status = context.Response.StatusCode,
                    Error = ReasonPhrases.GetReasonPhrase(context.Response.StatusCode),
                    Message = bodyText,
                    Path = context.Request.Path
                };

                context.Response.ContentType = "application/json";
                var wrapped = JsonSerializer.Serialize(error);

                context.Response.Body = originalBodyStream;
                await context.Response.WriteAsync(wrapped);
                return;
            }

            var hasMessage = HasJsonProperty(bodyText, "message");
            if (!hasMessage)
            {
                var extractedMessage = TryExtractMessageFromJson(bodyText) ?? ReasonPhrases.GetReasonPhrase(context.Response.StatusCode);
                var error = new ApiError
                {
                    Status = context.Response.StatusCode,
                    Error = ReasonPhrases.GetReasonPhrase(context.Response.StatusCode),
                    Message = extractedMessage,
                    Path = context.Request.Path
                };

                context.Response.ContentType = "application/json";
                var wrapped = JsonSerializer.Serialize(error);

                context.Response.Body = originalBodyStream;
                await context.Response.WriteAsync(wrapped);
                return;
            }
        }

        // Forward the original response
        newBodyStream.Seek(0, SeekOrigin.Begin);
        await newBodyStream.CopyToAsync(originalBodyStream);
        context.Response.Body = originalBodyStream;
    }

    private static bool IsJson(string? input)
    {
        input = input?.Trim();
        return input?.StartsWith("{") == true || input?.StartsWith("[") == true;
    }

    private static bool HasJsonProperty(string? input, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        try
        {
            using var json = JsonDocument.Parse(input);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in json.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string? TryExtractMessageFromJson(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(input);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (json.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString();
            }

            if (json.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
            {
                return title.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
