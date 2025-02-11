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

var internalJson = jsonAdapter.Transform(userJson);


```
