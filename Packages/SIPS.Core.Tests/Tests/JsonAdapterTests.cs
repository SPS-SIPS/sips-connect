using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SIPS.Adapter;
using SIPS.Adapter.Models;
using SIPS.ISO20022.Models.DTOs;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class JsonAdapterTests
{
    private readonly string _basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    private static JsonAdapter CreateAdapter(JsonAdapterOptions opts)
    {
        var logger = Mock.Of<ILogger<JsonAdapter>>();
        return new JsonAdapter(opts, logger);
    }

    [Fact]
    public void Transform_Maps_DotPath_With_StringType()
    {
        // Arrange
        var user = new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["amount"] = 123,
                ["ref"] = "ABC"
            }
        };

        var opts = new JsonAdapterOptions
        {
            Endpoints =
            {
                ["dotpath"] = new EndpointMapping
                {
                    FieldMappings =
                    {
                        new FieldMapping { InternalField = "amount", UserField = "data.amount", Type = "int" },
                        new FieldMapping { InternalField = "reference", UserField = "data.ref", Type = "string" },
                    }
                }
            }
        };

        var adapter = CreateAdapter(opts);

        // Act
        var mapped = adapter.Transform(user, "dotpath");

        // Assert
        mapped["amount"].Should().NotBeNull();
        mapped["amount"]!.GetValue<int>().Should().Be(123);
        mapped["reference"]!.GetValue<string>().Should().Be("ABC");
    }

    [Fact]
    public void Transform_Maps_ArrayIndex_With_EnumType()
    {
        // Arrange
        var user = new JsonObject
        {
            ["items"] = new JsonArray
            {
                new JsonObject { ["name"] = "first", ["value"] = 42 },
                new JsonObject { ["name"] = "second", ["value"] = 7 }
            }
        };

        var opts = new JsonAdapterOptions
        {
            Endpoints =
            {
                ["arraypath"] = new EndpointMapping
                {
                    FieldMappings =
                    {
                        new FieldMapping { InternalField = "firstName", UserField = "items[0].name", EnumType = MappingType.String },
                        new FieldMapping { InternalField = "secondValue", UserField = "items[1].value", EnumType = MappingType.Int },
                    }
                }
            }
        };

        var adapter = CreateAdapter(opts);

        // Act
        var mapped = adapter.Transform(user, "arraypath");

        // Assert
        mapped["firstName"]!.GetValue<string>().Should().Be("first");
        mapped["secondValue"]!.GetValue<int>().Should().Be(7);
    }

    [Fact]
    public void Transform_Converts_DateTime_To_ISO_String()
    {
        // Arrange
        var dt = new DateTime(2025, 01, 02, 03, 04, 05, DateTimeKind.Utc);
        var user = new JsonObject
        {
            ["meta"] = new JsonObject { ["createdAt"] = dt.ToString("o") }
        };

        var opts = new JsonAdapterOptions
        {
            Endpoints =
            {
                ["datetime"] = new EndpointMapping
                {
                    FieldMappings =
                    {
                        new FieldMapping { InternalField = "created", UserField = "meta.createdAt", Type = "datetime" },
                    }
                }
            }
        };
        var adapter = CreateAdapter(opts);

        // Act
        var mapped = adapter.Transform(user, "datetime");

        // Assert: normalized to explicit UTC millisecond format
        mapped["created"]!.GetValue<string>().Should().Be("2025-01-02T03:04:05.000Z");
    }

    [Fact]
    public void Transform_Object_Omits_Null_And_Empty_Fields_When_Configured()
    {
        var opts = new JsonAdapterOptions
        {
            Endpoints =
            {
                ["optional"] = new EndpointMapping
                {
                    FieldMappings =
                    {
                        new FieldMapping { InternalField = "InvoiceIdOrUpr", UserField = "invoiceIdOrUpr", Type = "string", OmitIfNull = true, OmitIfEmpty = true },
                        new FieldMapping { InternalField = "AccountNo", UserField = "accountNo", Type = "string", OmitIfNull = true, OmitIfEmpty = true },
                        new FieldMapping { InternalField = "Agent", UserField = "agent", Type = "string" },
                    }
                }
            }
        };
        var adapter = CreateAdapter(opts);

        var mapped = adapter.Transform(
            new { InvoiceIdOrUpr = "INV-123", AccountNo = (string?)null, Agent = "BANK01" },
            "optional");

        mapped["invoiceIdOrUpr"]!.GetValue<string>().Should().Be("INV-123");
        mapped["agent"]!.GetValue<string>().Should().Be("BANK01");
        mapped.ContainsKey("accountNo").Should().BeFalse();
    }

    [Theory]
    [InlineData("jsonAdapter.json")]
    [InlineData("jsonAdapter.node-a.json")]
    [InlineData("jsonAdapter.node-b.json")]
    public void Transform_CBVerificationResponse_Keeps_Descriptor_Out_Of_CreditorName(string adapterFile)
    {
        var adapterPath = Path.Combine(_basePath, adapterFile);
        var jsonContent = File.ReadAllText(adapterPath);
        var options = JsonSerializer.Deserialize<JsonAdapterOptions>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var adapter = CreateAdapter(options);
        var user = new JsonObject
        {
            ["isVerified"] = true,
            ["accountNo"] = "401005007403",
            ["accountType"] = "ACCT",
            ["name"] = "P2G1|IMO-IRS-00|PAYE-2024|INV-|USD|1500.00|20260731|TAX-123457|E58D8569",
            ["creditorName"] = "Treasury MDA",
            ["address"] = "Treasury MDA",
            ["currency"] = "USD"
        };

        var mapped = adapter.Transform(user, "CB_VerificationResponse");
        var dto = adapter.ToObject<VerificationResponseDto>(mapped);

        dto.Name.Should().Be("P2G1|IMO-IRS-00|PAYE-2024|INV-|USD|1500.00|20260731|TAX-123457|E58D8569");
        dto.CreditorName.Should().Be("Treasury MDA");
    }

    [Fact]
    public void Transform_PaymentRequest_Can_Use_Normalized_Bill_Fields()
    {
        var opts = new JsonAdapterOptions
        {
            Endpoints =
            {
                ["payment"] = new EndpointMapping
                {
                    FieldMappings =
                    {
                        new FieldMapping { InternalField = "AmountPayable", UserField = "amountPayable", Type = "double" },
                        new FieldMapping { InternalField = "CreditorAccount", UserField = "creditorAccount", Type = "string" },
                        new FieldMapping { InternalField = "CreditorName", UserField = "creditorName", Type = "string" },
                        new FieldMapping { InternalField = "CreditorAccountType", UserField = "creditorAccountType", Type = "string" },
                        new FieldMapping { InternalField = "Upr", UserField = "upr", Type = "string" },
                    }
                }
            }
        };
        var adapter = CreateAdapter(opts);
        var user = new JsonObject
        {
            ["amountPayable"] = 125.50,
            ["creditorAccount"] = "401005007403",
            ["creditorName"] = "Treasury MDA",
            ["creditorAccountType"] = "ACCT",
            ["upr"] = "UPR-123"
        };

        var mapped = adapter.Transform(user, "payment");
        var dto = adapter.ToObject<PaymentRequestDto>(mapped);

        dto.AmountPayable.Should().Be(125.50m);
        dto.CreditorAccount.Should().Be("401005007403");
        dto.CreditorName.Should().Be("Treasury MDA");
        dto.CreditorAccountType.Should().Be("ACCT");
        dto.Upr.Should().Be("UPR-123");
    }
}
