# Практическая работа № 5

**Дисциплина:** ПМ 03. Сопровождение и обслуживание программного обеспечения компьютерных систем  
**Тема:** CALS-концепция цифрового следа, Change Management, Expand and Contract и Docs-as-Code в ИС «Умный кампус»  
**Выполнил:** Тюрин Даниил Дмитриевич  
**Группа:** И13-24-11  
**Вариант:** № 1 — «Турникеты и пропускной режим»  
**Задача:** CAMPUS-101 — переименование таблицы `Passes` в `AccessPasses`  
**Город, год:** Санкт-Петербург, 2026

## Цель и постановка задачи

Цель — связать изменение кода с карточкой задачи, тестами и эксплуатационными
документами; подготовить безопасную замену таблицы без одновременной остановки
всех клиентов. Для учебной микросервисной ИС выбрана подсистема пропусков
на турникетах. Существующая `Passes` остаётся рабочей на переходном этапе,
новая `AccessPasses` принимает копии записей.

Файлы решения:

| Артефакт | Относительная ссылка |
| --- | --- |
| Контроллер с тегами Activity | [AccessPassesController.cs](src/Campus.AccessControl.Api/Controllers/AccessPassesController.cs) |
| Реализация dual-write | [AccessPassRepository.cs](src/Campus.AccessControl.Api/Data/AccessPassRepository.cs) |
| Конфигурация OpenTelemetry | [Program.cs](src/Campus.AccessControl.Api/Program.cs) |
| Expand | [01_expand.sql](sql/01_expand.sql) |
| Migrate | [02_migrate.sql](sql/02_migrate.sql) |
| Contract | [03_contract.sql](sql/03_contract.sql) |
| SQL-проверка | [verify_migration.sql](sql/verify_migration.sql) |
| Sanity-тест | [ProductionSanityTests.cs](tests/Campus.AccessControl.SanityTests/ProductionSanityTests.cs) |
| Change Request | [change-request-v1.9.0.md](docs/change-request-v1.9.0.md) |
| Branch Protection | [branch-protection.md](docs/branch-protection.md) |
| ADR 0009 | [0009-your-feature.md](docs/adr/0009-your-feature.md) |
| Changelog | [CHANGELOG.md](CHANGELOG.md) |
| Инструкция по запуску | [README.md](README.md) |

## 1. Цифровой след CALS и OpenTelemetry

Клиентский HTTP-запрос сопровождается `System.Diagnostics.Activity.Current`.
В обработчиках POST, GET и DELETE выполняется установка:

```csharp
Activity.Current?.SetTag("jira.issue", "CAMPUS-101");
Activity.Current?.SetTag("campus.pass_id", id.ToString("D"));
```

В `Program.cs` подключено ASP.NET Core-инструментирование OpenTelemetry и
OTLP exporter. При создании `pass_id` указывается сразу после генерации UUID.
Теги позволяют сопоставить запрос и бизнес-сущность с задачей в Jira, не
передавая ФИО, номер удостоверения или другие персональные данные.

**Сообщение Git-коммита:**

```text
[CAMPUS-101] Add tracing tags and business logic
```

Матрица цифрового следа:

| Идентификатор | Связанный артефакт | Назначение |
| --- | --- | --- |
| CAMPUS-101 | Теги Activity и commit | Идентификация причины изменения |
| CR-CAMPUS-101-190 | Change Request | План, окно обслуживания, откат |
| ADR 0009 | `docs/adr/0009-your-feature.md` | Обоснование Expand and Contract |
| v1.9.0 | `CHANGELOG.md` | Пользовательская история релиза |
| CI + sanity | Workflow и xUnit | Проверка работоспособности |

## 2. Change Request и регламент релиза

**Карточка:** CR-CAMPUS-101-190, статус Proposed (учебный), релиз v1.9.0,
заявитель — Тюрин Д. Д. Бизнес-цель — уточнить модель доступа, заменив
неоднозначное имя `Passes` на `AccessPasses` без потери истории и без поломки
старых клиентов.

**Окно обслуживания:** 1 ноября 2026 г., 02:00–03:00 МСК. Это предлагаемое
воскресное ночное окно, когда учебный проход обычно меньше дневного.
Фактический выбор требует сравнения трафика турникетов за четыре недели;
реальные показатели в учебном задании недоступны.

**Предрелизные условия:** резервная копия и проверенная процедура
восстановления, 2 одобрения pull request, успешные `build-and-test` и
`sql-migration-check`, доступность мониторинга 5xx/p95 и дежурного инженера.

**План:** (1) backup и baseline, (2) `01_expand.sql`, (3) deploy v1.9.0 в
`dual`-режиме, (4) `02_migrate.sql`, (5) проверка равенства legacy-записей,
(6) sanity с cleanup, (7) наблюдение, (8) переключение `new` после прекращения
работы старых клиентов, (9) отдельное разрешение на `03_contract.sql`.

**Откат до Contract:**

```bash
kubectl rollout undo deployment/campus-access-control -n campus-prod
kubectl rollout status deployment/campus-access-control -n campus-prod
```

Пока `Passes` существует, прежний бинарник продолжает работать. После
`03_contract.sql` простой `rollout undo` уже **небезопасен**: нужен
совместимый hotfix или восстановление из backup по аварийному регламенту.

