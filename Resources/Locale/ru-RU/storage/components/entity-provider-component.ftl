comp-entity-provider-cannot-receive = { CAPITALIZE(THE($refillTarget)) } нельзя пополнить!
comp-entity-provider-cannot-transfer = { CAPITALIZE(THE($provider)) } нельзя использовать для пополнения!
comp-entity-provider-full = { CAPITALIZE(THE($provider)) } уже заполнен!
comp-entity-provider-no-ejected = Нечего извлекать!
comp-entity-provider-select-entity = Выбрать: { MAKEPLURAL($entity) }.
comp-entity-provider-eject-all-specified-entities = Извлечь все: { MAKEPLURAL($entity) }.
comp-entity-provider-select-new-active = Сменить выбранный предмет в хранилище.
comp-entity-provider-no-stored-entities = Здесь пусто.
comp-entity-provider-has-entities = Здесь находится следующее:
comp-entity-provider-entity-listing = { $amount ->
    [one] [color=yellow]{ $amount }[/color] [color=gray]{ $name }[/color]
    *[other] [color=yellow]{ $amount }[/color] [color=gray]{ MAKEPLURAL($name) }[/color]
    }
