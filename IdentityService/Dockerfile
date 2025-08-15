# — Stage 1: runtime base
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# — Stage 2: restore & build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1) Copia apenas os contratos que o IdentityService consome
COPY ["Contracts.Identity/Contracts.Identity.csproj",    "Contracts.Identity/"]
COPY ["Contracts.Common/Contracts.Common.csproj", "Contracts.Common/"]
COPY ["Contracts.Logs/Contracts.Logs.csproj",     "Contracts.Logs/"]
COPY ["Contracts.Mail/Contracts.Mail.csproj",     "Contracts.Mail/"]

COPY ["Common.Observability/Common.Observability.csproj",     "Common.Observability/"]
COPY ["Common.Readiness.Kafka/Common.Readiness.Kafka.csproj",     "Common.Readiness.Kafka/"]
COPY ["Common.Readiness.Postgres/Common.Readiness.Postgres.csproj",     "Common.Readiness.Postgres/"]
COPY ["Common.Readiness.Redis/Common.Readiness.Redis.csproj",     "Common.Readiness.Redis/"]

# 2) Copia o csproj do IdentityService
COPY ["IdentityService/IdentityService.csproj", "IdentityService/"]

# 3) Restaura apenas o IdentityService (já conhece os contratos)
RUN dotnet restore "IdentityService/IdentityService.csproj"

COPY . .

# 5) Publica o IdentityService em Release
WORKDIR "/src/IdentityService"
RUN dotnet publish "IdentityService.csproj" -c Release -o /app/publish --no-restore

# — Stage 3: runtime image
FROM base AS final
WORKDIR /app

# instala curl (pra healthcheck) sem lixo
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

# 6) Copia os artefatos gerados
COPY --from=build /app/publish ./

# 7) Entry point
ENTRYPOINT ["dotnet", "IdentityService.dll"]
