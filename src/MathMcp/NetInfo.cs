using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MathMcp;

internal static class NetInfo
{
    public static bool TryProbeFreePort(int port, int retries = 5, int delayMs = 1000)
    {
        // Retry briefly so that a TCP port left in TIME_WAIT after a recent
        // service stop (e.g., during an upgrade) has a chance to clear before
        // we declare it unavailable. 5 × 1s = up to 5s tolerance.
        for (var attempt = 0; attempt < retries; attempt++)
        {
            try
            {
                var probe = new TcpListener(IPAddress.Any, port);
                probe.Start();
                probe.Stop();
                return true;
            }
            catch
            {
                if (attempt < retries - 1) System.Threading.Thread.Sleep(delayMs);
            }
        }
        return false;
    }

    public static string ResolveFqdn()
    {
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            var host = string.IsNullOrEmpty(props.HostName) ? Environment.MachineName : props.HostName;
            var domain = props.DomainName ?? "";
            if (string.IsNullOrEmpty(domain)) return host;
            if (host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)) return host;
            return $"{host}.{domain}";
        }
        catch
        {
            return Environment.MachineName;
        }
    }

    public static string HttpUrl(string host, int port, string path) =>
        port == 80 ? $"http://{host}{path}" : $"http://{host}:{port}{path}";

    public static string HttpsUrl(string host, int port, string path) =>
        port == 443 ? $"https://{host}{path}" : $"https://{host}:{port}{path}";
}
