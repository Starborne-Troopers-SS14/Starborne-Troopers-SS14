ent-ST14FieldDefibrillator = полевой дефибриллятор
    .desc = Зарядите на пациенте и разрядите в зелёной зоне. И не забудьте крикнуть.

st14-heartbeat-hint = Держись! Жми {$key} в такт сердцу
st14-heartbeat-revive = СЕЙЧАС! Жми {$key} на удар!

st14-field-defib-charging = Заряжаю!
st14-field-defib-clear = Разряд!
st14-field-defib-hint = Нажмите на пациента ещё раз в зелёной зоне
st14-field-defib-not-needed = Этому бойцу разряд не нужен.
st14-field-defib-broken = Заряд сброшен!
st14-field-defib-overload = Дефибриллятор перегрелся!

st14-medicine-too-late = Слишком поздно, бойца не вернуть.

## Части тела
st14-zone-head = голова
st14-zone-torso = грудь
st14-zone-armleft = левая рука
st14-zone-armright = правая рука
st14-zone-legleft = левая нога
st14-zone-legright = правая нога

## Раны
st14-wound-fracture-self = ХРУСТ! Сломана кость: { $zone }!
st14-wound-fracture-others = Слышен хруст кости: { $victim } сгибается от боли.
st14-wound-arterial-self = Кровь бьёт фонтаном, перебита артерия: { $zone }!
st14-bleeding-gushing = Кровь хлещет: { $zone }!
st14-pain-unbearable = НЕВЫНОСИМАЯ БОЛЬ!
st14-pain-groan = стонет от боли
st14-pain-shock = Болевой шок! Ноги подкашиваются.
st14-heart-stopped-self = Сердце замирает... в глазах темнеет...
st14-heart-stopped-others = { CAPITALIZE($victim) } хрипит и обмякает: сердце остановилось!
st14-heart-restarted = Сердце снова бьётся!
st14-tourniquet-ischemia = Под жгутом всё немеет и холодеет: { $zone }. Нужен медик!
st14-tourniquet-necrosis-self = Под жгутом всё омертвело: { $zone } больше не ваша.
st14-tourniquet-necrosis-others = Омертвевшая под жгутом конечность { $victim } отваливается.

## Препараты
reagent-name-st14-morphine = морфий
reagent-desc-st14-morphine = Сильное обезболивающее. Снимает боль и крики, в большой дозе угнетает дыхание. Антидот — налоксон.
reagent-name-st14-naloxone = налоксон
reagent-desc-st14-naloxone = Вырывает морфий из крови: останавливает передозировку, и вся боль возвращается разом.
reagent-name-st14-combat-stim = боевой стимулятор
reagent-desc-st14-combat-stim = Быстрые ноги, твёрдая рука, раны не тормозят. Нагружает сердце, после него наступает отходняк. С адреналином опасен.
reagent-name-st14-ketamine = кетамин
reagent-desc-st14-ketamine = Полевой наркоз: пациент спит и ничего не чувствует, пока препарата хватает.
st14-morphine-effect-warm = По телу разливается тёплая вата.
st14-morphine-effect-far = Боль где-то далеко, будто не твоя.
st14-naloxone-effect = Ледяная волна прокатывается по венам.
st14-stim-effect-heart = Сердце колотится о рёбра.
st14-stim-effect-sharp = Всё вокруг становится резким и медленным.
st14-ketamine-effect = Мир уплывает куда-то в сторону.
st14-drug-naloxone = Морфийный туман рвётся, и боль возвращается вся сразу!
st14-drug-crash = Стимулятор отпускает. Тело наливается свинцом.
st14-drug-morphine-overdose = Дыхание становится редким и поверхностным...

