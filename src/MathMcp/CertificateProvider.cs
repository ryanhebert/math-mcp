using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MathMcp;

public static class CertificateProvider
{
    private const int KeySizeBits = 2048;
    private const int ValidityDays = 365;
    private const string SubjectName = "CN=localhost";
    private const string SanDnsName = "localhost";

    public static void EnsureCert(string pfxPath)
    {
        if (File.Exists(pfxPath)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(pfxPath)!);

        using var rsa = RSA.Create(KeySizeBits);
        var request = new CertificateRequest(
            SubjectName,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(SanDnsName);

        // Also add the machine name and FQDN so clients connecting via those
        // hostnames (instead of "localhost") don't get a TLS hostname-mismatch
        // warning. ResolveFqdn falls back to the machine name when no domain
        // suffix is set, so the dedupe below catches that case.
        var sansAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SanDnsName };
        TryAddSan(sanBuilder, sansAdded, NetInfo.ResolveFqdn());
        TryAddSan(sanBuilder, sansAdded, Environment.MachineName);

        request.CertificateExtensions.Add(sanBuilder.Build());

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") }, // serverAuth
                critical: false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.AddDays(ValidityDays);

        using var cert = request.CreateSelfSigned(notBefore, notAfter);
        var pfxBytes = cert.Export(X509ContentType.Pfx);
        File.WriteAllBytes(pfxPath, pfxBytes);
    }

    public static X509Certificate2 Load(string pfxPath) =>
        new(pfxPath, (string?)null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

    private static void TryAddSan(SubjectAlternativeNameBuilder b, HashSet<string> seen, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!seen.Add(name)) return;
        try { b.AddDnsName(name); }
        catch { /* silently skip invalid DNS labels */ }
    }
}
