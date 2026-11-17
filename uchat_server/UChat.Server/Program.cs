using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using UChat.Application.Abstractions;
using UChat.Application.Auth;
using UChat.Domain.Entities;
using UChat.Infrastructure.Data;
using UChat.Infrastructure.Repositories;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using UChat.Server.Services;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using UChat.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddScoped<LoginUserService>();
builder.Services.AddDbContext<AppDbContext>();

builder.Services.AddScoped<IUserRepository, EfUserRepository>();
builder.Services.AddScoped<RegisterUserService>();

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();


var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT key is not configured.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("JWT issuer is not configured.");

var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("JWT audience is not configured.");

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "JWT key must contain at least 32 bytes.");
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
    }
};
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = JwtRegisteredClaimNames.Sub,
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)),
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
