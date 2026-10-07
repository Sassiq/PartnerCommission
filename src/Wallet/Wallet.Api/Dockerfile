# Build context is the repository root:  docker build -f src/Wallet/Wallet.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the NuGet layer is cached until a project file changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Shared/PartnerCommission.Contracts/*.csproj src/Shared/PartnerCommission.Contracts/
COPY src/Shared/PartnerCommission.ServiceDefaults/*.csproj src/Shared/PartnerCommission.ServiceDefaults/
COPY src/Shared/PartnerCommission.Messaging/*.csproj src/Shared/PartnerCommission.Messaging/
COPY src/Wallet/Wallet.Domain/*.csproj src/Wallet/Wallet.Domain/
COPY src/Wallet/Wallet.Api/*.csproj src/Wallet/Wallet.Api/
RUN dotnet restore src/Wallet/Wallet.Api/Wallet.Api.csproj

COPY src/Shared/ src/Shared/
COPY src/Wallet/ src/Wallet/
RUN dotnet publish src/Wallet/Wallet.Api/Wallet.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
# Endpoints are defined in appsettings (Kestrel:Endpoints); drop the base image's default port binding.
ENV ASPNETCORE_HTTP_PORTS=
EXPOSE 8080
ENTRYPOINT ["dotnet", "Wallet.Api.dll"]

