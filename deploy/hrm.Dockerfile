# hrm-app image — the standalone HR & Payroll SKU (ADR-0025).
#
# Same source tree as the ERP image, a different composition root. What ships here is the kernel,
# the shared shell and HR: no clinical code, and a database with three schemas instead of fourteen.
# That is the point of the two-host decision — a customer receives the product they bought.
# Same pinned toolchain as the ERP image (spec 0053 AC7) - both SKUs come off one source tree,
# so they must come off one compiler.
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src
COPY global.json hms-erp.slnx Directory.Build.props nuget.config* ./
COPY src/ src/
RUN dotnet publish src/Hms.Hr.Web -c Release -o /out /p:TreatWarningsAsErrors=true

# Standard (non-chiseled) runtime: the compose healthcheck needs a shell in the image, same as the
# ERP image. Chiseled remains the production goal for both.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
COPY deploy/entitlements/ /app/entitlements/
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    Entitlement__Path=/app/entitlements/hrm-only.json
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hms.Hr.Web.dll"]
