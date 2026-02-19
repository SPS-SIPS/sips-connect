using SIPS.PostgreSQL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SIPS.PostgreSQL.Persistence;
public sealed class StorageBroker(DbContextOptions<StorageBroker> options) : DbContext(options), IStorageBroker
{
    public DbSet<ISOMessage> ISOMessages { get; set; } = null!;
    public DbSet<Transaction> Transactions { get; set; } = null!;
    public DbSet<ISOMessageStatus> ISOMessageStatuses { get; set; } = null!;

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        return base.SaveChanges();
    }

    public Task<int> ExecuteSqlRawAsync(string query, CancellationToken cancellationToken)
    {
        return Database.ExecuteSqlRawAsync(query, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StorageBroker).Assembly);
    }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.EnableSensitiveDataLogging();
    }
    public bool IsRelational() => Database.IsRelational();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        return Database.BeginTransactionAsync(cancellationToken);
    }

    public void Detach<TEntity>(TEntity entity) where TEntity : class
    {
        Entry(entity).State = EntityState.Detached;
    }
}
