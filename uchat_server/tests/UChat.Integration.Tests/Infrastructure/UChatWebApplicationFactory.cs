using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UChat.Infrastructure.Data;

namespace UChat.Integration.Tests.Infrastructure;

public class UChatWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly string _environmentName;

    public UChatWebApplicationFactory(string environmentName = "Testing")
    {
        _environmentName = environmentName;
        // In-memory SQLite database connection must remain open to preserve tables
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "UChat.Server",
                ["Jwt:Audience"] = "UChat.Client",
                ["Jwt:Key"] = "uchat_development_secret_key_at_least_32_bytes_long_12345"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove existing AppDbContext and options
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();

            // Register in-memory SQLite DB
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(_sqliteConnection);
            });

            // Ensure database schema is created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            DatabaseInitializer.InitializeAsync(db).GetAwaiter().GetResult();
        });
    }

    public AppDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    public async Task<HubConnection> CreateHubConnectionAsync(string? accessToken = null)
    {
        var hubUri = new Uri(Server.BaseAddress, "hubs/chat");

        var builder = new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();

                if (!string.IsNullOrEmpty(accessToken))
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            });

        var connection = builder.Build();
        await connection.StartAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _sqliteConnection.Close();
            _sqliteConnection.Dispose();
        }
    }
}
