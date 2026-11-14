st14-energy-box-slot = Энергоблок
st14-energy-box-examine-empty = Энергоблока внутри [color=red]нет[/color].
st14-energy-box-examine-charge = Заряд энергоблока: [color=yellow]{ $percent }%[/color].
st14-energy-pylon-examine-unpowered = Пилон [color=red]обесточен[/color]: рядом нет энергощитка с заряженным энергоблоком.
st14-energy-pylon-examine-powered = Пилон [color=cyan]под током[/color], лучей: { $links }.

ent-ST14EnergyPylon = энергопилон
    .desc = Полевой пилон Мобильной пехоты. Запитанный от энергощитка рядом, бьёт трескучей дугой в следующий пилон на одной прямой, до семи тайлов. Жуков, пересекающих дугу, обжигает и отбрасывает.
ent-ST14EnergyBeamBase = энергодуга
    .desc = Трескучая дуга между двумя пилонами. Десантники проходят сквозь неё, жуки горят.
ent-ST14EnergyBeamHorizontal = { ent-ST14EnergyBeamBase }
    .desc = { ent-ST14EnergyBeamBase.desc }
ent-ST14EnergyBeamVertical = { ent-ST14EnergyBeamBase }
    .desc = { ent-ST14EnergyBeamBase.desc }
