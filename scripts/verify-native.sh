#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${NYSSAAI_REPOSITORY_POSTGRES:?Supply an isolated PostgreSQL test database}"

runtime="${1:-linux-x64}"
mkdir -p .temp
scratch="$(mktemp -d "$PWD/.temp/native-XXXXXXXX")"
dotnet publish test/NyssaAI.Logging.AotSmoke -c Release -r "$runtime" -o "$scratch/logging"
dotnet publish test/NyssaAI.Repository.AotSmoke -c Release -r "$runtime" -o "$scratch/repository"

"$scratch/logging/NyssaAI.Logging.AotSmoke"
repository="$scratch/repository/NyssaAI.Repository.AotSmoke"
export NYSSAAI_REPOSITORY_CONNECTION="Data Source=$scratch/repository.db;Pooling=False"
"$repository" verify sqlite
"$repository" verify postgresql

export NYSSAAI_REPOSITORY_KEY="$(head -c 32 /dev/urandom | base64)"
"$repository" setup "$scratch/settings" sqlite
"$repository" run "$scratch/settings"
"$repository" switch "$scratch/settings"
"$repository" run "$scratch/settings"
export NYSSAAI_REPOSITORY_KEY="$(head -c 32 /dev/urandom | base64)"
if "$repository" run "$scratch/settings"; then
    echo 'Protected settings unexpectedly opened with the wrong key.' >&2
    exit 1
fi
echo 'Native checks passed, including persisted settings and wrong-key rejection.'
