# — Stage 1: runtime base
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# — Stage 2: restore & build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1) Copia apenas os contratos que o AuthService consome
COPY ["Contracts.Auth/Contracts.Auth.csproj",     "Contracts.Auth/"]
COPY ["Contracts.Users/Contracts.Users.csproj",   "Contracts.Users/"]
COPY ["Contracts.Logs/Contracts.Logs.csproj",     "Contracts.Logs/"]
COPY ["Contracts.Common/Contracts.Common.csproj", "Contracts.Common/"]

# 2) Copia o csproj do AuthService
COPY ["AuthService/AuthService.csproj", "AuthService/"]

# 3) Restaura apenas o AuthService (já conhece os contratos)
RUN dotnet restore "AuthService/AuthService.csproj"

COPY . .

# 5) Publica o AuthService em Release
WORKDIR "/src/AuthService"
RUN dotnet publish "AuthService.csproj" -c Release -o /app/publish --no-restore

# — Stage 3: runtime image
FROM base AS final
WORKDIR /app

# 6) Copia os artefatos gerados
COPY --from=build /app/publish ./

# 7) Entry point
ENTRYPOINT ["dotnet", "AuthService.dll"]
