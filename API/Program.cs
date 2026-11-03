using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PlaymoxAPI;
using PlaymoxAPI.BLL;
using PlaymoxAPI.Extensions.Auth;
using PlaymoxAPI.Extensions.Errors;
using PlaymoxAPI.Extensions.Swagger;
using PlaymoxAPI.Helpers;
using PlaymoxAPI.Services.Platform;
using PlaymoxAPI.Services.Proxmox;

var builder = WebApplication.CreateBuilder(args);

Config.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(Config.ConnectionString))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
}

Config.PasswordSalt = builder.Configuration["Security:PasswordSalt"] ?? string.Empty;
if (string.IsNullOrWhiteSpace(Config.PasswordSalt))
{
    throw new InvalidOperationException("Security:PasswordSalt is not configured.");
}

Config.Secret = builder.Configuration["Security:AppSecret"] ?? string.Empty;
if (string.IsNullOrWhiteSpace(Config.Secret))
{
    throw new InvalidOperationException("Security:AppSecret is not configured.");
}

Config.AllowSelfSignedProxmoxCertificates = builder.Configuration.GetValue<bool>("Proxmox:AllowSelfSignedCertificates", true);

builder.Services.AddControllers().AddNewtonsoftJson(opt =>
{
    opt.SerializerSettings.DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Include;
    opt.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Include;
    opt.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

var allowedOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
builder.Services.AddCors(options =>
{
    options.AddPolicy("PlaymoxCors", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
            return;
        }

        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opt =>
{
    opt.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Playmox API",
        Version = "v1",
        Description = "API for Playmox"
    });

    // Include 'SecurityScheme' to use JWT Authentication
    var jwtSecurityScheme = new OpenApiSecurityScheme
    {
        BearerFormat = "JWT",
        Name = "JWT Authentication",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        Description = "Put **_ONLY_** your JWT Bearer token on textbox below!",

        Reference = new OpenApiReference
        {
            Id = JwtBearerDefaults.AuthenticationScheme,
            Type = ReferenceType.SecurityScheme
        }
    };

    opt.AddSecurityDefinition(jwtSecurityScheme.Reference.Id, jwtSecurityScheme);

    opt.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { jwtSecurityScheme, Array.Empty<string>() }
    });
});
builder.Services.AddDbContext<PlaymoxContext>();
builder.Services.AddTransient<ITokenService, TokenService>();
builder.Services.AddTransient<AdminProxmoxDashboardService>();
builder.Services.AddTransient<ProxmoxAutomationClient>();
builder.Services.AddTransient<IGameServerConnector, BlankVmGameServerConnector>();
builder.Services.AddTransient<IGameServerConnector, MinecraftJavaGameServerConnector>();
builder.Services.AddTransient<IGameServerConnector, TerrariaGameServerConnector>();
builder.Services.AddTransient<IGameServerConnector, UnturnedGameServerConnector>();
builder.Services.AddTransient<IGameServerConnector, FactorioGameServerConnector>();
builder.Services.AddSingleton<GameServerConnectorRegistry>();
builder.Services.AddTransient<InstanceLifecycleService>();
builder.Services.AddSingleton<SshWebSocketBridgeService>();
builder.Services.AddTransient<ProvisioningJobProcessor>();
builder.Services.AddHostedService<ProvisioningWorkerService>();
builder.Services.AddHostedService<InstanceStateSyncService>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddAuthentication(opt =>
    {
        opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.IncludeErrorDetails = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,

            ValidateIssuerSigningKey = true,
            ValidIssuer = "playmoxAPI",
            ValidAudience = "playmoxAPI",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Config.Secret)),
        };

        options.SaveToken = true;
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"].FirstOrDefault();
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrWhiteSpace(accessToken) &&
                    (path.StartsWithSegments("/api/ws", StringComparison.OrdinalIgnoreCase) ||
                     path.Value?.Contains("/game/backup", StringComparison.OrdinalIgnoreCase) == true))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(ms => ms.Value != null && ms.Value.Errors.Count > 0)
            .SelectMany(kvp => kvp.Value.Errors.Select(e => e.ErrorMessage))
            .ToList();

        // Combine all error messages into a single semicolon-separated string
        var message = string.Join("; ", errors);

        var apiError = new ApiError
        {
            Status = 400,
            Error = "Bad Request",
            Message = message,
            Path = context.HttpContext.Request.Path
        };

        var json = JsonSerializer.Serialize(apiError);

        return new ContentResult
        {
            Content = json,
            ContentType = "application/json",
            StatusCode = 400
        };
    };
});

Config.Mapper = AutoMapperConfiguration.DefaultConfiguration().CreateMapper();
Config.Init();

var app = builder.Build();

Config.UserHelper = new UserHelper(
    httpContextAccessor: app.Services.GetRequiredService<IHttpContextAccessor>(),
    tokenService: app.Services.GetRequiredService<ITokenService>());

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PlaymoxContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PlaymoxDatabaseMigration");
    var pendingMigrations = dbContext.Database.GetPendingMigrations().ToList();

    if (pendingMigrations.Count == 0)
    {
        logger.LogInformation("Database migrations are up to date. No pending migrations.");
    }
    else
    {
        logger.LogInformation(
            "Starting database migration. Pending migrations: {Count}. List: {Migrations}",
            pendingMigrations.Count,
            string.Join(", ", pendingMigrations));

        dbContext.Database.Migrate();

        logger.LogInformation("Database migration finished successfully. Applied migrations: {Count}.", pendingMigrations.Count);
    }

    BootstrapDataSeeder.SeedInitialData(dbContext, builder.Configuration, logger);
}

// Swagger
{
    if (!app.Environment.IsDevelopment())
    {
        app.UseSwaggerAuthorized();
    }
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("PlaymoxCors");
app.UseRateLimiter();
app.UseStatusCodePages(async statusContext =>
{
    var response = statusContext.HttpContext.Response;
    if (response.StatusCode < 400 || response.ContentLength.GetValueOrDefault() > 0 || response.HasStarted)
    {
        return;
    }

    var apiError = new ApiError
    {
        Status = response.StatusCode,
        Error = ReasonPhrases.GetReasonPhrase(response.StatusCode),
        Message = ReasonPhrases.GetReasonPhrase(response.StatusCode),
        Path = statusContext.HttpContext.Request.Path
    };

    response.ContentType = "application/json";
    var json = JsonSerializer.Serialize(apiError);
    await response.WriteAsync(json);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets();
app.UseMiddleware<PrettyErrorMiddleware>();

app.MapControllers();
app.MapGet("/api/ws/instances/{id:long}/ssh", async (HttpContext context, long id, InstanceLifecycleService lifecycleService, SshWebSocketBridgeService sshBridge) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        return Results.BadRequest("WebSocket request expected.");
    }

    var sid = context.User.Claims.SingleOrDefault(x => x.Type == ClaimTypes.Sid)?.Value;
    if (!long.TryParse(sid, out var userId))
    {
        return Results.Unauthorized();
    }

    var isAdmin = context.User.Claims.Any(x => x.Type == ClaimTypes.Role &&
                                               string.Equals(x.Value, "Admin", StringComparison.OrdinalIgnoreCase));
    SshSessionAccess access;
    try
    {
        access = await lifecycleService.GetSshSessionAccessAsync(userId, isAdmin, id, context.RequestAborted);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ex.Message);
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await sshBridge.ProxyAsync(socket, access, context.RequestAborted);
    return Results.Empty;
}).RequireAuthorization();

app.Run();