## Медик
st14-verb-category-medicine = Медицина
st14-treat-tourniquet = Наложить жгут
st14-treat-bandage = Перевязать
st14-treat-hemostatic = Затампонировать
st14-treat-splint = Наложить шину
st14-treat-suture = Зашить
st14-treat-verb = { $action }: { $zone }
st14-treat-nothing = Здесь { $item } не нужен.
st14-treat-lie-down = Пациента нужно уложить.
st14-treat-start-self = { $action }: { $zone }...
st14-treat-start-others = { CAPITALIZE($user) } занимается раной { $target }: { $action }, { $zone }.
st14-treat-done-tourniquet = Жгут затянут: { $zone }. Кровь остановлена.
st14-treat-done-bandage = Повязка наложена: { $zone }.
st14-treat-done-hemostatic = Рана затампонирована: { $zone }.
st14-treat-done-splint = Шина наложена: { $zone }.
st14-treat-done-suture = Раны зашиты: { $zone }.
st14-treat-remove-tourniquet = Снять жгут: { $zone } ({ $minutes } мин)
st14-treat-removing = Вы ослабляете жгут: { $zone }...
st14-treat-removed = Жгут снят: { $zone }.
st14-treat-removed-bleeding = Жгут снят, и кровь снова пошла: { $zone }!
st14-treat-pulse = Проверить пульс
st14-treat-cpr = Непрямой массаж сердца
st14-pulse-none = Пульса нет!
st14-pulse-reading = Пульс { $pulse }: { $kind }.
st14-pulse-steady = ровный
st14-pulse-fast = частый
st14-pulse-racing = бешеный
st14-pulse-weak = слабый
st14-pulse-thready = нитевидный, едва прощупывается
st14-pulse-erratic = сбивчивый, с перебоями
st14-cpr-start = { CAPITALIZE($user) } давит на грудь { $target }: раз, два, три...
st14-medic-call-shout = МЕДИК!
st14-medic-call-self = Вы зовёте медика. Медики видят над вами красный крест.

