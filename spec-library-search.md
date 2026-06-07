# Library Browser — Feature Spec

## Overview

A complementary navigation mechanism that lets admins browse through Jellyfin's library hierarchy to find items, instead of relying solely on the search bar. The browser mirrors the folder structure visible on Jellyfin's main page, using the same visual card style as search results (including images).

---

## UX Behaviour

### Visibility Rules

| Search field state | Library browser | Search results |
|---|---|---|
| Empty (initial or cleared) | **Visible** | Hidden |
| Has text (user is typing) | **Hidden instantly** | Visible |

- No animation on transition — swap is immediate.
- When the user clears the search field (backspace to empty or ×), the library browser reappears at its **last navigated position** (remembered in-memory for the session).

### Layout Position

The library browser is rendered as a distinct section **below** the search section (label + input + helper text), with its own heading: **"Browse Libraries"**.

```
┌─────────────────────────────────────────┐
│ Search library                          │
│ [________________________]              │
│ Start typing to search movies/episodes. │
│                                         │
│ ─── Browse Libraries ─────────────────  │
│ [Breadcrumb: Libraries > TV Shows]      │
│ ← Back                                  │
│ ┌─────────────────────────────────────┐ │
│ │ [spinner] Loading…                  │ │
│ │  — or —                             │ │
│ │ .result-card  Breaking Bad          │ │
│ │ .result-card  Better Call Saul      │ │
│ │ .result-card  …                     │ │
│ └─────────────────────────────────────┘ │
│ [← Prev]  Page 1 of 4  [Next →]        │
└─────────────────────────────────────────┘
```

---

## Navigation Model

### Hierarchy

The browser uses a **replace-in-place drill-down** pattern:

1. **Top level** — Video libraries only (filtered by `CollectionType`: `movies`, `tvshows`, `mixed`).
2. **Subsequent levels** — Children of the selected item, as reported by the Jellyfin `/Items` API with `ParentId`.
3. **Leaf items** — Movies or Episodes (items with no navigable children). Clicking opens the editor directly.

The depth mirrors whatever Jellyfin reports:
- Movies library → Movie (leaf)
- TV Shows library → Series → Season → Episode (leaf)
- Mixed library → may have any combination

### Breadcrumb + Back Button

- A clickable **breadcrumb trail** shows the full navigation path (e.g., `Libraries > TV Shows > Breaking Bad > Season 3`).
- Each segment in the breadcrumb is clickable and navigates directly to that level.
- A **← Back** button sits below the breadcrumb for quick one-level-up navigation.
- At the top level (libraries list), neither breadcrumb nor back button is shown.

### Item Identification (Leaf vs Folder)

An item is considered a **leaf** (opens editor on click) if it is of type:
- `Movie`
- `Episode`

All other types (`CollectionFolder`, `Series`, `Season`, `Folder`, etc.) are treated as navigable folders.

---

## Visual Design

### Item Cards

Reuse the existing `.result-card` component — identical to search results:
- Thumbnail image (primary image from Jellyfin)
- Title text
- Metadata line (year, type indicator)

For folder-type items (libraries, series, seasons), the card shows:
- The item's primary image (or a generic folder placeholder if none)
- The item name
- A subtle `›` or folder indicator to signal drill-down

### Spinner

A loading spinner is displayed **inside the browse area** whenever a navigation request is in-flight:
- Replaces the item list while loading
- Centred horizontally, compact size
- Same spinner style used elsewhere in Jellyfin admin pages (CSS-only)

### Empty State

If a level contains no children:
- Display: `"Empty folder"` centred text, muted style
- Breadcrumb and Back button remain functional

### Error State

If a request fails:
- Display: inline error message (e.g., `"Failed to load. Please try again."`)
- A **Retry** button below the message
- Breadcrumb remains intact and clickable (user can navigate to a parent level)

---

## Pagination

| Parameter | Value |
|---|---|
| Page size | 50 items |
| Controls | `← Prev` and `Next →` buttons |
| Position | Below the item list |
| Behaviour | Hidden if total items ≤ 50 |

