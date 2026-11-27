# Z-уровни Starborne Troopers — перенос в ST14 и оптимизация (v2)

Дата: 15.11.2026
Ветка: `st14-zlevel-optimization` (локальная, поверх v1 — см. `docs/ST14-ZLevels-Optimization.md`). Ничего не закоммичено и не запушено. Переносы через `git mv`, поэтому 147 переименований лежат в индексе как «R» (staged), а все правки содержимого — unstaged.
Движок (RobustToolbox) не изменялся.

## 1. Кратко

- Z-уровни теперь наши: весь код и ресурсы перенесены из `_CE` в `_ST14/ZLevels`, все имена `CE*` / `ce-` / `ce.` стали `ST14*` / `st14-` / `st14.`. Папок `_CE` в проекте больше нет.
- Лицензия: код CrystallEdge распространялся под **MIT**. Поэтому перенесённые файлы не могут стать «All rights reserved» (раздел 1 нашего `LICENSE.TXT`). В каждую папку `_ST14/ZLevels` положен свой `LICENSE.TXT` с текстом MIT и исключением из раздела 1 (так же, как отдельная лицензия лежит у `_ST14/Construction`). Подробности в разделе 3.
- Новые оптимизации, которые меняют внутреннее устройство:
  - кэш высоты земли по клеткам с полной инвалидацией;
  - игнор поворотов на месте;
  - ленивый список активных тел;
  - один `DirtyField` за `Update` вместо одного на подшаг;
  - сеттеры не грязнят неизменённое поле;
  - кэш клиентского скана плиток для отрисовки нижних уровней;
  - синк Z-гридов не телепортирует гриды, которые уже стоят на месте;
  - лишний `SetOffset` у статус-эффектов.
- **Шаг Z-физики** (`Update`): −44…47 % к v1. От исходного CE-кода (до v1): **−69…72 %** (16.43 → 5.12 мс на 3000 тел).
- **Клиент, проверка «видно ли нижний уровень»:** 152.6 → 1.0 мкс на уровень за кадр, примерно в 150 раз быстрее, если игрок стоит.
- **Геймплей сохранён:**
  - хэш детерминированной трассы (300 тел, 240 тиков, толчки, прыжки, падения) по-прежнему `5DDB3AC5C57063A4` — тот же, что и у исходного CE-кода;
  - новый тест кэша земли проходит;
  - 87/87 связанных интеграционных тестов;
  - YAML-линтер без ошибок.

## 2. Переименование

### 2.1 Папки (через `git mv`, история сохранена)

| Было | Стало |
|---|---|
| `Content.Shared/_CE/ZLevels` | `Content.Shared/_ST14/ZLevels` |
| `Content.Server/_CE/ZLevels` | `Content.Server/_ST14/ZLevels` |
| `Content.Server/_CE/AutoTilePlacement` (нужен только лестницам) | `Content.Server/_ST14/ZLevels/AutoTilePlacement` |
| `Content.Client/_CE/ZLevels` | `Content.Client/_ST14/ZLevels` |
| `Resources/Prototypes/_CE` | `Resources/Prototypes/_ST14/ZLevels` |
| `Resources/Textures/_CE` | `Resources/Textures/_ST14/ZLevels` |
| `Resources/Locale/en-US/_CE/zLevel` | `Resources/Locale/en-US/_ST14/zLevels` |
| `Resources/Locale/ru-RU/_CE/zLevel` | `Resources/Locale/ru-RU/_ST14/zLevels` |

Файлы `CE*.cs` переименованы в `ST14*.cs`. Ещё два особых случая: `CCvars.CEZLevels.cs` → `CCvars.ST14ZLevels.cs`, `ScalingViewport.CEZLevels.cs` → `ScalingViewport.ST14ZLevels.cs`.

### 2.2 Код и данные

