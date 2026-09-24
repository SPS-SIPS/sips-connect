using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace SIPS.PostgreSQL.Interfaces;

public interface IStorageBroker
{
    DbSet<Transaction> Transactions { get; set; }
    DbSet<ISOMessage> ISOMessages { get; set; }
    DbSet<ISOMessageStatus> ISOMessageStatuses { get; set; }
    DbSet<PapssOperation> PapssOperations { get; set; }
    DbSet<PapssOperationEvent> PapssOperationEvents { get; set; }
    DbSet<PapssOutboundResponse> PapssOutboundResponses { get; set; }
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    int SaveChanges();
    Task<int> ExecuteSqlRawAsync(string query, CancellationToken cancellationToken);
    bool IsRelational();
    void Dispose();

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
    void Detach<TEntity>(TEntity entity) where TEntity : class;
}
