FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["SIPS.Example.Consumer.csproj", "/src/SIPS.Example.Consumer"]

RUN dotnet restore "SIPS.Example.Consumer"

COPY . .
RUN dotnet build "SIPS.Example.Consumer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SIPS.Example.Consumer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SIPS.Example.Consumer.dll"]
