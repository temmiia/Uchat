using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;    
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using UChat.Application.Abstractions;
using UChat.Application.Auth;
using UChat.Application.Messages;
using UChat.Application.Users;
using UChat.Domain.Entities;
using UChat.Infrastructure.Data;
using UChat.Infrastructure.Repositories;
using UChat.Server.Hubs;
using UChat.Server.Services;

int? cliPort = null;
if (args.Length > 0)
{
    if (int.TryParse(args[0], out var p) && p is > 0 and <= 65535)
    {
        cliPort = p;
    }
    else if (args[0] is "--help" or "-h" or "-?" or "/?")
    {
        Console.WriteLine("Usage: uchat_server <port>");
        return 0;
    }
    else
    {
        Console.WriteLine("Error: Port must be an integer between 1 and 65535.");
        Console.WriteLine("Usage: uchat_server <port>");
        return 1;
    }
}
else
{
    var isDev = string.Equals(
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
        "Development",
        StringComparison.OrdinalIgnoreCase);

    if (!isDev)
    {
        Console.WriteLine("Usage: uchat_server <port>");
        return 1;
    }

    cliPort = 5049;
}

var builder = WebApplication.CreateBuilder(args);

if (cliPort.HasValue)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{cliPort.Value}");
}

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddSignalR();

builder.Services.AddSingleton<
    IUserIdProvider,
    AccountUserIdProvider>();
builder.Services.AddScoped<LoginUserService>();
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString =
        builder.Configuration.GetConnectionString("UChat");

    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseSqlite(connectionString);
    }
});

builder.Services.AddScoped<IUserRepository, EfUserRepository>();
builder.Services.AddScoped<RegisterUserService>();
builder.Services.AddScoped<SearchUsersService>();

builder.Services.AddScoped<IMessageRepository, EfMessageRepository>();
builder.Services.AddScoped<MessageService>();

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

const string DefaultDevelopmentSecret = "uchat_development_secret_key_at_least_32_bytes_long_12345";

var jwtKey = Environment.GetEnvironmentVariable("UCHAT_JWT_KEY")
    ?? Environment.GetEnvironmentVariable("UCHAT_JWT_SECRET")
    ?? builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(jwtKey))
{
    if (builder.Environment.IsDevelopment())
    {
        jwtKey = DefaultDevelopmentSecret;
    }
    else
    {
        throw new InvalidOperationException("In Production, UCHAT_JWT_KEY environment variable must be configured.");
    }
}

var trimmedJwtKey = jwtKey.Trim();

if (trimmedJwtKey.Length < 32 || Encoding.UTF8.GetByteCount(trimmedJwtKey) < 32)
{
    throw new InvalidOperationException(
        "JWT key must contain at least 32 non-whitespace characters.");
}

if (trimmedJwtKey.Distinct().Count() < 5)
{
    throw new InvalidOperationException(
        "JWT key has insufficient entropy (cannot be predictable or composed of repeated characters).");
}

if (!builder.Environment.IsDevelopment() && string.Equals(trimmedJwtKey, DefaultDevelopmentSecret, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Production environment cannot use the default development JWT key.");
}

var jwtIssuer = Environment.GetEnvironmentVariable("UCHAT_JWT_ISSUER")
    ?? builder.Configuration["Jwt:Issuer"];

if (string.IsNullOrWhiteSpace(jwtIssuer))
{
    throw new InvalidOperationException("JWT issuer must be specified and non-empty.");
}

var jwtAudience = Environment.GetEnvironmentVariable("UCHAT_JWT_AUDIENCE")
    ?? builder.Configuration["Jwt:Audience"];

if (string.IsNullOrWhiteSpace(jwtAudience))
{
    throw new InvalidOperationException("JWT audience must be specified and non-empty.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Read JWT from the query string only for SignalR connections.
                var accessToken = context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs/chat"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                context.Response.Headers.Append("Token-Validation-Error", "Authentication failed");
                return Task.CompletedTask;
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
            RoleClaimType = "role",
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(trimmedJwtKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<ChatEventPublisher>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth-login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

Console.WriteLine($"UChat Server PID: {Environment.ProcessId}");
if (cliPort.HasValue)
{
    Console.WriteLine($"Listening on port: {cliPort.Value}");
}

app.Logger.LogInformation(
    "Server started. Process ID: {ProcessId}, Port: {Port}",
    Environment.ProcessId,
    cliPort);

// Global Exception Handler: prevents leaking internal DB/stack trace details
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled server error on {Path}", context.Request.Path);
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "An internal server error occurred."
            });
        }
    }
});

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (builder.Configuration["EnableHttpsRedirection"] == "true")
{
    app.UseHttpsRedirection();
}

// Structured request logging without sensitive data (query strings masked, no passwords, no tokens)
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    // Log path only, never log query string that could contain access_token
    var path = context.Request.Path.Value ?? "";
    var startTimestamp = Stopwatch.GetTimestamp();

    await next();

    var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
    app.Logger.LogInformation(
        "HTTP {Method} {Path} returned {StatusCode} in {ElapsedMs:0.00} ms",
        method,
        path,
        context.Response.StatusCode,
        elapsedMs);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.UseRateLimiter();

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        Status = "Healthy",
        Service = "UChat.Server"
    });
});

// Initialize the database before accepting requests.
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseInitializer.InitializeAsync(context);
}

app.Run();
return 0;
