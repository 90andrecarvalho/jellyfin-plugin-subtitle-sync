"""Integration tests for the SubtitleContent endpoint."""
import os
import time

import pytest
import requests

JELLYFIN_URL = os.environ.get("JELLYFIN_URL", "http://localhost:8096")
MEDIA_DIR = os.environ.get("MEDIA_DIR", os.path.join(os.path.dirname(__file__), "../../docker/media"))


def get_subtitle_streams(headers, item_id):
    """Get subtitle streams for an item."""
    r = requests.get(f"{JELLYFIN_URL}/Items/{item_id}", headers=headers)
    r.raise_for_status()
    return [s for s in r.json().get("MediaStreams", []) if s.get("Type") == "Subtitle"]


def find_original_en_sub(headers, item_id):
    """Find the original English subtitle."""
    subs = get_subtitle_streams(headers, item_id)
    return next(
        (s for s in subs if s.get("IsExternal") and s.get("Path", "").endswith(".en.srt")),
        None,
    )


def cleanup_offset_files():
    """Remove offset files."""
    import glob
    for f in glob.glob(os.path.join(MEDIA_DIR, "*.Offset*.srt")):
        os.remove(f)


class TestSubtitleContent:
    """Tests for GET /SubtitleOffset/SubtitleContent/{itemId}/{streamIndex}."""

    def test_get_subtitle_content(self, admin_session, sample_item_id):
        """Test that subtitle content is returned correctly."""
        headers, _ = admin_session

        en_sub = find_original_en_sub(headers, sample_item_id)
        assert en_sub is not None

        r = requests.get(
            f"{JELLYFIN_URL}/SubtitleOffset/SubtitleContent/{sample_item_id}/{en_sub['Index']}",
            headers=headers,
        )
        assert r.status_code == 200, f"Expected 200, got {r.status_code}: {r.text}"
        data = r.json()
        assert data["Language"] == "en"
        assert data["IsOffsetFile"] is False
        assert data["ExistingOffsetMs"] == 0
        assert len(data["Entries"]) > 0
        # Check entry structure
        entry = data["Entries"][0]
        assert "Index" in entry
        assert "StartMs" in entry
        assert "EndMs" in entry
        assert "Text" in entry

    def test_subtitle_content_with_existing_offset(self, admin_session, sample_item_id):
        """Test that existingOffsetMs is populated when an offset file exists."""
        headers, _ = admin_session
        cleanup_offset_files()

        en_sub = find_original_en_sub(headers, sample_item_id)
        assert en_sub is not None

        # Generate an offset file
        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/Generate",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": en_sub["Index"],
                "offsetMs": 2500,
            },
            headers=headers,
        )
        assert r.status_code == 200

        time.sleep(2)

        # Now get subtitle content — should report existing offset
        r = requests.get(
            f"{JELLYFIN_URL}/SubtitleOffset/SubtitleContent/{sample_item_id}/{en_sub['Index']}",
            headers=headers,
        )
        assert r.status_code == 200
        data = r.json()
        assert data["ExistingOffsetMs"] == 2500

        cleanup_offset_files()

    def test_subtitle_content_invalid_stream(self, admin_session, sample_item_id):
        """Invalid stream index returns 400."""
        headers, _ = admin_session

        r = requests.get(
            f"{JELLYFIN_URL}/SubtitleOffset/SubtitleContent/{sample_item_id}/999",
            headers=headers,
        )
        assert r.status_code == 400
