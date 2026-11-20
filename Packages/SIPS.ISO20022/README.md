# SPS SIPS ISO 20022

The SIPS ISO 20022 package provides a set of classes for working with ISO 20022 messages.

## Overview

The SIPS ISO 20022 package provides a set of classes for working with ISO 20022 messages. The package includes the following features:

- **Message Parsing**: Parses ISO 20022 messages.
- **Message Generation**: Generates ISO 20022 messages.
- **Message Validation**: Validates ISO 20022 messages.
- **Message Transformation**: Transforms ISO 20022 messages.

## Installation

1. These libraries are not published to NuGet. Reference the project from this repository instead:

   ```xml
   <ProjectReference Include="..\Packages\SIPS.ISO20022\SIPS.ISO20022.csproj" />
   ```

Version 1.x includes the qualified WP-SIPS-01 ISO schemas and profiles. It does
not define PAPSS trust, transport, credential, or authorization policy.
