using Microsoft.EntityFrameworkCore;
using SIPS.Example.Consumer.Services;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("MyDatabase"));

// Get DB persistence file path from configuration
var dbFilePath = builder.Configuration["DbPersistenceFile"] ?? Path.Combine(AppContext.BaseDirectory, "db.json");
var dbPersistence = new SIPS.Example.Consumer.Services.DbPersistenceService(dbFilePath);

var app = builder.Build();


// Ensure the database is created and load data from JSON if exists
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Load from JSON file if exists
    var persistedData = dbPersistence.LoadAsync().GetAwaiter().GetResult();
    if (persistedData != null)
    {
        // Restore accounts from persisted data (the only source)
        if (db.Accounts.Any()) db.Accounts.RemoveRange(db.Accounts);
        if (persistedData.AccountsMapped.Any())
        {
            db.Accounts.AddRange(persistedData.AccountsMapped);
        }
        db.SaveChanges();
        // Restore InterBankTransactions
        if (db.InterBankTransactions.Any()) db.InterBankTransactions.RemoveRange(db.InterBankTransactions);
        db.SaveChanges();
        var validInterBankTransactions = persistedData.InterBankTransactionsMapped.Where(tx =>
            tx.ChargeBearer != null &&
            tx.PaymentTypeInformation != null &&
            tx.PaymentTypeInformationCategoryPurpose != null &&
            tx.SettlementMethod != null &&
            tx.Status != null
        ).ToList();
        db.InterBankTransactions.AddRange(validInterBankTransactions);
        db.SaveChanges();
    }
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();



// On shutdown, save DB to JSON using ApplicationStopping
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbPersistence.SaveAsync(db).GetAwaiter().GetResult();
});

app.Run();