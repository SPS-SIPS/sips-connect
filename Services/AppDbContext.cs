using Microsoft.EntityFrameworkCore;
using SIPS.Example.Consumer.Enums;
using SIPS.Example.Consumer.Models;
using SIPS.Example.Consumer.Models.DB;

namespace SIPS.Example.Consumer.Services;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts { get; set; } = null!;
    public DbSet<InterBankTransaction> InterBankTransactions { get; set; } = null!;
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<InterBankTransaction>().Property(e => e.SettlementMethod).HasConversion(x => x.Id, x => Enumeration<string>.FromId<SettlementMethods>(x));
        modelBuilder.Entity<InterBankTransaction>().Property(e => e.PaymentTypeInformation).HasConversion(x => x.Id, x => Enumeration<string>.FromId<LclInstrm>(x));
        modelBuilder.Entity<InterBankTransaction>().Property(e => e.PaymentTypeInformationCategoryPurpose).HasConversion(x => x.Id, x => Enumeration<string>.FromId<CategoryPurpose>(x));
        modelBuilder.Entity<InterBankTransaction>().Property(e => e.ChargeBearer).HasConversion(x => x.Id, x => Enumeration<string>.FromId<ChargeBearerType>(x));
        modelBuilder.Entity<InterBankTransaction>().Property(e => e.Status).HasConversion(x => x.Id, x => Enumeration<string>.FromId<TransactionStatus>(x));
    }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.EnableSensitiveDataLogging();
    }
}
