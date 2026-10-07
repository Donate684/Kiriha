# Kiriha — AI Agent Reference & Instructions

Welcome to the **Kiriha** repository. This document provides core operational context for AI assistants.

For the complete architectural breakdown, dependency graph, and legacy/tech-debt registry, refer to [PROJECT_MAP.md](file:///e:/Projects/Kiriha/docs/PROJECT_MAP.md).

---

## 1. Project Tech Stack & Rules

* **Target Framework:** `.NET 11.0` (C# 15)
* **UI Framework:** `Avalonia UI 12.1.3` with Fluent theme and compiled bindings enabled (`x:DataType` is required on views).
* **Database:** `SQLite` with EF Core 11 (DbContextFactory pattern, `NoTracking` by default, WAL mode via custom interceptor).
* **Video Player:** Native `libmpv-2.dll` (located in `subprojects/mpv/`), OpenGL rendering integration.
* **Compiler Rules:**
  * `<WarningsAsErrors>nullable</WarningsAsErrors>` is enabled. **Zero nullable warnings allowed.**
  * Do NOT break existing tests or remove XML comments explaining non-obvious domain logic.

---

## 2. Solution Layout Quick Reference

```
Kiriha.sln
├── src/Kiriha.Core.Domain/        # Domain entities, value objects, formats, genres (0 dependencies)
├── src/Kiriha.Core.Abstractions/  # Contracts for repositories, services, UI dispatcher
├── src/Kiriha.Infrastructure/     # Anisthesia (player detection), AnitomySharp, HTTP cache, Win32
├── src/Kiriha.Data/               # EF Core DbContext, migrations, SQLite repositories
├── src/Kiriha.Core.Tracking/      # MAL / Shiki / AniList API, sync manager, airing info, Nyaa RSS
├── src/Kiriha.Mpv/                # Native libmpv interop, OpenGL rendering
├── src/Kiriha.Mpv.UI/             # Player ViewModels & state controllers
└── src/Kiriha/                    # Main Avalonia application, ViewModels, Views, Composition (DI)
```

---

## 3. Critical Architectural Conventions

1. **Dependency Injection:**
   * Registrations reside in `src/Kiriha/Composition/*Registration.cs` (or project-level `*Registration.cs`).
   * When registering a class under multiple interfaces, use `AddForwardedSingleton<TImpl, TIface>()` or `sp.GetRequiredService<TImpl>()` to avoid duplicate singletons.
2. **Two Application Modes:**
   * Main App: Runs the full desktop tracker interface.
   * Player Mode (`--player <file>`): Spawns a lightweight player window without database/sync overhead.
3. **Player Architecture:**
   * `src/Kiriha.Mpv.UI`: Contains player ViewModels and state controllers (playback, hotkeys, subtitles, IPC client).
   * `src/Kiriha/Views/Player/`: Contains active Avalonia XAML views and controls for the player. All player UI modifications belong here.

---

## 4. Verification Commands

* **Compile Solution:**
  ```powershell
  dotnet build Kiriha.sln -c Debug
  ```
* **Run Unit Tests:**
  ```powershell
  dotnet test Tests/Kiriha.Tests/Kiriha.Tests.csproj -c Debug
  ```
