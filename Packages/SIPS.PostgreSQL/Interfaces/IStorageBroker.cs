using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace SIPS.PostgreSQL.Interfaces;

public interface IStorageBroker
{
    DbSet<Transaction> Transactions { get; set; }
    DbSet<ISOMessage> ISOMessages { get; set; }
    DbSet<ISOMessageStatus> ISOMessageStatuses { get; set; }

    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    int SaveChanges();
    Task<int> ExecuteSqlRawAsync(string query, CancellationToken cancellationToken);
    bool IsRelational();
    void Dispose();

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
}
