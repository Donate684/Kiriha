# Kiriha — Карта архитектуры и устройства проекта

> **Версия:** 1.5.1+  
> **Стек:** .NET 11.0 (C# 15), Avalonia UI 12.1.3, Entity Framework Core 11 (SQLite), libmpv  
> **Платформа:** Windows (x64)

Документ представляет собой системную карту архитектуры приложения Kiriha для разработчиков и ИИ-ассистентов, а также реестр выявленного технического долга и легаси-компонентов.

---

## 1. Обзор решения (Solution Overview)

Kiriha — настольное приложение на Avalonia UI для каталогизации, трекинга и автоматического скробблинга аниме (MyAnimeList, Shikimori, AniList), парсинга расписаний выхода серий, поиска релизов через Nyaa.si и воспроизведения видео через встроенный `libmpv`.

### Структура проектов (.csproj)

```
Kiriha.sln
│
├── src/
│   ├── Kiriha.Core.Domain/           # Чистые доменные сущности, DTO, перечисления, константы (0 зависимостей)
│   ├── Kiriha.Core.Abstractions/     # Контракты репозиториев, сервисов и системных интерфейсов
│   ├── Kiriha.Infrastructure/        # Сетевые кеши, Win32 API, SMTC, Discord RPC, парсер AnitomySharp, Anisthesia
│   ├── Kiriha.Data/                  # EF Core SQLite, миграции, реализации репозиториев
│   ├── Kiriha.Core.Tracking/         # API-клиенты (MAL, Shiki, AniList), синхронизация списков, аир-инфо, Nyaa RSS
│   ├── Kiriha.Mpv/                   # C# P/Invoke обертка над native libmpv-2.dll, цикл событий, рендерер OpenGL
│   ├── Kiriha.Mpv.UI/                # ViewModels и контроллеры плеера для легковесного режима
│   └── Kiriha/                       # Главное приложение: Avalonia XAML Views, ViewModels, DI Host, навигация
│
└── tests/
    ├── Kiriha.Tests/                 # Юнит- и интеграционные тесты (xUnit)
    └── Kiriha.Benchmarks/            # Бенчмарки производительности (BenchmarkDotNet)
```

---

## 2. Граф зависимостей проектов (Dependency Graph)

```mermaid
graph TD
    Domain[Kiriha.Core.Domain]
    Abstractions[Kiriha.Core.Abstractions]
    Infrastructure[Kiriha.Infrastructure]
    Data[Kiriha.Data]
    Tracking[Kiriha.Core.Tracking]
    Mpv[Kiriha.Mpv]
    MpvUI[Kiriha.Mpv.UI]
    App[Kiriha (Executable)]

    Abstractions --> Domain
    Infrastructure --> Abstractions
    Infrastructure --> Domain

    Data --> Domain
    Data --> Abstractions
    Data --> Infrastructure

    Tracking --> Domain
    Tracking --> Abstractions
    Tracking --> Infrastructure

    MpvUI --> Domain
    MpvUI --> Abstractions
    MpvUI --> Infrastructure
    MpvUI --> Data
    MpvUI --> Tracking
    MpvUI --> Mpv

    App --> Domain
    App --> Abstractions
    App --> Infrastructure
    App --> Data
    App --> Tracking
    App --> Mpv
    App --> MpvUI
```

---

## 3. Назначение и ответственность проектов

### 3.1. `Kiriha.Core.Domain`
* **Роль:** Самый глубокий слой. Не имеет внешних зависимостей.
* **Ключевые типы:**
  - `AnimeEntity` (частичный класс в 5 файлах): главная доменная модель тайтла в библиотеке пользователя.
  - `AnimeEntityPresentation`: проекция данных для биндингов Avalonia UI (бейджи выхода серий, прогресс, связи франшизы).
  - `AppSettings`: типизированная конфигурация (`UI`, `System`, `Player`, `Torrents`, `Api`).
  - `HistoryItem`: запись истории просмотров с JSON-состоянием синка по трекерам.
  - `CollectionReconciliation`: алгоритм 3-сторонней сверки коллекций через .NET 11 LINQ `FullJoin`.
  - `FormatCatalog`, `GenreCatalog`, `AnimeSearchQueryParser`: парсинг поисковых запросов с тегами `#format`, `#genre`.

### 3.2. `Kiriha.Core.Abstractions`
* **Роль:** Интерфейсы и контракты.
* **Группы контрактов:**
  - `Repositories/`: `IUserAnimeRepository`, `IMetadataRepository`, `IAnimeRepository`, `IHistoryRepository`, `ISyncTaskRepository`, `IEpisodeReleaseRepository`, `IHttpCacheRepository`, `ITorrentFilterRepository`, `ISeasonalHiddenRepository`, `IAnimeCountryRepository`.
  - `Services/`: `IMalApiService`, `IShikiApiService`, `IAniListApiService`, `ITrackerService`, `ISyncManager`, `IAnimeSyncOrchestrator`, `IAiringInfoService`, `IFranchiseService`, `IHistoryService`, `ISettingsService`, `INotificationService`, `IDiscordService`, `ILocalizer`.

### 3.3. `Kiriha.Infrastructure`
* **Роль:** Низкоуровневые сервисы ОС и внешних библиотек.
* **Ключевые компоненты:**
  - `Tracking/Anisthesia/`: модуль обнаружения внешних плееров (MPC-HC, mpv, VLC, PotPlayer) через Win32 хэндлы окон и аудио-сессии WASAPI.
  - `Utils/Parsing/AnitomySharp/`: порт Anitomy C++ для токенизации и распознавания имен файлов аниме (номер серии, группа ансаба, разрешение).
  - `Infrastructure/Http/HttpConditionalCache.cs`: поддержка условных HTTP GET (ETag / 304 Not Modified).
  - `Platform/`: `PathHelper`, `ShellLauncher`, `WindowsStartupManager` (автозагрузка через реестр Windows).

### 3.4. `Kiriha.Data`
* **Роль:** Хранилище данных на SQLite + EF Core.
* **Ключевые компоненты:**
  - `AppDbContext`: контекст БД с отключенным трекингом по умолчанию (`QueryTrackingBehavior.NoTracking`) и WAL-режимом через `SqlitePragmaConnectionInterceptor`.
  - `DesignTimeDbContextFactory`: фабрика для миграций `dotnet ef`.
  - `Repository/`: конкретные реализации интерфейсов репозиториев (например, `UserAnimeRepository.cs` разбит на части `Progress` и `Sync`).

### 3.5. `Kiriha.Core.Tracking`
* **Роль:** Вся интеграция с внешними аниме-сервисами и синхронизация.
* **Ключевые компоненты:**
  - `Services/Api/`: клиенты `MalApiService`, `ShikiApiService`, `AniListApiService`.
  - `Services/Auth/`: OAuth-аутентификация (PKCE, токены, рефреш).
  - `Services/Sync/`: `SyncManager` (очередь фонового синка с дедупликацией и ретраями), `AnimeSyncOrchestrator` (двустороннее слияние локальной библиотеки и облачных списков), `AnimeListActionService` (быстрые операции со статусом и сериями).
  - `Services/Tracking/Core/`: `TrackingService` (пайплайн матчинга медиа и скробблинга), `AiringInfoService` (кеш и таймеры выхода серий).
  - `Services/Tracking/Feed/`: `RssFeedService`, `NyaaFeedClient`, `NyaaTorrentParser` (торрент-лента Nyaa.si).

### 3.6. `Kiriha.Mpv` и `Kiriha.Mpv.UI`
* **Роль:** Видеоплеер.
* `Kiriha.Mpv`: низкоуровневая обертка P/Invoke `libmpv-2.dll`, OpenGL контекст, захват скриншотов, событийный цикл плеера.
* `Kiriha.Mpv.UI`: `PlayerViewModel`, `PlayerPlaybackController`, настройки плеера.

### 3.7. `Kiriha` (Основное приложение)
* **Роль:** UI на Avalonia UI, композиция DI, точка входа (`Program.cs`, `App.axaml.cs`).
* **Ключевые компоненты:**
  - `Composition/`: модульная регистрация DI (`DataServicesRegistration`, `TrackingServicesRegistration`, `BackgroundServicesRegistration`, `UiServicesRegistration`).
  - `Services/AppLifecycle/`: супервизор жизненного цикла (`AppStartupCoordinator`), запуск в обычном режиме или в `--player` режиме для ассоциации файлов.
  - `ViewModels/` и `Views/`: экраны списков (`AnimeListView`), сезонника (`SeasonalView`), плеера (`PlayerWindow`), аналитики (`AnalyticsView`), торрентов (`TorrentsView`), настроек (`SettingsView`).

---

## 4. Паттерны и архитектурные конвенции

1. **Регистрация сервисов в DI:**
   - Все зависимости регистрируются в модульных классах `Composition/*Registration.cs`.
   - Если класс реализует несколько интерфейсов или нужен и по конкретному типу, и по интерфейсу, используется метод-расширение `AddForwardedSingleton<TImpl, TIface>()` или `sp.GetRequiredService<TImpl>()`, чтобы не создавать дубликатов синглтонов.
2. **Строгая типизация и Null-Safety:**
   - В проекте включен `<WarningsAsErrors>nullable</WarningsAsErrors>`. Любое предупреждение CS8600..CS8625 ломает сборку.
   - Используются фичи C# 15: `union`, `extension`, строгие сопоставления с образцом.
3. **Обмен сообщениями (Messaging):**
   - Слабосвязанные события между компонентами передаются через `CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default`.
4. **Avalonia UI:**
   - Включены скомпилированные биндинги (`AvaloniaUseCompiledBindingsByDefault = true`). Все биндинги строго типизированы через `x:DataType`.

---

## 5. Реестр технического долга и легаси (Audit & Tech Debt)


## 6. Дорожная карта оптимизации
