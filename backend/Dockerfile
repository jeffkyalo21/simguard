# SimGuard API. Build context: backend/  (docker build -f backend/Dockerfile backend)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SimGuard.Api/SimGuard.Api.csproj SimGuard.Api/
RUN dotnet restore SimGuard.Api/SimGuard.Api.csproj
COPY SimGuard.Api/ SimGuard.Api/
RUN dotnet publish SimGuard.Api/SimGuard.Api.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Folder for the Data Protection key ring; mounted as a named volume.
RUN mkdir -p /keys && chown $APP_UID /keys
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    Hosting__DataProtectionKeysPath=/keys
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SimGuard.Api.dll"]
