#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load .env if present
if [ -f "${SCRIPT_DIR}/.env" ]; then
    set -a
    # shellcheck disable=SC1091
    source "${SCRIPT_DIR}/.env"
    set +a
fi

TARGET=${1:-}

if [ "$TARGET" = "pilot" ]; then
  BRANCH=master
  REMOTE_PATH=/opt/algreen/api/
  SERVICE=algreen-api
  HOST_VAR=DEPLOY_HOST_PILOT
elif [ "$TARGET" = "staging" ]; then
  BRANCH=staging
  REMOTE_PATH=/opt/alblue/api/
  SERVICE=alblue-api
  HOST_VAR=DEPLOY_HOST_STAGING
else
  echo "Usage: ./deploy.sh [staging|pilot]"
  echo "  staging → branch=staging  → /opt/alblue/api/   + alblue-api"
  echo "  pilot   → branch=master   → /opt/algreen/api/  + algreen-api"
  exit 1
fi

HOST="${!HOST_VAR:-}"
if [ -z "$HOST" ]; then
    echo "ERROR: $HOST_VAR not set. Copy .env.example to .env and fill in values."
    exit 1
fi

DEPLOY_USER="${DEPLOY_USER:-root}"
SSH_KEY_ARG=""
if [ -n "${DEPLOY_SSH_KEY:-}" ]; then
    SSH_KEY_ARG="-i ${DEPLOY_SSH_KEY}"
fi

if [ -n "$(git status --porcelain)" ]; then
  echo "❌ Working tree has uncommitted changes — commit or stash before deploying."
  exit 1
fi

echo "🌿 Switching to $BRANCH and pulling latest..."
git fetch origin "$BRANCH"
git checkout "$BRANCH"
git pull --ff-only origin "$BRANCH"

echo "🔨 Building backend (commit: $(git rev-parse --short HEAD))..."
dotnet publish AlgreenMES.API/AlgreenMES.API.csproj -c Release -o ./publish

echo "📦 Uploading to server ($TARGET → ${DEPLOY_USER}@${HOST}:$REMOTE_PATH)..."
rsync -az --delete --exclude='appsettings.Production.json' --exclude='uploads/' -e "ssh ${SSH_KEY_ARG}" ./publish/ "${DEPLOY_USER}@${HOST}:$REMOTE_PATH"

echo "🗄️  Applying database migrations..."
if ! ssh ${SSH_KEY_ARG} "${DEPLOY_USER}@${HOST}" "cd ${REMOTE_PATH} && ASPNETCORE_ENVIRONMENT=Production dotnet AlgreenMES.API.dll --migrate"; then
    echo "❌ Migration failed — aborting deploy. The new binaries are on disk but $SERVICE has NOT been restarted; the old process keeps serving traffic from its in-memory binary. Fix the migration and re-deploy."
    exit 1
fi

echo "🔄 Restarting $SERVICE..."
ssh ${SSH_KEY_ARG} "${DEPLOY_USER}@${HOST}" "systemctl restart $SERVICE"

echo "✅ Backend deployed to $TARGET (branch: $BRANCH, commit: $(git rev-parse --short HEAD))"
