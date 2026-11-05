using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EnergyMeter.Security;

public static class SecretVault {
    public static byte[] EncryptForCurrentUser(string secret) {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows");
        return ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), optionalEntropy: null,
            DataProtectionScope.CurrentUser);
    }
    public static string DecryptForCurrentUser(byte[] ciphertext) {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(ciphertext, null,
            DataProtectionScope.CurrentUser));
    }
}

public static class PasswordService {
    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    public static bool Verify(string password, string storedHash) =>
        BCrypt.Net.BCrypt.Verify(password, storedHash);
}

public static class CertificateService {
    public static bool ValidateCertificateChain(X509Certificate2 server,
        X509RevocationMode revocation = X509RevocationMode.Online) {
        if (server.NotBefore.ToUniversalTime() > DateTime.UtcNow ||
            server.NotAfter.ToUniversalTime() < DateTime.UtcNow) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = revocation;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1")); // serverAuth
        return chain.Build(server);
    }
    // Use normal HttpClient validation for hostname + chain + TLS protocol checks;
    // never use a permissive ServerCertificateCustomValidationCallback.
    public static HttpClient CreateSecureHttpClient() => new(new HttpClientHandler {
        CheckCertificateRevocationList = true
    });
}