| Что | Было | Стало |
|---|---|---|
| Пространства имён | `Content.{Shared,Server,Client}._CE.ZLevels…` | `Content.{Shared,Server,Client}._ST14.ZLevels…` |
| Пространство имён AutoTilePlacement | `Content.Server._CE.AutoTilePlacement` | `Content.Server._ST14.ZLevels.AutoTilePlacement` |
| Классы, компоненты, системы, события (136 идентификаторов) | `CE…` | `ST14…` |
| Примеры | `CEZLevelsSystem`, `CESharedZLevelsSystem`, `CEClientZLevelsSystem`, `CEZPhysicsComponent`, `CEZMapComponent`, `CEZMapNetworkComponent`, `CEZLevelViewerComponent`, `CEZGridSyncSystem`, `CEZLevelMapMoveEvent` | `ST14ZLevelsSystem`, `ST14SharedZLevelsSystem`, `ST14ClientZLevelsSystem`, `ST14ZPhysicsComponent`, `ST14ZMapComponent`, `ST14ZMapNetworkComponent`, `ST14ZLevelViewerComponent`, `ST14ZGridSyncSystem`, `ST14ZLevelMapMoveEvent` |
| Коллизия имени | `CEWeatherCommand` (команда `znetwork-weather`) | `ST14ZNetworkWeatherCommand` — `ST14WeatherCommand` уже был в `_ST14/Weather` |
| Имена компонентов в YAML | `CEZPhysics`, `CEZLevelHighGround`, `CEZLevelGhostMover`, `CEAutoTilePlacement`, … | `ST14ZPhysics`, `ST14ZLevelHighGround`, `ST14ZLevelGhostMover`, `ST14AutoTilePlacement`, … |
| ID прототипов сущностей (37 шт.) | `CEZLevelLadderStone`, `CEZLevelsLadderRoofBlue`, `CEActionToggleLookUp`, `CEZLevelEye`, `CEDustEffect`, … | `ST14ZLevelLadderStone`, `ST14ZLevelsLadderRoofBlue`, `ST14ActionToggleLookUp`, `ST14ZLevelEye`, `ST14DustEffect`, … |
| Шейдеры | `CEZBlur`, `CEZFog` | `ST14ZBlur`, `ST14ZFog` |
| Пути ресурсов | `_CE/Actions/misc.rsi`, `_CE/Structures/Architecture/Ladders/*.rsi`, `_CE/Shaders/zblur.swsl`, … | `_ST14/ZLevels/…` |
| Ключи локализации (8) | `ce-zlevel-voice-from-up`, `ce-zlevel-voice-from-down`, `ce-zlevels-safe-fall-amulet-crack`, `ce-floor-tile-wrong-tool`, `ce-floor-tile-tool-separator`, `ce-ceiling-tile-obstructed`, `ce-soft-landing`, `ce-soft-landing-too-high` | `st14-…` (то же окончание) |
| Консольные команды | `znetwork-*`, `showzleveldebug` | без изменений: CE-префикса у них не было |
| Тип прототипа | `zMap` | без изменений |

CVar-ы (13):

| Было | Стало |
|---|---|
| `zlevels.ce_base_falling_damage` | `st14.zlevels.base_falling_damage` |
| `zlevels.ce_base_falling_other_damage` | `st14.zlevels.base_falling_other_damage` |
| `zlevels.ce_base_falling_stun_time` | `st14.zlevels.base_falling_stun_time` |
| `zlevels.ce_base_falling_other_stun_time` | `st14.zlevels.base_falling_other_stun_time` |
| `zlevels.ce_physics.tick_rate` | `st14.zlevels.physics.tick_rate` |
| `zlevels.ce_physics.client_simulation` | `st14.zlevels.physics.client_simulation` |
| `ce.zlevels.physics.gravity_force` | `st14.zlevels.physics.gravity_force` |
| `ce.zlevels.physics.velocity_limit` | `st14.zlevels.physics.velocity_limit` |
| `ce.zlevels.physics.impact_velocity` | `st14.zlevels.physics.impact_velocity` |
| `ce.zlevels.rendering.max_zLevels_below_rendering` | `st14.zlevels.rendering.max_zLevels_below_rendering` |
| `ce.zlevels.vertical_distance_scale` | `st14.zlevels.vertical_distance_scale` |
| `ce.zlevels.audio.per_level_attenuation_db` | `st14.zlevels.audio.per_level_attenuation_db` |
| `ce.zlevels.audio.floor_occlusion` | `st14.zlevels.audio.floor_occlusion` |

В конфигах репозитория (`*.toml`, ConfigPresets) старые имена не встречались. Если на сервере или у игроков они были выставлены вручную, их надо переименовать: архивные клиентские (`tick_rate`, `client_simulation`, `audio.*`) вернутся к значениям по умолчанию.

### 2.3 Карты и миграция

