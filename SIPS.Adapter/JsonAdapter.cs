using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIPS.Adapter.Models;
using Microsoft.Extensions.Logging;

namespace SIPS.Adapter;
public class JsonAdapter(JsonAdapterOptions options, ILogger<JsonAdapter> logger) : IJsonAdapter
{
    private readonly JsonAdapterOptions _options = options;
    private readonly ILogger<JsonAdapter> _logger = logger;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true, // Case-insensitive property matching
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase // Use camelCase naming
    };

    public JsonObject Transform(JsonObject userJson, string endpointName)
    {
        if (!_options.Endpoints.TryGetValue(endpointName, out var endpointMapping))
        {
            throw new InvalidOperationException($"Endpoint '{endpointName}' is not configured.");
        }

        var outputJson = new JsonObject();

        foreach (var mapping in endpointMapping.FieldMappings)
        {
            string internalField = mapping.InternalField;
            string userField = mapping.UserField;
            string expectedType = mapping.Type;

            // Get the value from the nested JSON structure
            JsonNode? node = GetNestedJsonValue(userJson, userField);

            if (node != null)
            {
                try
                {
                    // Validate and convert the value to the expected type
                    object? convertedValue = ConvertToType(node.ToString(), expectedType);
                    outputJson[internalField] = JsonValue.Create(convertedValue);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Type mismatch for field '{userField}' in endpoint '{endpointName}': {ex.Message}");
                    outputJson[internalField] = null; // Set default or handle as needed
                }
            }

            // if (userJson.TryGetPropertyValue(userField, out JsonNode? node))
            // {
            //     try
            //     {
            //         // Validate and convert the value to the expected type
            //         object? convertedValue = node != null ? ConvertToType(node.ToString(), expectedType) : null;
            //         outputJson[internalField] = JsonValue.Create(convertedValue);
            //     }
            //     catch (Exception ex)
            //     {
            //         _logger.LogWarning("Type mismatch for field '{userField}' expected as '{expectedType}': {Message}", userField, expectedType, ex.Message);
            //         outputJson[internalField] = null;
            //     }
            // }
        }

        return outputJson;
    }

    public JsonObject Transform<T>(T localObject, string endpointName)
    {
        if (!_options.Endpoints.TryGetValue(endpointName, out var endpointMapping))
        {
            throw new InvalidOperationException($"Endpoint '{endpointName}' is not configured.");
        }

        var outputJson = new JsonObject();

        foreach (var mapping in endpointMapping.FieldMappings)
        {
            string internalField = mapping.InternalField;
            string userField = mapping.UserField;
            string expectedType = mapping.Type;

            try
            {
                // Get the value of the property from the object
                PropertyInfo? property = typeof(T).GetProperty(internalField);
                if (property == null)
                {
                    _logger.LogWarning($"Property '{internalField}' not found on type '{typeof(T).Name}'.");
                    outputJson[userField] = null;
                    continue;
                }
                object? value = property.GetValue(localObject);

                // Validate and convert the value to the expected type
                object? convertedValue = value != null ? ConvertToType(value.ToString() ?? string.Empty, expectedType) : null;
                outputJson[userField] = JsonValue.Create(convertedValue);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Type mismatch for field '{internalField}' expected as '{expectedType}': {ex.Message}");
                outputJson[userField] = null;
            }
        }

        return outputJson;
    }

    public T ToObject<T>(JsonObject json)
    {
        string validatedJsonString = json.ToJsonString();
        return JsonSerializer.Deserialize<T>(validatedJsonString, _serializerOptions) ?? throw new InvalidOperationException("Deserialization failed.");
    }

    private object? ConvertToType(string value, string expectedType)
    {
        // add current system culture date format
        return expectedType.ToLower() switch
        {
            "datetime" => DateTime.TryParse(value, out var dateTimeValue) ? dateTimeValue.ToString("o") : value,
            "string" => value,
            "int" => int.TryParse(value, out var intValue) ? intValue : throw new InvalidCastException("Invalid integer value."),
            "double" => double.TryParse(value, out var doubleValue) ? doubleValue : throw new InvalidCastException("Invalid double value."),
            "bool" => bool.TryParse(value, out var boolValue) ? boolValue : throw new InvalidCastException("Invalid boolean value."),
            _ => throw new NotSupportedException($"Type '{expectedType}' is not supported.")
        };
    }

    private static JsonNode? GetNestedJsonValue(JsonObject jsonObject, string userField)
    {
        var fields = userField.Split('.'); // Split the path into parts (e.g., "data.name" -> ["data", "name"])
        JsonNode? currentNode = jsonObject;

        foreach (var field in fields)
        {
            if (currentNode is JsonObject currentObject && currentObject.TryGetPropertyValue(field, out var nextNode))
            {
                currentNode = nextNode;
            }
            else
            {
                // Return null if the field doesn't exist
                return null;
            }
        }

        return currentNode; // Return the final node
    }

}