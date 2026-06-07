# Jellyfin Subtitle Sync Plugin

A self-contained Jellyfin plugin that provides an admin-only subtitle synchronization tool. Browse your library, preview video with HLS streaming, and visually adjust subtitle timing using an interactive timeline with draggable cue bars and optional audio waveform visualization.

## How It Works

```
Admin opens Plugin Settings → Subtitle Sync
  → Browses libraries or searches for a video
  → Selects subtitle track
  → Adjusts offset with controls or by dragging cues on timeline
  → Clicks "Save as Offset File" or "Replace Original"
  → All users see corrected subtitles
```

1. Admin opens the plugin config page in Jellyfin Dashboard
2. Browses the library hierarchy or searches by title, then selects an external `.srt` subtitle track
3. Uses the built-in video player and interactive timeline to find sync issues
4. Adjusts offset via buttons (±100/500/1000ms), numeric input, or dragging cue bars
5. Saves the corrected subtitles:
   - **"Save as Offset File"** → Creates `Movie.en.Offset+2000ms.srt` alongside original
   - **"Replace Original"** → Overwrites the source `.srt` directly (with confirmation)

**No client-side JavaScript injection required.** Everything lives within the plugin's config page.

## Requirements

- **Jellyfin Server 10.11+** (minimum requirement)
- .NET 9.0 SDK (for building)
- Docker & Docker Compose (for local development)
- ffmpeg (for generating sample video and waveform data)

## Installation

### Option A: Plugin Repository (recommended)

1. In Jellyfin, go to **Admin Dashboard → Plugins → Repositories**
2. Click **Add** and paste this URL:
   ```
   https://raw.githubusercontent.com/90andrecarvalho/jellyfin-plugin-subtitle-sync/main/manifest.json
   ```
3. Go back to **Plugins → Catalog**, find **Subtitle Sync**, and click **Install**
4. Restart Jellyfin

Updates will appear automatically in the dashboard.

### Option B: Manual Install

