#!/usr/bin/env bash
# The sample video (Big Buck Bunny) is bundled in the repository.
# This script is kept for backwards compatibility but does nothing.
set -e

MEDIA_DIR="$(cd "$(dirname "$0")/.." && pwd)/docker/media"

if [ -f "$MEDIA_DIR/Big Buck Bunny (2008).mp4" ]; then
    echo "Sample video exists: Big Buck Bunny (2008).mp4"
    exit 0
fi

echo "ERROR: Big Buck Bunny (2008).mp4 not found in docker/media/"
echo "Please ensure the repository is fully cloned."
exit 1
