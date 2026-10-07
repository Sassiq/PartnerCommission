# Build context is the repository root:  docker build -f src/Partners/Partners.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the NuGet layer is cached until a project file changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Shared/PartnerCommission.Contracts/*.csproj src/Shared/PartnerCommission.Contracts/
COPY src/Shared/PartnerCommission.ServiceDefaults/*.csproj src/Shared/PartnerCommission.ServiceDefaults/
COPY src/Partners/Partners.Domain/*.csproj src/Partners/Partners.Domain/
COPY src/Partners/Partners.Api/*.csproj src/Partners/Partners.Api/
RUN dotnet restore src/Partners/Partners.Api/Partners.Api.csproj

COPY src/Shared/ src/Shared/
COPY src/Partners/ src/Partners/
RUN dotnet publish src/Partners/Partners.Api/Partners.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
# Endpoints are defined in appsettings (Kestrel:Endpoints); drop the base image's default port binding.
ENV ASPNETCORE_HTTP_PORTS=
EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "Partners.Api.dll"]
