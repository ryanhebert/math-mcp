using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MathMcp;

internal static class NetInfo
{
    public static bool TryProbeFreePort(int port)
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
            return false;
        }
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
