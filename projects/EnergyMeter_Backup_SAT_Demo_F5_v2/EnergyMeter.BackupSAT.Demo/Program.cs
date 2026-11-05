using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

return BackupSatDemo.Run(args);

internal static class BackupSatDemo
{
    private sealed record TestResult(string Name, bool Passed, string Description);

    public static int Run(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("ENERGYMETER — РЕЗЕРВНОЕ КОПИРОВАНИЕ И SAT (ВАРИАНТ 15)");
        Console.WriteLine(new string('=', 67));

        string? externalDb = null;
        if (args.Length != 0)
        {
            if (args.Length == 2 && args[0] == "--source")
                externalDb = args[1];
            else
            {
                Console.WriteLine("Использование: BackupSAT [--source <путь-к-существующей-базе.sqlite>]");
                return 2;
            }
        }

        var runFolder = Path.Combine(AppContext.BaseDirectory, "DemoOutput",
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6]);
        var sourceDir = Path.Combine(runFolder, "source");
        var backupDir = Path.Combine(runFolder, "backups");
        var restoreDir = Path.Combine(runFolder, "restore-test");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(backupDir);
        Directory.CreateDirectory(restoreDir);

        var sourceDb = externalDb is null
            ? Path.Combine(sourceDir, "energymeter-demo.sqlite")
            : Path.GetFullPath(externalDb);

        if (externalDb is null)
        {
            try { CreateSampleDatabase(sourceDb); }
            catch (Exception ex)
            {
                Console.WriteLine("Не удалось создать учебную базу: " + ex.Message);
                return 1;
            }
        }

        var backupDb = Path.Combine(backupDir, "energymeter-backup.sqlite");
        var hashFile = backupDb + ".sha256.txt";
        var restoredDb = Path.Combine(restoreDir, "restored.sqlite");
        var satFile = Path.Combine(runFolder, "SAT_Protocol.md");
        var reportFile = Path.Combine(runFolder, "demo-report.txt");
        var tests = new List<TestResult>();

        void Check(string name, Func<bool> action, string description)
        {
            try
            {
                bool ok = action();
                tests.Add(new TestResult(name, ok, description));
                Console.WriteLine($"[{tests.Count}] {name}: {(ok ? "PASS" : "FAIL")}");
            }
            catch (Exception ex)
            {
                tests.Add(new TestResult(name, false,
                    $"{description} ({ex.GetType().Name}: {ex.Message})"));
                Console.WriteLine($"[{tests.Count}] {name}: FAIL ({ex.GetType().Name}: {ex.Message})");
            }
        }

        Check("Исходная SQLite-база доступна", () =>
            File.Exists(sourceDb) && new FileInfo(sourceDb).Length > 0,
            externalDb is null ? "Создана новая учебная база с пятью показаниями" :
                "Проверено существование выбранной пользователем базы");

        Check("Резервная копия SQLite создана", () =>
        {
            using var source = OpenDatabase(sourceDb, SqliteOpenMode.ReadOnly);
            using var destination = OpenDatabase(backupDb, SqliteOpenMode.ReadWriteCreate);
            source.BackupDatabase(destination); // штатный SQLite Online Backup API
            return File.Exists(backupDb) && new FileInfo(backupDb).Length > 0;
        }, "Использован SQLite Online Backup API, исходная база не изменяется");

        Check("Контрольная сумма SHA-256 создана", () =>
        {
            string digest = Digest(backupDb);
            File.WriteAllText(hashFile,
                $"{digest}  {Path.GetFileName(backupDb)}{Environment.NewLine}",
                new UTF8Encoding(false));
            return File.Exists(hashFile) && digest.Length == 64;
        }, "Контрольная сумма записана в отдельный файл .sha256.txt");

        Check("Контрольная сумма SHA-256 совпадает", () =>
        {
            string expected = File.ReadAllText(hashFile).Trim().Split(' ',
                StringSplitOptions.RemoveEmptyEntries)[0];
            return expected.Equals(Digest(backupDb), StringComparison.OrdinalIgnoreCase);
        }, "Подтверждена неизменность резервного файла после копирования");

