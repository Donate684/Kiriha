# Kiriha — Карта архитектуры и устройства проекта

> **Версия:** 1.7.2+  
> **Стек:** .NET 11.0 RC2 (C# 15), Avalonia UI 12.1.4, Entity Framework Core 11 RC2 (SQLite), libmpv, TorrServer  
> **Платформа:** Windows (x64)

Документ представляет собой системную карту архитектуры приложения Kiriha для разработчиков и ИИ-ассистентов, а также сводку архитектурных инвариантов ядра.

---

## 1. Обзор решения (Solution Overview)

Kiriha — настольное приложение на Avalonia UI для каталогизации, трекинга и автоматического скробблинга аниме и манги/ранобэ (MyAnimeList, Shikimori, AniList), парсинга расписаний выхода серий, поиска релизов через Nyaa.si, потокового воспроизведения торрентов через TorrServer, мульти-провайдерного онлайн-стриминга и воспроизведения видео через встроенный `libmpv`.

### Структура проектов (.csproj)

```
Kiriha.sln
│
├── src/
│   ├── Kiriha.Core.Domain/           # Чистые доменные сущности, модели, парсеры релизов/названий (0 внешних зависимостей)
│   ├── Kiriha.Core.Abstractions/     # Контракты репозиториев, сервисов, диспетчера UI и жизненного цикла
│   ├── Kiriha.Data/                  # EF Core SQLite, миграции, репозитории и обслуживание БД (зависит только от Domain + Abstractions)
│   ├── Kiriha.Infrastructure/        # TorrServer, условный HTTP-кеш, Win32 API, Discord RPC, IPC плеера, ExternalDetection
│   ├── Kiriha.Core.Tracking/         # API-клиенты (MAL, Shiki, AniList), синхронизация списков, история, аир-инфо, франшизы, Nyaa RSS
│   ├── Kiriha.Core.Streaming/        # Онлайн-стриминг (AniLibria, AnimeLib, DreamCast, YummyAnime, Anime 365, Kodik, Aniboom, Sibnet)
│   ├── Kiriha.Mpv/                   # C# P/Invoke обертка над native libmpv-2.dll, цикл событий, рендерер OpenGL
│   ├── Kiriha.Mpv.UI/                # ViewModels и контроллеры плеера, оверлей торрент-стриминга (без ссылок на EF Core / Data)
│   └── Kiriha/                       # Главное приложение: Avalonia XAML Views, ViewModels, DI Composition, навигация
│
└── tests/
    ├── Kiriha.Tests/                 # Юнит-, архитектурные и интеграционные тесты (xUnit)
    └── Kiriha.Benchmarks/            # Бенчмарки производительности (BenchmarkDotNet)
```

---

## 2. Граф зависимостей проектов (Dependency Graph)

```mermaid
graph TD
    Domain["Kiriha.Core.Domain"]
    Abstractions["Kiriha.Core.Abstractions"]
    Data["Kiriha.Data"]
    Infrastructure["Kiriha.Infrastructure"]
    Tracking["Kiriha.Core.Tracking"]
    Streaming["Kiriha.Core.Streaming"]
    Mpv["Kiriha.Mpv"]
    MpvUI["Kiriha.Mpv.UI"]
    App["Kiriha (Executable)"]

    Abstractions --> Domain

    Data --> Domain
    Data --> Abstractions

    Infrastructure --> Domain
    Infrastructure --> Abstractions

    Tracking --> Domain
    Tracking --> Abstractions

    Streaming --> Domain
    Streaming --> Abstractions

    MpvUI --> Domain
    MpvUI --> Abstractions
    MpvUI --> Infrastructure
    MpvUI --> Mpv

    App --> Domain
    App --> Abstractions
    App --> Data
    App --> Infrastructure
    App --> Tracking
    App --> Streaming
    App --> Mpv
    App --> MpvUI
```

---

## 3. Назначение и ответственность проектов

### 3.1. `Kiriha.Core.Domain`
* **Роль:** Самый глубокий слой ядра. Не имеет внешних NuGet- или проектных зависимостей.
* **Ключевые типы:**
  - `AnimeEntity` (частичный класс): главная доменная модель тайтла (аниме, манга, ранобэ по составному ключу `(Id, MediaKind)`).
  - `AnimeEntityPresentation`: проекция данных для биндингов Avalonia UI (бейджи выхода серий, прогресс, связи франшизы, переключение русских/оригинальных названий и описаний).
  - `AppSettings`: типизированная конфигурация (`UI`, `System`, `Player`, `Torrents`, `Api`).
  - `HistoryItem`, `HistoryActionType`: запись истории действий с явным `MediaKind` и JSON-состоянием синка по трекерам (`HistoryContext` удалён).
  - `TitleMappingEntity`, `ScrobbleProgressEntity`, `PlaybackResumeEntryEntity`, `AniListMediaMapEntity`: сущности локальных состояний и маппингов в SQLite; `ShikiMetadata` использует составной ключ `(Id, MediaKind)` без битовых масок.
  - `ILocalizer` (включая `ILocalizer.Passthrough`): единый базовый контракт локализации ядра и UI.
  - `Collections/`: чистые доменные коллекции и проекции (`BulkObservableCollection`, `AnimeFilterEngine`, `AnimeCollectionProjection`).
  - `Utils/Parsing/`, `Utils/Async/`, `Utils/Classification/`: чистые детерминированные парсеры, асинхронные примитивы и классификаторы без I/O (`AnimeReleaseParser`, `AnimeParseCache`, `AnimeStringHelper`, `AnimeLibraryMatcher`, `CustomLinkResolver`, `BearerTokenHelper`, `LruStringMemoizer`, `Debouncer`, `TaskExtensions`, `DonghuaDetector`).
  - `CollectionReconciliation`, `FormatCatalog`, `GenreCatalog`, `AnimeSearchQueryParser`.

### 3.2. `Kiriha.Core.Abstractions`
* **Роль:** Узкие интерфейсы, DI-хелперы (`AddForwardedSingleton`) и контракты между слоями.
* **Группы контрактов:**
  - `Repositories/`: `IUserAnimeRepository`, `IMetadataRepository`, `IAnimeRepository` (включая потокобезопасный O(1) `FindById`), `IHistoryRepository`, `ISyncTaskRepository`, `ITitleMappingRepository`, `IAniListMediaMapRepository`, `IMalSearchCacheRepository`, `IAnimeRelationRepository`, `IHttpCacheRepository`, `ITorrentFilterRepository`, `ISeasonalHiddenRepository`, `IAnimeCountryRepository`.
  - `Services/`: `IMalApiService`, `IShikiApiService`, `IAniListApiService` (`: ITrackerService`), `ITrackerService`, `ISyncManager`, `IAnimeSyncOrchestrator`, `IAiringInfoService`, `IFranchiseService`, `IHistoryService`, `IScrobbleProgressStore`, `IPlaybackResumeStore`, `ISettingsService`, `IDatabaseInitializer`, `IRecognitionCache`, `IMappingService`, `INotificationService`, `IDiscordService`, `ILocalizer`, `ITorrServerService`, `IInternalPlayerServer`.
  - Контракты `IHistoryService`, `IHistoryRepository` и `ISyncManager` не содержат Default Interface Methods или строковых перегрузок `actionType` — все вызовы требуют явного `MediaKind` и `HistoryActionType`.
  - `Infrastructure/`: `IUiDispatcher` (включая `CheckAccess()`), `IBackgroundTaskSupervisor`, `IAppPaths`, `IBrowserLauncher`.

### 3.3. `Kiriha.Data`
* **Роль:** Изолированное хранилище данных на SQLite + EF Core (не зависит от `Kiriha.Infrastructure`).
* **Ключевые компоненты:**
  - `DataLayerRegistration`: модульная DI-регистрация `AddKirihaPersistence(...)` всего слоя персистентности.
  - `Core/`: `AppDbContext` (`QueryTrackingBehavior.NoTracking`, WAL через `SqlitePragmaConnectionInterceptor`), `DatabaseInitializer` (базовая схема от `v1.8+`, `CurrentSchemaVersion = 1810`, миграция `Wave3StorageUnification`), `DatabaseMaintenance`, `CacheCleanupService`.
  - `Repository/`: реализации репозиториев (`UserAnimeRepository`, `MetadataRepository` по составному ключу `(Id, MediaKind)`, `HistoryRepository` с сериализацией RMW-обновлений `tracker_status_json`, `SyncTaskRepository`, `TitleMappingRepository`, `AniListMediaMapRepository`, `SqliteScrobbleProgressStore`, `SqlitePlaybackResumeStore` с автоматическим однократным импортом и удалением устаревших JSON-файлов).

### 3.4. `Kiriha.Infrastructure`
* **Роль:** Интеграция с ОС, реестром, процессами, файловой системой, настройками и дисковым кешированием.
* **Ключевые компоненты:**
  - `Settings/`: `SettingsService` (секционное хранение JSON-конфигураций с DPAPI-защитой токенов).
  - `Caching/`: `SeasonalCacheStore` — атомарный дисковый кеш сезонного каталога.
  - `Diagnostics/`: `CrashReporter`, `CrashReportBuilder`, `CrashReportReader`.
  - `TorrServer/`: `TorrServerService` — запуск/подключение к `torrserver.exe`, RAM-кеш (0 байт на диске), опрос прогресса и авто-дроп.
  - `Tracking/ExternalDetection/` и `Tracking/Integration/`: обнаружение внешних плееров (MPC-HC, mpv, VLC, PotPlayer) через Win32 хэндлы окон и WASAPI, `DiscordService`, дуплексный пайп-сервер `InternalPlayerServer`.
  - `Utils/Parsing/EmberTitleResolver.cs`: резолвинг названий релизов `[Ember]` по метаданным родительской директории с мемоизацией заголовка MKV.
  - `Platform/`: `PathHelper`, `AppPathsService`, `ShellLauncher` / `ShellBrowserLauncher`, `WindowsStartupManager`, `WindowsRegistryHelper`, `SystemIntegrationService`, `AppUserModelIdRegistrar`.

### 3.5. `Kiriha.Core.Tracking`
* **Роль:** Интеграция с внешними аниме-трекерами, in-memory библиотека, синхронизация, история, метаданные, граф франшиз и распознавание тайтлов (не зависит от `Kiriha.Infrastructure` и `Kiriha.Data`).
* **Ключевые компоненты:**
  - `Services/Library/`: `AnimeRepository` — потокобезопасное in-memory хранилище библиотеки и синхронизация с UI-коллекцией.
  - `Services/Api/`: `MalApiService`, `ShikiApiService` (с общим `ShikiStateCache` и `ShikiGraphQlPosterFetcher`), `AniListApiService`, `HttpConditionalCache` (`ETag` / `304 Not Modified`).
  - `Services/Sync/`: `SyncManager`, `AnimeSyncOrchestrator` + `LibrarySyncGate`, `AnimeListActionService`, `AnimeProgressService`.
  - `Services/Tracking/Core/`: `TrackingService`, `ScrobbleService`, `HistoryService`, `AiringInfoService`.
  - `Services/Mapping/`, `Services/Franchise/`, `Services/Metadata/`: `MappingService`, `RecognitionCache`, `ManualMappingService`, `FranchiseService`, `ShikiMetadataService`.
  - `Services/Feed/`: `RssFeedService`, `NyaaFeedClient`, `NyaaTorrentParser`.

### 3.6. `Kiriha.Core.Streaming`
* **Роль:** Каталоги онлайн-просмотра аниме, авторизация провайдеров, ранжирование выдачи и извлечение видеопотоков (HLS, MP4, DASH).
* **Ключевые компоненты:**
  - `Providers/`: `AniLibriaProvider`, `AnimeLibProvider`, `DreamCastProvider`, `YummyAnimeProvider`, `Anime365Provider`.
  - `Extractors/`: `KodikVideoExtractor`, `AniboomVideoExtractor`, `SibnetVideoExtractor`, `Anime365Extractor`.
  - `Services/` и `Helpers/`: `StreamingService`, `Anime365AuthService`, `Anime365HostResolver`, `OnlineRelevanceScorer`, `UrlRedactor` (редакция токенов в логах).

### 3.7. `Kiriha.Mpv` и `Kiriha.Mpv.UI`
* **Роль:** Видеоплеер.
* `Kiriha.Mpv`: низкоуровневая обертка P/Invoke `libmpv-2.dll`, OpenGL контекст, событийный цикл плеера.
* `Kiriha.Mpv.UI`: `PlayerViewModel`, `PlayerMediaMetadataResolver`, `FilenamePlayerMediaMetadataResolver`, `FilePlaybackResumeStore` (лёгкая файловая реализация `IPlaybackResumeStore` исключительно для автономного CLI-режима `--player`), контроллеры воспроизведения, субтитров и хоткеев, оверлей торрент-стриминга (`PlayerViewModel.TorrentStreaming.cs`). Не имеет зависимостей от `Kiriha.Data` или EF Core.

### 3.8. `Kiriha` (Основное приложение)
* **Роль:** UI на Avalonia UI, композиция DI, точка входа (`Program.cs`, `App.axaml.cs`).
* **Ключевые компоненты:**
  - `Composition/`: модульная регистрация DI (`DataServicesRegistration`, `TrackingServicesRegistration`, `BackgroundServicesRegistration`, `UiServicesRegistration`).
  - `Services/AppLifecycle/Shutdown/`: упорядоченный пайплайн завершения работы:
    1. `TorrServerShutdownHandler`
    2. `PlayerResidentShutdownHandler`
    3. `HostedServicesShutdownHandler` (остановка в обратном порядке запуска до отмены `BackgroundTaskSupervisor`)
    4. `BackgroundTasksShutdownHandler`
    5. `ManualMappingShutdownHandler`
    6. `SettingsShutdownHandler`
    7. `HistoryShutdownHandler`
    8. `DatabaseShutdownHandler` (`WAL checkpoint(TRUNCATE)`)

---

## 4. Паттерны и архитектурные конвенции

1. **Регистрация сервисов в DI:** `AddForwardedSingleton<TImpl, TIface>()` исключает создание дублирующихся синглтонов.
2. **Строгая типизация и Null-Safety:** `<WarningsAsErrors>nullable</WarningsAsErrors>` включён во всём решении.
3. **Потокобезопасность библиотеки:** Вне UI-потока поиск по библиотеке выполняется через потокобезопасный индекс `IAnimeRepository.FindById(id, kind)` или `GetSnapshotAsync()`, без прямого перебора `ObservableCollection`.
4. **Статический контроль качества (`HardcodeAnalysisTests`):** Автоматически запрещены захардкоженные URL вне `Constants/`, дублирующиеся нетривиальные строковые литералы между файлами, неименованные магические таймауты и кириллица в C#-коде вне констант.

---

## 5. Реестр закрытого технического долга и легаси

* **Удалены мёртвые подсистемы и таблицы:** `SmtcService`, `AnimeGoProvider`, пакет `AngleSharp`, таблицы `episode_releases`, `episode_list_meta`, `file_recognition_cache` (миграция `CoreSchemaCleanup`), дубликаты `SmoothScroll` в `Kiriha.Mpv.UI`, зомби-поля `HiddenSeasonalIds`/`HiddenAnimeIds`/`PerTitleFilters` в `AppSettings`.
* **Выровнены границы слоёв:** `Kiriha.Data` полностью отвязан от `Kiriha.Infrastructure`; чистые парсеры перенесены в `Kiriha.Core.Domain`; `HistoryService` вынесен в `Kiriha.Core.Tracking`.
* **Устранены скрытые рантайм-дефекты:** исправлено форматирование скорости/объёма торрентов (`:0.#` / `:0.##`), сохранение `ShikiMetadata.PosterUrl` в SQLite и защита метаданных `Manga`/`LightNovel` с битовыми масками от авто-очистки, сериализация обновлений `tracker_status_json` в `HistoryRepository`, отображение `LightNovel` во вкладке манги, проброс `MediaKind` в историю и синхронизацию, устранено удержание `LibrarySyncGate` во время HTTP-запросов `AiringInfoService`.

* **Унифицированы контракты, локализация и хранилища (Волна 3):** удалён неявный `AsyncLocal`-контекст `HistoryContext`, убраны все Default Interface Methods и строковые перегрузки из `IHistoryService`/`IHistoryRepository`/`ISyncManager`; 5 разрозненных механизмов локализации (`TrackingLoc`, `LocalizationStore`, `UIUtils.GetLoc` и др.) сведены к единому DI-контракту `ILocalizer` (`LocalizationService`); битовые маски `ShikiMetadata` (`0x40000000` / `0x20000000`), россыпь JSON-файлов (`title_mappings.json`, `scrobble_progress.json`, `playback_resume.json`) и хранение маппингов AniList в `http_response_cache` заменены нормализованными таблицами SQLite с автоматической миграцией данных (`Wave3StorageUnification`).

---

## 6. Архитектурные инварианты (проверяются автотестами)

* `Kiriha.Core.Domain` не имеет внешних зависимостей.
* `Kiriha.Data` не ссылается на `Kiriha.Infrastructure`, `Kiriha.Core.Tracking` или UI-проекты.
* `Kiriha.Core.Tracking` не ссылается на `Kiriha.Infrastructure`, `Kiriha.Data` или UI-проекты.
* `Kiriha.Mpv.UI` не ссылается на `Kiriha.Data` или `Microsoft.EntityFrameworkCore`.
* Все публичные типы в библиотечных проектах (`Kiriha.Core.Abstractions`, `Kiriha.Data`, `Kiriha.Core.Tracking`, `Kiriha.Core.Streaming`, `Kiriha.Infrastructure`, `Kiriha.Mpv.UI`) строго соответствуют корневому пространству имён своей сборки.
* Контракты `IHistoryService`, `IHistoryRepository` и `ISyncManager` не содержат Default Interface Methods, а `ShikiMetadata` не использует битовые маски ID.
* Любые изменения в `/src` обязаны проходить статический анализатор `HardcodeAnalysisTests` с нулём предупреждений компилятора.
