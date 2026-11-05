# ─── Build ───────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so layer caching survives source-only changes.
COPY HospitalSV/HospitalSV.csproj HospitalSV/
RUN dotnet restore HospitalSV/HospitalSV.csproj

COPY HospitalSV/ HospitalSV/
RUN dotnet publish HospitalSV/HospitalSV.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ─── Runtime ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

COPY --from=build /app/publish .

# The .NET 8 images ship a non-root "app" user.
USER app

ENTRYPOINT ["dotnet", "HospitalSV.dll"]
