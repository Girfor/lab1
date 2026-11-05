# EnergyMeter System — учебное портфолио (вариант 15)

Содержимое демонстрирует архитектуру системы сбора показаний энергопотребления,
обратную совместимость API, безопасное обновление ПО, локальную очередь показаний,
контур защиты, резервное копирование и протокол приемочных испытаний SAT.

## Состав

- `src/EnergyMeter.Api` — минимальный ASP.NET Core API v1/v2, роли и защита от CSRF.
- `src/EnergyMeter.Updater` — проверка SemVer и SHA-256, загрузка в staging.
- `src/EnergyMeter.Offline` — SQLite/EF Core, Polly и идемпотентная отправка.
- `src/EnergyMeter.Security` — DPAPI, BCrypt, проверка сертификатов X.509.
- `scripts/backup.ps1` — резервная копия с контрольной суммой.
- `scripts/new-sat.ps1` — генератор SAT протокола Markdown.
- `tests/compatibility.http` — ручной контрактный сценарий v1/v2.
- `tests/EnergyMeter.Api.Tests` — интеграционные тесты совместимости xUnit.

## Запуск (после установки .NET 8 SDK)

Откройте `EnergyMeterPortfolio.sln` двойным щелчком в Visual Studio 2022
с рабочей нагрузкой «ASP.NET и разработка веб-приложений» и установленным
.NET 8 SDK. Все четыре проекта и проект интеграционных тестов находятся в
одном решении. Для самостоятельной работы можно открывать и отдельные `.csproj`.

```powershell
dotnet build EnergyMeterPortfolio.sln
dotnet restore src/EnergyMeter.Api/EnergyMeter.Api.csproj
dotnet run --project src/EnergyMeter.Api/EnergyMeter.Api.csproj
dotnet test tests/EnergyMeter.Api.Tests/EnergyMeter.Api.Tests.csproj
```

Для API откройте выведенный адрес, например `http://localhost:5000/api/v1/readings`.
Демо API хранит показания в памяти и предназначен для изучения контрактов,
а не для эксплуатации или хранения производственных данных.

Для учебной отправки показаний задайте секрет в текущем PowerShell процессе:

```powershell
$env:ENERGYMETER_API_KEY = "временный-учебный-ключ"
dotnet run --project src/EnergyMeter.Api/EnergyMeter.Api.csproj
```

Откройте второй терминал для отправки команд на тот же URL. Команды
офлайн-клиента:

```powershell
dotnet run --project src/EnergyMeter.Offline -- enqueue "b6764e22-92e1-4be0-9f32-f40c3d849d12" 15.3
dotnet run --project src/EnergyMeter.Offline -- flush "http://localhost:5000" "временный-учебный-ключ"
```

Обновлятор (`Updater`) требует действующий HTTPS-манифест и пакет с точно
вычисленной SHA-256 суммой:

```powershell
dotnet run --project src/EnergyMeter.Updater -- 1.0.0 "https://example.org/manifest.json" .\staging
```

Скрипты оператора (PowerShell 7 или Windows PowerShell):

```powershell
.\scripts\new-sat.ps1 -Release "1.4.1" -OutputFile ".\out\SAT-1.4.1.md"
# Сервис SQLite перед следующим примером должен быть остановлен:
.\scripts\backup.ps1 -SourceDb ".\energymeter-buffer.sqlite" -BackupDir ".\backups"
```

## Безопасность

В `Production` Swagger не регистрируется. Служебные операции доступны только
после аутентификации и авторизации. Для приложений с cookie-сессией требуется
проверка анти-CSRF токена на меняющих состояние запросах. Демонстрационный API
использует заголовочный ключ и не выдает его в репозитории; для эксплуатации
предпочтителен полноценный OIDC/JWT или mTLS с централизованной ротацией ключей.

SHA-256 проверяет целостность обновления, но **не удостоверяет издателя**.
Перед распространением обновлений необходимо дополнительно проверять цифровую
подпись манифеста (например, Ed25519 или X.509 CMS), доставлять его по TLS,
обеспечить атомарное переключение и процедуру отката.

DPAPI работает только в Windows. Резервные копии следует хранить вне целевого
сервера и проверять восстановление. SAT-шаблоны не заменяют фактически выполненных
испытаний — поля результатов остаются незаполненными.

## Проверка

Исходники подготовлены как учебные примеры. В данной среде .NET SDK отсутствует;
компиляция и реальные FAT/SAT испытания не выполнялись. Перед сдачей запустить
`dotnet build`, интеграционные тесты и зафиксировать скриншоты результатов.