**Риски:** блокировки DDL, расхождение записей, оставшиеся legacy-подключения.
Меры: `lock_timeout=3s`, короткие пакеты с row locks, триггер для старых
клиентов, SQL-проверки, ручной guard и контроль клиентских версий.

Полное описание карточки содержится в
[change-request-v1.9.0.md](docs/change-request-v1.9.0.md).

### Branch Protection Rules

Для `main` предложены: только pull request, **2 одобрения**, сброс
устаревших одобрений после нового коммита, обязательные CI-проверки
`build-and-test` и `sql-migration-check`, актуальность ветки, разрешение
обсуждений, запрет force push/удаления, линейная история.
Это документируемая конфигурация, а не утверждение о уже включённых правилах.

## 3. Три фазы PostgreSQL Expand and Contract

Учебная схема: `id UUID` (PK), `person_id UUID`, `badge_code VARCHAR(64)`,
`valid_until TIMESTAMPTZ`, `is_active BOOLEAN`, `is_test BOOLEAN`,
`updated_at TIMESTAMPTZ`. Файл `00_demo_schema.sql` задаёт только исходную
тестовую таблицу и историческую запись. В существующей БД прежде потребуется
сверка фактических колонок, связей, индексов и прав.

### Фаза 1 — Expand

`01_expand.sql` создаёт `AccessPasses`, сохраняя `Passes`. Триггер
`trg_sync_access_passes` зеркалирует INSERT, UPDATE, DELETE от старых
приложений в той же транзакции. Релиз v1.9.0 при `CAMPUS_SCHEMA_MODE=dual`
записывает обе таблицы через один `NpgsqlTransaction`:

```csharp
await using var tx = await connection.BeginTransactionAsync(ct);
await WriteAsync(connection, tx, "\"Passes\"", pass, ct);
await WriteAsync(connection, tx, "\"AccessPasses\"", pass, ct);
await tx.CommitAsync(ct);
```

Здесь сокращённый фрагмент; реальный метод учитывает `dual`/`new` режимы.
Если вторая запись завершается ошибкой, транзакция не подтверждается.

### Фаза 2 — Migrate

`02_migrate.sql` переносит историю пакетами по 250 строк через
`FOR UPDATE OF p SKIP LOCKED`, освобождая row locks после каждого пакета.
Используются `ON CONFLICT DO NOTHING`, повтор для временно занятых строк и
последняя сверка всех старых записей с новыми. Скрипт нужно запускать в psql
с autocommit, без внешнего `BEGIN`.

### Фаза 3 — Contract

После полной адаптации клиентов приложение переключается в `new`-режим.
`03_contract.sql` проверяет наличие всех исторических ID в новой таблице и требует явного
`campus.contract_approved=yes`. Затем удаляет trigger/function и старую
таблицу `Passes`. Это необратимый breaking change для старого бинарника,
поэтому действие не выполняется во время первого развёртывания v1.9.0.

### Почему прямое RENAME запрещено

Старый код содержит SQL-ссылки на `Passes`. Одномоментное переименование
создаёт ошибку «relation does not exist» на работающих экземплярах.
Временное существование двух структур позволяет развернуть код по частям.

## 4. Sanity-тест и Docs-as-Code

Тест `[Trait("Category", "ProductionSanity")]` намеренно запускается
отдельно после релиза. Алгоритм: с разрешённым URL и API-key создать
тестовый пропуск (`is_test=true`), получить 201 и `id`, прочитать через GET
(200), в `finally` удалить через защищённый endpoint (204), затем
подтвердить отсутствие GET (404). Удалять обычные пропуска endpoint не может.

**Команда:**

```bash
dotnet test homework-5/tests/Campus.AccessControl.SanityTests \
  --filter 'Category=ProductionSanity'
```

Для выполнения заранее задаются `CAMPUS_SANITY_BASE_URL` и
`CAMPUS_SANITY_API_KEY`. CI не обращается к боевой БД и запускает только
юнит-тесты плюс SQL-проверку в изолированном PostgreSQL. Отчёт не выдаёт
исходные файлы за уже проверенный production: среда и доступы не предоставлены.

**ADR:** [0009-your-feature.md](docs/adr/0009-your-feature.md) фиксирует
контекст, выбор триггера + dual-write, альтернативы и последствия.
**Changelog:** [CHANGELOG.md](CHANGELOG.md) содержит Added, Changed, Fixed,
Breaking Changes и ссылку на отсроченное удаление старой схемы.

## 5. Результат и вывод

Подготовлены код ASP.NET Core 8, три обязательных SQL-скрипта, конфигурация
CI, sanity-тест с очисткой, CR, Branch Protection, ADR и Changelog.
Запуск тестов и релиза в настоящей инфраструктуре должен быть подтверждён
выводом CI/psql и логами дежурного; фактическое развёртывание не проводилось.
Поставленная задача решена на уровне воспроизводимого учебного проекта:
миграция допускает совместную работу старого и нового контракта на переходном
этапе, а опасное удаление выполняется только после отдельного допуска.

## Источники

1. Microsoft Learn. Add distributed tracing instrumentation —
   https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs
2. PostgreSQL Documentation. ALTER TABLE —
   https://www.postgresql.org/docs/current/sql-altertable.html
3. PostgreSQL Documentation. Overview of Trigger Behavior —
   https://www.postgresql.org/docs/current/trigger-definition.html
4. GitHub Docs. About protected branches —
   https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches
5. Keep a Changelog. Version 1.1.0 —
   https://keepachangelog.com/en/1.1.0/
