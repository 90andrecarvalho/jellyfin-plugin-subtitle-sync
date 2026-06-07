"""Integration tests for the subtitle offset generation endpoint."""
import os
import time

import pytest
import requests

JELLYFIN_URL = os.environ.get("JELLYFIN_URL", "http://localhost:8096")
MEDIA_DIR = os.environ.get("MEDIA_DIR", os.path.join(os.path.dirname(__file__), "../../docker/media"))


def get_subtitle_streams(headers, item_id):
    """Get subtitle streams for an item."""
    r = requests.get(
        f"{JELLYFIN_URL}/Items/{item_id}",
        headers=headers,
    )
    r.raise_for_status()
    item = r.json()
    streams = item.get("MediaStreams", [])
    return [s for s in streams if s.get("Type") == "Subtitle"]


def find_original_en_sub(headers, item_id):
    """Find the original (non-offset, non-malformed) English subtitle."""
    subs = get_subtitle_streams(headers, item_id)
    return next(
        (s for s in subs
         if s.get("IsExternal")
         and s.get("Path", "").endswith(".en.srt")),
        None,
    )


def cleanup_offset_files():
    """Remove any offset files from the media directory."""
    import glob
    for f in glob.glob(os.path.join(MEDIA_DIR, "*.Offset*.srt")):
        os.remove(f)


class TestGenerateOffset:
    """Tests for POST /SubtitleOffset/Generate."""

    def test_generate_creates_file(self, admin_session, sample_item_id):
        """Test that generating an offset creates a .srt file on disk."""
        headers, _ = admin_session
        cleanup_offset_files()

        # Refresh to clear stale streams
        time.sleep(2)

        en_sub = find_original_en_sub(headers, sample_item_id)
        assert en_sub is not None, "No original external English subtitle found"

        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/Generate",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": en_sub["Index"],
                "offsetMs": 2000,
            },
            headers=headers,
        )
        assert r.status_code == 200, f"Expected 200, got {r.status_code}: {r.text}"
        data = r.json()
        assert "generatedFile" in data
        assert "Offset+2000ms" in data["generatedFile"]
        assert "trackName" in data

        # Verify file exists on disk
        generated_path = os.path.join(MEDIA_DIR, data["generatedFile"])
        time.sleep(1)
        assert os.path.exists(generated_path), f"File not found: {generated_path}"

        # Cleanup
        os.remove(generated_path)

    def test_overwrite_previous_offset(self, admin_session, sample_item_id):
        """Test that a second generation overwrites the first."""
        headers, _ = admin_session
        cleanup_offset_files()
        time.sleep(2)

        en_sub = find_original_en_sub(headers, sample_item_id)
        assert en_sub is not None

        # Generate with +2000ms
        r1 = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/Generate",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": en_sub["Index"],
                "offsetMs": 2000,
            },
            headers=headers,
        )
        assert r1.status_code == 200
        first_file = r1.json()["generatedFile"]

        # Generate with +3500ms (should overwrite)
        r2 = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/Generate",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": en_sub["Index"],
                "offsetMs": 3500,
            },
            headers=headers,
        )
        assert r2.status_code == 200
        second_file = r2.json()["generatedFile"]
        assert "Offset+3500ms" in second_file

        # First file should be gone
        time.sleep(1)
        first_path = os.path.join(MEDIA_DIR, first_file)
        assert not os.path.exists(first_path), "Previous offset file was not deleted"

        # Cleanup
        second_path = os.path.join(MEDIA_DIR, second_file)
        if os.path.exists(second_path):
            os.remove(second_path)

    def test_zero_offset_deletes_file(self, admin_session, sample_item_id):
        """Test that offset=0 deletes the generated file."""
        headers, _ = admin_session
        cleanup_offset_files()
        time.sleep(2)

        en_sub = find_original_en_sub(headers, sample_item_id)
        assert en_sub is not None

        # First create one
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

        # Now set to 0
        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/Generate",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": en_sub["Index"],
                "offsetMs": 0,
            },
            headers=headers,
        )
        assert r.status_code == 204

        # Verify no offset files exist
        import glob
        offset_files = glob.glob(os.path.join(MEDIA_DIR, "*.Offset*.srt"))
        assert len(offset_files) == 0, f"Offset files still exist: {offset_files}"
