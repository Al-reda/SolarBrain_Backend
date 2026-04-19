# ── Build stage ────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj first for layer caching
COPY SolarBrain.Api/SolarBrain.Api.csproj SolarBrain.Api/
RUN dotnet restore SolarBrain.Api/SolarBrain.Api.csproj

# Copy everything else and build
COPY . .
WORKDIR /src/SolarBrain.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# ── Runtime stage ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy published output
COPY --from=build /app/publish .

# Render sets PORT env var; .NET reads it via Program.cs
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 10000

ENTRYPOINT ["dotnet", "SolarBrain.Api.dll"]
