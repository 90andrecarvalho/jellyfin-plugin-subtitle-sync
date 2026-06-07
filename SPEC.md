# Jellyfin Subtitle Sync Plugin — Technical Specification

## 1. Overview

A self-contained Jellyfin server plugin that provides an admin-only subtitle synchronization tool via the plugin's configuration page. Admins browse the library, select a video, preview it with HLS streaming, and visually adjust subtitle timing using an interactive timeline with draggable cue bars and optional audio waveform visualization. Once satisfied, the admin generates a corrected `.srt` file on disk. All users then see the corrected subtitle track in their player without any action on their part.

**No client-side injection required.** The entire UI lives within the plugin's config page, making it compatible with all Jellyfin versions (10.11+) and all installation types.

---

## 2. Goals

- **Fix subtitles for everyone** — permanently on disk, not per-session
- **Self-contained** — no external JavaScript injection, no reverse proxy tricks, no browser extensions
- **Universal compatibility** — works on Docker, bare-metal, any reverse proxy or none
- **Admin-only** — plugin config pages are inherently admin-restricted
- **Precise sync tooling** — video preview, interactive timeline, draggable cues, optional waveform
- **Two save modes** — create an offset file alongside the original, or replace the original directly

---

## 3. Architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│   Jellyfin Server                                                    │
│                                                                      │
│   Subtitle Sync Plugin                                               │
│   ┌────────────────────────────────────────────────────────────────┐ │
│   │  Config Page (HTML/JS/CSS — served via IHasWebPages)           │ │
│   │  ┌──────────────────────────────────────────────────────────┐  │ │
│   │  │  • Library search (queries Jellyfin API)                 │  │ │
│   │  │  • HLS video player (via Jellyfin transcoding API)       │  │ │
│   │  │  • Interactive subtitle cue timeline                     │  │ │
│   │  │  • Optional audio waveform overlay                       │  │ │
│   │  │  • Offset controls (drag + buttons + input)              │  │ │
│   │  │  • Real-time subtitle preview overlay                    │  │ │
│   │  │  • "Current cue" text display                            │  │ │
│   │  │  • Generate / Replace Original buttons                   │  │ │
│   │  └──────────────────────────────────────────────────────────┘  │ │
│   │                                                                │ │
│   │  API Controllers                                               │ │
│   │  ┌──────────────────────────────────────────────────────────┐  │ │
│   │  │  POST /SubtitleOffset/Generate                           │  │ │
│   │  │  POST /SubtitleOffset/ReplaceOriginal                    │  │ │
│   │  │  POST /SubtitleOffset/GenerateWaveform                   │  │ │
│   │  │  GET  /SubtitleOffset/Waveform/{itemId}                  │  │ │
│   │  │  DELETE /SubtitleOffset/CancelWaveform                   │  │ │
│   │  │  GET  /SubtitleOffset/SubtitleContent/{itemId}/{index}   │  │ │
│   │  └──────────────────────────────────────────────────────────┘  │ │
│   │                                                                │ │
│   │  Services                                                      │ │
│   │  ┌──────────────────────────────────────────────────────────┐  │ │
│   │  │  • OffsetFileService (SRT parsing, offset, file write)   │  │ │
│   │  │  • WaveformService (FFmpeg extraction, caching)          │  │ │
│   │  └──────────────────────────────────────────────────────────┘  │ │
│   └────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
```

### Components

| Component | Technology | Purpose |
|-----------|-----------|---------|
| Server Plugin | C# / .NET 9 | API endpoints, SRT parsing, waveform generation, file management |
| Config Page UI | HTML / JS / CSS | Admin tool: search, player, timeline, offset controls |
| Test Suite (unit) | xUnit / C# | SRT parser, offset logic, waveform service |
| Test Suite (integration) | Python / pytest | End-to-end API tests against running Jellyfin |
| Dev Environment | Docker Compose | Jellyfin 10.11.10 + sample media + sample .srt files |

---

## 4. Server Plugin

### 4.1 API Endpoints

#### `POST /SubtitleOffset/Generate`

Creates a new offset .srt file alongside the original.

**Request Body:**
```json
{
  "itemId": "guid",
  "subtitleStreamIndex": 3,
  "offsetMs": 2000
}
```

**Behavior:**
1. Verify admin privileges → 403 if not
2. Resolve media item → 404 if not found
3. Locate the **original** external .srt file for this track (if an offset file exists, trace back to the original)
4. Validate it is a `.srt` file → 400 if embedded or non-.srt
5. Validate the `.srt` is parseable → 400 `"Subtitle file is malformed"`
6. If `offsetMs == 0`: delete any existing offset file for this track, trigger rescan, return 204
7. Apply offset to all timestamps from the **original** file (clamp negatives to `00:00:00,000`)
8. Write new file, overwriting/renaming any previous offset file for this track
9. Trigger metadata refresh
10. Return 200

**Response (200):**
```json
{
  "generatedFile": "Movie (2020).en.Offset+2000ms.srt",
  "trackName": "English (Offset+2000ms)"
}
```

#### `POST /SubtitleOffset/ReplaceOriginal`

Overwrites the original .srt file with offset-adjusted content.

**Request Body:**
```json
{
  "itemId": "guid",
  "subtitleStreamIndex": 3,
  "offsetMs": 2000
}
```

**Behavior:**
1. Verify admin privileges → 403
2. Locate original .srt (same tracing logic as Generate)
3. Validate .srt → 400 if malformed
4. Apply offset to original file's timestamps
5. **Overwrite the original file** with adjusted content
6. Delete any existing offset file for this track (now redundant)
7. Trigger metadata refresh
8. Return 200 with offset controls reset to 0

**Response (200):**
```json
{
  "replacedFile": "Movie (2020).en.srt",
  "offsetApplied": 2000
}
```

#### `POST /SubtitleOffset/GenerateWaveform`

Generates audio waveform data for a video using FFmpeg.

**Request Body:**
```json
{
  "itemId": "guid"
}
```

**Behavior:**
1. Check if waveform already cached → return immediately if so
2. Extract audio amplitude data via FFmpeg at 1 sample/second
3. Stream progress updates via Server-Sent Events (SSE) or return progress via polling endpoint
4. Save to `{PluginDataDir}/waveforms/{itemId}.json`
5. Cancelable via `DELETE /SubtitleOffset/CancelWaveform`

**Response (200 on completion):**
```json
{
  "status": "complete",
  "sampleRate": 1,
  "samples": [0.12, 0.45, 0.78, ...]
}
```

**Progress (SSE stream):**
```
data: {"progress": 0.35, "elapsed": 12.5}
data: {"progress": 0.70, "elapsed": 24.1}
data: {"status": "complete"}
```

#### `DELETE /SubtitleOffset/CancelWaveform`

Cancels an in-progress waveform generation.

#### `GET /SubtitleOffset/Waveform/{itemId}`

Returns cached waveform data if available, 404 if not generated yet.

#### `GET /SubtitleOffset/SubtitleContent/{itemId}/{streamIndex}`

Returns parsed SRT content as JSON for the timeline and preview rendering.

**Response:**
```json
{
  "language": "en",
  "isOffsetFile": false,
  "existingOffsetMs": 0,
  "entries": [
    {"index": 1, "startMs": 5000, "endMs": 8000, "text": "Hello there."},
    {"index": 2, "startMs": 12000, "endMs": 15500, "text": "How are you?"}
  ]
}
```

If the selected track is an original that has an existing offset derivative:
```json
{
  "language": "en",
  "isOffsetFile": false,
  "existingOffsetMs": 2000,
  "entries": [...]
}
```

### 4.2 File Naming Convention

Format: `{VideoBaseName}.{lang}.Offset{sign}{ms}ms.srt`

Examples:
- `Movie (2020).en.Offset+2000ms.srt` → displays as **"English (Offset+2000ms) (External)"**
- `Movie (2020).es.Offset-1500ms.srt` → displays as **"Spanish (Offset-1500ms) (External)"**

### 4.3 .srt Parser

**Validation rules:**
- File must contain at least one valid subtitle entry
- Each entry must have: sequence number, timestamp line (`HH:MM:SS,mmm --> HH:MM:SS,mmm`), at least one text line

**Offset application:**
- Always computed from the **original** file, never from a previously-offset file
- Add `offsetMs` to both start and end timestamps
- Clamp negative results to `00:00:00,000`
- Keep entries even if both timestamps clamp to zero

### 4.4 File Encoding

- Detect encoding of original .srt (BOM detection + heuristics)
- Write new file in same encoding
- Preserve BOM if present

### 4.5 Overwrite & Rename Behavior

When generating an offset file:
1. Find any existing `{VideoBaseName}.{lang}.Offset*.srt` file
2. Delete it
3. Write the new file with the updated offset value in the name

When replacing the original:
1. Overwrite the original .srt with adjusted content
2. Delete any existing offset file for this track
3. Reset offset state to 0

**Note:** Jellyfin's subtitle delivery URLs are based on stream index, not filename. Users with active playback sessions will continue to see cached subtitles until they restart playback. This is documented in the UI: "Changes take effect on next playback start."

### 4.6 Metadata Refresh

After any file write/delete, trigger `ILibraryManager.RefreshItem()` for the affected item.

### 4.7 Waveform Service

- Uses Jellyfin's bundled FFmpeg (via `IMediaEncoder`)
- Extracts audio at 1 sample/second (RMS amplitude, normalized 0.0–1.0)
- Cached in `{PluginDataDir}/waveforms/{itemId}.json`
- Progress estimated by comparing processed duration vs total duration
- Cancelable (kills FFmpeg process on cancel request)
- If cache exists when video is loaded, waveform is served immediately

### 4.8 Transcode Session Management

- When the config page starts video playback, it opens a transcode session via Jellyfin's HLS API
- When the admin navigates away, selects a different video, or closes the page, the plugin page explicitly calls Jellyfin's playback stop API to clean up the transcode session
- Implemented client-side via `beforeunload` event and explicit cleanup on navigation

---

## 5. Config Page UI

### 5.1 Page States

1. **Search state** — Search bar + results list visible
2. **Editor state** — Video editor visible (search hidden, "← Back to search" link shown)

### 5.2 Search View

- Text input with placeholder "Search by title..."
- Queries Jellyfin's `Items` API with search term
- Returns flat list showing: thumbnail, title, year, type (Movie/Episode with series info)
- Click a result → transitions to Editor state

### 5.3 Editor View — Layout

**Responsive (CSS media queries):**
- **≥1024px (side-by-side):** Player on the left, controls panel on the right, timeline full-width below both
- **<1024px (stacked):** Player on top, controls below, timeline below controls

**Components:**

#### Video Player (compact)
- HTML5 video element with HLS.js for stream playback
- Uses Jellyfin's transcoding API (`/Videos/{itemId}/master.m3u8`)
- Standard controls: play/pause, seek, volume
- Subtitle overlay rendered as custom `<div>` positioned over the player
- Subtitles update in real-time as offset changes (debounced)

#### Subtitle Track Selector
- Dropdown listing only external .srt tracks (embedded/non-srt hidden)
- Shows only originals (offset derivative files hidden)
- If an offset file exists for a track, the offset control is pre-filled with its value
- Switching tracks with unsaved changes triggers a confirmation dialog: "You have unsaved changes to the {Language} track. Discard or save first?"

#### Current Cue Display
- Text box showing the subtitle cue text closest to current playback position
- Updates as video plays or as admin seeks on timeline
- Helps admin verify sync without squinting at the video overlay

#### Offset Controls
- **Numeric input** showing current offset in ms (e.g., `+2000`)
- **Preset buttons:** -1000ms, -500ms, -100ms, +100ms, +500ms, +1000ms
- Subtitle preview updates with **400ms debounce** for buttons/input
- **"Saved ✓" badge** appears next to offset value when on-disk file matches current offset; disappears when offset is modified

#### Timeline
- Full-width horizontal timeline showing the video duration
- **Subtitle cue bars:** Colored rectangular bars showing start/end of each subtitle entry
- **Optional audio waveform:** Displayed behind the cue bars when generated
- **"Generate Waveform" button:** Visible when no waveform cached; triggers generation with progress bar + cancel button
- **Waveform auto-shown** if cache exists for this video
- **Playback position indicator:** Vertical line showing current video time

**Timeline interactions:**
- **Click on background** → Seeks video to that timestamp
- **Drag on background** → Pans the timeline horizontally (when zoomed)
- **Drag on subtitle cue bar** → Adjusts offset (snaps to 100ms increments, 1000ms debounce for preview update)
- **Scroll wheel (cursor-anchored)** → Zooms in/out (max zoom: 10 seconds visible)
- **Playback head does NOT auto-follow** when zoomed in

#### Action Buttons
- **"Save as Offset File"** — Creates `{VideoBaseName}.{lang}.Offset{±N}ms.srt`
  - Shows loading spinner → green checkmark + filename on success
- **"Replace Original"** — Overwrites the source .srt directly
  - Shows confirmation dialog: "This will permanently overwrite {filename}. This cannot be undone. Continue?"
  - On confirm: overwrites original, deletes offset file if exists, resets offset to 0
  - Shows loading spinner → success confirmation

### 5.4 Real-Time Subtitle Preview

- Custom `<div>` overlay on the video player (not `<track>` element)
- Shows the subtitle text that would be displayed at the current playback time with the current offset applied
- Updates on:
  - Video playback (continuously)
  - Offset change via buttons/input (400ms debounce)
  - Offset change via timeline drag (1000ms debounce)

### 5.5 Waveform Generation UI

- **Button:** "Generate Waveform" (shown when no cache exists)
- **Progress bar:** Shows estimated % complete + cancel button
- **On completion:** Waveform renders behind subtitle cue bars on timeline
- **On subsequent visits:** Waveform loads automatically from cache

---

## 6. Offset Logic — Always From Original

When the admin selects a subtitle track:
1. The plugin locates the **original** .srt file (non-offset)
2. If an offset file exists (`{VideoBaseName}.{lang}.Offset*.srt`), extracts the offset value from the filename and pre-fills the offset control
3. All offset calculations are always applied to the **original** file's timestamps
4. This prevents accumulated drift from repeated edits

Example flow:
- Original: `Movie.en.srt`
- Admin sets +2000ms → generates `Movie.en.Offset+2000ms.srt`
- Admin comes back, opens the track → offset control shows +2000ms
- Admin adjusts to +2500ms → generates `Movie.en.Offset+2500ms.srt` (computed from original, not from the +2000ms file)

---

## 7. Project Structure

```
jellyfin-plugin-subtitle-offset/
├── README.md
├── SPEC.md
├── Makefile
├── docker-compose.yml
├── .gitignore
├── Jellyfin.Plugin.SubtitleOffset.sln
├── docker/
│   └── media/
│       ├── Big Buck Bunny (2008).en.srt
│       ├── Big Buck Bunny (2008).es.srt
│       └── Big Buck Bunny (2008).en.malformed.srt
├── src/
│   └── Jellyfin.Plugin.SubtitleOffset/
│       ├── Jellyfin.Plugin.SubtitleOffset.csproj
│       ├── Plugin.cs
│       ├── Configuration/
│       │   ├── PluginConfiguration.cs
│       │   └── configPage.html          ← Full admin UI
│       ├── Api/
│       │   ├── SubtitleOffsetController.cs
│       │   └── WaveformController.cs
│       ├── Srt/
│       │   ├── SrtParser.cs
│       │   ├── SrtEntry.cs
│       │   ├── SrtWriter.cs
│       │   ├── SrtParseException.cs
│       │   └── EncodingDetector.cs
│       └── Services/
│           ├── OffsetFileService.cs
│           └── WaveformService.cs
├── tests/
│   ├── Jellyfin.Plugin.SubtitleOffset.Tests/
│   │   ├── Jellyfin.Plugin.SubtitleOffset.Tests.csproj
│   │   ├── SrtParserTests.cs
│   │   ├── SrtWriterTests.cs
│   │   ├── OffsetFileServiceTests.cs
│   │   └── WaveformServiceTests.cs
│   └── integration/
│       ├── requirements.txt
│       ├── conftest.py
│       ├── test_generate_offset.py
│       ├── test_replace_original.py
│       ├── test_waveform.py
│       └── test_edge_cases.py
├── scripts/
│   ├── generate-sample-video.sh
│   └── setup-jellyfin.sh
└── build/
    └── plugin/
        └── meta.json
