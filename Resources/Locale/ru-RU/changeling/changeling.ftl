# antag selection
roles-antag-changeling-name = Генокрад
roles-antag-changeling-objective = Умный хищник, приобретающий личности своих жертв.

# devour
changeling-devour-attempt-failed-cannot-devour = Мы не можем поглотить это!
changeling-devour-attempt-failed-already-devoured = Мы уже поглотили это тело!
changeling-devour-attempt-failed-devoured-recently = Это тело слишком изуродовано, его нельзя поглотить!
changeling-devour-attempt-failed-not-dead = Это тело ещё живо! Мы не можем его поглотить!
changeling-devour-attempt-failed-rotting = У этого трупа только гниющая биомасса.
changeling-devour-attempt-failed-protected = Биомасса этой жертвы защищена бронёй!
changeling-devour-attempt-failed-no-space = Мы превысили максимально допустимое количество личностей!

changeling-devour-begin-windup-self = Наша жуткая пасть раскрывается с неземным голодом.
changeling-devour-begin-windup-others = { CAPITALIZE(POSS-ADJ($user)) } жуткая пасть раскрывается с неземным голодом.
changeling-devour-begin-consume-self = Наша жуткая пасть глубоко впивается в жертву.
changeling-devour-begin-consume-others = { CAPITALIZE(POSS-ADJ($user)) } жуткая пасть глубоко впивается в жертву.
changeling-devour-consume-complete-self = Наша жуткая пасть скрывается, биомасса поглощена.
changeling-devour-consume-complete-others = { CAPITALIZE(POSS-ADJ($user)) } жуткая пасть прячется.

# transformation
changeling-transform-attempt-self = Наши кости хрустят, мышцы рвутся, одна плоть превращается в другую.
changeling-transform-attempt-others = { CAPITALIZE(POSS-ADJ($user)) } кости хрустят, мышцы рвутся, тело превращается в другое.

# transformation BUI
changeling-transform-bui-select-entity = {$entity}
changeling-transform-bui-drop-identity-menu = Избавиться от поглощённой личности.
changeling-transform-bui-drop-identity-entity = Избавиться от {$entity}
changeling-transform-bui-drop-identity-entity-popup = Мы избавились от личности {$entity}.
changeling-transform-bui-drop-identity-cannot-drop = Мы не можем избавиться от текущей личности.

# voice mimicry
changeling-voice-mimic-window-title = Подражание голоса

# flesh clothing
changeling-flesh-clothing-removed-popop = {CAPITALIZE(THE($item))} распадается на мясистые куски!
changeling-flesh-clothing-examine-wearer = [color=crimson]Этот предмет замаскированная часть вашего тела. Он исчезнет, если вы его снимете![/color]
changeling-flesh-clothing-alert-name = Одежда из плоти
changeling-flesh-clothing-alert-desc = Нажмите, чтобы переключить имитацию одежды.

# biodegrade
changeling-biodegrade-used-popup = {CAPITALIZE(THE($user))} извергает кислоту на {POSS-ADJ($user)} {$restraint}!
changeling-biodegrade-used-popup-self = Мы извергаем кислоту на {$restraint}!


# last resort
changeling-takeover-not-dead = Это тело не мертво! Мы не можем заразить его.
changeling-takeover-is-changeling = Это тело отвергает наши попытки завладеть им!
changeling-takeover-start-others = {CAPITALIZE(THE($user))} начинает залезать в тело!
changeling-takeover-success-self = Мы успешно заразили тело и приняли его облик.

# stings
changeling-sting-success = Мы незаметно жалим {THE($target)}!

# other
changeling-paused-map-name = Карта хранения личностей генокрада

changeling-devour-doafter-windup = [color=red]{ CAPITALIZE(POSS-ADJ($user)) } лицо раскрывается, готовясь к пиршеству![/color]
changeling-devour-doafter = [color=red]{ CAPITALIZE(SUBJECT($user)) } разрывает плоть { $target }![/color]
changeling-transform-doafter = [color=red]Поверхность { POSS-ADJ($user) } кожи плавится, принимая другую форму.[/color]
changeling-takeover-doafter = [color=red]{ CAPITALIZE(SUBJECT($user)) } забирается внутрь { $target }![/color]
changeling-sting-hemotoxin = Вы чувствуете резкую боль в груди!
changeling-fake-mindshield-enabled = Мы излучаем поддельные волны импланта защиты разума.
changeling-fake-mindshield-disabled = Мы прекращаем излучать поддельные волны импланта защиты разума.
changeling-not-enough-chemicals = Нам не хватает химикатов.
changeling-action-fail-generic = Мы не можем сделать это сейчас.
changeling-cocoon-success = Мы заключаем { THE($target) } в гробницу из плоти.
changeling-cocoon-doafter = [color=red]{ CAPITALIZE(SUBJECT($user)) } покрывает { THE($target) } массой плоти![/color]
changeling-horror-alert-time-name = Оставшееся время
changeling-horror-alert-time-desc = Когда этот счётчик достигнет нуля, наши силы иссякнут.
changeling-horror-force-transform-self = Наша энергия иссякла, мы больше не можем поддерживать этот облик.
changeling-horror-force-transform-others = { CAPITALIZE(POSS-ADJ($user)) } плоть стремительно усыхает и отмирает.
changeling-horror-transform-fail = Мы накопили недостаточно ДНК, чтобы раскрыть свою истинную сущность.
