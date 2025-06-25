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
    public IActionResult Get()
    {
        var accounts = _broker.Accounts
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
        .AsNoTracking().ToList();

        return Ok(accounts);
    }
    [HttpPost("Verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyRequest request, CancellationToken ct)
    {
        if (ModelState.IsValid == false)
        {
            return BadRequest("Invalid request");
        }
        var response = new VerifyResponse();
        try
        {
            if (request.AccountNo.StartsWith("USD:"))
            {
                request.AccountNo = request.AccountNo.Substring(4);
            }

            if (request.AccountNo.StartsWith("SO"))
            {
                request.AccountType = "IBAN";
            }

            var query = _broker.Accounts.AsNoTracking().AsQueryable();
            if (request.AccountType == "IBAN")
            {
                query = query.Where(a => a.IBAN == request.AccountNo);
            }
            else if (request.AccountType == "MSIS")
            {
                query = query.Where(a => a.Phone!.Contains(request.AccountNo));
            }
            else if (request.AccountType == "EWLT")
            {
                query = query.Where(a => a.WalletId == request.AccountNo);
            }
            else if (request.AccountType == "ACCT")
            {
                query = query.Where(a => a.Id == long.Parse(request.AccountNo));
            }
            else
            {
                return BadRequest("Invalid account type");
            }


            var account = await query.FirstOrDefaultAsync(ct);

            if (account == null)
            {
                response.Reason = "MISS";
                response.Message = "Account not found";
                response.IsVerified = false;
                return NotFound(response);
            }
            response.AccountNo = account.IBAN!;
            response.AccountType = "IBAN";
            response.Currency = account.Currency!;
            response.Name = account.CustomerName!;
            response.Address = account.Address!;
            response.IsVerified = true;
            response.Message = account.Id.ToString();
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying account");
            response.Message = "Error verifying account";
            return Ok(response);
        }
    }

    [HttpPost("Status")]
    public async Task<IActionResult> Status([FromBody] StatusRequest request, CancellationToken ct)
    {
        var response = new StatusResponse();
        try
        {
            var query = _broker.InterBankTransactions.AsNoTracking()
                .Where(t => t.TransactionIdentification == request.TxId);


            var txn = await query.FirstOrDefaultAsync(ct);

            if (txn == null)
            {
                response.Status = "RJCT";
                response.Reason = "MISS";
                response.AdditionalInfo = "Creditor account not found";
                return NotFound(response);
            }
            // return the response
            response.Status = txn.Status.Id;
            response.AcceptanceDate = txn.AcceptanceDateTime.Date;
            response.TxId = request.TxId;
            response.EndToEndId = request.EndToEndId;
            response.Amount = txn.InterbankSettlementAmount;
            response.Currency = txn.InstructedCurrency;
            response.DebtorName = txn.Debtor;
            response.DebtorPostalAddress = txn.DebtorPostalAddress;
            response.DebtorAccount = txn.DebtorAccount;
            response.DebtorAccountType = txn.DebtorAccountType;
            response.DebtorAgentBIC = txn.InstructedAgent;

            response.CreditorName = txn.Creditor;
            response.CreditorPostalAddress = "";
            response.CreditorAccount = txn.CreditorAccount;
            response.CreditorAccountType = txn.CreditorAccountType;
            response.CreditorAgentBIC = txn.InstructingAgent;

            response.RemittanceInformation = txn.Purpose;
            response.Status = txn.Status.Id;
            response.FromBIC = txn.InstructingAgent;
            response.ToBIC = txn.InstructedAgent;
            response.LocalInstrument = txn.PaymentTypeInformation.Id;
            response.CategoryPurpose = txn.PaymentTypeInformationCategoryPurpose.Id;
            response.SettlementMethod = txn.SettlementMethod.Id;
            response.ChargeBearer = txn.ChargeBearer.Id;
            response.Date = txn.CreDt.Date;
            response.BizMsgIdr = txn.BizMsgIdr;
            response.MsgDefIdr = txn.MsgDefIdr;
            response.ClearingSystem = txn.ClearingSystem;
            response.MsgId = txn.TransactionIdentification;

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying account");
            return Ok(response);
        }
    }

    [HttpPost("Transfer")]
    public async Task<IActionResult> Transfer([FromBody] TransferRequest request, CancellationToken ct)
    {
        var response = new TransferResponse
        {
            TxId = request.TxId,
            EndToEndId = request.EndToEndId,
            AcceptanceDate = DateTime.UtcNow
        };
        try
        {
            var query = _broker.Accounts.AsQueryable();

            if (request.CreditorAccountType == "IBAN")
            {
                query = query.Where(a => a.IBAN == request.CreditorAccount);
            }
            else if (request.CreditorAccountType.Equals("phone", StringComparison.CurrentCultureIgnoreCase))
            {
                query = query.Where(a => a.Phone!.Contains(request.CreditorAccount));
            }
            else if (request.CreditorAccountType.Equals("wallet", StringComparison.CurrentCultureIgnoreCase))
            {
                query = query.Where(a => a.WalletId!.Contains(request.CreditorAccount));
            }
            else
            {
                var id = long.TryParse(request.CreditorAccount, out long accountId) ? accountId : 0;
                query = query.Where(a => a.Id == id);
            }

            var toAccount = await query.FirstOrDefaultAsync(ct);

            if (toAccount == null)
            {
                response.Status = "RJCT";
                response.Reason = "MISS";
                response.AdditionalInfo = "Creditor account not found";
                return NotFound(response);
            }

            // check if the creditor account is active and has enough balance
            if (!toAccount.Active)
            {
                response.Status = "RJCT";
                response.Reason = "AC01";
                response.AdditionalInfo = "Creditor account is not active";
                return Ok(response);
            }

            if (toAccount.Balance < request.Amount)
            {
                response.Status = "RJCT";
                response.Reason = "AC02";
                response.AdditionalInfo = "Insufficient funds";
                return Ok(response);
            }

            // update the creditor account balance
            toAccount.Balance -= request.Amount;

            // return the response
            response.Status = "ACSC";
            response.AcceptanceDate = DateTime.UtcNow;
            response.TxId = request.TxId;
            response.EndToEndId = request.EndToEndId;

            // create the transaction
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
                InstructionForNextAgent = "",
                Purpose = request.RemittanceInformation,
                RemittanceInformationStructuredType = "",
                RemittanceInformationStructuredNumber = "",
                RemittanceInformationUnstructured = request.RemittanceInformation,
                Status = TransactionStatus.AcceptedSettlementCompleted
            };

            // save the transaction
            await _broker.InterBankTransactions.AddAsync(txn, ct);
            await _broker.SaveChangesAsync(ct);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transferring funds");
            response.Status = "RJCT";
            response.Reason = "ERRR";
            response.AdditionalInfo = "Error receiving funds";
            return BadRequest(response);
        }
    }

    [HttpPost("Return")]
    public async Task<IActionResult> Return([FromBody] ReturnRequest request, CancellationToken ct)
    {
        Console.WriteLine("Return called");
        var response = new ReturnResponse
        {
            TxId = request.TxId,
            EndToEndId = request.EndToEndId,
            ReturnId = request.ReturnId,
            Agent = request.Agent,
        };

        try
        {
            var query = _broker.InterBankTransactions.AsNoTracking()
                .Where(t => t.TransactionIdentification == request.TxId && t.EndToEndIdentification == request.EndToEndId);

            var txn = await query.FirstOrDefaultAsync(ct);

            if (txn == null)
            {
                response.Status = "RJCT";
                response.Reason = "MISS";
                response.AdditionalInfo = "Creditor account not found";
                return NotFound(response);
            }

            var fromAccount = await query.FirstOrDefaultAsync(ct);

            if (fromAccount == null)
            {
                response.Status = "RJCT";
                response.Reason = "MISS";
                response.AdditionalInfo = "Debtor account not found";
                return NotFound(response);
            }

            // return the response
            response.Status = "ACSC";
            response.TxId = request.TxId;

            // save the transaction
            await _broker.SaveChangesAsync(ct);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transferring funds");
            response.Status = "RJCT";
            response.Reason = "ERRR";
            response.AdditionalInfo = "Error receiving funds";
            return BadRequest(response);
        }
    }

}
