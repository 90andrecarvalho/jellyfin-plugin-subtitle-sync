"""Integration tests for the Waveform endpoints."""
import os
import time

import pytest
import requests

JELLYFIN_URL = os.environ.get("JELLYFIN_URL", "http://localhost:8096")


class TestWaveform:
    """Tests for waveform generation and caching endpoints."""

    def test_waveform_not_cached_initially(self, admin_session, sample_item_id):
        """GET /Waveform should return 404 when not generated."""
        headers, _ = admin_session

        r = requests.get(
            f"{JELLYFIN_URL}/SubtitleOffset/Waveform/{sample_item_id}",
            headers=headers,
        )
        assert r.status_code == 404

    def test_generate_waveform(self, admin_session, sample_item_id):
        """POST /GenerateWaveform should generate and cache waveform data."""
        headers, _ = admin_session

        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/GenerateWaveform",
            json={"itemId": sample_item_id},
            headers=headers,
            timeout=60,
        )
        assert r.status_code == 200, f"Expected 200, got {r.status_code}: {r.text}"
        data = r.json()
        assert data["status"] == "complete"
        assert data["sampleRate"] == 1
        assert len(data["samples"]) > 0

    def test_waveform_cached_after_generation(self, admin_session, sample_item_id):
        """GET /Waveform should return cached data after generation."""
        headers, _ = admin_session

        # Ensure it's generated first
        requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/GenerateWaveform",
            json={"itemId": sample_item_id},
            headers=headers,
            timeout=60,
        )

        r = requests.get(
            f"{JELLYFIN_URL}/SubtitleOffset/Waveform/{sample_item_id}",
            headers=headers,
        )
        assert r.status_code == 200
        data = r.json()
        assert "samples" in data
        assert data["sampleRate"] == 1

    def test_cancel_waveform(self, admin_session):
        """DELETE /CancelWaveform should return success."""
        headers, _ = admin_session

        r = requests.delete(
            f"{JELLYFIN_URL}/SubtitleOffset/CancelWaveform",
            headers=headers,
        )
        assert r.status_code == 200
        assert r.json()["status"] == "cancelled"

    def test_non_admin_rejected(self, viewer_session, sample_item_id):
        """Non-admin user should receive 403 for waveform endpoints."""
        headers, _ = viewer_session

        r = requests.post(
            f"{JELLYFIN_URL}/SubtitleOffset/GenerateWaveform",
            json={"itemId": sample_item_id},
            headers=headers,
        )
        assert r.status_code == 403
