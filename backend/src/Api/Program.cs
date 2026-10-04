using ComicPlatform.Api.Endpoints;
using ComicPlatform.Api.Infrastructure;
using ComicPlatform.Application;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Infrastructure;
using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Persistence;
using ComicPlatform.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Local development reads the repository-root .env; containers use real environment variables only.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddInMemoryCollection(DotEnv.LoadWithoutOverridingEnvironment(builder.Environment.ContentRootPath));
}

var settings = PlatformSettings.FromConfiguration(builder.Configuration);
var configProblems = settings.Validate(builder.Environment.IsProduction());
if (configProblems.Count > 0)
{
    throw new InvalidOperationException("Invalid configuration:" + Environment.NewLine + " - " + string.Join(Environment.NewLine + " - ", configProblems));
}

// ---------- Logging (never logs bodies, query strings, tokens or secrets) ----------
builder.Host.UseSerilog((context, _, logger) =>
{
    logger.MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("App", "comicplatform-api");

    if (context.HostingEnvironment.IsDevelopment())
    {
        logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
    }
    else
    {
        logger.WriteTo.Console(new RenderedCompactJsonFormatter());
    }

    if (!string.IsNullOrWhiteSpace(settings.Web.LogDirectory))
    {
        logger.WriteTo.File(new RenderedCompactJsonFormatter(), Path.Combine(settings.Web.LogDirectory, "api-.log"),
            rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, fileSizeLimitBytes: 100 * 1024 * 1024, rollOnFileSizeLimit: true);
    }
});

// ---------- Kestrel limits: small bodies by default; the upload endpoint raises its own limit ----------
builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = 1 * 1024 * 1024;
    o.Limits.MinRequestBodyDataRate = new MinDataRate(bytesPerSecond: 240, gracePeriod: TimeSpan.FromSeconds(10));
    o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
});

// ---------- Services ----------
var services = builder.Services;
services.AddInfrastructure(settings, backgroundServices: builder.Configuration["BACKGROUND_SERVICES_ENABLED"] != "false");
services.AddApplication();
services.AddHttpContextAccessor();
services.AddScoped<ICurrentUser, CurrentUser>();
services.AddProblemDetails();
services.AddExceptionHandler<ProblemExceptionHandler>();

services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = TokenService.CreateValidationParameters(settings.Jwt);
        o.Events = new JwtBearerEvents
        {
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                var expired = ctx.AuthenticateFailure is Microsoft.IdentityModel.Tokens.SecurityTokenExpiredException;
                await ProblemExceptionHandler.WriteAsync(ctx.HttpContext, 401, expired ? "token_expired" : "unauthorized",
                    expired ? "Phiên đăng nhập đã hết hạn." : "Vui lòng đăng nhập để tiếp tục.");
            },
            OnForbidden = ctx => ProblemExceptionHandler.WriteAsync(ctx.HttpContext, 403, "forbidden", "Bạn không có quyền thực hiện thao tác này."),
        };
    });
services.AddAuthorization();

services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (settings.Web.CorsAllowedOrigins.Length > 0)
    {
        p.WithOrigins(settings.Web.CorsAllowedOrigins)
            .AllowCredentials()
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Authorization", "Content-Type", "X-House-Id", "X-Requested-With")
            .WithExposedHeaders("Retry-After")
            .SetPreflightMaxAge(TimeSpan.FromHours(1));
    }
}));

services.AddPlatformRateLimiting(settings.Web);
services.AddRequestTimeouts(o =>
{
    o.DefaultPolicy = new RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(30) };
    o.AddPolicy("upload", TimeSpan.FromMinutes(10));
    o.AddPolicy("publish", settings.Processing.PublishUploadTimeout + TimeSpan.FromSeconds(30));
});
services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
if (builder.Environment.IsDevelopment())
{
    services.AddOpenApi();
}

var app = builder.Build();

// ---------- Pipeline ----------
// Request logging wraps the exception handler so it records the final (mapped) status code.
app.UseSerilogRequestLogging(o =>
{
    o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms";
    o.GetLevel = (ctx, _, ex) => ex is not null || ctx.Response.StatusCode >= 500
        ? LogEventLevel.Error
        : ctx.Request.Path.StartsWithSegments("/api/health") ? LogEventLevel.Verbose : LogEventLevel.Information;
});
app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseRequestTimeouts();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.MapGroup("/api");
api.MapHealthEndpoints();
api.MapAuthEndpoints();
api.MapPublicEndpoints();
api.MapLibraryEndpoints();
api.MapHouseEndpoints();
api.MapAdminEndpoints();
app.MapObjectEndpoints(settings.Storage);

await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);

await app.RunAsync();

public partial class Program;
