using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace PlaymoxAPI.Extensions.Swagger;

public class SwaggerBasicAuthMiddleware
{
    private readonly RequestDelegate next;
    private readonly IConfiguration configuration;
    public SwaggerBasicAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        this.next = next;
        this.configuration = configuration;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            var swaggerUser = configuration["Security:Swagger:Username"];
            var swaggerPass = configuration["Security:Swagger:Password"];
            if (string.IsNullOrWhiteSpace(swaggerUser) || string.IsNullOrWhiteSpace(swaggerPass))
            {
                throw new InvalidOperationException("Security:Swagger credentials are not configured.");
            }

            string authHeader = context.Request.Headers["Authorization"];
            if (authHeader != null && authHeader.StartsWith("Basic "))
            {
                // Get the credentials from request header
                var header = AuthenticationHeaderValue.Parse(authHeader);
                var inBytes = Convert.FromBase64String(header.Parameter);
                var credentialsRaw = Encoding.UTF8.GetString(inBytes);
                var separatorIndex = credentialsRaw.IndexOf(':');
                if (separatorIndex > 0)
                {
                    var username = credentialsRaw[..separatorIndex];
                    var password = credentialsRaw[(separatorIndex + 1)..];
                    if (string.Equals(username, swaggerUser, StringComparison.Ordinal) &&
                        string.Equals(password, swaggerPass, StringComparison.Ordinal))
                    {
                        await next.Invoke(context).ConfigureAwait(false);
                        return;
                    }
                }
            }
            context.Response.Headers["WWW-Authenticate"] = "Basic";
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
        }
        else
        {
            await next.Invoke(context).ConfigureAwait(false);
        }
    }
    
}

public static class SwaggerExtensions
{
    public static IApplicationBuilder UseSwaggerAuthorized(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SwaggerBasicAuthMiddleware>();
    }
}