        Check("Целостность резервной базы проверена", () =>
        {
            using var database = OpenDatabase(backupDb, SqliteOpenMode.ReadOnly);
            return CheckIntegrity(database);
        }, "Выполнен SQLite PRAGMA integrity_check, результат должен быть ok");

        Check("Восстановление выполнено отдельно", () =>
        {
            File.Copy(backupDb, restoredDb);
            return File.Exists(restoredDb) &&
                Path.GetFullPath(restoredDb) != Path.GetFullPath(sourceDb);
        }, "Копия восстановлена в restore-test, оригинал не перезаписан");

        Check("Восстановленная база читается без потерь", () =>
        {
            using var backup = OpenDatabase(backupDb, SqliteOpenMode.ReadOnly);
            using var restored = OpenDatabase(restoredDb, SqliteOpenMode.ReadOnly);
            if (!CheckIntegrity(restored)) return false;
            var expected = GetTableCounts(backup);
            var actual = GetTableCounts(restored);
            return expected.Count > 0 && expected.OrderBy(x => x.Key)
                .SequenceEqual(actual.OrderBy(x => x.Key));
        }, "Сравнены число таблиц и количество записей после восстановления");

        Check("Протокол SAT сформирован", () =>
        {
            bool restorePassed = tests.Count == 7 && tests.All(x => x.Passed);
            string sat = BuildSatProtocol(restorePassed, externalDb is null);
            File.WriteAllText(satFile, sat, new UTF8Encoding(false));
            return File.Exists(satFile) && new FileInfo(satFile).Length > 0;
        }, "Создан SAT_Protocol.md; SAT-07 фиксирует реальные результаты, остальные сценарии не выдуманы");

        bool allPassed = tests.Count == 8 && tests.All(t => t.Passed);
        var report = new StringBuilder();
        report.AppendLine("EnergyMeter System — Backup & SAT Demo / вариант 15");
        report.AppendLine("Дата запуска: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
        report.AppendLine(externalDb is null ? "Режим: учебная база SQLite" : "Режим: резервная копия выбранной SQLite-базы");
        report.AppendLine("Источник: " + sourceDb);
        report.AppendLine("Резервная копия: " + backupDb);
        report.AppendLine("Контрольная сумма: " + hashFile);
        report.AppendLine("Восстановленная база: " + restoredDb);
        report.AppendLine();
        foreach (var test in tests)
        {
            report.AppendLine($"{test.Name}: {(test.Passed ? "PASS" : "FAIL")}");
            report.AppendLine("  " + test.Description);
        }
        report.AppendLine();
        report.AppendLine("ИТОГ: " + (allPassed ? "PASS" : "FAIL"));
        report.AppendLine("SAT-01..06: в данном автономном модуле не проверялись; требуется дополнительное подтверждение.");
        report.AppendLine("Исходный файл SQLite не перезаписывался.");
        File.WriteAllText(reportFile, report.ToString(), new UTF8Encoding(false));

        Console.WriteLine(new string('=', 67));
        Console.WriteLine(allPassed ? "ИТОГ: ВСЕ 8 ДЕМО-ТЕСТОВ ПРОЙДЕНЫ" : "ИТОГ: ОБНАРУЖЕНЫ ОШИБКИ");
        Console.WriteLine("Папка результатов: " + runFolder);
        Console.WriteLine("Отчёт: " + reportFile);
        Console.WriteLine("SAT: " + satFile);
        Console.WriteLine("Примечание: SAT-01..06 пока остаются неподтверждёнными в этом модуле.");
        if (Environment.UserInteractive && !Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.Write("Нажмите Enter, чтобы закрыть окно...");
            Console.ReadLine();
        }
        return allPassed ? 0 : 1;
    }

