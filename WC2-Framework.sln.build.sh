#!/usr/bin/env bash
# Generates the solution and builds all modules.
set -e
dotnet new sln -n WC2-Framework --force
for proj in src/*/*.csproj; do dotnet sln add "$proj"; done
dotnet build -c Release
echo ""
echo "Deploy: copy each src/WC2.*/bin/Release/net8.0/ output into"
echo "  <server>/game/csgo/addons/counterstrikesharp/plugins/<ModuleName>/"
