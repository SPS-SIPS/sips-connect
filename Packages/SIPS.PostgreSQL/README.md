# SIPS PostgreSQL DB For Message Logging

This package provides a PostgreSQL database for message logging for SIPS.

## Overview

The SIPS PostgreSQL DB for Message Logging package provides a PostgreSQL database for message logging. The package includes the following features:

- **Message Logging**: Logs messages to a PostgreSQL database.
- **Message Retrieval**: Retrieves messages from a PostgreSQL database.

## Installation

1. These libraries are not published to NuGet. Reference the project from this repository instead:

   ```xml
   <ProjectReference Include="..\Packages\SIPS.PostgreSQL\SIPS.PostgreSQL.csproj" />
   ```

2. Add the following configuration to your `appsettings.json` file:

```json
{
  "ConnectionStrings": {
    "db": "Host=localhost;Port=5432;Database=<database>;Username=<username>;Password=<password>"
  }
}
```

3. Run the following command to create the database:

```bash
dotnet ef database update
```

## Usage

To use the package, follow these steps:

1. Add it to your Service Collection:

```csharp
services.AddPostgreSQL(configuration);

// The following services will be registered:
// - IStorageBroker (DbContext abstraction)
// - IIncomingRecorder
// - IStorageBrokerInitializer

```
