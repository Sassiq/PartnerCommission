FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Shared/PartnerCommission.ServiceDefaults/*.csproj src/Shared/PartnerCommission.ServiceDefaults/
COPY src/Gateway/Gateway.Api/*.csproj src/Gateway/Gateway.Api/
RUN dotnet restore src/Gateway/Gateway.Api/Gateway.Api.csproj

COPY src/Shared/PartnerCommission.ServiceDefaults/ src/Shared/PartnerCommission.ServiceDefaults/
COPY src/Gateway/ src/Gateway/
RUN dotnet publish src/Gateway/Gateway.Api/Gateway.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=
EXPOSE 8080
ENTRYPOINT ["dotnet", "Gateway.Api.dll"]
