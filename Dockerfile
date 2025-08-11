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
# bring in the wait-for-it script from build context
COPY --from=build /src/wait-for-it.sh . 
RUN chmod +x wait-for-it.sh

COPY --from=publish /app/publish .
ENTRYPOINT ["/bin/sh", "-c", "./wait-for-it.sh \"$DB_HOST:$DB_PORT\" --timeout=60 --strict -- dotnet SIPS.Connect.dll"]