#!/usr/bin/env bash
# Setup script for the local development environment.
# Creates admin and viewer users in Jellyfin and configures the library.
set -e

JELLYFIN_URL="${JELLYFIN_URL:-http://localhost:8096}"
MAX_WAIT=60

echo "Waiting for Jellyfin to be ready..."
elapsed=0
until curl -sf "$JELLYFIN_URL/health" > /dev/null 2>&1; do
    sleep 2
    elapsed=$((elapsed + 2))
    if [ $elapsed -ge $MAX_WAIT ]; then
        echo "ERROR: Jellyfin did not become healthy within ${MAX_WAIT}s"
        exit 1
    fi
done
echo "Jellyfin is healthy."

# Check if initial setup is needed
STARTUP_INFO=$(curl -sf "$JELLYFIN_URL/Startup/Configuration" 2>/dev/null || echo "")

if [ -z "$STARTUP_INFO" ]; then
    echo "Jellyfin appears to already be configured. Skipping setup."
    exit 0
fi

echo "Running initial Jellyfin setup..."

# Complete startup wizard
curl -sf -X POST "$JELLYFIN_URL/Startup/Configuration" \
    -H "Content-Type: application/json" \
    -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'

# Create admin user
curl -sf -X POST "$JELLYFIN_URL/Startup/User" \
    -H "Content-Type: application/json" \
    -d '{"Name":"admin","Password":"admin"}'

# Set library paths
curl -sf -X POST "$JELLYFIN_URL/Library/VirtualFolders" \
    -H "Content-Type: application/json" \
    -d '{"Name":"Movies","CollectionType":"movies","Paths":["/media"],"LibraryOptions":{}}'

# Complete startup
curl -sf -X POST "$JELLYFIN_URL/Startup/Complete"

echo "Initial setup complete."

# Authenticate as admin to create viewer user
echo "Creating viewer user..."
AUTH_HEADER='MediaBrowser Client="Setup", Device="script", DeviceId="setup-001", Version="1.0.0"'

AUTH_RESULT=$(curl -sf -X POST "$JELLYFIN_URL/Users/AuthenticateByName" \
    -H "Content-Type: application/json" \
    -H "X-Emby-Authorization: $AUTH_HEADER" \
    -d '{"Username":"admin","Pw":"admin"}')

TOKEN=$(echo "$AUTH_RESULT" | python3 -c "import sys, json; print(json.load(sys.stdin)['AccessToken'])" 2>/dev/null || echo "")

if [ -z "$TOKEN" ]; then
    echo "WARNING: Could not authenticate as admin. Viewer user not created."
    exit 0
fi

AUTH_WITH_TOKEN="MediaBrowser Client=\"Setup\", Device=\"script\", DeviceId=\"setup-001\", Version=\"1.0.0\", Token=\"$TOKEN\""

# Create viewer user
curl -sf -X POST "$JELLYFIN_URL/Users/New" \
    -H "Content-Type: application/json" \
    -H "X-Emby-Authorization: $AUTH_WITH_TOKEN" \
    -d '{"Name":"viewer","Password":"viewer"}' || echo "Viewer user may already exist"

echo ""
echo "Setup complete! Admin: admin/admin, Viewer: viewer/viewer"
echo "Jellyfin available at: $JELLYFIN_URL"
