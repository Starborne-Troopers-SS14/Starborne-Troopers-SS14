st14-sector-troopers-won = Десант зачистил сектор: улей уничтожен.
st14-sector-hive-won = Улей удержал сектор: десант уничтожен.

st14-sector-template-command-description = Создаёт пустую карту сектора 200×200 с бурей по краю, высадкой слева и ульем справа и сохраняет её в папку данных сервера.
st14-sector-template-command-help = Использование: st14_sector_template [путь] [тайл]. По умолчанию /st14_sector_template.yml и ST14FloorDunes.
st14-sector-template-command-no-tile = Нет тайла { $tile }.
st14-sector-template-command-failed = Не удалось сохранить { $path }.
st14-sector-template-command-done = Шаблон сектора сохранён: { $path }.

ent-ST14SpawnPointEngineer = инженер
ent-ST14SectorQueenSlot = место королевы
    .desc = Здесь в начале раунда может появиться королева улья.

st14-sector-time-up = Время вышло: нападение провалилось.
st14-sector-gate-closed = Буря закрывает этот путь.
st14-sector-gate-not-initialized = За сектор { $code } ещё не было боёв, прохода нет.
ent-ST14SectorGateBase = проход в сектор
    .desc = За бурей лежит следующий сектор. Пройти туда можно, только если за него уже шли бои.

## Подсекторы: соту держит тот, чьи постройки стоят на всех её ресурсных точках
st14-nest-no-point = Рядом нет ресурсной точки.
st14-nest-already = На этой точке уже растёт гнездо.
st14-nest-enemy = Точку держит бур людей. Сначала сломай его.
st14-nest-start = Над ресурсной точкой начинает расти гнездо.
st14-nest-done = Гнездо выросло: эта часть сектора за ульем.
ent-ST14HiveNest = гнездо улья
    .desc = Набухший нарост над ресурсной точкой. Пока он стоит, эта часть сектора за ульем.
ent-ST14ActionPlantNest = Вырастить гнездо
    .desc = Вырастить гнездо над ресурсной точкой рядом. Пока оно стоит, эта часть сектора за ульем. Бур людей на точке сначала нужно сломать.

## Сторона на войну за регион
st14-side-title = Ваша сторона
st14-side-none = Сторона не выбрана.
    Сменить её до захвата региона будет нельзя.
st14-side-chosen = Вы воюете за { $side }
    до захвата региона.
st14-side-chosen-region = Вы воюете за { $side }
    до захвата региона { $region }.
st14-side-federation = Федерацию
st14-side-arachnids = арахнид
st14-side-pick-federation = За Федерацию
st14-side-pick-arachnids = За арахнид

st14-side-confirm = Точно? { $side }
