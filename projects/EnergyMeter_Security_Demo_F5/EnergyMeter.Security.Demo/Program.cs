using EnergyMeter.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

return SecurityDemonstration.Run();

internal static class SecurityDemonstration
{
    public static int Run()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("ENERGYMETER SECURITY — ДЕМОНСТРАЦИЯ (ВАРИАНТ 15)");
        Console.WriteLine(new string('=', 62));

        var directory = Path.Combine(AppContext.BaseDirectory, "DemoOutput",
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(directory);

        var tests = new List<(string Name, string Status, string Details)>();
        void Check(string name, Func<bool> assertion, string details)
        {
            try
            {
                bool passed = assertion();
                tests.Add((name, passed ? "PASS" : "FAIL", details));
                Console.WriteLine($"[{tests.Count}] {name}: {(passed ? "PASS" : "FAIL")}");
            }
            catch (Exception ex)
            {
                tests.Add((name, "FAIL", $"{details}; {ex.GetType().Name}: {ex.Message}"));
                Console.WriteLine($"[{tests.Count}] {name}: FAIL ({ex.GetType().Name})");
            }
        }

        Check("Защита секрета Windows DPAPI", () =>
        {
            string secret = "EnergyMeter-demo-" + Guid.NewGuid().ToString("N");
            var ciphertext = SecretVault.EncryptForCurrentUser(secret);
            return ciphertext.Length > 0 &&
                   !ciphertext.SequenceEqual(Encoding.UTF8.GetBytes(secret)) &&
                   SecretVault.DecryptForCurrentUser(ciphertext) == secret;
        }, "Секрет зашифрован DPAPI и расшифрован в контексте текущего пользователя Windows");

        Check("DPAPI: повреждение данных отклонено", () =>
        {
            var ciphertext = SecretVault.EncryptForCurrentUser("test-integrity");
            ciphertext[ciphertext.Length / 2] ^= 0xFF;
            try
            {
                _ = SecretVault.DecryptForCurrentUser(ciphertext);
                return false;
            }
            catch (CryptographicException) { return true; }
        }, "Изменение зашифрованного сообщения приводит к отказу расшифрования");

        Check("BCrypt: правильный пароль принят", () =>
        {
            const string password = "Lab-Demo!\u21162026";
            var hash = PasswordService.Hash(password);
            return hash.StartsWith("$2", StringComparison.Ordinal) &&
                   !hash.Contains(password, StringComparison.Ordinal) &&
                   PasswordService.Verify(password, hash);
        }, "Проверена соль и положительная проверка BCrypt");

        Check("BCrypt: чужой пароль отклонён", () =>
        {
            var hash = PasswordService.Hash("correct-password");
            return !PasswordService.Verify("wrong-password", hash);
        }, "Отрицательная проверка BCrypt");

        Check("BCrypt: разные соли дают разные хеши", () =>
        {
            var a = PasswordService.Hash("same-password");
            var b = PasswordService.Hash("same-password");
            return a != b && PasswordService.Verify("same-password", a) &&
                   PasswordService.Verify("same-password", b);
        }, "Два хеша одного пароля различаются благодаря случайной соли");

        try
        {
            using var certificates = new LocalCertificates();

            Check("X.509: доверенная цепочка принята", () =>
                CertificateService.ValidateCertificateChainWithTrustedRoot(
                    certificates.ValidServer, certificates.Root),
                "Временный учебный корневой сертификат является явно доверенным; сеть не используется");

            Check("X.509: неизвестный корень отклонён", () =>
                !CertificateService.ValidateCertificateChain(
                    certificates.ValidServer, X509RevocationMode.NoCheck),
                "Учебный корень не установлен в системное хранилище доверия");

            Check("X.509: просроченный сертификат отклонён", () =>
                !CertificateService.ValidateCertificateChainWithTrustedRoot(
                    certificates.ExpiredServer, certificates.Root),
                "Невалидный период действия сертификата проверен локально");

            Check("X.509: неподходящее назначение отклонено", () =>
                !CertificateService.ValidateCertificateChainWithTrustedRoot(
                    certificates.WrongUsageServer, certificates.Root),
                "Сертификат Code Signing не подходит для аутентификации TLS-сервера");
        }
        catch (Exception ex)
        {
            Check("X.509: создание тестовых сертификатов", () => throw new InvalidOperationException(
                "Не удалось подготовить тестовые X.509 сертификаты", ex),
                "При исключении проверка получает FAIL");
        }

        Check("HTTP: защищённый клиент создан", () =>
        {
            using var client = CertificateService.CreateSecureHttpClient();
            return client is not null;
        }, "Создан HttpClient со штатной проверкой TLS, без сетевого запроса");

        var success = tests.All(t => t.Status == "PASS") && tests.Count == 10;
        var reportPath = Path.Combine(directory, "demo-report.txt");
        var report = new StringBuilder();
        report.AppendLine("EnergyMeter System — Security Demo / вариант 15");
        report.AppendLine("Дата запуска: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
        report.AppendLine("Среда: .NET 8, Windows, без сервера и интернет-соединения");
        report.AppendLine();
        foreach (var item in tests)
        {
            report.AppendLine($"{item.Name}: {item.Status}");
            report.AppendLine("  " + item.Details);
        }

        report.AppendLine();
        report.AppendLine("ИТОГ: " + (success ? "PASS" : "FAIL"));
        report.AppendLine("Примечание: корневой сертификат создан в памяти только для демонстрации.");
        report.AppendLine("Проверки безопасности являются локальными учебными тестами, не аудитом системы.");
        File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));

        Console.WriteLine(new string('=', 62));
        Console.WriteLine(success ? "ИТОГ: ВСЕ ДЕМО-ТЕСТЫ ПРОЙДЕНЫ" : "ИТОГ: ЕСТЬ ОШИБКИ");
        Console.WriteLine("Отчёт: " + reportPath);
        Console.WriteLine("Примечание: реальный сервер и сеть не использовались.");
        if (Environment.UserInteractive && !Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.Write("Нажмите Enter, чтобы закрыть окно...");
            Console.ReadLine();
        }

        return success ? 0 : 1;
    }
}

internal sealed class LocalCertificates : IDisposable
{
    private readonly RSA _rootKey = RSA.Create(2048);

    public X509Certificate2 Root { get; }
    public X509Certificate2 ValidServer { get; }
    public X509Certificate2 ExpiredServer { get; }
    public X509Certificate2 WrongUsageServer { get; }

    public LocalCertificates()
    {
        var rootRequest = new CertificateRequest("CN=EnergyMeter Demo CA " + Guid.NewGuid().ToString("N"),
            _rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        Root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-30),
            DateTimeOffset.UtcNow.AddDays(30));

        ValidServer = Issue("energymeter.demo.local", DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(7), true);
        ExpiredServer = Issue("expired.energymeter.demo.local", DateTimeOffset.UtcNow.AddDays(-14),
            DateTimeOffset.UtcNow.AddDays(-1), true);
        WrongUsageServer = Issue("codesign.energymeter.demo.local", DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(7), false);
    }

    private X509Certificate2 Issue(string hostname, DateTimeOffset from,
        DateTimeOffset until, bool serverAuthentication)
    {
        using var leafKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + hostname, leafKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature, true));
        var eku = new OidCollection();
        eku.Add(new Oid(serverAuthentication
            ? "1.3.6.1.5.5.7.3.1"   // serverAuth
            : "1.3.6.1.5.5.7.3.3")); // codeSigning
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(hostname);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        return request.Create(Root, from, until, RandomNumberGenerator.GetBytes(16));
    }

    public void Dispose()
    {
        WrongUsageServer.Dispose();
        ExpiredServer.Dispose();
        ValidServer.Dispose();
        Root.Dispose();
        _rootKey.Dispose();
    }
}
