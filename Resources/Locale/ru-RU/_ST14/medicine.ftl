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
st14-limbs-command-help = st14_limbs tear | fit <протез> | wound <часть> <cut|puncture|burn|fracture> | treat <лечение> <часть> | drug <реагент> <ед> | infect <часть> <0..1> | arrest | scan
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

## Новые препараты
reagent-name-st14-atropine = атропин
reagent-desc-st14-atropine = Разгоняет медленное сердце, рассеивает морфийный туман, частично выводит яд жуков. С адреналином сердце скачет.
reagent-name-st14-amiodarone = амиодарон
reagent-desc-st14-amiodarone = Держит ритм сердца под любой нагрузкой и помогает разряду и массажу завести остановившееся.
reagent-name-st14-ketorolac = кеторолак
reagent-desc-st14-ketorolac = Обезболивает и не туманит голову, но разжижает кровь.
reagent-name-st14-antibiotic = антибиотик
reagent-desc-st14-antibiotic = Медленно убивает заражение ран.
reagent-name-st14-antipyretic = жаропонижающее
reagent-desc-st14-antipyretic = Снимает жар и немного боль. Само заражение продолжается.
reagent-name-st14-osteogen = остеоген
reagent-desc-st14-osteogen = Сращивает сломанную кость за полторы минуты: ровно под шиной, криво без неё.
reagent-name-st14-regenerol = регенерол
reagent-desc-st14-regenerol = Раны затягиваются втрое быстрее.
reagent-name-st14-antivenom = антитоксин
reagent-desc-st14-antivenom = Связывает яд жуков и выводит его из крови.
reagent-name-st14-modafinil = модафинил
reagent-desc-st14-modafinil = Будит тело: снимает отходняк после стимулятора и морфийный туман. Ещё одна нагрузка на сердце.
reagent-name-st14-plasma = плазмозаменитель
reagent-desc-st14-plasma = Наполняет сосуды, когда крови для переливания нет. Разбавляет оставшуюся кровь.
reagent-name-st14-bug-venom = яд жуков
reagent-desc-st14-bug-venom = Яд с когтей жуков-тигров: руки и ноги наливаются тяжестью и немеют, в глазах зеленеет, много яда останавливает дыхание.
st14-atropine-effect = Во рту пересыхает, сердце ускоряется.
st14-osteogen-effect = Кость внутри ноет и зудит.
st14-modafinil-effect = Голова проясняется, как от ледяной воды.
st14-venom-effect-burn = Яд жжёт изнутри.
st14-venom-effect-numb = Пальцы немеют и не слушаются.
st14-venom-breathing = Грудь сдавливает, вдохнуть всё труднее!
st14-venom-injected = Когти были ядовитыми: жжение расползается по венам!
st14-drug-antidote-pain = Обезболивающее вырвано из крови, и боль возвращается вся сразу!
st14-drug-ketorolac-overdose = Желудок скручивает тупой болью.
st14-drug-atropine-overdose = Сердце бьётся как сумасшедшее, в глазах плывёт.
st14-drug-amiodarone-overdose = Сердце бьётся всё реже и реже...
st14-drug-antipyretic-overdose = Под рёбрами справа тупая тяжесть.

## Что препарат делает (в сканере)
st14-drug-summary-morphine = сильно обезболивает, туманит; с 20 ед душит
st14-drug-summary-ketorolac = обезболивает без тумана, разжижает кровь
st14-drug-summary-ketamine = наркоз, ничего не чувствует
st14-drug-summary-naloxone = выводит морфий, боль возвращается
st14-drug-summary-adrenaline = держит сердце, помогает массажу
st14-drug-summary-atropine = разгоняет сердце, снимает туман, выводит яд
st14-drug-summary-amiodarone = держит ритм, помогает завести сердце
st14-drug-summary-stim = скорость и твёрдая рука, потом отходняк
st14-drug-summary-modafinil = снимает отходняк и туман
st14-drug-summary-tranexamic = кровь сворачивается быстрее
st14-drug-summary-plasma = восполняет кровь, разбавляет её
st14-drug-summary-antibiotic = убивает заражение
st14-drug-summary-antipyretic = снимает жар, заражение идёт
st14-drug-summary-osteogen = сращивает кости
st14-drug-summary-regenerol = раны затягиваются втрое быстрее
st14-drug-summary-antivenom = выводит яд жуков
st14-drug-summary-venom = яд: тяжесть, онемение, удушье
st14-drug-summary-alcohol = чуть обезболивает, разжижает кровь

