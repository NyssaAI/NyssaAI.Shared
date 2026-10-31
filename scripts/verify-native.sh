#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${NYSSAAI_REPOSITORY_POSTGRES:?Supply an isolated PostgreSQL test database}"

runtime="${1:-linux-x64}"
mkdir -p .temp
scratch="$(mktemp -d "$PWD/.temp/native-XXXXXXXX")"
dotnet publish nyssa-ai-logging-cs/test/aot_smoke -c Release -r "$runtime" -o "$scratch/logging"
dotnet publish nyssa-ai-dbgateway-cs/test/aot_smoke -c Release -r "$runtime" -o "$scratch/dbgateway"

"$scratch/logging/NyssaAI.Logging.AotSmoke"
dbgateway="$scratch/dbgateway/NyssaAI.DbGateway.AotSmoke"
export NYSSAAI_DBGATEWAY_CONNECTION="Data Source=$scratch/dbgateway.db;Pooling=False"
export NYSSAAI_DBGATEWAY_POSTGRES="$NYSSAAI_REPOSITORY_POSTGRES"
"$dbgateway" verify sqlite
"$dbgateway" verify postgresql
"$dbgateway" run sqlite
"$dbgateway" run postgresql
"$dbgateway" switch
echo 'Native checks passed.'
