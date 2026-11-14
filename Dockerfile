# =============================================================================
# Stage 1: Build & Publish (.NET 10 SDK)
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project manifests first for optimal Docker layer caching
COPY ["NuGet.Config", "./"]
COPY ["Clippy/Clippy.csproj", "Clippy/"]
COPY ["Clippy.Client/Clippy.Client.csproj", "Clippy.Client/"]
RUN dotnet restore "Clippy/Clippy.csproj"

# Copy full source tree and publish optimized release build
COPY . .
WORKDIR "/src/Clippy"
RUN dotnet publish "Clippy.csproj" -c Release -o /app/publish /p:UseAppHost=false

# =============================================================================
# Stage 2: Hardened Production Runtime (.NET 10 ASP.NET Runtime)
# =============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Configure standard container port and production environment
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

# Run as non-root user for container security best practices
USER $APP_UID

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Clippy.dll"]
