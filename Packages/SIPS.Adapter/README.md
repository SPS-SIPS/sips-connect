# SIPS Adapter

This package provides a JSON adapter for SIPS.

## Overview

- Implements essential JSON **conversion** for SIPS integration.
- Follows NuGet best practices for package documentation.

## JSON Mapping Details

The adapter translates JSON from the request body to the internal system JSON format using the following mappings:

- "InternalField": Maps to the internal system field.
- "UserField": Maps to the value provided by the user.
- "Type": Specifies the type of the JSON payload.

## Usage

To use the adapter, follow these steps:

1. Install the package.
2. Add the package to your IServiceCollection.
3. Use the IJsonAdapter interface to convert the JSON.

```csharp
services.AddJsonAdapter();

var jsonAdapter = serviceProvider.GetRequiredService<IJsonAdapter>();

// Provide the endpoint name that corresponds to your configured mapping
var internalJson = jsonAdapter.Transform(userJson, endpointName);
```

### Configuring Endpoint Mappings

The adapter requires an `endpointName` to determine how to map incoming JSON. Provide a configured `JsonAdapterOptions` instance via DI (the library registers an empty default you can replace):

```csharp
using SIPS.Adapter.Models;

services.AddSingleton(new JsonAdapterOptions
{
    Endpoints = new Dictionary<string, EndpointMapping>
    {
        ["createPayment"] = new EndpointMapping
        {
            FieldMappings = new List<FieldMapping>
            {
                new FieldMapping { InternalField = "amount", UserField = "data.amount", Type = MappingType.Int },
                new FieldMapping { InternalField = "reference", UserField = "data.ref", Type = MappingType.String },
                new FieldMapping { InternalField = "timestamp", UserField = "meta.createdAt", Type = MappingType.DateTime }
            }
        }
    }
});

// Later
var mapped = jsonAdapter.Transform(userJson, "createPayment");
```

### Reverse Mapping From Object to External JSON

To map from a local object into an external (user) JSON shape, use the generic overload:

```csharp
var userJsonOut = jsonAdapter.Transform(localObject, endpointName);
