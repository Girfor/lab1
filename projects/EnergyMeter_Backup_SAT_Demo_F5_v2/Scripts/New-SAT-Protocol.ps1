[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Release,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

$ErrorActionPreference = 'Stop'
$today = Get-Date -Format 'yyyy-MM-dd'
$folder = Split-Path -Parent $OutputFile
if ($folder -and -not (Test-Path -LiteralPath $folder)) {
    New-Item -Path $folder -ItemType Directory -Force | Out-Null
}

$template = @"
# Протокол SAT — EnergyMeter System

- Вариант: 15
- Релиз: $Release
- Дата: $today
- Исполнитель: ___________________
- Заказчик: ______________________
- Среда / хост: __________________

| ID | Сценарий | Критерий приёмки | Факт | Результат |
| --- | --- | --- | --- | --- |
| SAT-01 | Приём показаний | 100 корректных событий без потерь | — | Не проведено |
| SAT-02 | Автономный режим | Данные сохраняются при отключении сети | — | Не проведено |
| SAT-03 | Восстановление связи | Очередь выгружается без повторной записи | — | Не проведено |
| SAT-04 | Swagger Production | /swagger недоступен (404/403) | — | Не проведено |
| SAT-05 | CSRF | Недопустимый токен блокирует запрос | — | Не проведено |
| SAT-06 | Авторизация | Пользователь без прав не выполняет admin-операцию | — | Не проведено |
| SAT-07 | Резервное восстановление | SHA256 и чтение восстановленной БД успешны | — | Не проведено |

## Замечания и дефекты

Заполняются после фактических испытаний.

## Решение комиссии

Не принято до завершения всех испытаний.

Подписи: ______________________
"@

$template | Set-Content -LiteralPath $OutputFile -Encoding UTF8
Write-Host "SAT template created: $OutputFile"
