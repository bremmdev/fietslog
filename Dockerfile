# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Fietslog.Worker/Fietslog.Worker.csproj src/Fietslog.Worker/
RUN dotnet restore src/Fietslog.Worker/Fietslog.Worker.csproj
COPY src/ src/
RUN dotnet publish src/Fietslog.Worker/Fietslog.Worker.csproj -c Release -o /app --no-restore -p:UseAppHost=false

# Run. The regular (non-chiseled) runtime image includes tzdata for Europe/Amsterdam.
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
# Railway mounts the volume here. The image runs as root (the .NET default), so it can write to the
# root-owned volume. If you add `USER $APP_UID`, also set RAILWAY_RUN_UID=0 on the Railway service.
ENV Bot__DatabasePath=/data/fietslog.db
ENTRYPOINT ["dotnet", "Fietslog.Worker.dll"]