## Взаимодействия
st14-interaction-heart-pounding = Сердце колотится так, что больно.
st14-interaction-heavy-breath = Дышать тяжело, грудь не поднимается.
st14-interaction-slow-heart = Сердце бьётся медленно и тяжело.
st14-interaction-bad-trip = Стены дышат, и в каждой тени кто-то есть.
st14-scan-warn-morphine-alcohol = Морфий с алкоголем: угнетение дыхания
st14-scan-warn-morphine-venom = Морфий с ядом: дыхание останавливается
st14-scan-warn-ketorolac-tranexamic = Кеторолак гасит гемостатик
st14-scan-warn-ketorolac-alcohol = Кеторолак с алкоголем: кровь не сворачивается
st14-scan-warn-atropine-adrenaline = Атропин с адреналином: сердце скачет
st14-scan-warn-atropine-amiodarone = Атропин и амиодарон гасят друг друга
st14-scan-warn-bradycardia = Амиодарон с морфием: сердце замедляется
st14-scan-warn-modafinil-stim = Модафинил со стимулятором: перегрузка сердца
st14-scan-warn-ketamine-stim = Кетамин со стимулятором: тяжёлый бред, нагрузка на сердце
st14-scan-warn-plasma-tranexamic = Плазмозаменитель разбавляет гемостатик
st14-scan-warn-venom = Яд жуков в крови: нужен антитоксин
st14-scan-warn-sepsis = Сепсис: заражение в крови, нужен антибиотик
st14-scan-warn-infection = Заражение раны: антисептик и антибиотик
st14-scan-warn-infection-hidden = Жар сбит, но заражение идёт: нужен антибиотик
st14-scan-warn-sewn-dirty = Зашита грязная рана: гниёт под швами
st14-scan-warn-malunion = Кость срослась криво: сломать и вправить шовным набором

## Заражение и кости
st14-wound-infected = Рана воспалилась и пульсирует: { $zone }.
st14-wound-sepsis = Озноб и слабость: заражение уже в крови!
st14-bone-healed = Кость срослась: { $zone }.
st14-bone-malunion = Кость срослась криво: { $zone }. Без шины её никто не держал.
st14-fever-shiver = трясётся в ознобе
st14-examine-infected = [color=#b5d94a]Рана воспалена и опухла: { $zone }.[/color]
st14-examine-fever = [color=#e0a040]Лицо горит, по нему катится пот.[/color]
st14-scan-wound-malunion = кривое сращение
st14-scan-wound-infected = заражение { $percent }%
st14-scan-wound-clean = обработано
st14-scan-wound-dirty = грязная
st14-scan-wound-healing = срастается { $percent }%
st14-treat-disinfect = Обработать антисептиком
st14-treat-done-disinfect = Раны обработаны: { $zone }.
st14-stack-antiseptic = антисептик

## Новые предметы
ent-ST14AtropinePen = автоинъектор атропина
    .desc = Разгоняет медленное сердце, рассеивает морфийный туман и частично выводит яд жуков. С адреналином сердце скачет.
ent-ST14AmiodaronePen = автоинъектор амиодарона
    .desc = Держит ритм сердца под любой нагрузкой и помогает разряду и массажу завести остановившееся. Не вместе с атропином.
ent-ST14KetorolacPen = автоинъектор кеторолака
    .desc = Обезболивает и не туманит голову. Разжижает кровь: не вместе с гемостатиком и не с алкоголем.
ent-ST14AntibioticPen = автоинъектор антибиотика
    .desc = За несколько минут убивает заражение ран. Сначала обработайте рану.
ent-ST14AntipyreticPen = автоинъектор жаропонижающего
    .desc = Снимает жар. Заражение при этом продолжается, остановит его только антибиотик.
ent-ST14OsteogenPen = автоинъектор остеогена
    .desc = Сращивает сломанную кость за полторы минуты. Сначала шина, иначе кость срастётся криво.
ent-ST14RegenerolPen = автоинъектор регенерола
    .desc = Раны затягиваются втрое быстрее. Слегка нагружает сердце.
ent-ST14AntivenomPen = автоинъектор антитоксина
    .desc = Выводит из крови яд жуков-тигров.
ent-ST14ModafinilPen = автоинъектор модафинила
    .desc = Будит тело: снимает отходняк после стимулятора и морфийный туман. Не поверх стимулятора.
ent-ST14PlasmaPen = автоинъектор плазмозаменителя
    .desc = Наполняет сосуды, когда крови для переливания нет. Разбавляет оставшуюся кровь.
ent-ST14Antiseptic = антисептик
    .desc = Тюбик антисептического геля. Обрабатывает раны части тела: они перестают гнить, свежее заражение погибает. Сделайте это до того, как зашивать. Щиплет.
    .suffix = Полный
ent-ST14MedicDrugCase = укладка медика
    .desc = Препараты на то, что приходит после первых минут ранения: сердце, заражение, кости, яд и кровь.