- Ни одна карта в `Resources/Maps` не содержит CE-компонентов и CE-прототипов (проверено поиском). Z-сети создаются командами `znetwork-*`, а не картами. Поэтому править карты не понадобилось.
- Для карт, сохранённых вне репозитория (например, через `znetwork-save`), в `Resources/migration.yml` добавлены 29 строк `CE<Id>: ST14<Id>` для всех неабстрактных прототипов Z-уровней (лестницы, действия, глаз, эффект пыли). Абстрактные базы туда не попали.
- Миграции имён компонентов в RobustToolbox нет. Если где-то есть сохранённая карта с `type: CEZPhysics` и т.п., её нужно поправить заменой текста `CEZ` → `ST14Z`. В репозитории таких карт нет.

### 2.4 Файлы вне `_ST14/ZLevels`, которые пришлось поправить

| Файл | Что |
|---|---|
| `Content.Client/Parallax/ParallaxOverlay.cs` | `using` и тип системы (`CESharedZLevelsSystem` → `ST14SharedZLevelsSystem`), строки уже были помечены `// ST14` |
| `Resources/Prototypes/Entities/Mobs/base.yml`, `Objects/base_item.yml`, `Structures/base_structure.yml` | `type: CEZPhysics` → `ST14ZPhysics` (по одной строке) |
| `Resources/Prototypes/Entities/Mobs/Player/observer.yml` | `CEZPhysics`, `CEZLevelGhostMover` → `ST14…` |
| `Resources/Prototypes/_ST14/Entities/Mobs/Bugs/base.yml` | **это твой файл с незакоммиченными правками**; в нём заменена ровно одна строка `type: CEZPhysics` → `type: ST14ZPhysics`, остальное не тронуто. Без этой замены прототип жуков не загрузится |
| `Resources/migration.yml` | +29 строк миграции |
| `Content.IntegrationTests/Tests/_ST14/ST14ZLevelPerfTest.cs` | новые имена и новые тесты |

Остальные из 849 твоих изменённых файлов не тронуты.

## 3. Лицензия

- В шапке почти всех файлов CE стоит: «This file is sublicensed under MIT License» со ссылкой на MIT-лицензию SS14. Все 91 C#-файл пришли в проект одним коммитом `4008226e0f` («Z layers (#18)»), поэтому 5 файлов без шапки тоже считаю частью того же MIT-кода. Им добавлена короткая шапка «Based on CrystallEdge z-levels (MIT License)».
- MIT разрешает менять, переименовывать, сублицензировать и использовать код в своём проекте (в том числе коммерческом). Условие одно — сохранять уведомление об авторском праве и текст лицензии. Объявить этот код «All rights reserved» нельзя: на сам код CE и любые его копии MIT продолжает действовать.
- Что сделано: в `Content.{Shared,Server,Client}/_ST14/ZLevels/` и `Resources/Prototypes/_ST14/ZLevels/` лежит `LICENSE.TXT`. В нём:
  - откуда код;
  - явное исключение из раздела 1 корневого `LICENSE.TXT`;
  - полный текст MIT с правообладателями «CrystallEdge contributors» и «Denlero and Starborne Troopers contributors (modifications)»;
  - то же самое по-русски.
- Ассеты (`Resources/Textures/_ST14/ZLevels`) остаются под своими лицензиями из `meta.json`. Это CC-BY-SA-3.0 (dust: TheShuEd) и CC-BY-SA-4.0 (иконки действий: TheShuEd; лестницы: TheShuEd, Vojner; крыши: omsoyk). Раздел 3 нашего `LICENSE.TXT` это прямо допускает.
- ⚠ **Найдено попутно:** корневой `LICENSE.TXT` (раздел 2) ссылается на `LICENSE-MIT.TXT`, но такого файла в репозитории нет. Для MIT-кода SS14 текст лицензии формально должен лежать рядом. Я этот файл не создавал — решать тебе. Свои папки Z-уровней я закрыл, вписав текст MIT прямо в их `LICENSE.TXT`.

## 4. Как мерил

Тест тот же — `Content.IntegrationTests/Tests/_ST14/ST14ZLevelPerfTest.cs`, переименованный и дополненный:

