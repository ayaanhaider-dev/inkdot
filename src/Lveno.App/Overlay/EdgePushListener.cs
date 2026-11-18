using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia.Threading;
using Lveno.Plugins;

namespace Lveno.App.Overlay;

/// <summary>What the driver's push-to-switch plugin reports. Coordinates are in the live screen layout.</summary>
public sealed record EdgePushEvent(string Type, string Edge, uint Screen, uint From, uint To, float X, float Y, float Progress);

/// <summary>Listens on 127.0.0.1 for push progress and switches from the driver plugin.</summary>
public sealed class EdgePushListener : IDisposable
{
    private readonly UdpClient? _udp;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Raised on the UI thread.</summary>
    public event Action<EdgePushEvent>? Received;

    public EdgePushListener()
    {
        try
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, EdgePushFilter.EventPort));
            _ = Task.Run(LoopAsync);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Push-to-switch listener unavailable: {ex.Message}");
        }
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested && _udp != null)
        {
            try
            {
                var result = await _udp.ReceiveAsync(_stop.Token);
                if (Parse(result.Buffer) is { } e)
                    Dispatcher.UIThread.Post(() => Received?.Invoke(e), DispatcherPriority.Render);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore a bad message and keep listening.
            }
        }
    }

    private static EdgePushEvent? Parse(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        var r = doc.RootElement;
        string S(string name) => r.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
        uint U(string name) => r.TryGetProperty(name, out var v) && v.TryGetUInt32(out var u) ? u : 0;
        float F(string name) => r.TryGetProperty(name, out var v) && v.TryGetSingle(out var f) ? f : 0;
        return new EdgePushEvent(S("t"), S("e"), U("s"), U("from"), U("to"), F("x"), F("y"), F("p"));
    }

    public void Dispose()
    {
        _stop.Cancel();
        _udp?.Dispose();
    }
}
