using System.Net.Sockets;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.CursorVisible = false;

var host = args.Length > 0 ? args[0] : "127.0.0.1";
var port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 9000;

using var tcp = new TcpClient();
await tcp.ConnectAsync(host, port);
tcp.NoDelay = true;
using var stream = tcp.GetStream();
using var reader = new StreamReader(stream, Encoding.UTF8);
using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

char my = 'X', opp = 'O', turn = 'X';
while (true)
{
    var line = await reader.ReadLineAsync();
    if (line is null) throw new Exception("Connection closed before start.");
    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 2 && parts[0] == "ASSIGN")
    {
        var serverMark = parts[1][0];
        my = (serverMark == 'X') ? 'O' : 'X';
        opp = my == 'X' ? 'O' : 'X';
        turn = 'X';
        break;
    }
}

char[,] board = new char[3,3];
int cx = 1, cy = 1;
bool running = true;
bool gameOver = false;
string endMessage = "";

var rxTask = Task.Run(async () =>
{
    try
    {
        while (running)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                gameOver = true; endMessage = "Connection closed.";
                break;
            }
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1 && parts[0] == "QUIT")
            {
                gameOver = true; endMessage = "Opponent left.";
                break;
            }
            if (parts.Length == 3 && parts[0] == "MOVE" &&
                int.TryParse(parts[1], out var x) && int.TryParse(parts[2], out var y))
            {
                if (InRange(x,y) && board[y,x] == '\0')
                {
                    board[y,x] = opp;

                    if (CheckWin(board, opp))
                    {
                        gameOver = true; endMessage = "You lost.";
                    }
                    else if (IsBoardFull(board))
                    {
                        gameOver = true; endMessage = "Draw.";
                    }
                    else
                    {
                        turn = my; 
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        gameOver = true; endMessage = "Network error: " + ex.Message;
    }
});

try
{
    while (running)
    {
        Draw(board, cx, cy, my, turn, isMyTurn: turn == my && !gameOver, status: "[CLIENT]", gameOver, endMessage);

        var key = Console.ReadKey(true).Key;

        if (key == ConsoleKey.Escape)
        {
            await SafeSendAsync(writer, "QUIT");
            running = false;
            try {tcp.Client.Shutdown(SocketShutdown.Both);} catch { }
            try { tcp.Close(); } catch { }
            break;
        }

        if (!gameOver)
        {
            if (key == ConsoleKey.LeftArrow)  cx = Math.Max(0, cx - 1);
            if (key == ConsoleKey.RightArrow) cx = Math.Min(2, cx + 1);
            if (key == ConsoleKey.UpArrow)    cy = Math.Max(0, cy - 1);
            if (key == ConsoleKey.DownArrow)  cy = Math.Min(2, cy + 1);

            if (key == ConsoleKey.Enter && turn == my)
            {
                if (board[cy, cx] == '\0')
                {
                    board[cy, cx] = my;

                    await SafeSendAsync(writer, $"MOVE {cx} {cy}");

                    if (CheckWin(board, my))
                    {
                        gameOver = true; endMessage = "You won!";
                    }
                    else if (IsBoardFull(board))
                    {
                        gameOver = true; endMessage = "Draw.";
                    }
                    else
                    {
                        turn = opp;
                    }
                }
            }
        }
    }
}
finally
{
    running = false;
    await rxTask;
    Console.CursorVisible = true;
}

static void Draw(char[,] b, int cx, int cy, char me, char turn, bool isMyTurn, string status, bool gameOver, string endMessage)
{
    Console.SetCursorPosition(0, 0);
    Console.WriteLine($"{status} You: {me} | Turn: {turn} | {(isMyTurn ? "Your move" : "Waiting for opponent")}           ");
    Console.WriteLine();

    for (int y = 0; y < 3; y++)
    {
        for (int x = 0; x < 3; x++)
        {
            bool hl = (x == cx && y == cy) && isMyTurn;
            var ch = b[y, x] == '\0' ? ' ' : b[y, x];
            if (hl) Console.BackgroundColor = ConsoleColor.DarkGray;
            Console.Write($" {ch} ");
            Console.ResetColor();
        }
        Console.WriteLine();
    }

    Console.WriteLine();
    if (gameOver)
        Console.WriteLine($"Game over: {endMessage}  (Esc — exit)");
    else
        Console.WriteLine("Enter — place mark, Esc — exit");
}

static bool InRange(int x, int y) => x is >= 0 and <= 2 && y is >= 0 and <= 2;

static bool CheckWin(char[,] b, char who)
{
    for (int y = 0; y < 3; y++)
        if (b[y,0] == who && b[y,1] == who && b[y,2] == who) return true;

    for (int x = 0; x < 3; x++)
        if (b[0,x] == who && b[1,x] == who && b[2,x] == who) return true;

    if (b[0,0] == who && b[1,1] == who && b[2,2] == who) return true;
    if (b[0,2] == who && b[1,1] == who && b[2,0] == who) return true;

    return false;
}

static bool IsBoardFull(char[,] b)
{
    for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
            if (b[y, x] == '\0') return false;
    return true;
}

static async Task SafeSendAsync(StreamWriter w, string line)
{
    try { await w.WriteLineAsync(line); } catch {  }
}