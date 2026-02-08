using System;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SIPS.Adapter;
using SIPS.Adapter.Models;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class JsonAdapterTests
{
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

        // Assert: normalized ISO string retained
        mapped["created"]!.GetValue<string>().Should().Be(dt.ToString("o"));
    }
}
