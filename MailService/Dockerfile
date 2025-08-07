# — Stage 1: runtime base
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# — Stage 2: restore & build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1) Copia apenas os contratos que o MailService consome
COPY ["Contracts.Common/Contracts.Common.csproj", "Contracts.Common/"]
COPY ["Contracts.Logs/Contracts.Logs.csproj",     "Contracts.Logs/"]
COPY ["Contracts.Users/Contracts.Users.csproj",     "Contracts.Users/"]
COPY ["Contracts.Mail/Contracts.Mail.csproj",     "Contracts.Mail/"]

# 2) Copia o csproj do MailService
COPY ["MailService/MailService.csproj", "MailService/"]

# 3) Restaura apenas o MailService (já conhece os contratos)
RUN dotnet restore "MailService/MailService.csproj"

COPY . .

# 5) Publica o MailService em Release
WORKDIR "/src/MailService"
RUN dotnet publish "MailService.csproj" -c Release -o /app/publish --no-restore

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
ENTRYPOINT ["dotnet", "MailService.dll"]