1. Download the latest `.zip` from [GitHub Releases](https://github.com/90andrecarvalho/jellyfin-plugin-subtitle-sync/releases)
2. Extract and copy `Jellyfin.Plugin.SubtitleOffset.dll`, `meta.json`, and `thumb.png` to your plugins directory:

   - **Linux**: `/var/lib/jellyfin/plugins/SubtitleSync/`
   - **Docker**: `/config/plugins/SubtitleSync/`
   - **Windows**: `C:\ProgramData\Jellyfin\Server\plugins\SubtitleSync\`

3. Restart Jellyfin

### Access the Tool

Go to **Admin Dashboard → Plugins → Subtitle Sync**

That's it! No additional configuration needed.

## Building from Source

```bash
cd src/Jellyfin.Plugin.SubtitleOffset
dotnet build -c Release -o ../../build/plugin
```

## Features

| Feature | Description |
|---------|-------------|
| Library search | Search videos by title |
| Library browser | Navigate through libraries hierarchically (shows when search is empty) |
| HLS video preview | Play any format via Jellyfin's transcoding |
| Interactive timeline | Visual subtitle cue bars with zoom/pan |
| Audio waveform | Optional visualization (generated on demand, cached) |
| Draggable cues | Drag subtitle layer to adjust offset visually |
| Precision controls | ±100/500/1000ms buttons + numeric input |
| Real-time preview | Subtitle overlay updates as you adjust |
| Current cue display | Shows nearest subtitle text at playback position |
| Save as Offset File | Non-destructive: creates new file alongside original |
| Replace Original | Destructive: overwrites source file (with confirmation) |
| Always from original | Offset is always computed from the original file |
| Responsive layout | Side-by-side ≥1024px, stacked below |

## Usage

| Action | Result |
|--------|--------|
| Save with +2000ms offset | Creates `Movie.en.Offset+2000ms.srt` |
| Save with -1500ms offset | Creates `Movie.en.Offset-1500ms.srt` |
| Change offset and save again | Overwrites/renames previous offset file |
| Save with 0ms offset | Deletes the offset file |
| Replace Original | Overwrites `.srt`, deletes offset file, resets to 0 |

### Notes

- Offset is always computed from the **original** file (no accumulated drift)
- Only external `.srt` files are supported
- Embedded subtitles are not shown in the tool
- Only one offset file per language per video
- "Replace Original" changes take effect on next playback start (browser caching)
- Waveform data is cached per video in the plugin data directory

## Local Development

### Quick Start

```bash
make up
```

This will:
1. Build the plugin
2. Generate a sample test video (requires ffmpeg)
3. Start Jellyfin 10.11.10 via Docker Compose
4. Run initial setup (create admin/viewer users, add library)

Access Jellyfin at **http://localhost:8096**
- Admin: `admin` / `admin`
- Viewer: `viewer` / `viewer`
- Plugin: http://localhost:8096/web/#/configurationpage?name=Subtitle%20Sync

### Dev Preview (Standalone UI)

To iterate on the config page UI without running Jellyfin:

```bash
python3 -m http.server 8080
```

Then open **http://localhost:8080/dev-preview.html** in your browser.

This loads `configPage.html` with mock data (10 subtitle cues, fake library items) so the timeline, cue bars, and offset controls all work. Edit `configPage.html`, refresh the page, and see changes instantly — no Docker, no API, no Jellyfin required.

### Available Commands

```bash
make build        # Build the plugin DLL
make test-unit    # Run xUnit unit tests
make test-int     # Run pytest integration tests (requires running Jellyfin)
make test         # Run all tests
make up           # Start dev environment
make down         # Stop dev environment
make clean        # Tear down everything + remove generated files
```

## Project Structure

```
├── SPEC.md                          # Technical specification
├── README.md                        # This file
├── dev-preview.html                 # Standalone UI preview (no Jellyfin needed)
├── manifest.json                    # Jellyfin plugin repository manifest
├── Makefile                         # Common commands
├── docker-compose.yml               # Dev environment (Jellyfin 10.11.10)
├── .github/workflows/
│   ├── ci.yml                       # Build + test on push/PR
│   └── release.yml                  # Build, package, and publish on tag
├── scripts/
│   ├── generate-sample-video.sh     # Creates test video via ffmpeg
│   └── setup-jellyfin.sh           # Configures Jellyfin users & library
├── src/
│   └── Jellyfin.Plugin.SubtitleOffset/
│       ├── Plugin.cs                # Plugin entry point
│       ├── Api/
│       │   └── SubtitleOffsetController.cs  # REST API endpoints
│       ├── Configuration/
│       │   ├── PluginConfiguration.cs
│       │   ├── configPage.html      # Full admin UI (search + editor)
│       │   └── logo.svg             # Plugin icon source (SVG)
│       ├── Services/
│       │   ├── OffsetFileService.cs # Subtitle file operations
│       │   └── WaveformService.cs   # FFmpeg waveform generation
│       └── Srt/
│           ├── SrtParser.cs         # .srt file parser
│           ├── SrtWriter.cs         # .srt file writer
│           ├── SrtEntry.cs          # Data model
│           ├── SrtParseException.cs # Parse error type
│           └── EncodingDetector.cs  # File encoding detection
├── tests/
│   ├── Jellyfin.Plugin.SubtitleOffset.Tests/  # xUnit unit tests
│   └── integration/                 # pytest integration tests
├── docker/
│   └── media/                       # Sample video + subtitle files
└── build/
    └── plugin/
        ├── meta.json               # Plugin metadata (targetAbi: 10.11.0.0)
        └── thumb.png               # Plugin icon (displayed in Jellyfin dashboard)
```

## API Reference

### `POST /SubtitleOffset/Generate`

Creates a new offset `.srt` file alongside the original.

**Request:**
```json
{ "itemId": "guid", "subtitleStreamIndex": 3, "offsetMs": 2000 }
```

**Response (200):** `{ "generatedFile": "Movie.en.Offset+2000ms.srt", "trackName": "English (Offset+2000ms)" }`
**Response (204):** When `offsetMs=0` — deletes existing offset file.

### `POST /SubtitleOffset/ReplaceOriginal`

Overwrites the original `.srt` with adjusted timings.

**Request:**
```json
{ "itemId": "guid", "subtitleStreamIndex": 3, "offsetMs": 2000 }
```

**Response (200):** `{ "replacedFile": "Movie.en.srt", "offsetApplied": 2000 }`

### `GET /SubtitleOffset/SubtitleContent/{itemId}/{streamIndex}`

Returns parsed subtitle entries for the timeline.

**Response (200):**
```json
{
  "language": "en",
  "isOffsetFile": false,
  "existingOffsetMs": 2000,
  "entries": [{ "index": 1, "startMs": 5000, "endMs": 8000, "text": "Hello" }]
}
```

### `POST /SubtitleOffset/GenerateWaveform`

Generates audio waveform data (1 sample/sec, cached).

**Request:** `{ "itemId": "guid" }`
**Response (200):** `{ "status": "complete", "sampleRate": 1, "samples": [0.12, 0.45, ...] }`

### `GET /SubtitleOffset/Waveform/{itemId}`

Returns cached waveform data (404 if not generated).

### `DELETE /SubtitleOffset/CancelWaveform`

Cancels in-progress waveform generation.

## Compatibility

| Jellyfin Version | Supported |
|-----------------|-----------|
| < 10.11 | ❌ Not supported |
| 10.11+ | ✅ Full support |

## License

MIT
