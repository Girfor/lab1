param(
    [Parameter(Mandatory=$true)][string]$Release,
    [Parameter(Mandatory=$true)][string]$OutputFile
)
$today = Get-Date -Format 'yyyy-MM-dd'
$template = @"
# Протокол SAT — EnergyMeter System

- Вариант: 15
- Релиз: $Release
- Дата испытания: $today
- Заказчик: ___________________
- Испытатель: _________________
- Среда / хост: ______________

| ID | Сценарий | Критерий приемки | Факт | Результат |
|---|---|---|---|---|
| SAT-01 | Прием показаний | 100 корректных событий, без потерь и дублей | — | Не проведено |
| SAT-02 | Автономный режим | Данные сохраняются при отключении сети | — | Не проведено |
| SAT-03 | Восстановление связи | Очередь выгружается без повторной записи | — | Не проведено |
| SAT-04 | Swagger Production | /swagger и debug недоступны (404/403) | — | Не проведено |
| SAT-05 | CSRF | Невалидный токен блокирует cookie-запрос | — | Не проведено |
| SAT-06 | Авторизация | Оператор не может выполнять админ-операции | — | Не проведено |
| SAT-07 | Резервное восстановление | Контрольная сумма и чтение БД успешны | — | Не проведено |

## Замечания и дефекты

Заполняется после фактических испытаний.

## Решение комиссии

Не принято до выполнения и подписания испытаний.

Подписи: ______________________________
"@
$folder = Split-Path -Parent $OutputFile
if ($folder) { New-Item -Path $folder -ItemType Directory -Force | Out-Null }
$template | Set-Content -LiteralPath $OutputFile -Encoding utf8
Write-Output "SAT template created: $OutputFile"
