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
            MappingType expectedType = ResolveMappingType(mapping);

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
                    _logger.LogWarning("Type mismatch for field '{UserField}' in endpoint '{EndpointName}' expected as '{ExpectedType}': {Message}", userField, endpointName, expectedType, ex.Message);
                    outputJson[internalField] = null; // Set default or handle as needed
                }
            }
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
            MappingType expectedType = ResolveMappingType(mapping);

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

    private object? ConvertToType(string value, MappingType expectedType)
    {
        // Normalize into expected type; DateTime normalized to ISO-8601 string
        return expectedType switch
        {
            MappingType.DateTime => DateTime.TryParse(value, out var dateTimeValue) ? dateTimeValue.ToString("o") : value,
            MappingType.String => value,
            MappingType.Int => int.TryParse(value, out var intValue) ? intValue : throw new InvalidCastException("Invalid integer value."),
            MappingType.Double => double.TryParse(value, out var doubleValue) ? doubleValue : throw new InvalidCastException("Invalid double value."),
            MappingType.Bool => bool.TryParse(value, out var boolValue) ? boolValue : throw new InvalidCastException("Invalid boolean value."),
            _ => throw new NotSupportedException($"Type '{expectedType}' is not supported.")
        };
    }

    private static MappingType ResolveMappingType(FieldMapping mapping)
    {
        if (mapping.EnumType.HasValue) return mapping.EnumType.Value;
        return ParseType(mapping.Type);
    }

    private static MappingType ParseType(string type)
    {
        return type.Trim().ToLowerInvariant() switch
        {
            "string" => MappingType.String,
            "int" => MappingType.Int,
            "double" => MappingType.Double,
            "bool" => MappingType.Bool,
            "datetime" => MappingType.DateTime,
            _ => throw new NotSupportedException($"Type '{type}' is not supported."),
        };
    }

    private static JsonNode? GetNestedJsonValue(JsonObject jsonObject, string userField)
    {
        var segments = userField.Split('.');
        JsonNode? current = jsonObject;

        foreach (var segment in segments)
        {
            if (current == null) return null;

            // Handle array indexer e.g., items[0]
            var name = segment;
            int? index = null;
            var bracketStart = segment.IndexOf('[');
            if (bracketStart >= 0 && segment.EndsWith("]"))
            {
                name = segment.Substring(0, bracketStart);
                var indexStr = segment.Substring(bracketStart + 1, segment.Length - bracketStart - 2);
                if (int.TryParse(indexStr, out var idx))
                {
                    index = idx;
                }
                else
                {
                    return null;
                }
            }

            if (!string.IsNullOrEmpty(name))
            {
                if (current is JsonObject obj && obj.TryGetPropertyValue(name, out var next))
                {
                    current = next;
                }
                else
                {
                    return null;
                }
            }

            if (index.HasValue)
            {
                if (current is JsonArray arr)
                {
                    if (index.Value < 0 || index.Value >= arr.Count) return null;
                    current = arr[index.Value];
                }
                else
                {
                    return null;
                }
            }
        }

        return current;
    }
}