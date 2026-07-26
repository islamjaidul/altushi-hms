#!/bin/sh
# One-shot solution scaffold (spec 0005 T1). Idempotent-ish: skips existing projects.
set -eu
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

cd "$(dirname "$0")/.."

[ -f hms-erp.sln ] || dotnet new sln -n hms-erp

new_lib() {  # $1 = path, $2 = name
  [ -d "$1" ] || dotnet new classlib -o "$1" -n "$2" --no-restore
  dotnet sln add "$1" >/dev/null
}

# Kernel
new_lib src/Hms.Kernel Hms.Kernel

# Modules (ADR-0003): implementation + Contracts each
for M in Registration Appointments Billing Diagnostics Lis Dashboard Admin Notifications; do
  new_lib "src/Modules/$M/Hms.$M.Contracts" "Hms.$M.Contracts"
  new_lib "src/Modules/$M/Hms.$M" "Hms.$M"
done

# Host
[ -d src/Hms.Web ] || dotnet new web -o src/Hms.Web -n Hms.Web --no-restore
dotnet sln add src/Hms.Web >/dev/null

# Test projects (S1 set; per-module unit tests join as modules gain domain logic)
for T in Hms.Kernel.Tests Hms.Architecture.Tests Hms.Integration.Tests Hms.PrintGolden.Tests; do
  [ -d "tests/$T" ] || dotnet new xunit -o "tests/$T" -n "$T" --no-restore
  dotnet sln add "tests/$T" >/dev/null
done

echo "scaffold: OK"
