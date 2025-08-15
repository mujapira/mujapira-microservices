# — Stage 1: runtime base
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

# — Stage 2: restore & build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1) Copia apenas os contratos que o ApiGateway consome
COPY ["Contracts.Common/Contracts.Common.csproj","Contracts.Common/"]

# 2) Copia o csproj do ApiGateway
COPY ["ApiGateway/ApiGateway.csproj", "ApiGateway/"]

# 3) Restaura apenas o ApiGateway (já conhece os contratos)
RUN dotnet restore "ApiGateway/ApiGateway.csproj"

COPY . .

# 5) Publica o ApiGateway em Release
WORKDIR "/src/ApiGateway"
RUN dotnet publish "ApiGateway.csproj" -c Release -o /app/publish --no-restore

# — Stage 3: runtime image
FROM base AS final
WORKDIR /app

# instala curl (pra healthcheck) sem lixo
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

# 6) Copia os artefatos gerados
COPY --from=build /app/publish ./

# 7) Entry point
ENTRYPOINT ["dotnet", "ApiGateway.dll"]
