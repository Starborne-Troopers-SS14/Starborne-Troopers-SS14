## Strings for the "grant_connect_bypass" command.

cmd-grant_connect_bypass-desc = Временно разрешает пользователю обходить обычные проверки при подключении.
cmd-grant_connect_bypass-help = Использование: grant_connect_bypass <пользователь> [длительность в минутах]
    Temporarily grants a user the ability to bypass regular connections restrictions.
    The bypass only applies to this game server and will expire after (by default) 1 hour.
    They will be able to join regardless of whitelist, panic bunker, or player cap.

cmd-grant_connect_bypass-arg-user = <пользователь>
cmd-grant_connect_bypass-arg-duration = [duration minutes]

cmd-grant_connect_bypass-invalid-args = Ожидалось 1 или 2 аргумента
cmd-grant_connect_bypass-unknown-user = Не удалось найти пользователя «{ $user }»
cmd-grant_connect_bypass-invalid-duration = Некорректная длительность «{ $duration }»

cmd-grant_connect_bypass-success = Пользователю «{ $user }» разрешён обход проверок
