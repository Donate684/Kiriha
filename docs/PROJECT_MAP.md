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

В ходе начального аудита слоя `Kiriha.Core.Domain` и общей структуры решения обнаружены следующие кандидаты на чистку:

| № | Категория | Файл / Компонент | Проблема / Описание | Статус / Действие |
|---|---|---|---|---|
| 1 | **Дублирование проекта / файлов** | `src/Kiriha.Mpv.UI/Views/` vs `src/Kiriha/Views/Player/` | Вся иерархия XAML-вьюх и контролов плеера полностью дублировалась в двух проектах. Приложение `Kiriha` использует свои собственные вьюхи из `Kiriha/Views/Player/`, а из `Kiriha.Mpv.UI` берет только ViewModels. В тестах (`HardcodeAnalysisTests.cs`) был костыльный фильтр исключения `Kiriha.Mpv.UI mirror project`. | **Исправлено.** Удалены все 43 дублирующихся файла представлений/стилей/контролов из `src/Kiriha.Mpv.UI/Views/`. `Kiriha.Mpv.UI` стал чистой библиотекой ViewModels и сервисов плеера. Удалены костыльные исключения в `HardcodeAnalysisTests.cs`. |
| 2 | **Мертвый код** | `src/Kiriha.Core.Domain/Models/Entities/AnimeEntity.Notifications.cs` | Приватные методы `NotifyTitleChanged()`, `NotifySynopsisChanged()`, `NotifySeasonChanged()`, `NotifyEpisodesAiredChanged()`, `NotifyNextEpisodeChanged()`, `NotifyProgressChanges()` имели 0 вызовов. | **Исправлено.** Удалены неиспользуемые методы. |
| 3 | **Мертвые свойства** | `src/Kiriha.Core.Domain/Models/Entities/TorrentEntity.cs` | Свойства `PubDate` (дубликат `PublishDate`), `MagnetUri` (дубликат `MagnetLink`), `TorrentUrl` (дубликат `DownloadLink`), `PageUrl` имели 0 обращений. | **Исправлено.** Удалены заброшенные поля. |
| 4 | **Брошенная сущность и диалог** | `AnimeOfflineItem.cs`, `AmbiguousMatchView.axaml`, `AmbiguousMatchViewModel.cs` | Сущность с пометкой `[Table("offline_anime")]` (таблицы даже нет в `AppDbContext`). Использовалась в мертвом поле `_relatedAnime` в `AnimeDetailsViewModel` и в `AmbiguousMatchView`, который нигде не открывался. | **Исправлено.** Удалены `AnimeOfflineItem.cs`, `AmbiguousMatchView.axaml`, `AmbiguousMatchView.axaml.cs`, `AmbiguousMatchViewModel.cs`, регистрация в `ViewLocator`. |
| 5 | **Брошенные таблицы БД** | `AnimeStaff.cs`, `AnimeStaffMeta.cs` | Сущности добавлены в миграции `20260630064226_AddAnimeStaff`, объявлены в `AppDbContext`, но **никогда не используются**: нет репозитория, нет сервиса, нет данных из API, нет отображения в UI. | **Вычищено.** Сущности и конфигурации удалены из C# кода, `DbSet` убраны из `AppDbContext`, в `DatabaseInitializer` добавлен `DROP TABLE IF EXISTS` для очистки существующих БД пользователей. Запланировано полное вырезание миграций в v1.6. |
| 6 | **Несоответствие структуры файлов** | `src/Kiriha.Core.Domain/Models/GlobalStatusMessage.cs` | Файл назывался `GlobalStatusMessage.cs`, а внутри объявлен единственный рекорд `AnimeListRefreshMessage`. | **Исправлено.** Файл переименован в `AnimeListRefreshMessage.cs`. |
| 7 | **Несоответствие пространств имен** | `src/Kiriha.Core.Domain/Abstractions/ILocalizer.cs` | Лежит в `Kiriha.Core.Domain`, но пространство имен — `Kiriha.Core.Abstractions.Services`. | **Выявлено.** `AnimeEntityPresentation` зависит от `ILocalizer`, перенос в `Abstractions` создаст циклическую зависимость `Abstractions -> Domain -> Abstractions`. Зафиксировано архитектурное ограничение. |
| 8 | **Опасный / мертвый конструктор** | `src/Kiriha.Core.Tracking/Services/Tracking/Core/AiringInfoService.cs` | Конструктор с 5 параметрами передавал `null!, null!` вместо `IShikiApiService` и `ISettingsService`. | **Исправлено.** Удален мертвый конструктор с нарушением nullability. |
| 9 | **Затенение имен и псевдо-UI в ядре** | `src/Kiriha.Core.Tracking/Utils/UIUtils.cs` | Файл в трекинг-слое объявлял класс `UIUtils` в чужом пространстве имен `Kiriha.Core`, создавая ложное впечатление о зависимости бекенда от UI. | **Исправлено.** Переименован в `TrackingLoc.cs` в пространстве имен `Kiriha.Core.Tracking.Utils`. |
| 10 | **Моджибаке в комментариях** | `PlayerWindow.Controls.cs`, `Lifecycle.cs`, `PointerActions.cs` | Поврежденные символы псевдографики (`â”€â”€â”€`) в разделительных комментариях. | **Исправлено.** Очищены битые комментарии. |
| 11 | **Хардкод шрифта в 4 файлах** | `AppSettings.PlayerConfig.cs`, `SubtitleCallbacks.cs`, `SettingsLoader.cs`, `SubtitleSettings.cs` | Строковый литерал `"Lato ExtraBold"` дублировался в 4 файлах настроек субтитров. | **Исправлено.** Вынесен в `AppConstants.Player.DefaultSubtitleFont`. |
| 12 | **Несоответствие имени файла и класса** | `src/Kiriha/Services/Settings/SettingsSection.cs` | Файл назывался `SettingsSection.cs` (пережиток старого энума), но содержал `AppSettingsJsonContext : JsonSerializerContext`. | **Исправлено.** Файл переименован в `AppSettingsJsonContext.cs`. |
| 13 | **Устаревший Regex в ядре** | `src/Kiriha.Core.Tracking/Services/Api/ShikiHostResolver.cs` | Использовался рантаймовый `new Regex(..., RegexOptions.Compiled)`. | **Исправлено.** Переведено на `[GeneratedRegex]` source generator (C# 15 / .NET 11). |
| 14 | **Утечка подписки на события** | `src/Kiriha.Core.Tracking/Services/Tracking/Core/TrackingService.cs` | Подписка на `IInternalPlayerServer.PlayerStateChanged` выполнялась через анонимную лямбду и не отписывалась в `Dispose()`. | **Исправлено.** Выделен обработчик `OnPlayerStateChanged`, добавлена корректная отписка в `Dispose()`. |
| 15 | **Избыточная nullability зависимости** | `AniListApiService.cs`, `AiringInfoService.cs` | Поле `_settingsService` было объявлено как nullable (`ISettingsService?`), несмотря на обязательную передачу в DI и конструкторе. | **Исправлено.** Приведено к строгой non-nullable зависимости, устранены проверки `?`. |
| 16 | **Неверное расположение типа домена UI** | `src/Kiriha/ViewModels/Settings/AdultFilterMode.cs` | Энум `AdultFilterMode` лежал в `ViewModels/Settings`, хотя относился исключительно к фильтрации поиска (`SearchViewModel`) и вынуждал модуль поиска зависеть от пространства имен настроек. | **Исправлено.** Перенесен в `src/Kiriha/ViewModels/Search/AdultFilterMode.cs` в правильное пространство имен `Kiriha.ViewModels.Search`, убран лишний `using` из `SearchViewModel`. |
| 17 | **Неоптимальный поиск байт и Regex** | `src/Kiriha.Infrastructure/Utils/Parsing/EmberTitleResolver.cs` | Ручной 5-байтовый цикл по массиву в `ScanFileForEmber` и динамический `Regex.Replace` при каждом обращении в `GetMeaningfulDirectoryName`. | **Исправлено.** `ScanFileForEmber` переведен на SIMD-векторизованный `Span<byte>.IndexOf("EMBER"u8)`, а регулярное выражение — на source generator `[GeneratedRegex]`. |
| 18 | **Устаревший Debug.WriteLine** | `src/Kiriha.Infrastructure/Player/SonokoIntegration.cs` | Использовался `System.Diagnostics.Debug.WriteLine` вместо структурированного логирования Serilog. | **Исправлено.** Переведено на `Log.Debug(ex, "Sonoko send error")`. |
| 19 | **Дублирование кода и затенение методов** | `MainWindow.axaml.cs`, `AboutWindow.axaml.cs` | В обоих окнах был объявлен `public new void ApplyMica()`, полностью повторявший метод базового класса `KirihaWindowBase.ApplyMica()`. | **Исправлено.** Дубликаты удалены, используется наследуемый метод из `KirihaWindowBase`. |
| 20 | **Сбой жизненного цикла XAML-вьюх при навигации** | `AnimeListView.axaml.cs`, `SeasonalView.axaml.cs` | В `OnUnloaded` отписывались обработчики `ElementPrepared` и диспозился `_revealController`, но в `OnLoaded` они не восстанавливались. При возврате на экран ломалась ленивая загрузка обложек и анимации карточек. | **Исправлено.** Добавлена гарантированная подписка и пересоздание контроллеров отображения в `OnLoaded`. |
| 21 | **Устаревший бойлерплейт сообщений** | `src/Kiriha/Models/NavigationMessage.cs` | Класс сообщения навигации содержал ручной конструктор и свойство вместо современного синтаксиса рекордов C#. | **Исправлено.** Преобразован в `public sealed record NavigationMessage(NavigationPage Page);`. |
| 22 | **Нестандартизированные DI-регистрации** | `DataServicesRegistration.cs`, `BackgroundServicesRegistration.cs` | Использовался длинный синтаксис `services.AddSingleton<TIface>(sp => sp.GetRequiredService<TImpl>())` вместо существующего экстеншена `AddForwardedSingleton`. | **Исправлено.** Все 11 регистраций приведены к единообразному виду `AddForwardedSingleton<TImpl, TIface>()`. |
| 23 | **Неуправляемые фоновые задачи сервиса франшиз** | `src/Kiriha/Services/Franchise/FranchiseService.cs` | Построение индекса франшиз и цикл очереди резолва запускались через `_ = Task.Run(...)` вне контроля супервизора жизненного цикла. | **Исправлено.** Подключен `IBackgroundTaskSupervisor` с корректным graceful shutdown и отменой токенов. |
| 24 | **Антипаттерн фиктивных конструкторов для моков** | `ShikiMetadataService.cs`, `ImageCacheService.cs` | Из-за отсутствия интерфейсов классы сервисов имели конструкторы `protected ...()` с инициализацией приватных полей значениями `null!`, а методы объявлялись как `virtual` исключительно для работы Moq в `MetadataFetchMaintenanceTaskTests`. | **Исправлено.** Выделены контракты `IShikiMetadataService` и `IImageCacheService`, внедрены в сервисы и зависимые компоненты, удалены фиктивные конструкторы и `null!` хаки, зарегистрированы через `AddForwardedSingleton`, тесты переведены на мокирование интерфейсов. |
| 25 | **Лишние строковые аллокации при загрузке локализации** | `src/Kiriha/Services/Metadata/LocalizationService.cs` | Метод `LoadNamespace` вычитывал поток файла через `StreamReader.ReadToEnd()` в промежуточную строку и парсил `JsonDocument.Parse(json)` для всех 20 JSON-файлов переводов. | **Исправлено.** Переведено на прямой стриминг `JsonDocument.Parse(stream)` без промежуточных строковых буферов. |
| 26 | **Неоптимальная конкатенация и экранирование XML** | `src/Kiriha/Services/Notifications/ToastRenderer.cs` | 5 последовательных вызовов `.Replace()` в цикле по строкам уведомлений со сборкой XML через строковый оператор `+=`. | **Исправлено.** Переведено на `System.Security.SecurityElement.Escape` и `StringBuilder`. |
| 27 | **Устаревшие I/O и вызовы в OAuth и трекинге** | `OAuthHelper.cs`, `AiringInfoService.cs` | Прямой вызов `Process.Start` без перехвата исключений вместо `ShellLauncher.OpenUrl`, устаревший `WriteAsync(buffer, 0, len)`, лишний nullable-оператор `?` и неоптимальный `!toSync.Any()`. | **Исправлено.** Подключен `ShellLauncher.OpenUrl`, современный перегруз `WriteAsync(buffer, ct)`, строгая ссылка на `_settingsService` и быстрая проверка `Count == 0`. |
| 28 | **Каскадный шторм событий перерисовки в Torrents** | `TorrentsViewModel.cs`, `TorrentsViewModel.Search.cs`, `TorrentsViewModel.Grouping.cs` | При поиске 50 торрентов вызов `Torrents.Clear()` и последующий `Torrents.Add()` в цикле порождал 52 полных пересчета `RebuildGroupedTorrents()`, каждый из которых поэлементно заполнял `GroupedTorrents`. | **Исправлено.** `Torrents`, `GroupedTorrents` и `WatchingAnime` переведены на `BulkObservableCollection<T>` с атомарными вызовами `.Reset()` в один такт UI. |
| 29 | **Дублирование парсинга Anitomy XML в RSS-ленте** | `src/Kiriha.Core.Tracking/Services/Tracking/Feed/RssFeedService.Queue.cs` | 35 строк ручного парсинга XML, Anitomy-токенизации и создания `TorrentEntity` дублировали готовую логику из `NyaaTorrentParser.ParseItem(item)`. | **Исправлено.** Парсинг унифицирован через `NyaaTorrentParser.ParseItem(item)`, устранены лишние LINQ-проверки. |
| 30 | **Утечка обработчиков и остаточный моджибаке** | `TorrentsViewModel.HideMenu.cs`, XAML-представления | В `RefreshWatchingList()` старые элементы `HideMenuItems` не отписывались от `HiddenChanged`. В 6 файлах оставались поврежденные символы псевдографики (`â• `, `â”€`, `Ã—`, `â‡„`). | **Исправлено.** Добавлена отписка старых элементов при обновлении, полностью вычищены все поврежденные комментарии по всему проекту. |
| 31 | **Парсинг Regex и аллокации в резолверах метаданных плеера** | `PlayerMediaMetadataResolver.cs`, `FilenamePlayerMediaMetadataResolver.cs`, `PlayerViewModel.MetadataHandling.cs` | Повторяющийся динамический `Regex.Replace` при открытии каждого медиафайла, отсутствие `AsNoTracking()` в БД-запросах плеера и LINQ-аллокации массива символов в `NormalizeTitle`. | **Исправлено.** Регулярное выражение переведено на `[GeneratedRegex]`, в БД подключен `AsNoTracking()`, нормализация заголовка оптимизирована через `Span<char>` / `stackalloc`. |
| 32 | **Неполный контракт IHistoryService и прямые зависимости** | `IHistoryService.cs`, `HistoryViewModel.cs`, `AnalyticsViewModel.cs`, `CacheCleanupService.cs`, `HistoryShutdownHandler.cs` | Из-за отсутствия `GetHistoryAsync` и `FlushAsync` в интерфейсе 4 класса и вьюмодели зависели от конкретного `HistoryService`. Подписка на трекер выполнялась анонимной лямбдой в конструкторе. | **Исправлено.** Методы вынесены в `IHistoryService`, все потребители переведены на интерфейс, обработчик вынесен в именованный метод `OnTrackerStatusUpdated`. |
| 33 | **Холостое фоновое сканирование RSS и лишняя блокировка синхронизации** | `RssMaintenanceTask.cs`, `BackgroundServicesRegistration.cs`, `AnimeListViewModel.cs`, `AnimeListViewModel.Sync.cs` | `RssMaintenanceTask` каждые 15 минут делал сетевой запрос к Nyaa.si и парсил фид в коллекцию `TorrentItems`, которую не читала ни одна вьюха. Ручной `SyncMal()` блокировался на холостом `CheckFeedsAsync()`. | **Исправлено.** `RssMaintenanceTask` удален из DI, холостой вызов и зависимость `RssFeedService` удалены из `AnimeListViewModel`, исключен сетевой спам и ускорена синхронизация. |

---

## 6. TO DO / Дорожная карта оптимизации

### [TO DO — Версия 1.6] Полный сброс миграций БД и обязательное версионирование схемы (Schema Reset & Migration Squash)

**Контекст и цели:**
На текущий момент в `src/Kiriha.Data/Migrations` накопилось 6 исторических миграций, часть из которых создавала неиспользуемые сущности (`anime_staff`), а в `DatabaseInitializer.cs` присутствуют временные runtime DDL-костыли (`CREATE TABLE IF NOT EXISTS anime_country_origin`, блоки `ALTER TABLE history ADD COLUMN ...` в блоках `try/catch`). В версии 1.6 база данных приводится к монолитной эталонной структуре без груза старых миграций.

**План реализации для v1.6:**
1. **Вырезание всех legacy-миграций:**
   - Полное удаление файлов миграций `20260430184034_InitialCreate` ... `20260926123000_AddUserStateTables`.
   - Замена на единый чистый базовый слепок схемы (Unified Baseline Migration `InitialCreate_v1.6` или прямой вызов генерации схемы).
2. **Обязательная метка версии схемы (Forced Schema Version Marker):**
   - Использование нативного для SQLite заголовка `PRAGMA user_version = 1600;`.
   - Все новые и актуальные базы помечаются этим маркером.
3. **Автоматическое распознавание устаревших баз и принудительное пересоздание:**
   - При старте `DatabaseInitializer` проверяет значение `PRAGMA user_version;`.
   - Если файл базы уже существует, но `user_version < 1600` (старая база от версий 1.0–1.5 с легаси-структурой):
     - Логируется предупреждение: *"Detected legacy pre-1.6 database schema (version {v}). Forcing recreate with backup."*.
     - Старый файл `kiriha.db` безопасно переименовывается в `kiriha.db.pre16.bak`.
     - Создается новая чистая база с эталонной схемой v1.6.
     - Устанавливается `PRAGMA user_version = 1600;`.
     - Данные библиотеки пользователя прозрачно восстанавливаются из облачных сервисов (MyAnimeList / Shikimori / AniList) при первой фоновой синхронизации без потери пользовательских списков.
4. **Устранение runtime DDL-хаков:**
   - Таблица `anime_country_origin` вносится как полноценная сущность `AnimeCountryOrigin` в EF Core и `AppDbContext`.
   - Колонки `tracker_status_json` и `poster_url` для `history` переносятся в конфигурацию `HistoryItemConfiguration`.
   - Из `DatabaseInitializer.cs` полностью удаляются вызовы `ExecuteSqlRawAsync` с `ALTER TABLE` и `CREATE TABLE IF NOT EXISTS`.

