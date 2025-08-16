using System.Net.Sockets;
using System.Text;

const string Host = "127.0.0.1";
const int Port = 5000;

try
{
    using var client = new TcpClient();
    Console.WriteLine($"[CLIENT] Connecting to {Host}:{Port}...");
    await client.ConnectAsync(Host, Port);

    using var stream = client.GetStream();
    using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
    using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        { AutoFlush = true };
    
    Console.WriteLine("[SERVER] " + (await reader.ReadLineAsync()));

    Console.WriteLine("Type message and press Enter (type 'exit' to quit):");
    while (true)
    {
        string? input = Console.ReadLine();
        if (string.IsNullOrEmpty(input)) continue;

        await writer.WriteLineAsync(input);
        if (input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

        string? reply = await reader.ReadLineAsync();
        Console.WriteLine("[SERVER] " + reply);
    }
}
catch (SocketException ex)
{
    Console.WriteLine($"[CLIENT] Socket error: {ex.Message}");
}