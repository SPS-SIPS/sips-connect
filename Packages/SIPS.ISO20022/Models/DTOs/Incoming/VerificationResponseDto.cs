using SIPS.ISO20022.Models;
namespace SIPS.ISO20022.Models.DTOs;

public sealed class VerificationResponseDto
{
    public bool IsVerified { get; set; }
    public string? Status { get; set; }
    public string? Message { get; set; }
    // Absent if IsVerified is true.
    public string Reason { get; set; } = string.Empty;
    public string SIPSRequestId { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("accountNo")]
    public string? AccountNo { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("accountType")]
    public string? AccountType { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("address")]
    public string? Address { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("currency")]
    public string? Currency { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("invoiceId")]
    public string? InvoiceId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("upr")]
    public string? Upr { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("billReference")]
    public string? BillReference { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("mda")]
    public string? Mda { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("mdaId")]
    public string? MdaId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("mdaCode")]
    public string? MdaCode { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("amountPayable")]
    public decimal? AmountPayable { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("creditorAccount")]
    public string? CreditorAccount { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("creditorName")]
    public string? CreditorName { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("creditorAccountType")]
    public string? CreditorAccountType { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("amountLocked")]
    public bool AmountLocked { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("creditorLocked")]
    public bool CreditorLocked { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("parsed")]
    public QrCodeData? Parsed { get; set; }
}
