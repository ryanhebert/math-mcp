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
}