    private static void CreateSampleDatabase(string path)
    {
        using var database = OpenDatabase(path, SqliteOpenMode.ReadWriteCreate);
        using (var create = database.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE MeterReadings (
                    Id TEXT PRIMARY KEY,
                    MeterId TEXT NOT NULL,
                    Kwh REAL NOT NULL CHECK (Kwh >= 0),
                    TimestampUtc TEXT NOT NULL
                );
                """;
            create.ExecuteNonQuery();
        }
        for (int i = 1; i <= 5; i++)
        {
            using var insert = database.CreateCommand();
            insert.CommandText = """
                INSERT INTO MeterReadings (Id, MeterId, Kwh, TimestampUtc)
                VALUES ($id, $meter, $kwh, $utc);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("$meter", "EM-" + i.ToString("000"));
            insert.Parameters.AddWithValue("$kwh", 10.0 + i);
            insert.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.AddMinutes(-i).ToString("O"));
            insert.ExecuteNonQuery();
        }
    }

    private static SqliteConnection OpenDatabase(string filePath, SqliteOpenMode mode)
    {
        // Отключаем pooling: Windows может удерживать дескриптор SQLite-файла
        // после Dispose, из-за чего контрольная сумма получает IOException.
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = mode,
            Pooling = false
        };
        var connection = new SqliteConnection(cs.ToString());
        connection.Open();
        return connection;
    }

    private static string Digest(string filePath)
    {
        // Читаем завершённый backup потоково, с совместным доступом для
        // защитных программ и SQLite. Краткие сторонние блокировки повторяем.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, bufferSize: 8192,
                    options: FileOptions.SequentialScan);
                return Convert.ToHexString(SHA256.HashData(input));
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(200 * (attempt + 1));
            }
        }
    }

    private static bool CheckIntegrity(SqliteConnection database)
    {
        using var command = database.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        return string.Equals(command.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, long> GetTableCounts(SqliteConnection database)
    {
        var names = new List<string>();
        using (var find = database.CreateCommand())
        {
            find.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            using var reader = find.ExecuteReader();
            while (reader.Read()) names.Add(reader.GetString(0));
        }

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            using var count = database.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM \"" + name.Replace("\"", "\"\"") + "\";";
            counts[name] = Convert.ToInt64(count.ExecuteScalar());
        }
        return counts;
    }

    private static string BuildSatProtocol(bool backupPassed, bool demoMode)
    {
        var date = DateTime.Now.ToString("dd.MM.yyyy");
        var result = backupPassed ? "PASS" : "FAIL";
        var observed = demoMode ? "Учебная SQLite-база: backup, SHA-256, restore, integrity_check" :
            "Выбранная SQLite-база: backup, SHA-256, restore, integrity_check";

        return $"""
            # Протокол SAT — EnergyMeter System

            - Вариант: 15
            - Дата испытаний: {date}
            - Релиз: 1.0.0-demo
            - Исполнитель: ______________________________
            - Заказчик: ________________________________
            - Среда: Windows / .NET 8 / SQLite

            | ID | Сценарий | Критерий приёмки | Фактический результат | Статус |
            | --- | --- | --- | --- | --- |
            | SAT-01 | Приём показаний | 100 корректных событий без потерь | Не проверено этим модулем | Не проведено |
            | SAT-02 | Автономный режим | Данные сохраняются при отсутствии сети | См. отдельную демонстрацию Offline | Не проведено |
            | SAT-03 | Возобновление соединения | Очередь обрабатывается без повторов | См. отдельную демонстрацию Offline | Не проведено |
            | SAT-04 | Swagger Production | Swagger недоступен в Production (404/403) | Требуется отдельный запуск в Production | Не проведено |
            | SAT-05 | Защита от CSRF | Запрос без допустимого токена отклонён | См. отдельную демонстрацию API | Не проведено |
            | SAT-06 | Авторизация | Операция без прав запрещена | См. отдельную демонстрацию API | Не проведено |
            | SAT-07 | Резервное восстановление | Контрольная сумма, целостность и чтение базы подтверждены | {observed} | {result} |

            ## Замечания и дефекты

            Сценарии SAT-01—SAT-06 требуют прикрепления реальных свидетельств испытаний перед изменением их статуса.

            ## Решение комиссии

            Финальное решение по приёмке не принято до выполнения и подписания всех обязательных сценариев SAT.

            Подписи: ______________________________
            """ + Environment.NewLine;
    }
}
