st14-energy-box-slot = Энергоблок
st14-energy-box-examine-empty = Энергоблока внутри [color=red]нет[/color]: щиток не проводит ток.
st14-energy-box-examine-charge = Заряд энергоблока: [color=yellow]{ $percent }%[/color].
st14-energy-box-examine-breaker-on = Рубильник [color=lime]включён[/color].
st14-energy-box-examine-breaker-off = Рубильник [color=red]выключен[/color].
st14-energy-box-verb-breaker-on = Включить рубильник
st14-energy-box-verb-breaker-off = Выключить рубильник

st14-energy-pylon-examine-unanchored = Пилон [color=red]не закреплён[/color].
st14-energy-pylon-examine-unpowered = Пилон [color=red]обесточен[/color]: к нему нужен провод НВ от энергощитка.
st14-energy-pylon-examine-powered = Пилон [color=cyan]под током[/color], связей: { $links }.

st14-field-generator-examine-on = Генератор [color=lime]работает[/color].
st14-field-generator-examine-off = Генератор [color=red]выключен[/color].

st14-trench-dig-start = Вы начинаете копать ямку под провод.
st14-trench-fill-start = Вы начинаете засыпать ямку.
st14-tiles-dunes-trench = ямка под провод

ent-ST14EnergyPylon = энергопилон
    .desc = Полевой пилон Мобильной пехоты. Питается по проводу от энергощитка, сам связывается с ближайшими пилонами до девяти тайлов и бьёт током жуков, подошедших к линии между ними.
ent-ST14EnergyBox = энергощиток
    .desc = Полевой энергощиток Мобильной пехоты. Берёт ток от полевых генераторов по проводу СВ и отдаёт пилонам по проводу НВ, но только с энергоблоком внутри. Энергоблок копит излишек и покрывает нехватку.
ent-ST14EnergyBlock = энергоблок
    .desc = Тяжёлая запаянная энергоячейка Мобильной пехоты, носится двумя руками. В энергощитке копит излишек генераторов и держит забор, когда их не хватает. Огонёк на корпусе показывает, сколько осталось заряда.
ent-ST14FieldGenerator = полевой генератор
    .desc = Переносной генератор Мобильной пехоты. Питает провод СВ забора. Включается и выключается рукой; открутите его, чтобы перетащить на другое место.
ent-ST14EntrenchingTool = сапёрная лопатка
    .desc = Складная полевая лопатка. Копает в песке ямку под провод и засыпает её обратно. Закопанный провод видно только т-лучевым сканером.
