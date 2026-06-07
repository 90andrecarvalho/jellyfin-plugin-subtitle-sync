"""Integration tests for the ReplaceOriginal endpoint."""
import os
import shutil
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


class TestReplaceOriginal:
    """Tests for POST /SubtitleOffset/ReplaceOriginal."""

    def setup_method(self):
        """Backup original subtitle before each test."""
        self.original = os.path.join(MEDIA_DIR, "Big Buck Bunny (2020).en.srt")
        self.backup = self.original + ".bak"
        if os.path.exists(self.original):
            shutil.copy2(self.original, self.backup)

    def teardown_method(self):
        """Restore original subtitle after each test."""
        if os.path.exists(self.backup):
            shutil.copy2(self.backup, self.original)
            os.remove(self.backup)
        cleanup_offset_files()

    def test_replace_original_overwrites(self, admin_session, sample_item_id):
        """Test that ReplaceOriginal overwrites the original .srt file."""
        headers, _ = admin_session

        en_sub = find_original_en_sub(headers, sample_item_id)
        assert en_sub is not None

        # Read original content
        with open(self.original, "r") as f:
            original_content = f.read()

        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/ReplaceOriginal",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": en_sub["Index"],
                "offsetMs": 2000,
            },
            headers=headers,
        )
        assert r.status_code == 200, f"Expected 200, got {r.status_code}: {r.text}"
        data = r.json()
        assert data["offsetApplied"] == 2000

        # Verify file content changed
        time.sleep(1)
        with open(self.original, "r") as f:
            new_content = f.read()
        assert new_content != original_content, "File content should have changed"

    def test_replace_original_deletes_offset_file(self, admin_session, sample_item_id):
        """Test that ReplaceOriginal also removes any existing offset file."""
        headers, _ = admin_session
        cleanup_offset_files()

        en_sub = find_original_en_sub(headers, sample_item_id)
        assert en_sub is not None

        # First create an offset file
        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/Generate",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": en_sub["Index"],
                "offsetMs": 1000,
            },
            headers=headers,
        )
        assert r.status_code == 200

        # Now replace original
        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/ReplaceOriginal",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": en_sub["Index"],
                "offsetMs": 2000,
            },
            headers=headers,
        )
        assert r.status_code == 200

        # Verify no offset files remain
        time.sleep(1)
        import glob
        offset_files = glob.glob(os.path.join(MEDIA_DIR, "*.Offset*.srt"))
        assert len(offset_files) == 0, f"Offset files still exist: {offset_files}"

    def test_replace_original_zero_offset_rejected(self, admin_session, sample_item_id):
        """Zero offset should be rejected for ReplaceOriginal."""
        headers, _ = admin_session

        en_sub = find_original_en_sub(headers, sample_item_id)
        assert en_sub is not None

        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/ReplaceOriginal",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": en_sub["Index"],
                "offsetMs": 0,
            },
            headers=headers,
        )
        assert r.status_code == 400