- `GroundCacheInvalidationTest` (новый, обычный, идёт в CI). Тело стоит на полу, пол под ним убирают — тело должно упасть. Тело ходит внутри клетки — не должно падать. Под стоящее тело ставят ступень высотой 0.5 — земля должна стать 0.5, и тело должно подняться.
- В `ServerHotPathTiming` добавлен замер **turn+zstep**: поворот всех N тел на месте (`SetLocalRotation`) плюс шаг Z-физики. Так ведут себя игроки, которые всё время поворачиваются за мышью.
- `ClientTileScanTiming` (новый, `[Explicit]`). Экран примерно 30×17 клеток полностью закрыт непрозрачным полом — худший случай, когда читаются все клетки. Скан без кэша — это старый алгоритм `TryFindEmptyTiles`, с кэшем — текущее поведение, пока вид не сдвинулся на клетку. Тест также проверяет, что после смены плитки кэш сбрасывается.

«v2-до» — код после v1 и переименования, без новых оптимизаций. Снят 3 прогонами; turn+zstep снят отдельной серией на сохранённом снимке того же кода. «v2-после» — финальный код, 3 прогона. В таблицах медианы. Железо то же: Ryzen 5 5500, 32 ГБ, Windows 10, .NET 10.0.401, Release.

## 5. Результаты

### Сервер (медиана из 3 прогонов)

| Замер | N | Исходный CE | v1 | v2-до | v2-после | v2 к v2-до | Итого к CE |
|---|---|---|---|---|---|---|---|
| zstep, мс | 1000 | 8.07 | 3.67 | 4.27 | **2.26** | **−47 %** | **−72 %** |
| zstep, мс | 3000 | 16.43 | 8.63 | 9.10 | **5.12** | **−44 %** | **−69 %** |
| turn+zstep, мс | 1000 | — | — | 9.06 | **6.09** | **−33 %** | — |
| turn+zstep, мс | 3000 | — | — | 22.39 | 21.68 | −3 % (шум) | — |
| move (сдвиг всех тел), мс | 1000 | 10.64 | 6.33 | 7.01 | 6.79 | ≈0 | −36 % |
| move, мс | 3000 | 34.89 | 20.59 | 21.50 | 22.11 | ≈0 (шум) | −37 % |
| tiles 1024, мс/вызов | 1000 | 7.35 | 5.93 | 6.24 | 6.25 | ≈0 | — |
| tiles 1024, мс/вызов | 3000 | 3.69* | 7.27 | 7.88 | 8.33 | ≈0 (шум) | — |
| tiles 1024, память/вызов | 1000/3000 | 162 672 / 211 344 Б | 67 184 Б | 67 184 Б | 67 184 Б | 0 | −59 / −68 % |

\* Аномалия базового замера v1 — разобрана в отчёте v1.

Пояснения:
- **zstep** — главный выигрыш v2: кэш высоты земли (не пересчитывается при движении внутри клетки) плюс ленивый список и батч `DirtyField`.
- **turn+zstep** при N=1000 −33 %: повороты больше не ставят тело в очередь пересчёта. При N=3000 разница в шуме: почти всё время уходит на движковый `SetLocalRotation` (события перемещения, обновление lookup), а это не наш код.
- **move и tiles** почти целиком — стоимость движка (`SetWorldPosition`, `SetTiles`), поэтому v2 их почти не сдвинул. В tiles теперь дополнительно проверяется уровень выше (это исправление, см. 6.1), а время не выросло: этот поиск оплачен переходом на один общий lookup для больших пачек плиток.
- Аллокации zstep (394 / 660 Б) не изменились — их делает не код Z-уровней.

### Клиент

| Замер | v2-до (без кэша) | v2-после (с кэшем) |
|---|---|---|
| Скан «видно ли уровень ниже», экран закрыт полом, мкс на уровень за кадр | 152.6 (127.3 / 152.6 / 167.1) | **1.0** (0.98 / 1.00 / 1.14) |
| pre+post anim pass, 3000 спрайтов, мс/кадр | 1.60 / 1.71 (2 прогона) | 1.65 (1.41 / 1.65 / 1.69) |

Скан вызывается каждый кадр для каждого уровня ниже, пока не найдётся прозрачная клетка. Когда игрок стоит на сплошном полу, раньше каждый кадр читалось ~600 клеток, теперь — сравнение ключа. Пересчёт происходит, только когда вид сдвинулся на клетку или где-то изменилась плитка.

Проход по спрайтам в headless без изменений — честно, в пределах шума. По одному прогону клиентского замера в сериях v1-до и v2-до упали при создании тестовой пары, ещё до замеров (через 7–9 с). Повторно это не воспроизводилось; причина — нестабильность пула пар тестов, а не код.

## 6. Оптимизации v2 по пунктам

