using System.Security.Cryptography;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// F5 / запуск без аргументов: полностью локальная демонстрация.
// С тремя аргументами: режим проверки реального HTTPS-обновления.
if (args.Length == 0)
{
    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine("ENERGYMETER AUTO-UPDATER — ДЕМОНСТРАЦИЯ (ВАРИАНТ 15)");
    Console.WriteLine(new string('=', 58));

    int demoExitCode;
    try
    {
        demoExitCode = await DemoRunner.RunAsync() ? 0 : 1;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine("Ошибка демонстрации: " + error.Message);
        demoExitCode = 1;
    }

    if (!Console.IsInputRedirected)
    {
        Console.WriteLine();
        Console.Write("Нажмите Enter, чтобы закрыть окно...");
        Console.ReadLine();
    }
    return demoExitCode;
}

// Usage: dotnet run --project ... -- 1.0.0 https://example.org/manifest.json ./staging
if (args.Length != 3) {
    Console.Error.WriteLine("Usage: updater <installed-semver> <manifest-https-url> <staging-dir>");
    return 2;
}
try {
    var installed = SemVersion.Parse(args[0]);
    Uri manifestUri = new(args[1], UriKind.Absolute);
    if (manifestUri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("HTTPS required");
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var manifest = await client.GetFromJsonAsync<UpdateManifest>(manifestUri)
        ?? throw new InvalidDataException("Empty manifest");
    var target = SemVersion.Parse(manifest.Version);
    if (target.CompareTo(installed) <= 0) { Console.WriteLine("Up-to-date"); return 0; }
    Uri artifactUri = new(manifest.Url, UriKind.Absolute);
    if (artifactUri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("HTTPS required");
    if (!Regex.IsMatch(manifest.Sha256, "^[a-fA-F0-9]{64}$"))
        throw new InvalidDataException("Invalid SHA-256 format");
    Directory.CreateDirectory(args[2]);
    string part = Path.Combine(args[2], "energymeter-" + target + ".partial");
    string package = Path.Combine(args[2], "energymeter-" + target + ".zip");
    await using (var source = await client.GetStreamAsync(artifactUri))
    await using (var dest = File.Create(part)) await source.CopyToAsync(dest);
    await using (var stream = File.OpenRead(part)) {
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        if (!actual.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase)) {
            File.Delete(part);
            throw new CryptographicException("Package digest mismatch");
        }
    }
    File.Move(part, package, overwrite: true);
    Console.WriteLine("Verified package staged: " + package);
    Console.WriteLine("Installation requires separately approved atomic deployment and rollback");
    return 0;
} catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

public sealed record UpdateManifest(string Version, string Url, string Sha256);

internal static class DemoRunner
{
    public static async Task<bool> RunAsync()
    {
        // Каждому запуску выделяется собственная папка; чужие файлы не удаляются.
        string root = Path.Combine(Environment.CurrentDirectory, "DemoOutput",
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        string releases = Path.Combine(root, "releases");
        string staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(releases);
        Directory.CreateDirectory(staging);

        const string installedText = "1.0.0";
        const string availableText = "1.1.0";
        var installed = SemVersion.Parse(installedText);
        var available = SemVersion.Parse(availableText);
        bool newer = available.CompareTo(installed) > 0;

        Console.WriteLine($"[1] Установленная версия: {installed}");
        Console.WriteLine($"[2] Доступная версия:     {available}");
        Console.WriteLine("    SemVer: " + (newer ? "обновление найдено" : "нет обновлений"));

        // Формируем безопасный учебный пакет: ZIP содержит только текстовую заметку.
        string releaseFile = Path.Combine(releases, $"energymeter-{available}.zip");
        using (var archive = ZipFile.Open(releaseFile, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("README.txt");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.WriteLine("EnergyMeter System: demo release 1.1.0");
            writer.WriteLine("This sample is not executable software.");
            writer.WriteLine("Purpose: SemVer and SHA-256 validation.");
        }

        string expectedHash = await Sha256Async(releaseFile);
        var manifest = new UpdateManifest(availableText,
            "demo://energymeter-1.1.0.zip", expectedHash);
        string manifestFile = Path.Combine(root, "manifest.json");
        await File.WriteAllTextAsync(manifestFile,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }), Encoding.UTF8);

        // Читаем локальный манифест так же, как был бы прочитан сетевой JSON.
        var loadedManifest = JsonSerializer.Deserialize<UpdateManifest>(
            await File.ReadAllTextAsync(manifestFile),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Не удалось прочитать манифест");

        Console.WriteLine("[3] Локальный манифест создан");
        Console.WriteLine("    SHA-256: " + loadedManifest.Sha256);

        // Имитация загрузки: копируем архив в staging и проверяем контрольную сумму.
        string partial = Path.Combine(staging, $"energymeter-{available}.partial");
        File.Copy(releaseFile, partial);
        bool integrityOk = string.Equals(await Sha256Async(partial),
            loadedManifest.Sha256, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine("[4] Проверка оригинального пакета: " +
            (integrityOk ? "УСПЕШНО" : "ОШИБКА"));

        string stagedFile = Path.Combine(staging, $"energymeter-{available}.zip");
        if (newer && integrityOk)
        {
            File.Move(partial, stagedFile);
            Console.WriteLine("[5] Проверенный пакет помещен в staging");
        }

        // Отрицательный тест: портим копию пакета и проверяем обнаружение подмены.
        string tamperedFile = Path.Combine(root, "tampered-package.zip");
        File.Copy(releaseFile, tamperedFile);
        await using (var tampered = new FileStream(tamperedFile,
            FileMode.Append, FileAccess.Write))
        {
            await tampered.WriteAsync(Encoding.UTF8.GetBytes("UNAUTHORIZED MODIFICATION"));
        }
        bool corruptionDetected = !string.Equals(await Sha256Async(tamperedFile),
            loadedManifest.Sha256, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine("[6] Поврежденный пакет отклонен: " +
            (corruptionDetected ? "УСПЕШНО" : "ОШИБКА"));

        bool semanticRulesOk = SemVersion.Parse("1.10.0")
            .CompareTo(SemVersion.Parse("1.9.0")) > 0
            && SemVersion.Parse("1.1.0-beta.1").CompareTo(available) < 0;
        Console.WriteLine("[7] Тесты порядка SemVer: " +
            (semanticRulesOk ? "УСПЕШНО" : "ОШИБКА"));

        bool allPassed = newer && integrityOk && corruptionDetected && semanticRulesOk
            && File.Exists(stagedFile);
        string reportFile = Path.Combine(root, "demo-report.txt");
        await File.WriteAllLinesAsync(reportFile, new[]
        {
            "EnergyMeter System — Auto-Updater demo / вариант 15",
            $"Дата запуска: {DateTime.Now:dd.MM.yyyy HH:mm:ss}",
            $"Установлено: {installed}; доступно: {available}",
            $"SemVer: {(semanticRulesOk ? "PASS" : "FAIL")}",
            $"SHA-256 исходного пакета: {(integrityOk ? "PASS" : "FAIL")}",
            $"Обнаружение повреждения: {(corruptionDetected ? "PASS" : "FAIL")}",
            $"Помещение в staging: {(File.Exists(stagedFile) ? "PASS" : "FAIL")}",
            $"ИТОГ: {(allPassed ? "PASS" : "FAIL")}",
            "Демонстрация не устанавливает исполняемый код и не использует сеть.",
            "SHA-256 подтверждает целостность, но не подлинность издателя."
        }, Encoding.UTF8);

        Console.WriteLine(new string('-', 58));
        Console.WriteLine("ИТОГ: " + (allPassed ? "ВСЕ ДЕМО-ТЕСТЫ ПРОЙДЕНЫ" : "ЕСТЬ ОШИБКИ"));
        Console.WriteLine("Файлы демонстрации: " + root);
        Console.WriteLine("Отчёт: " + reportFile);
        Console.WriteLine("Примечание: реальное обновление НЕ устанавливалось.");
        return allPassed;
    }

    private static async Task<string> Sha256Async(string filename)
    {
        await using var stream = File.OpenRead(filename);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
}

public readonly record struct SemVersion(int Major, int Minor, int Patch, string? Prerelease)
    : IComparable<SemVersion>
{
    private static readonly Regex Pattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled);
    public static SemVersion Parse(string value) {
        var m = Pattern.Match(value);
        if (!m.Success) throw new FormatException("Invalid SemVer: " + value);
        return new(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
            int.Parse(m.Groups[3].Value), m.Groups[4].Success ? m.Groups[4].Value : null);
    }
    public int CompareTo(SemVersion other) {
        int core = Major.CompareTo(other.Major);
        if (core == 0) core = Minor.CompareTo(other.Minor);
        if (core == 0) core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;
        var a = Prerelease.Split('.'); var b = other.Prerelease.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) {
            bool an = long.TryParse(a[i], out var av), bn = long.TryParse(b[i], out var bv);
            int comparison = an && bn ? av.CompareTo(bv) : an ? -1 : bn ? 1
                : StringComparer.Ordinal.Compare(a[i], b[i]);
            if (comparison != 0) return comparison;
        }
        return a.Length.CompareTo(b.Length);
    }
    public override string ToString() => $"{Major}.{Minor}.{Patch}" +
        (Prerelease is null ? "" : "-" + Prerelease);
}