- Page number resets to 1 when navigating to a new level.
- Current page is remembered per level if the user navigates back (since state is re-fetched, page number is reset — acceptable trade-off given no caching).

---

## Data & API

### Top-Level Libraries

```
GET /Users/{userId}/Items
  ?Fields=PrimaryImageAspectRatio
  &IncludeItemTypes=CollectionFolder
  &Recursive=false
```

Filter results client-side to only include items where `CollectionType` is one of: `movies`, `tvshows`, `mixed`, `unknown` (or null — for custom libraries that may contain video).

### Drill-Down (Children of a Folder)

```
GET /Users/{userId}/Items
  ?ParentId={itemId}
  &Fields=PrimaryImageAspectRatio
  &Recursive=false
  &StartIndex={page * 50}
  &Limit=50
  &SortBy=SortName
  &SortOrder=Ascending
```

- `SortBy=SortName` with `SortOrder=Ascending` matches Jellyfin's default display order.
- Response includes `TotalRecordCount` for pagination math.

### Image URLs

Primary image for each item:
```
/Items/{itemId}/Images/Primary?maxWidth=80&quality=90
```

Fallback: hide image container or show a generic placeholder icon.

---

## State Management

### In-Memory State

```javascript
state.libraryNav = {
  path: [],           // Array of { id, name } representing breadcrumb
  currentParentId: null,  // null = top-level libraries
  currentPage: 0,
  items: [],          // Current level's items (from last fetch)
  totalCount: 0,      // For pagination
  loading: false,
  error: null         // Error message string or null
};
```

### Persistence

- State is **session-only** (in-memory). No localStorage or server persistence.
- Returning from the editor (← Back to search) restores the last `libraryNav` state and re-fetches the current level.
- Full page reload resets to top-level libraries.

---

## Interaction Flows

### Flow 1: Initial Page Load

1. Page loads → search field is empty → library browser section is visible.
2. Immediately fetch top-level libraries (with spinner).
3. Display library cards once loaded.

### Flow 2: Drilling Down

1. User clicks a folder-type card (e.g., "TV Shows").
2. Spinner appears, current list is cleared.
3. Fetch children of clicked item.
4. Update breadcrumb: `Libraries > TV Shows`.
5. Show Back button.
6. Render child items as cards.

### Flow 3: Selecting a Leaf Item

1. User clicks a movie or episode card.
2. Library browser is hidden (editor takes over — same behaviour as search result click).
3. `state.libraryNav` is preserved in memory.

### Flow 4: Returning from Editor

1. User clicks "← Back to search".
2. Search field is empty → library browser appears.
3. Re-fetch the last level (using `currentParentId` and `currentPage`).
4. Breadcrumb is restored from `state.libraryNav.path`.

### Flow 5: Using Search While Browsing

1. User is browsing deep in a library.
2. User types in the search field → library browser disappears instantly, search results appear.
3. User clears search field → library browser reappears at its previous depth (re-fetches).

### Flow 6: Error & Retry

1. Fetch fails (network error, server error).
2. Spinner is replaced with error message + Retry button.
3. User clicks Retry → spinner appears, same request is retried.
4. Breadcrumb still works — user can click a parent level to escape.

---

## Filtering Rules

- **Only video libraries** at the top level (`CollectionType` in `[movies, tvshows, mixed, null]`).
- **No subtitle filtering** — all items are shown regardless of whether they have .srt files.
- **No search/filter within the browser** — the main search bar covers that use case.

---

## Edge Cases

| Scenario | Behaviour |
|---|---|
| Library with 0 items | Show "Empty folder", breadcrumb intact |
| Item has no primary image | Hide image container, show title only |
| User navigates very deep (5+ levels) | Breadcrumb wraps naturally (CSS `flex-wrap`) |
| Rapid clicking (double navigation) | Debounce/ignore clicks while `loading === true` |
| Session expires during navigation | Error state with retry (re-auth handled by Jellyfin's page framework) |

---

## Non-Goals

- No drag-and-drop or multi-select.
- No inline metadata editing.
- No filtering/sorting controls within the browser.
- No image lazy-loading optimisation (50 items × small thumbnails is acceptable).
- No keyboard navigation beyond standard tab/enter.
