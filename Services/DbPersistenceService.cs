using System.Text.Json;
using SIPS.Example.Consumer.Enums;
using SIPS.Example.Consumer.Models;
using SIPS.Example.Consumer.Models.DB;

namespace SIPS.Example.Consumer.Services;

public class DbPersistenceService(string filePath)
{
    private readonly string _filePath = filePath;

    public async Task SaveAsync(AppDbContext db)
    {
        var data = new PersistedData
        {
            InterBankTransactions = db.InterBankTransactions.Select(tx => new InterBankTransactionDto
            {
                Id = tx.Id,
                VerificationMessageId = tx.VerificationMessageId,
                BizMsgIdr = tx.BizMsgIdr,
                MsgDefIdr = tx.MsgDefIdr,
                CreDt = tx.CreDt,
                CreDtTm = tx.CreDtTm,
                NbOfTxs = tx.NbOfTxs,
                SettlementMethod = tx.SettlementMethod.Id,
                ClearingSystem = tx.ClearingSystem,
                PaymentTypeInformation = tx.PaymentTypeInformation.Id,
                PaymentTypeInformationCategoryPurpose = tx.PaymentTypeInformationCategoryPurpose.Id,
                InstructingAgent = tx.InstructingAgent,
                InstructedAgent = tx.InstructedAgent,
                EndToEndIdentification = tx.EndToEndIdentification,
                TransactionIdentification = tx.TransactionIdentification,
                UETR = tx.UETR,
                InterbankSettlementAmount = tx.InterbankSettlementAmount,
                InterbankSettlementCurrency = tx.InterbankSettlementCurrency,
                AcceptanceDateTime = tx.AcceptanceDateTime,
                InstructedAmount = tx.InstructedAmount,
                InstructedCurrency = tx.InstructedCurrency,
                Total = tx.Total,
                ChargeBearer = tx.ChargeBearer.Id,
                Debtor = tx.Debtor,
                DebtorPostalAddress = tx.DebtorPostalAddress,
                DebtorAccount = tx.DebtorAccount,
                DebtorAccountType = tx.DebtorAccountType,
                Creditor = tx.Creditor,
                CreditorAccount = tx.CreditorAccount,
                CreditorAccountType = tx.CreditorAccountType,
                InstructionForNextAgent = tx.InstructionForNextAgent,
                Purpose = tx.Purpose,
                RemittanceInformationStructuredType = tx.RemittanceInformationStructuredType,
                RemittanceInformationStructuredNumber = tx.RemittanceInformationStructuredNumber,
                RemittanceInformationUnstructured = tx.RemittanceInformationUnstructured,
                Status = tx.Status.Id
            }).ToList(),
            Accounts = db.Accounts.Select(acc => new AccountDto
            {
                Id = acc.Id,
                IBAN = acc.IBAN,
                CustomerName = acc.CustomerName,
                Address = acc.Address,
                Phone = acc.Phone,
                Currency = acc.Currency,
                Balance = acc.Balance,
                Active = acc.Active,
                WalletId = acc.WalletId
            }).ToList()
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(data, options);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public async Task<PersistedData?> LoadAsync()
    {
        if (!File.Exists(_filePath)) return null;
        var json = await File.ReadAllTextAsync(_filePath);
        var data = JsonSerializer.Deserialize<PersistedData>(json);
        if (data != null)
        {
            // Map InterBankTransactionDto to InterBankTransaction
            data.InterBankTransactionsMapped = data.InterBankTransactions
                .Select(dto => new InterBankTransaction
                {
                    Id = dto.Id,
                    VerificationMessageId = dto.VerificationMessageId,
                    BizMsgIdr = dto.BizMsgIdr,
                    MsgDefIdr = dto.MsgDefIdr,
                    CreDt = dto.CreDt,
                    CreDtTm = dto.CreDtTm,
                    NbOfTxs = dto.NbOfTxs,
                    SettlementMethod = Enumeration<string>.FromId<SettlementMethods>(dto.SettlementMethod),
                    ClearingSystem = dto.ClearingSystem,
                    PaymentTypeInformation = Enumeration<string>.FromId<LclInstrm>(dto.PaymentTypeInformation),
                    PaymentTypeInformationCategoryPurpose = Enumeration<string>.FromId<CategoryPurpose>(dto.PaymentTypeInformationCategoryPurpose),
                    InstructingAgent = dto.InstructingAgent,
                    InstructedAgent = dto.InstructedAgent,
                    EndToEndIdentification = dto.EndToEndIdentification,
                    TransactionIdentification = dto.TransactionIdentification,
                    UETR = dto.UETR,
                    InterbankSettlementAmount = dto.InterbankSettlementAmount,
                    InterbankSettlementCurrency = dto.InterbankSettlementCurrency,
                    AcceptanceDateTime = dto.AcceptanceDateTime,
                    InstructedAmount = dto.InstructedAmount,
                    InstructedCurrency = dto.InstructedCurrency,
                    Total = dto.Total,
                    ChargeBearer = Enumeration<string>.FromId<ChargeBearerType>(dto.ChargeBearer),
                    Debtor = dto.Debtor,
                    DebtorPostalAddress = dto.DebtorPostalAddress,
                    DebtorAccount = dto.DebtorAccount,
                    DebtorAccountType = dto.DebtorAccountType,
                    Creditor = dto.Creditor,
                    CreditorAccount = dto.CreditorAccount,
                    CreditorAccountType = dto.CreditorAccountType,
                    InstructionForNextAgent = dto.InstructionForNextAgent,
                    Purpose = dto.Purpose,
                    RemittanceInformationStructuredType = dto.RemittanceInformationStructuredType,
                    RemittanceInformationStructuredNumber = dto.RemittanceInformationStructuredNumber,
                    RemittanceInformationUnstructured = dto.RemittanceInformationUnstructured,
                    Status = Enumeration<string>.FromId<TransactionStatus>(dto.Status)
                })
                .ToList();

            // Map AccountDto to Account
            data.AccountsMapped = data.Accounts
                .Select(dto => new Account
                {
                    Id = dto.Id,
                    IBAN = dto.IBAN,
                    CustomerName = dto.CustomerName,
                    Address = dto.Address,
                    Phone = dto.Phone,
                    Currency = dto.Currency,
                    Balance = dto.Balance,
                    Active = dto.Active,
                    WalletId = dto.WalletId
                })
                .ToList();
        }
        return data;
    }

    public class PersistedData
    {
        public List<InterBankTransactionDto> InterBankTransactions { get; set; } = new();
        public List<AccountDto> Accounts { get; set; } = new();
        // This property is not serialized, only used at runtime after mapping
        [System.Text.Json.Serialization.JsonIgnore]
        public List<InterBankTransaction> InterBankTransactionsMapped { get; set; } = new();
        // This property is not serialized, only used at runtime after mapping
        [System.Text.Json.Serialization.JsonIgnore]
        public List<Account> AccountsMapped { get; set; } = new();
    }
}
