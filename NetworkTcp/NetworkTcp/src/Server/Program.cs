using System.Net;
using System.Net.Sockets;
using System.Text;

const int Port = 5000;
var listener = new TcpListener(IPAddress.Loopback, Port); 
listener.Start();
Console.WriteLine($"[SERVER] Listening on {IPAddress.Loopback}:{Port}");

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    while (!cts.IsCancellationRequested)
    {
        var client = await listener.AcceptTcpClientAsync(cts.Token);
        _ = HandleClientAsync(client, cts.Token); 
    }
}
catch (OperationCanceledException) { }
finally
{
    listener.Stop();
    Console.WriteLine("[SERVER] Stopped.");
}

static async Task HandleClientAsync(TcpClient client, CancellationToken ct)
{
    var endpoint = client.Client.RemoteEndPoint;
    Console.WriteLine($"[SERVER] Client connected: {endpoint}");

    using (client)
    using (var stream = client.GetStream())
    using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
               { AutoFlush = true })
    {
        await writer.WriteLineAsync("HELLO from server. Type 'exit' to close.");

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync();
            if (line is null) break;             
            if (line.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

            string response = $"[{DateTime.Now:HH:mm:ss}] echo: {line}";
            await writer.WriteLineAsync(response);
            Console.WriteLine($"[SERVER] <- '{line}' | -> '{response}'");
        }
    }

    Console.WriteLine($"[SERVER] Client disconnected: {endpoint}");
}
