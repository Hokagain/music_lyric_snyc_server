using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using music_lyric_snyc_server.Models;

namespace music_lyric_snyc_server.Services;

public sealed class LyricSocketServerService : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private TcpClient? _connectedClient;
    private StreamWriter? _writer;
    private LyricSocketPayload? _latestPayload;

    public int UdpPort { get; }
    public int TcpPort { get; }

    public event Action<string, string>? LogGenerated;

    public LyricSocketServerService(int udpPort = 33332, int tcpPort = 33333)
    {
        UdpPort = udpPort;
        TcpPort = tcpPort;
    }

    public Task StartAsync()
    {
        if (_loopTask is not null)
        {
            return Task.CompletedTask;
        }

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunStateMachineAsync(_cts.Token));
        Log("INFO", $"歌词网络服务已启动。UDP={UdpPort}, TCP={TcpPort}");
        return Task.CompletedTask;
    }

    public async Task PublishAsync(LyricSocketPayload payload)
    {
        lock (_sync)
        {
            _latestPayload = payload;
        }

        await SendIfConnectedAsync(payload);
    }

    private async Task RunStateMachineAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunUdpModeAsync(cancellationToken);
                await RunTcpModeAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log("ERROR", $"网络状态机异常: {ex.Message}，2 秒后重试。");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task RunUdpModeAsync(CancellationToken cancellationToken)
    {
        Log("INFO", $"进入 UDP 监听模式，端口 {UdpPort}。");
        using var udp = CreateUdpListener(UdpPort);

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udp.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var message = Encoding.UTF8.GetString(received.Buffer).Trim();
            if (!IsDiscoveryMessage(message))
            {
                continue;
            }

            var localIp = GetLocalIpv4Address();
            var response = Encoding.UTF8.GetBytes(localIp);
            await udp.SendAsync(response, received.RemoteEndPoint, cancellationToken);
            Log("INFO", $"收到 UDP 发现请求，已回复 {localIp} 给 {received.RemoteEndPoint.Address}:{received.RemoteEndPoint.Port}。");
            return;
        }
    }

    private async Task RunTcpModeAsync(CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Any, TcpPort);
        listener.Start();
        Log("INFO", $"切换到 TCP 模式，等待客户端连接，端口 {TcpPort}。");

        TcpClient client;
        try
        {
            client = await listener.AcceptTcpClientAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        client.NoDelay = true;
        using (client)
        using (var stream = client.GetStream())
        using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true })
        {
            lock (_sync)
            {
                _connectedClient = client;
                _writer = writer;
            }

            var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            Log("INFO", $"TCP 客户端已连接: {remote}");

            LyricSocketPayload? initialPayload;
            lock (_sync)
            {
                initialPayload = _latestPayload;
            }

            if (initialPayload is not null)
            {
                await SendIfConnectedAsync(initialPayload);
            }

            await MonitorClientDisconnectAsync(stream, cancellationToken);

            lock (_sync)
            {
                _connectedClient = null;
                _writer = null;
            }

            Log("WARN", "TCP 客户端已断开，返回 UDP 监听模式。");
        }
    }

    private async Task MonitorClientDisconnectAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        while (!cancellationToken.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                return;
            }

            if (read == 0)
            {
                return;
            }
        }
    }

    private async Task SendIfConnectedAsync(LyricSocketPayload payload)
    {
        StreamWriter? writer;
        lock (_sync)
        {
            writer = _writer;
        }

        if (writer is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload, _jsonOptions);

        await _sendGate.WaitAsync();
        try
        {
            await writer.WriteLineAsync(json);
        }
        catch (Exception ex)
        {
            Log("ERROR", $"发送 TCP 歌词信息失败: {ex.Message}");
            CloseConnectedClient();
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private void CloseConnectedClient()
    {
        lock (_sync)
        {
            try
            {
                _connectedClient?.Close();
            }
            catch
            {
                // Ignore close failures.
            }

            _connectedClient = null;
            _writer = null;
        }
    }

    private static bool IsDiscoveryMessage(string message)
    {
        return message.Equals("[who is server]", StringComparison.OrdinalIgnoreCase) ||
               message.Equals("who is server", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLocalIpv4Address()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            var properties = networkInterface.GetIPProperties();
            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(unicast.Address))
                {
                    return unicast.Address.ToString();
                }
            }
        }

        return "127.0.0.1";
    }

    private static UdpClient CreateUdpListener(int port)
    {
        var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        return udp;
    }

    private void Log(string level, string message)
    {
        LogGenerated?.Invoke(level, message);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
        }

        CloseConnectedClient();

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask;
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation.
            }
        }

        _sendGate.Dispose();
        _cts?.Dispose();
        Log("INFO", "歌词网络服务已停止。");
    }
}
