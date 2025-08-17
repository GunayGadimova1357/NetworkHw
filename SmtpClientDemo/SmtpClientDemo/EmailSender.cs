using System;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace SmtpClientDemo
{
    public class EmailSender
    {
        private readonly IConfiguration _config;

        public EmailSender(IConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Send(EmailMessage message)
        {
            try
            {
                
                string host = _config["Smtp:Host"] 
                    ?? throw new InvalidOperationException("Smtp:Host is missing in configuration.");

                int port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;

                bool enableSsl = bool.TryParse(_config["Smtp:EnableSsl"], out var ssl) && ssl;

                string username = _config["Smtp:Username"] ?? "";
                string password = _config["Smtp:Password"] ?? "";

                Console.WriteLine("➡ Connecting to SMTP server...");
                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = enableSsl,
                    Credentials = string.IsNullOrWhiteSpace(username)
                        ? CredentialCache.DefaultNetworkCredentials
                        : new NetworkCredential(username, password)
                };

                Console.WriteLine("➡ Building mail message...");
                using var mail = new MailMessage(message.From, message.To, message.Subject, message.Body);

                Console.WriteLine("➡ Sending...");
                client.Send(mail);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Sent successfully");
                Console.ResetColor();
            }
            catch (SmtpException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"SMTP error: {ex.StatusCode} - {ex.Message}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
            }
        }
    }
}