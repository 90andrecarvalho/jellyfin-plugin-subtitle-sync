.PHONY: build test test-unit test-int up down setup clean generate-video

DOTNET_ROOT ?= /opt/homebrew/opt/dotnet@9/libexec
export PATH := $(DOTNET_ROOT):$(PATH)

# Build the plugin
build:
	cd src/Jellyfin.Plugin.SubtitleOffset && dotnet build -c Release -o ../../build/plugin

# Run unit tests
test-unit:
	cd tests/Jellyfin.Plugin.SubtitleOffset.Tests && dotnet test --nologo

# Run integration tests (requires running Jellyfin)
test-int:
	cd tests/integration && pip install -r requirements.txt -q && pytest -v

# Run all tests
test: test-unit test-int

# Generate sample video (requires ffmpeg)
generate-video:
	bash scripts/generate-sample-video.sh

# Start the dev environment
up: build generate-video
	docker compose up -d
	@echo "Waiting for Jellyfin to start..."
	@sleep 10
	bash scripts/setup-jellyfin.sh
	@echo ""
	@echo "✅ Dev environment ready!"
	@echo "   Jellyfin: http://localhost:8096"
	@echo "   Admin:    admin / admin"
	@echo "   Viewer:   viewer / viewer"
	@echo "   Plugin:   http://localhost:8096/web/#/configurationpage?name=Subtitle%20Offset"

# Stop the dev environment
down:
	docker compose down

# Clean everything
clean: down
	rm -rf build/
	rm -f docker/media/*.Offset*.srt
	docker compose down -v