### 6.1 Кэш высоты земли по клеткам
**Файлы:** `ST14SharedZLevelsSystem.Movement.cs`, `ST14ZPhysicsComponent.cs`, `ST14ZMapComponent.cs`, `Content.Server/_ST14/ZLevels/Core/ST14ZLevelsSystem.Maps.cs`

**Проблема.** На каждое перемещение каждого тела `RequestCacheMovement` вызывался с `force = true` и полностью пересчитывал высоту земли:
- поиск грида на карте;
- перебор заякоренных сущностей клетки;
- при отсутствии пола — переход на карту ниже и ещё один поиск.

Ходьба по ровному полу пересчитывала одно и то же каждый тик.

**Что сделано.** Высота «плоской» земли (пол, дыра, отсутствие карты ниже) кэшируется по ключу «клетка + грид/карта + эпоха». Пока тело в той же клетке того же грида, пересчёта нет. Высокая земля (лестницы, ступени) зависит от точной позиции в клетке и от вертикальной скорости (липкость), поэтому она помечается `CachedGroundDynamic` и пересчитывается при каждом движении, как раньше. Инвалидация:
- смена плиток на Z-карте → пересчёт тел на этих клетках этой карты **и карты выше** (тело над дырой стоит на полу нижнего уровня). Для пачек больше 32 плиток — один lookup по общему прямоугольнику и фильтр по клетке, а не lookup на каждую плитку; поиск сразу по компоненту `ST14ZPhysics`;
- ступень появилась, заякорена или раскреплена, повернулась → пересчёт тел на этой клетке и над ней;
- ступень удалена, Z-карта добавлена или удалена, пришло её состояние, сервер изменил состав сети, сменились плитки любого не-Z грида → «эпоха» кэша++, и каждое тело пересчитает землю при следующем движении.

**Что изменилось в поведении.**
- Тела, которые ходят по ровному полу, получают ту же высоту земли, что и раньше. Трасса совпадает бит в бит, хэш тот же.
- Ступень, поставленная под **стоящее** тело, теперь сразу поднимает его. Раньше — только после того, как тело сдвинется.
- Смена пола на нижнем уровне теперь сразу обновляет тела над дырой на верхнем уровне. Раньше — только после движения.
- Удаление ступени под стоящим телом работает как раньше: земля обновится при следующем движении.

### 6.2 Повороты на месте не трогают Z-физику
**Файл:** `ST14SharedZLevelsSystem.Movement.cs` (`OnMoveEvent`)

**Проблема.** `MoveEvent` приходит и при чистом повороте (игрок поворачивается за мышью каждый кадр). Каждый поворот ставил тело в очередь: пересчёт земли + `RefreshBody`.

**Что сделано.** Если родитель и позиция не изменились (`NewPosition == OldPosition`), событие игнорируется.

**Что изменилось.** Высота земли от поворота тела не зависит — ступени учитывают поворот самой ступени, а не тела. Единственное отличие: стоящее тело, которое только вертится, теперь может «уснуть» через 2 с покоя, как любое неподвижное тело. Любое настоящее движение, сеттер скорости или высоты и смена плиток будят его как раньше.

### 6.3 Ленивый список активных тел
**Файлы:** `ST14SharedZLevelsSystem.Activation.cs`, `.Update.cs`, `ST14ZPhysicsComponent.cs`

**Проблема (после v1).** Засыпание тела делало `List.Remove` — O(n) на каждое засыпающее тело.

**Что сделано.**
- Истина — `HashSet` активных тел; `WakeBody` и `SleepBody` работают за O(1).
- Список — только порядок обхода. Уснувшие тела выбрасываются из него один раз за шаг, одним проходом O(n) с сохранением порядка (`CompactActiveBodies`).
- Флаг `InActiveList` не даёт телу попасть в список дважды, если оно уснуло и проснулось до уплотнения.
- Тела, которых усыпила обработка другого тела в этом же шаге, пропускаются — как раньше, когда их сразу удаляли.
- Вместо swap-remove выбрано ленивое уплотнение: оно тоже O(1) на засыпание, но не меняет порядок обработки.

**Что изменилось.** `ActiveBodies` теперь отдаёт множество (`IReadOnlyCollection`), а не список: используется только отладочным оверлеем и тестом. Порядок обработки может отличаться только в редком случае «усыпили и разбудили в одном шаге» — тогда тело обработается в этом же шаге, а не в следующем.

