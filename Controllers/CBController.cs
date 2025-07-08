using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SIPS.Example.Consumer.Enums;
using SIPS.Example.Consumer.Models;
using SIPS.Example.Consumer.Models.CB;
using SIPS.Example.Consumer.Models.DB;
using SIPS.Example.Consumer.Services;

namespace SIPS.Example.Consumer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CBController(ILogger<CBController> logger, AppDbContext broker) : ControllerBase
{
    private readonly ILogger<CBController> _logger = logger;
    private readonly AppDbContext _broker = broker;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        _logger.LogInformation("GET /api/CB called");
        return Ok(await _broker.Accounts
            .Select(a => new AccountDto
            {
                Id = a.Id,
                IBAN = a.IBAN,
                CustomerName = a.CustomerName,
                Address = a.Address,
                Phone = a.Phone,
                Currency = a.Currency,
                Balance = a.Balance,
                Active = a.Active,
                WalletId = a.WalletId
            })
            .AsNoTracking()
            .ToListAsync());
    }

    [HttpGet("Transactions")]
    public async Task<IActionResult> GetTransactions()
    {
        _logger.LogInformation("GET /api/CB/Transactions called");
        return Ok(await _broker.InterBankTransactions
            .Select(t => new
            {
                t.Id,
                t.TransactionIdentification,
                t.EndToEndIdentification,
                t.InstructedAgent,
                t.InstructingAgent,
                t.InstructedAmount,
                t.InstructedCurrency,
                t.InterbankSettlementAmount,
                t.InterbankSettlementCurrency,
                t.AcceptanceDateTime,
                t.CreDt,
                t.CreDtTm,
                t.Debtor,
                t.DebtorPostalAddress,
                t.DebtorAccount,
                t.DebtorAccountType,
                t.Creditor,
                t.CreditorAccount,
                t.CreditorAccountType,
                t.Purpose,
                Status = t.Status.Id,
                PaymentTypeInformation = t.PaymentTypeInformation.Id,
                PaymentTypeInformationCategoryPurpose = t.PaymentTypeInformationCategoryPurpose.Id,
                SettlementMethod = t.SettlementMethod.Id,
                ChargeBearer = t.ChargeBearer.Id,
                t.BizMsgIdr,
                t.MsgDefIdr,
                t.ClearingSystem,
                FromBIC = t.InstructingAgent,
                ToBIC = t.InstructedAgent,
                LocalInstrument = t.PaymentTypeInformation.Id,
                CategoryPurpose = t.PaymentTypeInformationCategoryPurpose.Id,
            })
            .AsNoTracking()
            .ToListAsync());
    }

    [HttpPost("Verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyRequest request, CancellationToken ct)
    {
        _logger.LogInformation("POST /api/CB/Verify called with body: {Request}", System.Text.Json.JsonSerializer.Serialize(request));
        if (!ModelState.IsValid)
            return BadRequest("Invalid request");

        try
        {
            var accountNo = request.AccountNo.StartsWith("USD:", StringComparison.OrdinalIgnoreCase) ? request.AccountNo[4..] : request.AccountNo;
            var accountType = accountNo.StartsWith("SO", StringComparison.OrdinalIgnoreCase) ? "IBAN" : request.AccountType;

            var query = _broker.Accounts.AsNoTracking().AsQueryable();
            query = accountType switch
            {
                "IBAN" => query.Where(a => a.IBAN == accountNo),
                "MSIS" => query.Where(a => a.Phone!.Contains(accountNo)),
                "EWLT" => query.Where(a => a.WalletId == accountNo),
                "ACCT" => query.Where(a => a.Id == long.Parse(accountNo)),
                _ => query
            };

            var account = await query.FirstOrDefaultAsync(ct);
            if (account is null)
                return NotFound(new VerifyResponse { Reason = "MISS", Message = "Account not found", IsVerified = false });

            return Ok(new VerifyResponse
            {
                AccountNo = account.IBAN!,
                AccountType = "IBAN",
                Currency = account.Currency!,
                Name = account.CustomerName!,
                Address = account.Address!,
                IsVerified = true,
                Message = account.Id.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying account");
            return Ok(new VerifyResponse { Message = "Error verifying account" });
        }
    }

    [HttpPost("Status")]
    public async Task<IActionResult> Status([FromBody] StatusRequest request, CancellationToken ct)
    {
        _logger.LogInformation("POST /api/CB/Status called with body: {Request}", System.Text.Json.JsonSerializer.Serialize(request));
        try
        {
            var txn = await _broker.InterBankTransactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.TransactionIdentification == request.TxId, ct);

            if (txn is null)
            {
                return NotFound(new StatusResponse
                {
                    Status = "RJCT",
                    Reason = "MISS",
                    AdditionalInfo = "Creditor account not found"
                });
            }

            return Ok(new StatusResponse
            {
                Status = txn.Status.Id,
                AcceptanceDate = txn.AcceptanceDateTime.Date,
                TxId = request.TxId,
                EndToEndId = request.EndToEndId,
                Amount = txn.InterbankSettlementAmount,
                Currency = txn.InstructedCurrency,
                DebtorName = txn.Debtor,
                DebtorPostalAddress = txn.DebtorPostalAddress,
                DebtorAccount = txn.DebtorAccount,
                DebtorAccountType = txn.DebtorAccountType,
                DebtorAgentBIC = txn.InstructedAgent,
                CreditorName = txn.Creditor,
                CreditorPostalAddress = string.Empty,
                CreditorAccount = txn.CreditorAccount,
                CreditorAccountType = txn.CreditorAccountType,
                CreditorAgentBIC = txn.InstructingAgent,
                RemittanceInformation = txn.Purpose,
                FromBIC = txn.InstructingAgent,
                ToBIC = txn.InstructedAgent,
                LocalInstrument = txn.PaymentTypeInformation.Id,
                CategoryPurpose = txn.PaymentTypeInformationCategoryPurpose.Id,
                SettlementMethod = txn.SettlementMethod.Id,
                ChargeBearer = txn.ChargeBearer.Id,
                Date = txn.CreDt.Date,
                BizMsgIdr = txn.BizMsgIdr,
                MsgDefIdr = txn.MsgDefIdr,
                ClearingSystem = txn.ClearingSystem,
                MsgId = txn.TransactionIdentification
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying account");
            return Ok(new StatusResponse { Status = "RJCT", Reason = "ERRR", AdditionalInfo = "Error verifying account" });
        }
    }

    [HttpPost("Transfer")]
    public async Task<IActionResult> Transfer([FromBody] TransferRequest request, CancellationToken ct)
    {
        _logger.LogInformation("POST /api/CB/Transfer called with body: {Request}", System.Text.Json.JsonSerializer.Serialize(request));
        var response = new TransferResponse
        {
            TxId = request.TxId,
            EndToEndId = request.EndToEndId,
            AcceptanceDate = DateTime.UtcNow
        };
        try
        {
            var query = _broker.Accounts.AsQueryable();
            var creditorAccount = request.CreditorAccount.StartsWith("USD:", StringComparison.OrdinalIgnoreCase) ? request.CreditorAccount[4..] : request.CreditorAccount;
            var creditorAccountType = creditorAccount.StartsWith("SO", StringComparison.OrdinalIgnoreCase) ? "IBAN" : request.CreditorAccountType;

            query = creditorAccountType switch
            {
                "IBAN" => query.Where(a => a.IBAN == creditorAccount),
                var t when t.Equals("phone", StringComparison.CurrentCultureIgnoreCase) => query.Where(a => a.Phone!.Contains(creditorAccount)),
                var t when t.Equals("wallet", StringComparison.CurrentCultureIgnoreCase) => query.Where(a => a.WalletId!.Contains(creditorAccount)),
                _ => long.TryParse(creditorAccount, out var id) ? query.Where(a => a.Id == id) : query.Where(a => false)
            };

            var toAccount = await query.FirstOrDefaultAsync(ct);
            if (toAccount is null)
                return NotFound(new TransferResponse { Status = "RJCT", Reason = "MISS", AdditionalInfo = "Creditor account not found" });

            if (!toAccount.Active)
                return Ok(new TransferResponse { Status = "RJCT", Reason = "AC01", AdditionalInfo = "Creditor account is not active" });

            if (toAccount.Balance < request.Amount)
                return Ok(new TransferResponse { Status = "RJCT", Reason = "AC02", AdditionalInfo = "Insufficient funds" });

            toAccount.Balance -= request.Amount;
            response.Status = "ACSC";
            response.AcceptanceDate = DateTime.UtcNow;

            var settlementMethod = request.SettlementMethod!.ToUpper();
            var localInstrument = request.LocalInstrument!.ToUpper();
            var categoryPurpose = request.CategoryPurpose!.ToUpper();
            var chargeBearer = request.ChargeBearer!.ToUpper();

            var txn = new InterBankTransaction
            {
                VerificationMessageId = request.TxId,
                BizMsgIdr = "pacs.008.001.10",
                MsgDefIdr = "pacs.008.001.10",
                CreDt = request.Date.ToUniversalTime(),
                CreDtTm = request.Date.ToUniversalTime(),
                NbOfTxs = 1,
                SettlementMethod = Enumeration<string>.FromId<SettlementMethods>(settlementMethod),
                ClearingSystem = settlementMethod,
                PaymentTypeInformation = Enumeration<string>.FromId<LclInstrm>(localInstrument),
                PaymentTypeInformationCategoryPurpose = Enumeration<string>.FromId<CategoryPurpose>(categoryPurpose),
                InstructingAgent = request.FromBIC,
                InstructedAgent = request.ToBIC,
                EndToEndIdentification = request.EndToEndId,
                TransactionIdentification = request.TxId,
                InterbankSettlementAmount = request.Amount,
                InterbankSettlementCurrency = request.Currency,
                AcceptanceDateTime = response.AcceptanceDate,
                InstructedAmount = request.Amount,
                InstructedCurrency = request.Currency,
                ChargeBearer = Enumeration<string>.FromId<ChargeBearerType>(chargeBearer),
                Debtor = request.DebtorName!,
                DebtorPostalAddress = request.DebtorAddress,
                DebtorAccount = request.DebtorAccount,
                DebtorAccountType = request.DebtorAccountType,
                Creditor = request.CreditorName,
                CreditorAccount = request.CreditorAccount,
                CreditorAccountType = request.CreditorAccountType,
                InstructionForNextAgent = string.Empty,
                Purpose = request.RemittanceInformation,
                RemittanceInformationStructuredType = string.Empty,
                RemittanceInformationStructuredNumber = string.Empty,
                RemittanceInformationUnstructured = request.RemittanceInformation,
                Status = TransactionStatus.AcceptedSettlementCompleted
            };

            await _broker.InterBankTransactions.AddAsync(txn, ct);
            await _broker.SaveChangesAsync(ct);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transferring funds");
            return BadRequest(new TransferResponse { Status = "RJCT", Reason = "ERRR", AdditionalInfo = "Error receiving funds" });
        }
    }

    [HttpPost("Return")]
    public async Task<IActionResult> Return([FromBody] ReturnRequest request, CancellationToken ct)
    {
        _logger.LogInformation("POST /api/CB/Return called with body: {Request}", System.Text.Json.JsonSerializer.Serialize(request));
        try
        {
            var txn = await _broker.InterBankTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.TransactionIdentification == request.TxId, ct);

            // As we have issue in our multi broker setup (network level), we will always return the transaction as found
            // In a real-world scenario, you would check if the transaction exists and is eligible for return
            // For now, we assume the transaction exists and is eligible for return
            // return the response
            return Ok(new ReturnResponse
            {
                TxId = request.TxId,
                EndToEndId = request.EndToEndId,
                ReturnId = request.ReturnId,
                Agent = request.Agent,
                Status = "ACSC"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transferring funds");
            return BadRequest(new ReturnResponse { Status = "RJCT", Reason = "ERRR", AdditionalInfo = "Error receiving funds" });
        }
    }
}
