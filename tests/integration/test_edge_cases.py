"""Edge case integration tests."""
import os

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


class TestEdgeCases:
    """Tests for error handling and edge cases."""

    def test_non_admin_rejected(self, viewer_session, sample_item_id):
        """Non-admin user should receive 403."""
        headers, _ = viewer_session

        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/Generate",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": 0,
                "offsetMs": 2000,
            },
            headers=headers,
        )
        assert r.status_code == 403

    def test_invalid_item_id(self, admin_session):
        """Non-existent item should return 404 or 400."""
        headers, _ = admin_session

        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/Generate",
            json={
                "itemId": "00000000-0000-0000-0000-000000000000",
                "subtitleStreamIndex": 0,
                "offsetMs": 2000,
            },
            headers=headers,
        )
        assert r.status_code in (400, 404)

    def test_invalid_stream_index(self, admin_session, sample_item_id):
        """Invalid subtitle stream index should return 400."""
        headers, _ = admin_session

        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/Generate",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": 999,
                "offsetMs": 2000,
            },
            headers=headers,
        )
        assert r.status_code == 400

    def test_offset_exceeds_max(self, admin_session, sample_item_id):
        """Offset beyond max limit should return 400."""
        headers, _ = admin_session

        en_sub = find_original_en_sub(headers, sample_item_id)
        assert en_sub is not None

        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/Generate",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": en_sub["Index"],
                "offsetMs": 9999999,
            },
            headers=headers,
        )
        assert r.status_code == 400
        assert "maximum" in r.json().get("error", "").lower()

    def test_malformed_srt_rejected(self, admin_session, sample_item_id):
        """Malformed .srt should return 400."""
        headers, _ = admin_session

        subs = get_subtitle_streams(headers, sample_item_id)
        malformed_sub = next(
            (s for s in subs if s.get("IsExternal") and "malformed" in s.get("Path", "").lower()),
            None,
        )
        if malformed_sub is None:
            pytest.skip("No malformed subtitle available")

        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/Generate",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": malformed_sub["Index"],
                "offsetMs": 2000,
            },
            headers=headers,
        )
        assert r.status_code == 400
        assert "malformed" in r.json().get("error", "").lower()

    def test_negative_offset_works(self, admin_session, sample_item_id):
        """Negative offset should succeed and produce correct filename."""
        headers, _ = admin_session
        cleanup_offset_files()

        en_sub = find_original_en_sub(headers, sample_item_id)
        if en_sub is None:
            pytest.skip("No external English subtitle available")

        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/Generate",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": en_sub["Index"],
                "offsetMs": -1500,
            },
            headers=headers,
        )
        assert r.status_code == 200
        assert "Offset-1500ms" in r.json()["generatedFile"]

        # Verify clamping (first entry starts at 1000ms, -1500ms should clamp to 0)
        generated_path = os.path.join(MEDIA_DIR, r.json()["generatedFile"])
        with open(generated_path, "r") as f:
            content = f.read()
        assert "00:00:00,000" in content, "Negative timestamps not clamped to zero"

        # Cleanup
        os.remove(generated_path)

    def test_offset_file_traces_to_original(self, admin_session, sample_item_id):
        """Applying offset to an already-offset file should trace back to original."""
        headers, _ = admin_session
        cleanup_offset_files()

        en_sub = find_original_en_sub(headers, sample_item_id)
        assert en_sub is not None

        # First, generate an offset file with +1000ms
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

        # Wait for metadata refresh so the offset file gets a stream index
        import time
        time.sleep(5)

        # Find the offset stream
        subs = get_subtitle_streams(headers, sample_item_id)
        offset_sub = next(
            (s for s in subs if s.get("IsExternal") and "Offset" in s.get("Path", "")),
            None,
        )
        if offset_sub is None:
            pytest.skip("Offset file not yet indexed")

        # Apply +2000ms to the offset file — should trace back to original
        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/Generate",
            json={
                "itemId": sample_item_id,
                "subtitleStreamIndex": offset_sub["Index"],
                "offsetMs": 2000,
            },
            headers=headers,
        )
        assert r.status_code == 200
        assert "Offset+2000ms" in r.json()["generatedFile"]

        cleanup_offset_files()