### 6.4 Один `DirtyField` за `Update`
**Файл:** `ST14SharedZLevelsSystem.Update.cs`

**Проблема.** Физика делает несколько подшагов за тик (60 Гц Z-физики против тикрейта сервера). На каждом подшаге падающее тело дважды вызывало `DirtyField` — поиск индекса поля по строке плюс учёт «грязности».

**Что сделано.** Подшаги только помечают тело (`PendingDirtyVelocity` / `PendingDirtyPosition`), а `FlushPendingDirty` в конце `Update` вызывает `DirtyField` по одному разу.

**Что изменилось.** Ничего для сети: все подшаги одного `Update` идут в одном тике, поэтому в состояние уходят те же поля с тем же номером тика.

### 6.5 Сеттеры не грязнят неизменённое поле
**Файл:** `ST14SharedZLevelsSystem.Movement.cs`

`SetZPosition`, `SetZVelocity`, `SetZGravity` и `AddZVelocity(0)` больше не вызывают `DirtyField`, если значение не изменилось. Тело при этом будится как раньше. Это меньше сетевого трафика от систем, которые выставляют одно и то же каждый тик (полёт, лестницы).

### 6.6 Клиент: кэш скана плиток для отрисовки нижних уровней
**Файлы:** `Content.Client/_ST14/ZLevels/Core/ST14ClientZLevelsSystem.cs`, `ScalingViewport.ST14ZLevels.cs`, счётчик `TileVersion` в shared-системе

**Проблема.** `TryFindEmptyTiles` каждый кадр, для каждого из до 6 уровней, читал все клетки экрана, пока не находил пустую или прозрачную. На сплошном полу — все ~600 клеток каждый кадр.

**Что сделано.** Скан перенесён в `ST14ClientZLevelsSystem.AnyEmptyOrTransparentTile`. Результат кэшируется на карту по ключу «грид + клетка левого нижнего угла + клетка правого верхнего угла + `TileVersion`». `TileVersion` растёт при любой смене плиток.

**Что изменилось.** Ничего: прозрачность плиток статична, а любая смена плиток или клеток в кадре сбрасывает кэш. Тест проверяет это явно.

**Замер:** 152.6 → 1.0 мкс на уровень за кадр.

### 6.7 Синхронизация Z-гридов: не телепортировать стоящие гриды
**Файл:** `Content.Server/_ST14/ZLevels/Core/ST14ZGridSyncSystem.cs`

Летящая сеть гридов каждый физический тик переставляла каждый грид в усреднённую позу через `SetWorldPositionRotation`: поиск грида, события перемещения. Теперь грид, который уже стоит в целевой позе (отклонение меньше 1e-5 по позиции и 1e-7 рад по углу), пропускается. Движок в этом случае и так ничего бы не изменил, так что поведение то же. Замерить не на чем: в наших картах летящих Z-сетей гридов нет.

### 6.8 Мелочи
- Кэш лестниц (`ST14ZLevelsLaddersCacheSystem`) теперь слушает `ST14ZHighGroundChangedEvent`. Движок разрешает одну подписку на пару «компонент + событие», а `MapInit` / `Anchor` / `Shutdown` лестниц нужны и кэшу земли. Подписка осталась одна, в shared-системе, и она пересылает событие. Логика кэша лестниц не изменилась.
- Статус-эффекты на клиенте: `SetOffset` пропускается, если смещение уже нужное.
- `ST14ZMapComponent` генерирует `AfterAutoHandleStateEvent` — нужно для инвалидации кэша земли на клиенте.
- В `Content.Shared/_ST14/ZLevels/CLAUDE.md` добавлен раздел «Performance rules»: правила кэша и инвалидации, чтобы будущие правки их не сломали.

## 7. Изменённые файлы (v2, сверх v1)

- Перенесено: 147 файлов (`git mv`, см. раздел 2.1) + переименования `CE*` → `ST14*` внутри.
- Новые: 4 × `LICENSE.TXT` в папках `_ST14/ZLevels`, `docs/ST14-ZLevels-Optimization-v2.md`, `docs/ST14-Оптимизация-Z-уровней-v2.pdf`.
- Содержательные правки оптимизаций:
  - `Content.Shared/_ST14/ZLevels/Core/EntitySystems/ST14SharedZLevelsSystem.{Movement,Activation,Update}.cs`
  - `Content.Shared/_ST14/ZLevels/Core/Components/{ST14ZPhysicsComponent,ST14ZMapComponent}.cs`
  - `Content.Server/_ST14/ZLevels/Core/{ST14ZLevelsSystem.Maps,ST14ZGridSyncSystem}.cs`
  - `Content.Server/_ST14/ZLevels/LaddersCache/ST14ZLevelsLaddersCacheSystem.cs`
  - `Content.Client/_ST14/ZLevels/Core/{ST14ClientZLevelsSystem,ScalingViewport.ST14ZLevels}.cs`
  - `Content.IntegrationTests/Tests/_ST14/ST14ZLevelPerfTest.cs`