```

---

## 8. Testing

### 8.1 Unit Tests (xUnit)

| Test | Description |
|------|-------------|
| `ParseValidSrt` | Parses a well-formed .srt and returns correct entries |
| `ParseMalformedSrt_Throws` | Returns error on invalid timestamp formats |
| `ApplyPositiveOffset` | All timestamps shifted forward correctly |
| `ApplyNegativeOffset_Clamp` | Timestamps clamped to 00:00:00,000 |
| `ApplyZeroOffset_NoChange` | Output identical to input |
| `PreservesEncoding_Utf8Bom` | BOM preserved in output |
| `PreservesEncoding_Latin1` | Non-UTF8 characters preserved |
| `OverwriteExistingOffsetFile` | Old offset file deleted, new one written |
| `DeleteOnZeroOffset` | Offset file removed when offset is 0 |
| `FileNaming_Positive` | Correct filename for +2000ms |
| `FileNaming_Negative` | Correct filename for -1500ms |
| `AlwaysComputeFromOriginal` | Offset applied to original even when offset file selected |
| `ReplaceOriginal_OverwritesFile` | Original file content replaced with offset version |
| `ReplaceOriginal_DeletesOffsetFile` | Offset derivative removed after replace |
| `WaveformGeneration_CorrectSampleCount` | 1 sample/sec for known duration |
| `WaveformCache_ReturnsFromDisk` | Cached waveform served without FFmpeg |

### 8.2 Integration Tests (pytest)

| Test | Description |
|------|-------------|
| `test_generate_creates_file` | POST creates .srt file on disk |
| `test_new_track_appears_after_refresh` | Jellyfin API shows new subtitle stream |
| `test_overwrite_previous_offset` | Second POST replaces first file and renames |
| `test_zero_offset_deletes_file` | POST with 0 removes file and track |
| `test_replace_original` | POST replaces original content, deletes offset file |
| `test_non_admin_rejected` | Non-admin user gets 403 |
| `test_embedded_subtitle_rejected` | Returns 400 for embedded track |
| `test_malformed_srt_rejected` | Returns 400 with error message |
| `test_waveform_generate` | Waveform generated and cached |
| `test_waveform_cancel` | Cancel stops FFmpeg and returns partial |
| `test_subtitle_content_endpoint` | Returns parsed SRT as JSON |

---

## 9. Local Development Environment

### Docker Compose

- **Jellyfin server** (`jellyfin/jellyfin:10.11.10`) with:
  - Plugin DLL pre-loaded in `/config/plugins/SubtitleOffset/`
  - Sample media mounted at `/media/`
  - Admin user auto-created (user: `admin`, pass: `admin`)
  - Non-admin user auto-created (user: `viewer`, pass: `viewer`)
- **Sample media:**
  - A small .mp4 video (generated test pattern via FFmpeg)
  - Multiple .srt files: well-formed (English, Spanish), and one malformed for testing
- **Build requires:** .NET 9 SDK

### Commands

```bash
make up          # Start Jellyfin with plugin + sample media
make build       # Build the plugin DLL
make test        # Run xUnit tests
make test-int    # Run integration tests (requires running Jellyfin)
make clean       # Tear down containers and remove generated files
```

---

## 10. Installation Guide (Production)

1. **Requirements:** Jellyfin 10.11 or later
2. Build the plugin: `dotnet build -c Release`
3. Copy output DLL + `meta.json` to `{JellyfinDataDir}/plugins/SubtitleOffset/`
4. Restart Jellyfin
5. Go to **Admin Dashboard → Plugins → Subtitle Sync** to access the sync tool
6. No additional configuration needed — the tool is fully self-contained

---

## 11. Non-Goals (v1)

- `.ass` / `.ssa` / `.vtt` subtitle support
- Per-user offset storage (this generates a universal file for all users)
- Automatic offset detection (e.g., audio-to-subtitle sync algorithms)
- Offset applied to embedded subtitles
- Multiple offset files per language (always overwrite)
- Audit logging
- Client-side player integration (all work done via config page)

---

## 12. Security Considerations

- All API endpoints enforce admin-only access via `[Authorize(Policy = "RequiresElevation")]`
- Plugin writes files ONLY to the directory where the video already resides (no path traversal)
- Input validation on `offsetMs` (reject absurd values, e.g., > ±600,000ms / 10 minutes)
- Filename sanitization: offset value is always formatted as integer, no user-controlled strings in filenames
- Waveform generation limited to one concurrent operation per server
- FFmpeg process killed on cancel to prevent resource leaks

---

## 13. Compatibility

| Jellyfin Version | Supported |
|-----------------|-----------|
| < 10.11 | ❌ Not supported |
| 10.11+ | ✅ Full support |

**Runtime:** .NET 9
**Dependencies:** None beyond Jellyfin's bundled FFmpeg (used for waveform generation)

---

## 14. Future Considerations (v2+)

- Support `.ass`/`.vtt` formats
- Batch offset: apply same offset to all episodes in a season
- Auto-detect offset via audio fingerprinting
- Keyboard shortcuts in the editor (←/→ for ±100ms, Shift+←/→ for ±1000ms)
- Undo history within a session
- Export/import offset presets for series
