# EnergyMeter — учебные проекты для Visual Studio

Здесь собраны исходные C# проекты EnergyMeter. Каждая папка в каталоге [projects](projects) соответствует своему учебному проекту или его версии.

## Версии для запуска по F5

| Проект | Файл для открытия |
| --- | --- |
| API, Swagger, CSRF | `projects/EnergyMeter_API_Swagger_CSRF_Demo_F5/EnergyMeter.Api.Demo.sln` |
| Безопасность (DPAPI, BCrypt, X.509) | `projects/EnergyMeter_Security_Demo_F5/EnergyMeter.Security.Demo.sln` |
| Офлайн-буфер, SQLite, EF Core, Polly | `projects/EnergyMeter_Offline_Demo_F5/EnergyMeter.Offline.sln` |
| Автоматическое обновление | `projects/EnergyMeter_AutoUpdater_Demo_F5/EnergyMeter.Updater.sln` |
| Backup и SAT | `projects/EnergyMeter_Backup_SAT_Demo_F5_v2/EnergyMeter.BackupSAT.Demo.sln` |
| Общее решение (API, тесты, безопасность, offline и updater) | `projects/EnergyMeter_Full_VisualStudio_Solution/EnergyMeterPortfolio/EnergyMeterPortfolio.sln` |
| ПР3, ПР4, ПР6 | `projects/EnergyMeter_Full_Solution/EnergyMeterProjects/EnergyMeter.sln` |

**Как запустить:** установить Visual Studio и .NET 8 SDK; открыть необходимый `.sln`, восстановить NuGet-пакеты и нажать F5. Если в проекте есть `README.md`, обратите внимание на его инструкции.

## Другие сохранённые версии

В каталоге `projects` также находятся предыдущие версии API, обновления, offline-модуля, безопасности и PowerShell-скриптов, а также исходники отдельных ПР3/ПР4.

> Важно: проекты учебные. Тестовые пароли в тестах — только фиктивные данные. Для демонстрационного входа в ПР4 задайте собственный `Demo__Password` через переменные окружения. Реальных учётных данных и токенов репозиторий содержать не должен.

Основная ветка `main` существующего репозитория `lab1` не изменялась.
