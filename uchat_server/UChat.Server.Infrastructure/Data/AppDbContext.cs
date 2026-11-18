using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UChat.Domain.Entities;
using UChat.Infrastructure.Configurations;

namespace UChat.Infrastructure.Data;

// Main EF Core database context for UChat
public class AppDbContext : DbContext
{
    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMember> Members => Set<ConversationMember>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbDir = Path.Combine(appDataDir, "UChat");
            Directory.CreateDirectory(dbDir);
            var dbPath = Path.Combine(dbDir, "uchat.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply entity configurations
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new ConversationConfiguration());
        modelBuilder.ApplyConfiguration(new ConversationMemberConfiguration());
        modelBuilder.ApplyConfiguration(new MessageConfiguration());

        // Value converter for DateTime to guarantee UTC DateTimeKind upon read from SQLite
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? v.Value.ToUniversalTime() : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(utcConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(nullableUtcConverter);
                }
            }
        }
    }

    public override int SaveChanges()
    {
        ValidateAccountIdImmutability();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ValidateAccountIdImmutability();
        return base.SaveChangesAsync(cancellationToken);
    }

    // 3rd layer of defense: interceptor ensuring AccountId can never be tampered with
    private void ValidateAccountIdImmutability()
    {
        var modifiedUsers = ChangeTracker.Entries<User>()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in modifiedUsers)
        {
            var accountIdProp = entry.Property(u => u.AccountId);
            if (accountIdProp.IsModified && !string.Equals(accountIdProp.OriginalValue, accountIdProp.CurrentValue, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Violation of domain invariant: AccountId is immutable and cannot be modified.");
            }
        }
    }
}
