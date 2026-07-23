# syntax=docker/dockerfile:1.6

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
ARG TARGETARCH

# Copy project files first for efficient restore caching
COPY ["SIPS.Connect/SIPS.Connect.csproj", "SIPS.Connect/"]
COPY ["Packages/SIPS.Core/SIPS.Core.csproj", "Packages/SIPS.Core/"]
COPY ["Packages/SIPS.PostgreSQL/SIPS.PostgreSQL.csproj", "Packages/SIPS.PostgreSQL/"]
COPY ["Packages/SIPS.ISO20022/SIPS.ISO20022.csproj", "Packages/SIPS.ISO20022/"]
COPY ["Packages/SIPS.Adapter/SIPS.Adapter.csproj", "Packages/SIPS.Adapter/"]
COPY ["Packages/SIPS.XMLDsig.Xades/SIPS.XMLDsig.Xades.csproj", "Packages/SIPS.XMLDsig.Xades/"]
COPY ["Packages/SIPS.Emv/SIPS.Emv.csproj", "Packages/SIPS.Emv/"]
COPY ["Directory.Build.props", "./"]

RUN arch="$TARGETARCH"; \
    if [ "$arch" = "amd64" ]; then arch="x64"; fi; \
    dotnet restore "SIPS.Connect/SIPS.Connect.csproj" -a "$arch"

# Copy everything else
COPY . .

WORKDIR "/src/SIPS.Connect"
RUN arch="$TARGETARCH"; \
    if [ "$arch" = "amd64" ]; then arch="x64"; fi; \
    dotnet build "SIPS.Connect.csproj" -c Release -o /app/build -a "$arch" --no-restore

FROM build AS publish
RUN arch="$TARGETARCH"; \
    if [ "$arch" = "amd64" ]; then arch="x64"; fi; \
    dotnet publish "SIPS.Connect.csproj" -c Release -o /app/publish -a "$arch" --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["/bin/sh", "-c", "update-ca-certificates && dotnet SIPS.Connect.dll"]
