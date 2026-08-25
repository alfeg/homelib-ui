using System.Net;
using System.Net.Sockets;

namespace MyHomeLib.Web.Services;

/// <summary>
/// Builds an <see cref="HttpMessageHandler"/> that dials IPv4 addresses before IPv6.
/// </summary>
/// <remarks>
/// On a machine where "localhost" resolves to ::1 but nothing is listening there — common
/// when a VPN or proxy adapter is installed — every request pays a failed IPv6 connect
/// before falling back. Measured on this project: ~208ms per connection against
/// http://localhost:7701 versus ~0.8ms against http://127.0.0.1:7701, on a search
/// Meilisearch itself answers in 2ms. With a request per document batch, that tax lands on
/// indexing as well as on every query.
///
/// Ordering the addresses ourselves keeps the fix independent of whatever host name the
/// operator puts in configuration, and leaves genuine IPv6-only hosts working.
/// </remarks>
public static class Ipv4FirstHandler
{
    public static SocketsHttpHandler Create() => new()
    {
        ConnectCallback = async (context, cancellationToken) =>
        {
            var endpoint = context.DnsEndPoint;
            var addresses = await ResolveAsync(endpoint.Host, cancellationToken);

            Exception? lastError = null;

            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true,
                };

                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex)
                {
                    socket.Dispose();
                    lastError = ex;
                }
            }

            throw lastError ?? new SocketException((int)SocketError.HostNotFound);
        },
    };

    private static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal)) return [literal];

        var addresses = await Dns.GetHostAddressesAsync(host, ct);

        return [.. addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)];
    }
}
