FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first for efficient restore caching
COPY ["SIPS.Connect/SIPS.Connect.csproj", "SIPS.Connect/"]
COPY ["Packages/SIPS.Core/SIPS.Core.csproj", "Packages/SIPS.Core/"]
COPY ["Packages/SIPS.PostgreSQL/SIPS.PostgreSQL.csproj", "Packages/SIPS.PostgreSQL/"]
COPY ["Packages/SIPS.ISO20022/SIPS.ISO20022.csproj", "Packages/SIPS.ISO20022/"]
COPY ["Packages/SIPS.Adapter/SIPS.Adapter.csproj", "Packages/SIPS.Adapter/"]
COPY ["Packages/SIPS.XMLDsig.Xades/SIPS.XMLDsig.Xades.csproj", "Packages/SIPS.XMLDsig.Xades/"]
COPY ["Packages/SIPS.Emv/SIPS.Emv.csproj", "Packages/SIPS.Emv/"]
COPY ["Directory.Build.props", "./"]

RUN dotnet restore "SIPS.Connect/SIPS.Connect.csproj"

# Copy everything else
COPY . .

WORKDIR "/src/SIPS.Connect"
RUN dotnet build "SIPS.Connect.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SIPS.Connect.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["/bin/sh", "-c", "update-ca-certificates && dotnet SIPS.Connect.dll"]