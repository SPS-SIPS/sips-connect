using Microsoft.EntityFrameworkCore;
using SIPS.Example.Consumer.Enums;
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

        modelBuilder.Entity<Account>().HasData(
            new Account { Id = 234567891220, IBAN = "SO440005501234567891220", CustomerName = "Chief Abdullahi Bihi", Address = "Mogadishu", Phone = "0293939393", Currency = "USD", Balance = 1000, Active = true, WalletId = "2345" },
            new Account { Id = 234567891210, IBAN = "SO440005501234567891210", CustomerName = "Eng. Abdullahi Ahmed", Address = "Mogadishu", Phone = "0293939432", Currency = "USD", Balance = 1000, Active = true, WalletId = "4536" },
            new Account { Id = 123456789120, IBAN = "SO440005501123456789120", CustomerName = "ENG. ABDIKARIM OSMAN", Address = "Hargeisa", Phone = "0293939444", Currency = "USD", Balance = 1000, Active = true, WalletId = "4531" },
            new Account { Id = 123456789320, IBAN = "SO440005501123456789320", CustomerName = "Sahra Ali Mohamed", Address = "Hargeisa", Phone = "0293939827", Currency = "USD", Balance = 1000, Active = true, WalletId = "3456" }
        );
    }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.EnableSensitiveDataLogging();
    }
}