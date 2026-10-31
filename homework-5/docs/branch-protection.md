# Предлагаемые Branch Protection Rules для `main`

Настройка в GitHub: **Settings → Branches → Add branch protection rule**,
branch name pattern: `main` (для публичного репозитория).

1. **Require a pull request before merging** — включить.
2. **Required approvals: 2** — включить; один автор не может сам себе одобрить.
3. **Dismiss stale pull request approvals** — включить после новых коммитов.
4. **Require status checks to pass** — включить `build-and-test` и
   `sql-migration-check` (после первого запуска workflow в GitHub).
5. **Require branches to be up to date before merging** — включить.
6. **Require conversation resolution** — включить.
7. **Block force pushes** и **block deletions** — включить; обход администраторами
   также запрещён, где позволяет тариф/права.
8. **Require linear history** — включить; слияние через squash/rebase.

В CI юнит-тесты выполняются отдельно от `ProductionSanity`: последний
предназначен только для ручного запуска после релиза с секретами в переменных
окружения. Правила выше являются **предлагаемой конфигурацией**; без доступа
к репозиторию нельзя утверждать, что они уже включены.

Документация GitHub:
https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches
