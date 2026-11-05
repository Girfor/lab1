using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EnergyMeter.Security;

/// <summary>Хранение небольших секретов в профиле текущего пользователя Windows.</summary>
public static class SecretVault
{
    public static byte[] EncryptForCurrentUser(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI доступен только в Windows.");

        return ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null,
            DataProtectionScope.CurrentUser);
    }

    public static string DecryptForCurrentUser(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI доступен только в Windows.");

        return Encoding.UTF8.GetString(ProtectedData.Unprotect(ciphertext, null,
            DataProtectionScope.CurrentUser));
    }
}

/// <summary>Невосстановимые хеши паролей BCrypt с автоматической солью.</summary>
public static class PasswordService
{
    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public static bool Verify(string password, string storedHash) =>
        BCrypt.Net.BCrypt.Verify(password, storedHash);
}

public static class CertificateService
{
    /// <summary>Проверка цепочки по доверенным сертификатам операционной системы.</summary>
    public static bool ValidateCertificateChain(X509Certificate2 server,
        X509RevocationMode revocation = X509RevocationMode.Online) =>
        ValidateCore(server, revocation, null);

    /// <summary>
    /// Проверка по явно переданному доверенному корню частного PKI.
    /// Передавать можно ТОЛЬКО предварительно проверенный доверенный сертификат.
    /// В учебной демонстрации корень создаётся временно в памяти.
    /// </summary>
    public static bool ValidateCertificateChainWithTrustedRoot(
        X509Certificate2 server, X509Certificate2 trustedRoot) =>
        ValidateCore(server, X509RevocationMode.NoCheck, trustedRoot);

    private static bool ValidateCore(X509Certificate2 server,
        X509RevocationMode revocation, X509Certificate2? trustedRoot)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (trustedRoot is not null)
            ArgumentNullException.ThrowIfNull(trustedRoot);

        var now = DateTime.UtcNow;
        if (server.NotBefore.ToUniversalTime() > now ||
            server.NotAfter.ToUniversalTime() < now)
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = revocation;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1")); // serverAuth

        if (trustedRoot is not null)
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(trustedRoot);
        }

        return chain.Build(server);
    }

    /// <summary>HTTP-клиент со штатной проверкой TLS; обход проверки запрещён.</summary>
    public static HttpClient CreateSecureHttpClient() => new(new HttpClientHandler
    {
        CheckCertificateRevocationList = true
    });
}
