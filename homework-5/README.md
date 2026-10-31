# CAMPUS-101 — безопасная миграция Passes → AccessPasses

Учебная работа по сопровождению ИС «Умный кампус» для версии **v1.9.0**.
Исполнитель: **Тюрин Даниил Дмитриевич**, группа **И13-24-11**, вариант **1**.
Основной отчёт: [REPORT.md](REPORT.md).

## Структура

```text
homework-5/
  REPORT.md
  CHANGELOG.md
  src/Campus.AccessControl.Api/{Program.cs, Controllers/, Data/, Models/}
  tests/Campus.AccessControl.SanityTests/
  sql/{00_demo_schema.sql, 01_expand.sql, 02_migrate.sql,
       03_contract.sql, verify_migration.sql}
  docs/{change-request-v1.9.0.md, branch-protection.md,
        adr/0009-your-feature.md}
```

CI располагается в корне Git-репозитория: `.github/workflows/ci.yml`.

## Локальный запуск (.NET 8 и PostgreSQL 16)

1. Создайте **отдельную учебную** БД PostgreSQL. Пример Docker:

   ```bash
   docker run --name campus101-db -e POSTGRES_DB=campus \
     -e POSTGRES_USER=campus -e POSTGRES_PASSWORD=local-only \
     -p 55432:5432 -d postgres:16
   ```

2. Из **корня репозитория**, где находится папка `homework-5`, выполните:

   ```bash
   export PGPASSWORD=local-only
   psql -h localhost -p 55432 -U campus -d campus -v ON_ERROR_STOP=1 \
     -f homework-5/sql/00_demo_schema.sql
   psql -h localhost -p 55432 -U campus -d campus -v ON_ERROR_STOP=1 \
     -f homework-5/sql/01_expand.sql
   ```

3. Запустите учебный API в режиме dual-write:

   ```bash
   export CAMPUS_DB_CONNECTION='Host=localhost;Port=55432;Database=campus;Username=campus;Password=local-only'
   export CAMPUS_API_KEY='local-test-key-change-me'
   export CAMPUS_SCHEMA_MODE=dual
   dotnet run --project homework-5/src/Campus.AccessControl.Api --urls http://localhost:5108
   ```

4. В другом терминале проведите перенос истории и SQL-проверку:

   ```bash
   psql -h localhost -p 55432 -U campus -d campus -v ON_ERROR_STOP=1 \
     -f homework-5/sql/02_migrate.sql
   psql -h localhost -p 55432 -U campus -d campus -v ON_ERROR_STOP=1 \
     -f homework-5/sql/verify_migration.sql
   ```

5. Обычные юнит-тесты:

   ```bash
   dotnet test homework-5/tests/Campus.AccessControl.SanityTests \
     --filter 'Category!=ProductionSanity'
   ```

6. Sanity-тест против разрешённого тестового сервера (можно использовать
   локальный `http://localhost:5108/`; реальный production запускать только
   после согласования с владельцем):

   ```bash
   export CAMPUS_SANITY_BASE_URL='http://localhost:5108/'
   export CAMPUS_SANITY_API_KEY='local-test-key-change-me'
   dotnet test homework-5/tests/Campus.AccessControl.SanityTests \
     --filter 'Category=ProductionSanity'
   ```

   Он создаёт `is_test=true`, проверяет чтение, удаляет запись в `finally`
   и проверяет 404. Если серверная запись завершилась, но ответ POST был
   потерян, возможен «осиротевший» тестовый пропуск: в production требуется
   фоновая очистка по `is_test` и TTL/времени создания.

7. После полного вывода старых приложений установить
   `CAMPUS_SCHEMA_MODE=new`, перезапустить API и повторить sanity.
   **Только после отдельного согласования** и сохранения backup:

   ```bash
   PGOPTIONS='-c campus.contract_approved=yes' \
     psql -h localhost -p 55432 -U campus -d campus -v ON_ERROR_STOP=1 \
     -f homework-5/sql/03_contract.sql
   ```

   Не запускать `03_contract.sql` до завершения переключения: действие
   удаляет старую таблицу, после него откат старого бинарника невозможен.

## Ограничения учебного примера

- `00_demo_schema.sql` предназначен **только для пустой учебной БД**.
  Перед работой с реальной схемой необходимо сравнить её колонки, индексы,
  ограничения, связи, права и зависимости со схемой примера.
- Нет действительного production-развёртывания, доступа к мониторингу и
  хранилищу секретов; CR — согласовываемый проект документа.
- Для реальной эксплуатации API-key следует заменить на OIDC/JWT с RBAC;
  мониторинг и реальную базу подключает администратор инфраструктуры.
- PostgreSQL DDL не гарантирует буквально нулевые блокировки, поэтому
  предусмотрен короткий тайм-аут и повтор по результатам наблюдения.

## Git и цифровой след

Из корня будущего репозитория:

```bash
git add .
git commit -m "[CAMPUS-101] Add tracing tags and business logic"
```

Связь артефактов: задача `CAMPUS-101` → commit/PR → два CI-статуса →
CR `CR-CAMPUS-101-190` → SQL → ADR 0009 → `CHANGELOG.md` → sanity-тест.
