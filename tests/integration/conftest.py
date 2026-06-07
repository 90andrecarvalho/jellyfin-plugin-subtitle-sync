"""Pytest configuration for integration tests."""
import os
import time

import pytest
import requests

JELLYFIN_URL = os.environ.get("JELLYFIN_URL", "http://localhost:8096")
ADMIN_USER = os.environ.get("JELLYFIN_ADMIN_USER", "admin")
ADMIN_PASS = os.environ.get("JELLYFIN_ADMIN_PASS", "admin")
VIEWER_USER = os.environ.get("JELLYFIN_VIEWER_USER", "viewer")
VIEWER_PASS = os.environ.get("JELLYFIN_VIEWER_PASS", "viewer")


def wait_for_jellyfin(timeout=60):
    """Wait for Jellyfin to be healthy."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            r = requests.get(f"{JELLYFIN_URL}/health", timeout=5)
            if r.status_code == 200:
                return True
        except requests.ConnectionError:
            pass
        time.sleep(2)
    raise TimeoutError("Jellyfin did not become healthy")


def authenticate(username, password):
    """Authenticate with Jellyfin and return auth headers."""
    r = requests.post(
        f"{JELLYFIN_URL}/Users/AuthenticateByName",
        json={"Username": username, "Pw": password},
        headers={
            "X-Emby-Authorization": (
                'MediaBrowser Client="Integration Tests", '
                'Device="pytest", DeviceId="test-device-001", '
                'Version="1.0.0"'
            )
        },
    )
    r.raise_for_status()
    data = r.json()
    token = data["AccessToken"]
    user_id = data["User"]["Id"]
    return {
        "X-Emby-Authorization": (
            f'MediaBrowser Client="Integration Tests", '
            f'Device="pytest", DeviceId="test-device-001", '
            f'Version="1.0.0", Token="{token}"'
        ),
    }, user_id


@pytest.fixture(scope="session", autouse=True)
def ensure_jellyfin():
    """Ensure Jellyfin is running before tests."""
    wait_for_jellyfin()


@pytest.fixture(scope="session")
def admin_session():
    """Return authenticated admin session headers and user ID."""
    headers, user_id = authenticate(ADMIN_USER, ADMIN_PASS)
    return headers, user_id


@pytest.fixture(scope="session")
def viewer_session():
    """Return authenticated viewer session headers and user ID."""
    headers, user_id = authenticate(VIEWER_USER, VIEWER_PASS)
    return headers, user_id


@pytest.fixture(scope="session")
def sample_item_id(admin_session):
    """Find the sample movie item ID."""
    headers, _ = admin_session
    r = requests.get(
        f"{JELLYFIN_URL}/Items",
        params={"Recursive": True, "SearchTerm": "Big Buck Bunny"},
        headers=headers,
    )
    r.raise_for_status()
    items = r.json().get("Items", [])
    assert len(items) > 0, "Big Buck Bunny not found in library"
    return items[0]["Id"]


@pytest.fixture(scope="session")
def jellyfin_url():
    return JELLYFIN_URL
