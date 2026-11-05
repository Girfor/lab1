using System.Security.Cryptography;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

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
