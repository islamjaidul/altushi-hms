# hms-app image (ADR-0001): multi-stage, chiseled runtime, multi-arch.
# Build toolchain pinned to the SDK global.json names, so the image is compiled by the same
# compiler that ran the tests. A floating sdk:10.0 meant CI could prove one binary and ship
# another (spec 0053 AC7).
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src
COPY global.json hms-erp.slnx Directory.Build.props nuget.config* ./
COPY src/ src/
RUN dotnet publish src/Hms.Web -c Release -o /out /p:TreatWarningsAsErrors=true

# Standard (non-chiseled) runtime for now: the compose healthcheck needs a shell in the image.
# Chiseled is the production goal (smaller RSS/attack surface) — swap back once the healthcheck
# moves to a compiled probe; tracked in spec 0005 notes.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
COPY deploy/entitlements/ /app/entitlements/
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    Entitlement__Path=/app/entitlements/dev-all-modules.json
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hms.Web.dll"]
