# Kiriha — AI Agent Reference & Instructions

Welcome to the **Kiriha** repository. This document provides core operational context for AI assistants.

For the complete architectural breakdown, dependency graph, and legacy/tech-debt registry, refer to [PROJECT_MAP.md](file:///e:/Projects/Kiriha/docs/PROJECT_MAP.md).

---

## 1. Project Tech Stack & Rules

* **Target Framework:** `.NET 11.0` (C# 15)
* **UI Framework:** `Avalonia UI 12.1.6` with Fluent theme and compiled bindings enabled (`x:DataType` is required on views).
* **Database:** `SQLite` with EF Core 11 (DbContextFactory pattern, `NoTracking` by default, WAL mode via custom interceptor).
* **Video Player:** Native `libmpv-2.dll` (located in `subprojects/mpv/`), OpenGL rendering integration.
* **Torrent Streaming:** Integrated `TorrServer` (`torrserver.exe` managed via `scripts/download-torrserver.ps1` or external instance), in-memory BitTorrent streaming with direct `libmpv` playback.
* **Compiler Rules:**
  * `<WarningsAsErrors>nullable</WarningsAsErrors>` is enabled. **Zero nullable warnings allowed.**
  * `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` is active.
  * Do NOT break existing tests or remove XML comments explaining non-obvious domain logic.

---

## 2. Engineering Standards & AI Ergonomics (Goldilocks Zone 150 – 400 Lines)

### 2.1. The Goldilocks Zone (150 – 400 Lines per File)
- ⚠️ **MANDATORY FOR ALL CODE GENERATION, REFACTORING & ARCHITECTURAL DESIGN:**
  - Every class, helper, parser, and ViewModel must ideally target **150 to 400 lines of code**.
  - **Key Invariants & AI Benefits:**
    1. **1 File = 1 Domain Responsibility (SRP):** Each file focuses on a single distinct concern (e.g. tokenization, regex matching, provider stream extraction, scoring, layout calculation).
    2. **Single-Call Full Read (`read_file`):** The entire file fits completely within a single read call without truncation, chunking, or pagination. Context is 100% visible, eliminating context drift and hallucinations.
    3. **Exhaustive Scope Context:** All private helpers, invariants, and local state needed by a method are visible together in the same compact scope.
    4. **Surgical Diff Modification:** Allows precise, error-free diffs (`edit_file`) without risk of search-target ambiguity or accidental collateral damage across large unrelated blocks.
    5. **Zero Micro-Splitting (Avoid Anti-Pattern):** Do **NOT** splinter cohesive logic into dozens of trivial 10-line files. Group related domain logic together so the class remains meaningful and self-contained within the 150–400 line band.
- **Strict Superfile Prohibition (God Objects > 600 lines, strictly forbidden > 1,000 lines):**
  - Monolithic helpers (e.g. `AnimeReleaseParser`, `AnimeLibraryMatcher`, `FranchiseLayoutEngine`) must be decomposed into domain modules (`Tokens/`, `Scoring/`, `Matching/`, `Layout/`). The original class remains a lightweight facade (<100 lines) preserving all public signatures for 100% backward compatibility.
  - Large ViewModels must be partitioned into SRP partial classes by functional domain (`<Name>ViewModel.<Domain>.cs`, e.g. `.Online.cs`, `.Filters.cs`, `.Search.cs`, `.Auth.cs`).

### 2.2. Modern C# 15 & .NET 11 Idioms
- **Collection Expressions:** Use `[...]` instead of `new List<T>()`, `new[] { ... }`, or `Array.Empty<T>()`.
- **Frozen Collections:** Always use `FrozenSet<T>` and `FrozenDictionary<K, V>` for static/immutable lookup tables (e.g. genres, Chinese/Korean studio classifications, tag prefixes) to ensure zero-allocation reads.
- **Primary Constructors:** Use primary constructors for dependency injection across services, controllers, and repositories where clean.
- **Pattern Matching & Switch Expressions:** Prefer switch expressions and pattern matching over deep `if / else` chains.
- **Null Safety:** Strictly honor nullable reference types. Always handle nullable returns with explicit fallbacks or guards rather than using `!`.

---

## 3. UI/UX & Avalonia Standards

1. **Compiled Bindings:** Always specify `x:DataType` on root views, styles, and DataTemplates.
2. **TitleBar Drag vs. PointerPressed:**
   - **NEVER** put `PointerPressed="OnTitleBarPressed"` on the root `<Grid>` or root `<Window>`. It intercepts pointer events from children and breaks `ComboBox`, `ColorPicker`, and popup dropdowns.
   - `PointerPressed="OnTitleBarPressed"` must ONLY be declared on the dedicated top titlebar element.
3. **DataGrid & CheckBox Alignment Standards:**
   - Never add horizontal margins inside `DataGridTemplateColumn.CellTemplate` (causes double indentation).
   - Centered discrete metadata columns (status, type, size, episode numbers).
   - Text columns left-aligned with `TextTrimming="CharacterEllipsis"`.
   - Star-sizing on expandable text columns to eliminate trailing blank gray space.
4. **ListBox & ScrollViewer Gutters:**
   - Always provide an explicit right margin (12–14px) on the rightmost element in item templates to prevent overlay scrollbars from covering text.

---

## 4. Solution Layout Quick Reference

```
Kiriha.sln
├── src/Kiriha.Core.Domain/        # Domain entities, value objects, formats, genres (0 dependencies)
├── src/Kiriha.Core.Abstractions/  # Contracts for repositories, services, UI dispatcher, ITorrServerService
├── src/Kiriha.Infrastructure/     # ExternalDetection (player detection), TorrServer, HTTP cache, Win32
├── src/Kiriha.Data/               # EF Core DbContext, migrations, SQLite repositories
├── src/Kiriha.Core.Tracking/      # MAL / Shiki / AniList API, sync manager, airing info, Nyaa RSS
├── src/Kiriha.Core.Streaming/     # Online anime streaming (AniLibria, AnimeLib, DreamCast, YummyAnime, Anime 365, Kodik, Aniboom, Sibnet)
├── src/Kiriha.Mpv/                # Native libmpv interop, OpenGL rendering
├── src/Kiriha.Mpv.UI/             # Player ViewModels, torrent streaming & state controllers
└── src/Kiriha/                    # Main Avalonia application, ViewModels, Views, Composition (DI)
```

---

## 5. Critical Architectural Conventions

1. **Dependency Injection:**
   * Registrations reside in `src/Kiriha/Composition/*Registration.cs` (or project-level `*Registration.cs`).
   * When registering a class under multiple interfaces, use `AddForwardedSingleton<TImpl, TIface>()` or `sp.GetRequiredService<TImpl>()` to avoid duplicate singletons.
2. **Two Application Modes:**
   * Main App: Runs the full desktop tracker interface.
   * Player Mode (`--player <file>`): Spawns a lightweight player window without database/sync overhead.
3. **Player Architecture:**
   * `src/Kiriha.Mpv.UI`: Contains player ViewModels and state controllers (playback, hotkeys, subtitles, IPC client).
   * `src/Kiriha/Views/Player/`: Contains active Avalonia XAML views and controls for the player. All player UI modifications belong here.
4. **Torrent Streaming (TorrServer):**
   * Managed via `ITorrServerService` (`src/Kiriha.Infrastructure/TorrServer/`).
   * Runs as a managed child process or connects to an existing instance. Configured for pure RAM streaming cache (0 disk bytes).
   * Direct streaming to `libmpv` via loopback HTTP; player overlay tracks buffer, peer count, and download speed in real-time (`PlayerViewModel.TorrentStreaming.cs`), with graceful termination via `TorrServerShutdownHandler`.
5. **Online Anime Streaming & UI Integration:**
   * Multi-provider search and stream extraction reside in `src/Kiriha.Core.Streaming/`.
   * Dual Torrents/Online search toggle is persisted via `AppSettings.TorrentConfig.IsOnlineMode` across restarts.
   * Relevance scoring (`OnlineRelevanceScorer`) ranks exact and close title matches above fuzzy/junk results.
   * Provider badges in search results strictly adhere to `GroupChip` styling (clean typography, no icons, 5px radius, 6,2 padding, themed `AnalyticsSection*Brush`). Default online source is AniLibria only (`1/5`).

---

## 6. Verification Commands

* **Compile Solution:**
  ```powershell
  dotnet build Kiriha.sln -c Debug
  ```
* **Run Unit Tests:**
  ```powershell
  dotnet test Tests/Kiriha.Tests/Kiriha.Tests.csproj -c Debug
  ```
