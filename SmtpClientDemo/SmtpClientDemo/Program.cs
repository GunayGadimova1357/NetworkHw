using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using SmtpClientDemo;
using System.IO;

class Program
{
    static void Main()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
        
        Console.Write("From: ");
        string from = Console.ReadLine()!;

        Console.Write("To: ");
        string to = Console.ReadLine()!;

        Console.Write("Subject: ");
        string subject = Console.ReadLine()!;

        Console.Write("Body: ");
        string body = Console.ReadLine()!;
        
        var email = new EmailMessage
        {
            From = from,
            To = to,
            Subject = subject,
            Body = body
        };
        
        var sender = new EmailSender(config);
        sender.Send(email);
    }
}