- Вне `_ST14`: файлы из раздела 2.4.

## 8. Тесты

| Набор | Результат |
|---|---|
| `FallThroughOpeningTest`, `GroundCacheInvalidationTest` (новый) | пройдены |
| `DeterministicTraceHash` | `5DDB3AC5C57063A4` во всех прогонах v2-до и v2-после — совпадает с исходным CE-кодом |
| `_ST14`, `Chasm`, `Climbing`, `Gravity`, `GravityGridTest`, `Movement`, `Buckle`, `Eye`, `Physics`, `PrototypeSaveTest`, `Localization`, `DummyIconTest`, `ItemSpriteTest`, `ClickableTest` | **87 / 87 пройдено** |
| YAML-линтер (`Content.YAMLLinter`) | «No errors found» (до и после оптимизаций) |
| `EntityTest` + `PostMapInitTest` (все карты) | 510 / 512 пройдено. Не прошли `SpawnAndDirtyAllEntities` и `SpawnAndDeleteEntityCountTest`. Причина у обоих — только известная старая ошибка спрайта `ST14GroundFire` («Attempted to set layer sprite scale to very small values»). В v1 я проверял: она падает так же и на исходном коде. Других ошибок в этих тестах нет. Все карты (`PostMapInitTest`) загружаются |

## 9. Release-сборка

- `dotnet build Content.Server -c Release` — **0 ошибок** (90 предупреждений в пересобранных проектах, все уже были).
- `dotnet build Content.Client -c Release` — **0 ошибок** (54 предупреждения в пересобранных проектах, все уже были).

Перед сборкой проверил: процессов `Content.Server` / `Content.Client` не было. Интеграционные тесты и YAML-линтер тоже собирались в Release — 0 ошибок.

## 10. Что не сделано и почему

1. **Сетевой трафик Z-физики «не слать LocalPosition/Velocity в покое».** Так уже и есть: в покое поля не грязнятся (проверяется изменение больше 0.001). А в движении эти поля нужны другим клиентам, которые это тело не предсказывают. Убрать их — значит сломать отображение прыжков и падений у чужих игроков.
2. **PVS-глаза.** На каждого игрока заводится до 5 глаз ниже и до 3 выше, все подписаны на PVS. Можно подписывать только уровни, которые реально видны (есть проём), но это вызовет «подгрузку» уровня с задержкой при подъёме и спуске — заметное изменение геймплея. Не стал без твоего решения.
3. **Клиентский проход по всем спрайтам с Z-физикой каждый кадр.** В `base_structure.yml` (правка ST14) Z-физика есть у **каждой** конструкции, включая стены. Поэтому оба прохода спрайтов на клиенте каждый кадр обходят все стены в PVS. Pre-проход ещё и каждый кадр принудительно ставит им `DrawDepth`, перебивая другие системы. Ограничить обход «только тела в воздухе» нельзя без изменения этой семантики. Самый большой резерв на клиенте — убрать `ST14ZPhysics` из заякоренных конструкций, которые не должны падать. Но это геймплейное решение (смогут ли постройки падать в дыры) — оставляю тебе.
4. **Событийный проход статус-эффектов.** Смещение эффекта должно каждый кадр следовать за высотой родителя, так что это по сути покадровая работа. Сделал только пропуск лишнего `SetOffset`.
5. **move и tiles** упираются в движок: `SetWorldPosition` и `SetTiles` — это RobustToolbox, по правилам его не трогаю.
6. **Рендер** (оверлеи блюра и тумана, параллакс, свет на каждом уровне) замерить в headless невозможно. Блюр требует копию экрана для каждого уровня ниже — это суть эффекта. Для цифр нужен профайлер на настоящем клиенте.
7. **`LICENSE-MIT.TXT`** отсутствует в корне, хотя корневой `LICENSE.TXT` на него ссылается (раздел 3).