## Осмотр
st14-examine-tourniquet = [color=#d8b26a]Жгут затянут ({ $minutes } мин): { $zone }.[/color]
st14-examine-tourniquet-long = [color=#e06040]Жгут уже { $minutes } мин, конечность синеет: { $zone }![/color]
st14-examine-stump = [color=#c03030]Культя на месте: { $zone }.[/color]
st14-examine-gushing = [color=#ff3030]Кровь хлещет: { $zone }![/color]
st14-examine-bleeding = [color=#c04040]Кровоточит: { $zone }.[/color]
st14-examine-bandaged = Перевязано: { $zone }.
st14-examine-splinted = Наложена шина: { $zone }.
st14-examine-broken = [color=#d08040]Неестественно согнута: { $zone }.[/color]
st14-examine-no-pulse = [color=#ff3030]Грудь не поднимается. Пульса нет![/color]

## Сканер
st14-scan-title = ПОЛЕВОЙ МЕДСКАНЕР
st14-scan-pulse = ПУЛЬС
st14-scan-blood = КРОВЬ
st14-scan-pain = БОЛЬ
st14-scan-bleeding = КРОВОТЕЧЕНИЕ
st14-scan-heart-load = НАГРУЗКА НА СЕРДЦЕ
st14-scan-drugs = В КРОВИ
st14-scan-no-drugs = Ничего
st14-scan-warnings = ОПАСНО
st14-scan-all-clear = Угрозы жизни нет
st14-scan-wounds = РАНЫ
st14-scan-no-wounds = Ран нет
st14-scan-out-of-range = Пациент вне досягаемости
st14-scan-state-alive = В сознании
st14-scan-state-crit = Без сознания
st14-scan-state-dead = Мёртв
st14-scan-state-arrest = ОСТАНОВКА СЕРДЦА
st14-scan-per-second = { $value } ед/с
st14-scan-tourniquet = жгут { $minutes }:{ $seconds }
st14-scan-missing = нет
st14-scan-wound-cut = порез
st14-scan-wound-puncture = пулевое
st14-scan-wound-burn = ожог
st14-scan-wound-fracture = перелом
st14-scan-wound-stump = культя
st14-scan-wound-arterial = артерия
st14-scan-wound-bandaged = повязка
st14-scan-wound-packed = тампон
st14-scan-wound-sutured = зашито
st14-scan-wound-splinted = шина
st14-scan-warn-arrest = Остановка сердца: массаж, адреналин, дефибриллятор!
st14-scan-warn-bleeding = Сильное кровотечение: жгут или тампон!
st14-scan-warn-ischemia = Жгут слишком долго, отомрёт через { $left } мин: { $zone }
st14-scan-warn-blood = Большая кровопотеря: нужна кровь
st14-scan-warn-arrhythmia = Аритмия: сердце может остановиться
st14-scan-warn-stim-adrenaline = Стимулятор с адреналином: риск остановки сердца
st14-scan-warn-speedball = Морфий со стимулятором: сердце на пределе
st14-scan-warn-overdose = Передозировка: { $drug }
st14-scan-warn-stump = Культя не зашита
st14-scan-warn-fracture = Перелом без шины
st14-scan-warn-anaesthesia = Пациент под наркозом
st14-scan-warn-crash = Отходняк после стимулятора

## Отрыв конечностей
st14-limb-arm-left = левую руку
st14-limb-arm-right = правую руку
st14-limb-leg-left = левую ногу
st14-limb-leg-right = правую ногу
st14-limb-arm-left-of = левой руки
st14-limb-arm-right-of = правой руки
st14-limb-leg-left-of = левой ноги
st14-limb-leg-right-of = правой ноги
st14-limb-torn-self = ВАМ ОТОРВАЛО { $limb }!
st14-limb-torn-others = { CAPITALIZE($victim) } лишается { $limbOf }!

## Протезы
st14-prosthesis-not-needed = { CAPITALIZE($target) } не нужен такой протез.
st14-prosthesis-lie-down = Стальной протез ставят лёжа: уложите раненого.
st14-prosthesis-suture-first = Сначала зашейте культю, на открытую рану протез не встанет.
st14-prosthesis-start = { CAPITALIZE($user) } начинает ставить протез на культю { $target }.
st14-prosthesis-pain = ПРОТЕЗ ВГОНЯЮТ ПРЯМО В КУЛЬТЮ!
st14-prosthesis-done = Протез встал на место.
st14-limbs-command-description = Отладка медицины на своём теле: оторвать конечность, поставить протез, ранить, остановить сердце.
st14-limbs-command-help = st14_limbs tear | fit <протез> | wound <часть> <cut|puncture|burn|fracture> | arrest | scan
st14-limbs-command-nothing = Ничего не вышло.

## Должность
st14-job-name-medic = Медик Starborne
st14-job-description-medic = Полевой медик Мобильной пехоты: жгуты, тампоны, наркоз и сердце, которое надо завести.
st14-job-supervisors-medic = командиру
st14-department-medical = Медицинская служба
st14-department-medical-description = Медики Мобильной пехоты.
st14-stack-bandage = перевязочный пакет
st14-stack-hemostatic = гемостатическая марля
st14-stack-splint = шина

## Предметы
ent-ST14Tourniquet = жгут
    .desc = Ремень с закруткой. Затянутый выше раны, намертво останавливает кровь конечности, даже оторванной. Больше десяти минут, и конечность отомрёт.
ent-ST14Bandage = перевязочный пакет
    .desc = Давящая повязка. Останавливает небольшую рану и замедляет большую. Перебитую артерию не удержит.
    .suffix = Полный
ent-ST14HemostaticGauze = гемостатическая марля
    .desc = Марля с кровоостанавливающим составом, её набивают глубоко в рану. Держит то, куда не наложить жгут: грудь, шею, артерию. Больно.
    .suffix = Полный
ent-ST14Splint = полевая шина
    .desc = Две планки и ремни. Сломанная нога перестаёт подламываться, сломанная рука перестаёт сбивать прицел.
    .suffix = Полный
ent-ST14SurgeryKit = шовный набор
    .desc = Иглы, зажимы и нить. Зашивает раны части тела насовсем и закрывает культю под протез. Пациент лежит, а без кетамина или морфия это пытка.
ent-ST14MedScanner = полевой медсканер
    .desc = Показывает пациента по частям тела, сердце, кровь, препараты в крови и что убьёт его первым. Нажмите им на пациента или используйте в руке на себе.
ent-ST14MorphinePen = автоинъектор морфия
    .desc = Доза морфия. Боль уходит, крик стихает. Вторая доза замедляет дыхание, третья может его остановить.
ent-ST14AdrenalinePen = автоинъектор адреналина
    .desc = Держит умирающее сердце и помогает завести остановившееся при массаже. Со стимулятором может остановить и здоровое.
ent-ST14CombatStimPen = автоинъектор боевого стимулятора
    .desc = Быстрые ноги, твёрдая рука, раны не тормозят и не валят с ног. Платит сердце, а потом наступает отходняк. Никогда с адреналином.
ent-ST14TranexamicPen = автоинъектор гемостатика
    .desc = Транексамовая кислота. Кровь сворачивается быстрее, мелкие раны закрываются сами, крупные кровят меньше. Оторванную конечность не удержит, нужен жгут.
ent-ST14NaloxonePen = автоинъектор налоксона
    .desc = Антидот морфия. Возвращает дыхание при передозировке, и всю боль вместе с ним.
ent-ST14KetaminePen = автоинъектор кетамина
    .desc = Полевой наркоз. Пациент отключается примерно на минуту и ничего не чувствует. Хватит, чтобы зашить культю или поставить протез.
ent-ST14FieldKit = полевой набор десантника
    .desc = Два жгута, перевязочный пакет, морфий и гемостатик. Хватит, чтобы дожить до медика.
ent-ST14MedicBag = рюкзак полевого медика
    .desc = Всё, что медик Мобильной пехоты берёт в бой.
ent-ST14ActionCallMedic = Позвать медика
    .desc = Крикнуть «Медик!». Минуту все медики видят над вами красный крест.
ent-ST14FieldProsthesisArm = полевой протез руки
    .desc = Деревянная рука на кожаных ремнях с крюком вместо кисти. Грубо, но винтовку держит. Ставится на зашитую культю где угодно.
ent-ST14FieldProsthesisLeg = полевой протез ноги
    .desc = Деревяшка на кожаных ремнях. Ходить можно, но медленнее. Ставится на зашитую культю где угодно.
ent-ST14SteelProsthesisArm = стальной протез руки
    .desc = Армейская стальная рука с рабочей кистью. Не хуже той, что забрали жуки. Ставится лежащему раненому на зашитую культю.
ent-ST14SteelProsthesisLeg = стальной протез ноги
    .desc = Армейская стальная нога. На ней ходят и бегают, как раньше. Ставится лежащему раненому на зашитую культю.
ent-ST14OrganFieldArmLeft = деревянная левая рука
ent-ST14OrganFieldArmRight = деревянная правая рука
ent-ST14OrganFieldHandLeft = левый крюк
ent-ST14OrganFieldHandRight = правый крюк
ent-ST14OrganFieldLegLeft = деревянная левая нога
ent-ST14OrganFieldLegRight = деревянная правая нога
ent-ST14OrganFieldFootLeft = левая культяпка
ent-ST14OrganFieldFootRight = правая культяпка
ent-ST14OrganSteelArmLeft = стальная левая рука
ent-ST14OrganSteelArmRight = стальная правая рука
ent-ST14OrganSteelHandLeft = стальная левая кисть
ent-ST14OrganSteelHandRight = стальная правая кисть
ent-ST14OrganSteelLegLeft = стальная левая нога
ent-ST14OrganSteelLegRight = стальная правая нога
ent-ST14OrganSteelFootLeft = стальная левая стопа
ent-ST14OrganSteelFootRight = стальная правая стопа
