using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using SIPS.Adapter;
using SIPS.Adapter.Models;
using SIPS.ISO20022.Models.DTOs;
using Xunit;
using System.Text.Json;

namespace SIPS.Core.Tests.Tests;

public class VerificationGatewayContractTests
{
    private readonly string _basePath;

    public VerificationGatewayContractTests()
    {
        // Try to locate the root of the repo starting from the test assembly location
        _basePath = FindRepoRoot(AppContext.BaseDirectory);
    }

    private string FindRepoRoot(string path)
    {
        var current = new DirectoryInfo(path);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "SIPS.sln")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new Exception("Could not find repo root with SIPS.sln");
    }

    [Theory]
    [InlineData("jsonAdapter.json")]
    [InlineData("jsonAdapter.node-a.json")]
    [InlineData("jsonAdapter.node-b.json")]
    public void VerifyPayee_JsonResponse_Contract_IsConsistent(string adapterFile)
    {
        // Arrange
        var adapterPath = Path.Combine(_basePath, adapterFile);
        var jsonContent = File.ReadAllText(adapterPath);
        var options = JsonSerializer.Deserialize<JsonAdapterOptions>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var adapter = new JsonAdapter(options, NullLogger<JsonAdapter>.Instance);

        var dto = new VerificationResponseDto
        {
            IsVerified = true,
            SIPSRequestId = "MSG_ID_123", // Now re-anchored to MsgId
            Reason = "SUCC",
            AccountNo = "401005007403",
            AccountType = "ACCT",
            Name = "John Doe",
            Address = "Washington, DC",
            Currency = "USD"
        };

        // Act
        var transformed = adapter.Transform(dto, "VerificationResponse");

        // Assert
        // requestId should map from SIPSRequestId
        Assert.Equal("MSG_ID_123", transformed["requestId"]?.GetValue<string>());
        
        // address should map from Address
        Assert.Equal("Washington, DC", transformed["address"]?.GetValue<string>());
        
        // isVerified should map from IsVerified
        Assert.True(transformed["isVerified"]?.GetValue<bool>());
        
        // message should map from Reason
        Assert.Equal("SUCC", transformed["message"]?.GetValue<string>());
        
        // pam should map from AccountNo
        Assert.Equal("401005007403", transformed["pam"]?.GetValue<string>());
        
        // type should map from AccountType
        Assert.Equal("ACCT", transformed["type"]?.GetValue<string>());

        // customer_name should map from Name
        Assert.Equal("John Doe", transformed["customer_name"]?.GetValue<string>());

        // account_currency should map from Currency
        Assert.Equal("USD", transformed["account_currency"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("jsonAdapter.json")]
    [InlineData("jsonAdapter.node-a.json")]
    [InlineData("jsonAdapter.node-b.json")]
    public void CB_VerificationResponse_Accepts_P2G_Creditor_Response_Shape(string adapterFile)
    {
        var adapterPath = Path.Combine(_basePath, adapterFile);
        var jsonContent = File.ReadAllText(adapterPath);
        var options = JsonSerializer.Deserialize<JsonAdapterOptions>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var adapter = new JsonAdapter(options, NullLogger<JsonAdapter>.Instance);

        var p2gResponse = new JsonObject
        {
            ["isVerified"] = true,
            ["status"] = "SUCC",
            ["message"] = "Verified",
            ["creditorAccount"] = "401005007403",
            ["creditorAccountType"] = "ACCT",
            ["creditorName"] = "Revenue Account",
            ["currency"] = "USD"
        };

        var transformed = adapter.Transform(p2gResponse, "CB_VerificationResponse");

        Assert.True(transformed["IsVerified"]?.GetValue<bool>());
        Assert.Equal("SUCC", transformed["Status"]?.GetValue<string>());
        Assert.Equal("Verified", transformed["Message"]?.GetValue<string>());
        Assert.Equal("401005007403", transformed["AccountNo"]?.GetValue<string>());
        Assert.Equal("ACCT", transformed["AccountType"]?.GetValue<string>());
        Assert.Equal("Revenue Account", transformed["Name"]?.GetValue<string>());
        Assert.Equal("USD", transformed["Currency"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("jsonAdapter.json")]
    [InlineData("jsonAdapter.node-a.json")]
    [InlineData("jsonAdapter.node-b.json")]
    public void VerifyPayee_JsonResponse_Includes_Normalized_Bill_Details(string adapterFile)
    {
        var adapterPath = Path.Combine(_basePath, adapterFile);
        var jsonContent = File.ReadAllText(adapterPath);
        var options = JsonSerializer.Deserialize<JsonAdapterOptions>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var adapter = new JsonAdapter(options, NullLogger<JsonAdapter>.Instance);

        var dto = new VerificationResponseDto
        {
            IsVerified = true,
            SIPSRequestId = "MSG_ID_123",
            Reason = "SUCC",
            AccountNo = "401005007403",
            AccountType = "ACCT",
            Name = "Treasury MDA",
            Currency = "USD",
            InvoiceId = "INV-123",
            Upr = "UPR-123",
            BillReference = "UPR-123",
            Mda = "Ministry of Finance",
            MdaId = "MDA-001",
            MdaCode = "MOF",
            AmountPayable = 125.50m,
            CreditorAccount = "401005007403",
            CreditorName = "Treasury MDA",
            CreditorAccountType = "ACCT",
            AmountLocked = true,
            CreditorLocked = true
        };

        var transformed = adapter.Transform(dto, "VerificationResponse");

        Assert.Equal("UPR-123", transformed["upr"]?.GetValue<string>());
        Assert.Equal("UPR-123", transformed["billReference"]?.GetValue<string>());
        Assert.Equal("Ministry of Finance", transformed["mda"]?.GetValue<string>());
        Assert.Equal("MDA-001", transformed["mdaId"]?.GetValue<string>());
        Assert.Equal("MOF", transformed["mdaCode"]?.GetValue<string>());
        Assert.Equal(125.50d, transformed["amountPayable"]?.GetValue<double>());
        Assert.Equal("401005007403", transformed["creditorAccount"]?.GetValue<string>());
        Assert.Equal("Treasury MDA", transformed["creditorName"]?.GetValue<string>());
        Assert.Equal("ACCT", transformed["creditorAccountType"]?.GetValue<string>());
        Assert.True(transformed["amountLocked"]?.GetValue<bool>());
        Assert.True(transformed["creditorLocked"]?.GetValue<bool>());
    }
}
