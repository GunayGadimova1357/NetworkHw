using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TcpDuplexChat;

internal static class Program
{
    public static async Task Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("=== Chat ===");
        Console.Write("Your display name: ");
        var displayName = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(displayName)) displayName = $"User{Random.Shared.Next(1000,9999)}";

        Console.Write("Local listen port (e.g., 5001): ");
        var listenPortStr = Console.ReadLine();
        if (!int.TryParse(listenPortStr, out var listenPort) || listenPort <= 0 || listenPort > 65535)
        {
            Console.WriteLine("Invalid listen port.");
            return;
        }

        Console.Write("Remote IP (or empty to wait): ");
        var remoteIpStr = Console.ReadLine()?.Trim();
        IPAddress? remoteIp = null;
        if (!string.IsNullOrWhiteSpace(remoteIpStr))
        {
            if (!IPAddress.TryParse(remoteIpStr, out remoteIp))
            {
                Console.WriteLine("Invalid remote IP address.");
                return;
            }
        }

        Console.Write("Remote port (or empty to wait): ");
        var remotePortStr = Console.ReadLine();
        int? remotePort = null;
        if (!string.IsNullOrWhiteSpace(remotePortStr))
        {
            if (!int.TryParse(remotePortStr, out var rp) || rp <= 0 || rp > 65535)
            {
                Console.WriteLine("Invalid remote port.");
                return;
            }
            remotePort = rp;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        
        var node = new ChatNode(displayName!, listenPort);
        await node.RunAsync(remoteIp, remotePort, cts.Token);
    }
}

internal sealed class ChatNode : IAsyncDisposable
{
    private readonly string _name;
    private readonly int _listenPort;
    private readonly TcpListener _listener;
    private TcpClient? _connection;
    private NetworkStream? _netStream;
    private Task? _acceptLoopTask;
    private Task? _recvLoopTask;
    private Task? _sendLoopTask;
    private readonly object _consoleLock = new();

    public ChatNode(string displayName, int listenPort)
    {
        _name = displayName;
        _listenPort = listenPort;
        _listener = new TcpListener(IPAddress.Any, _listenPort);
    }

    public async Task RunAsync(IPAddress? remoteIp, int? remotePort, CancellationToken token)
    {
        _listener.Start();
        SafeWriteLine($"Listening on 0.0.0.0:{_listenPort} …");
        
        _acceptLoopTask = AcceptLoopAsync(token);
        
        if (remoteIp is not null && remotePort is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ConnectOutAsync(remoteIp, remotePort.Value, token);
                }
                catch (Exception ex)
                {
                    SafeWriteLine($"[connect] {ex.Message}");
                }
            }, token);
        }
        
        _sendLoopTask = SendLoopAsync(token);
        
        await WaitAnyCriticalAsync(token);
        
        await DisposeAsync();
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                if (token.IsCancellationRequested)
                {
                    client.Dispose();
                    break;
                }
                
                if (_connection is not null)
                {
                    SafeWriteLine("[accept] Already connected; dropping extra inbound connection.");
                    client.Close();
                    continue;
                }

                SetConnection(client);
                SafeWriteLine($"[accept] Inbound connected from {client.Client.RemoteEndPoint}");
                StartRecvLoop(token);
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception ex)
        {
            SafeWriteLine($"[accept] {ex.Message}");
        }
    }

    private async Task ConnectOutAsync(IPAddress ip, int port, CancellationToken token)
    {
        if (_connection is not null) return;

        var client = new TcpClient();
        SafeWriteLine($"[connect] Connecting to {ip}:{port} …");
        await client.ConnectAsync(ip, port, token);
        if (token.IsCancellationRequested) return;
        
        if (_connection is not null)
        {
            SafeWriteLine("[connect] Already connected via inbound; closing outbound socket.");
            client.Close();
            return;
        }

        SetConnection(client);
        SafeWriteLine($"[connect] Outbound connected to {client.Client.RemoteEndPoint}");
        StartRecvLoop(token);
    }

    private void SetConnection(TcpClient client)
    {
        _connection = client;
        _netStream = client.GetStream();
        _netStream.ReadTimeout = Timeout.Infinite;
        _netStream.WriteTimeout = Timeout.Infinite;
    }

    private void StartRecvLoop(CancellationToken token)
    {
        if (_recvLoopTask is not null) return;
        _recvLoopTask = Task.Run(() => RecvLoopAsync(token), token);
    }

    private async Task RecvLoopAsync(CancellationToken token)
    {
        if (_netStream is null) return;
        var reader = new StreamReader(_netStream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        try
        {
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line is null)
                {
                    SafeWriteLine("[net] Remote closed the connection.");
                    break;
                }
                SafeWriteLine(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException)
        {
            SafeWriteLine("[net] Connection lost.");
        }
        catch (Exception ex)
        {
            SafeWriteLine($"[recv] {ex.Message}");
        }
    }

    private async Task SendLoopAsync(CancellationToken token)
    {
        SafeWriteLine("Type messages and press Enter. Commands: /quit to exit");
        try
        {
            while (!token.IsCancellationRequested)
            {
                var text = await Console.In.ReadLineAsync();
                if (text is null) break;

                if (text.Equals("/quit", StringComparison.OrdinalIgnoreCase))
                    break;
                
                if (_netStream is null)
                {
                    SafeWriteLine("[warn] Not connected yet. Your message was not sent.");
                    continue;
                }

                var writer = new StreamWriter(_netStream, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true)
                { AutoFlush = true };

                var payload = $"{Timestamp()} [{_name}]: {text}";
                await writer.WriteLineAsync(payload);
                SafeWriteLine(payload, echoOnly: true);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SafeWriteLine($"[send] {ex.Message}");
        }
    }

    private async Task WaitAnyCriticalAsync(CancellationToken token)
    {
        var tasks = new List<Task>();
        if (_sendLoopTask is not null) tasks.Add(_sendLoopTask);
        if (_recvLoopTask is not null) tasks.Add(_recvLoopTask);

        if (tasks.Count == 0)
        {
            while (!token.IsCancellationRequested && _recvLoopTask is null && _sendLoopTask is null)
            {
                await Task.Delay(50, token);
            }
            if (_sendLoopTask is not null) tasks.Add(_sendLoopTask);
            if (_recvLoopTask is not null) tasks.Add(_recvLoopTask);
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAny(tasks);
        }
    }

    private void SafeWriteLine(string msg, bool echoOnly = false)
    {
        lock (_consoleLock)
        {
            Console.WriteLine(msg);
        }
    }

    private static string Timestamp() => DateTime.Now.ToString("HH:mm:ss");

    public async ValueTask DisposeAsync()
    {
        try { _listener.Stop(); } catch { }

        try
        {
            if (_netStream is not null) await _netStream.FlushAsync();
        }
        catch {  }

        try { _connection?.Close(); } catch { }
        
        await Task.Delay(50);
    }
}