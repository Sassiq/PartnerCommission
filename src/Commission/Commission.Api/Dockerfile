FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Shared/PartnerCommission.Contracts/*.csproj src/Shared/PartnerCommission.Contracts/
COPY src/Shared/PartnerCommission.ServiceDefaults/*.csproj src/Shared/PartnerCommission.ServiceDefaults/
COPY src/Shared/PartnerCommission.Messaging/*.csproj src/Shared/PartnerCommission.Messaging/
COPY src/Commission/Commission.Domain/*.csproj src/Commission/Commission.Domain/
COPY src/Commission/Commission.Api/*.csproj src/Commission/Commission.Api/
RUN dotnet restore src/Commission/Commission.Api/Commission.Api.csproj

COPY src/Shared/ src/Shared/
COPY src/Commission/ src/Commission/
RUN dotnet publish src/Commission/Commission.Api/Commission.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=
EXPOSE 8080
ENTRYPOINT ["dotnet", "Commission.Api.dll"]
