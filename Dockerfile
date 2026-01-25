FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["SIPS.Connect.csproj", "/src/SIPS.Connect"]

RUN dotnet restore "SIPS.Connect"

COPY . .
RUN dotnet build "SIPS.Connect.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SIPS.Connect.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app

COPY --from=publish /app/publish .
ENTRYPOINT ["/bin/sh", "-c", "update-ca-certificates && dotnet SIPS.Connect.dll"]