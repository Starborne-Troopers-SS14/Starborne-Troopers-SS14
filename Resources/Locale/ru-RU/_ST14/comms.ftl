st14-chat-radio-squad = Отряд
st14-chat-radio-rodger = Роджер Янг
st14-squad-create-leader-only = Отряд может создать только командир отряда.

st14-radio-no-squad = Вы не в отряде — частота отряда молчит.
st14-radio-squad-no-signal = Нет связи с отрядом: рядом нет связиста с ранцем.
st14-radio-rodger-no-tower = Нет связи с «Роджером Янгом»: ни одна вышка не держит частоту.
st14-radio-rodger-no-signal = Нет связи с «Роджером Янгом»: рядом нет ранца, настроенного на частоту вышки, и нет вышки.

st14-tower-signaller-only = Искать частоту умеет только связист.
st14-tower-no-pack = Без ранца связиста к вышке не подключиться.
st14-tower-busy = На вышке уже работает другой связист.
st14-tower-caught = Частота поймана: { $frequency } МГц! Выставьте её на ранце.
st14-tower-says-caught = Связь с «Роджером Янгом» на частоте { $frequency } МГц. Связистам — выставить её на ранцах.
st14-tower-says-fading = Частота { $frequency } МГц уплывает, сигнал слабеет. Связисту — найти на вышке новую.
st14-tower-says-lost = Связь с «Роджером Янгом» на { $frequency } МГц потеряна.
st14-tower-examine-held = Держит частоту [color=lightgreen]{ $frequency } МГц[/color], сигнал { $percent }%.
st14-tower-examine-none = Частота не поймана: связист должен найти её на тюнере вышки.

st14-radio-pack-verb-tune = Настроить частоту
st14-radio-pack-signaller-only = Настраивать ранец умеет только связист.
st14-radio-pack-examine = Ранец настроен на [color=lightblue]{ $frequency } МГц[/color].

st14-tuner-title = Тюнер вышки связи
st14-tuner-title-pack = Частота ранца
st14-tuner-steps =
    1. Крутите частоту, следите за сигналом.
    2. Где волна чистая и шкала полная — частота рядом.
    3. Не трогайте ручку, пока «Захват» не заполнится.
    4. Выставьте пойманную частоту на ранце (ПКМ по ранцу).
st14-tuner-steps-pack =
    Выставьте частоту вышки связи (видна при осмотре вышки)
    или найдите её по сигналу.
st14-tuner-mhz-unit = МГц
st14-tuner-model-tower = ВЫШКА СВЯЗИ · ПОИСК ЧАСТОТЫ
st14-tuner-model-pack = РАНЦЕВАЯ РАЦИЯ ПР-14
st14-tuner-section-dial = Частота
st14-tuner-section-power = Питание
st14-tuner-section-range = Мощность передатчика
st14-tuner-section-memory = Память частот
st14-tuner-power-on = ВКЛ
st14-tuner-power-off = ВЫКЛ
st14-tuner-range-0 = МАЛАЯ
st14-tuner-range-1 = СРЕДНЯЯ
st14-tuner-range-2 = БОЛЬШАЯ
st14-tuner-range-note = Отряд слышит друг друга в { $tiles } тайлах от вас.
st14-tuner-memory-cell = П{ $slot } · { $frequency }
st14-tuner-memory-save = запомнить
st14-tuner-pack-off = Ранец выключен.
st14-tuner-pack-status-off = Ранец выключен: отряд вас не слышит.
st14-tuner-lock = Захват
st14-tuner-signal-none = Тишина: частоты здесь нет.
st14-tuner-signal-weak = Слабый сигнал — ищите дальше.
st14-tuner-signal-close = Сигнал рядом — подстройте точнее (±0.1).
st14-tuner-signal-on = На частоте! Держите, идёт захват.
st14-tuner-signal-caught = Частота поймана.
st14-tuner-caught = Поймано: { $frequency } МГц. Теперь выставьте эту частоту на ранце.
st14-tuner-tower-holds = Сейчас вышка держит { $frequency } МГц ({ $percent }%).
st14-tuner-no-link = Связи с «Роджером Янгом» нет.
st14-tuner-pack-on = На частоте вышки.
st14-tuner-pack-linked = «Роджер Янг» на связи через ранец, сигнал { $percent }%.
st14-tuner-pack-not-linked = Ни одна вышка не держит эту частоту — «Роджер Янг» не слышен.

ent-ST14ClothingBackRadioPack = ранец связиста
    .desc = Полевая рация на раме. На спине несёт частоту отряда в большом радиусе и показывает отряд на боевой карте. Настроенный на частоту вышки связи, несёт и «Роджера Янга».
ent-ST14CommsTower = вышка связи
    .desc = Полевой ретранслятор до «Роджера Янга» на орбите. Связист ищет на её тюнере частоту «Роджера Янга»; ранцы, настроенные на неё, несут «:r» для всех отрядов планеты. Частота уплывает, и найти новую может только связист.
ent-ST14EnergyBlock = энергоблок
    .desc = Запаянная энергоячейка Мобильной пехоты. Огонёк на корпусе показывает, что она заряжена.
ent-ST14EnergyBox = энергощиток
    .desc = Полевой энергощиток Мобильной пехоты.
