using System;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace ServerApp
{
    class Program
    {
        public static bool flag = false;
        public static bool flag1 = false;
        const string controllerIp = "127.0.0.1"; // آدرس IP RemoteController
        const int port = 5000; // پورتی که سرور گوش می‌دهد
        const int cport = 5001; // پورتی که سرور گوش می‌دهد
        const int fport = 5002; // پورتی که سرور گوش می‌دهد
        public static string clientName = Environment.MachineName;
        static TcpListener command_listener;
       private static string secretKey = "this-is-a-very-secure-and-long-key-1234567890";

        static void Main(string[] args)
        {
            StartServer();
            void StartServer()
            {
                TcpClient client = null;

                while (true)
                {
                    try
                    {
                        client = new TcpClient();
                        Console.WriteLine("\nAttempting to connect to RemoteController...");
                        client.Connect("127.0.0.1", 5000);
                        Console.WriteLine("Connected to RemoteController.");
                        break;
                    }
                    catch (SocketException)
                    {
                        Console.WriteLine("\nController is not available. Retrying in 5 seconds...");
                        Thread.Sleep(5000);
                    }
                }

                try
                {
                    NetworkStream stream = client.GetStream();
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                    StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                    while (true)
                    {
                        if (!flag)
                        {
                            writer.WriteLine($"register:{clientName}");
                            Console.WriteLine("\nRegistration message sent.");
                            flag = true;
                        }
                        else
                        {
                            writer.WriteLine($"heartbeat:{clientName}");
                            Console.WriteLine("\nHeartbeat message sent.");
                        }

                        // بررسی دستورات از کنترلر
                        while (stream.DataAvailable)
                        {
                            if (!flag1)
                            {
                                string receivedMessage = reader.ReadLine();

                                Console.WriteLine($"raw Command received: {receivedMessage}");

                                if (receivedMessage.StartsWith("cmd:"))
                                {
                                    string jwt = receivedMessage.Substring(4);
                                    if (ValidateJwt(jwt, out string command))
                                    {
                                        Console.WriteLine($"Command received: {command}");

                                        // اجرای دستور
                                        string result = ExecuteCommand(command);
                                        Console.WriteLine($"Result: {result}");

                                        // ارسال نتیجه
                                        string[] resultLines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                                        foreach (var line in resultLines)
                                        {
                                            writer.WriteLine($"result:{line}");
                                        }
                                        writer.WriteLine("endresult");
                                        flag1 = true;
                                    }
                                    else
                                    {
                                        Console.WriteLine("Invalid JWT. Message rejected.");
                                    }
                                }

                            }
                            else
                            {
                                string receivedMessage = reader.ReadLine();

                                Console.WriteLine($"raw Command received: {receivedMessage}");

                                if (receivedMessage.StartsWith("cmd:"))
                                {
                                    string jwt = receivedMessage.Substring(5);
                                    if (ValidateJwt(jwt, out string command))
                                    {
                                        Console.WriteLine($"Command received: {command}");

                                        // اجرای دستور
                                        string result = ExecuteCommand(command);
                                        Console.WriteLine($"Result: {result}");

                                        // ارسال نتیجه
                                        string[] resultLines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                                        foreach (var line in resultLines)
                                        {
                                            writer.WriteLine($"result:{line}");
                                        }
                                        writer.WriteLine("endresult");
                                    }
                                    else
                                    {
                                        Console.WriteLine("Invalid JWT. Message rejected.");
                                    }
                                }

                            }
                        }

                        Thread.Sleep(10000);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in communication: {ex.Message}");
                }
                finally
                {
                    client?.Close();
                    Console.WriteLine("Connection to RemoteController closed.");
                }
            }

            bool ValidateJwt(string token, out string command)
            {
                command = null;
                var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
                var tokenHandler = new JwtSecurityTokenHandler();

                try
                {
                    var validationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = securityKey,
                        ValidateIssuer = false,
                        ValidateAudience = false
                    };

                    var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
                    command = principal.FindFirst("command")?.Value;
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"JWT validation failed: {ex.Message}");
                    return false;
                }
            }

            Thread thread3 = new Thread(() =>
            {
               
                TcpListener listener = new TcpListener(IPAddress.Any, fport);
                listener.Start();
                Console.WriteLine($"\nListening for files on port {fport}...");

                while (true)
                {
                    try
                    {
                        using (TcpClient client = listener.AcceptTcpClient())
                        using (NetworkStream stream = client.GetStream())
                        using (BinaryReader reader = new BinaryReader(stream))
                        {
                            string fileName = reader.ReadString(); // دریافت نام فایل
                            string destinationPath = reader.ReadString(); // دریافت مسیر مقصد
                            long fileLength = reader.ReadInt64(); // دریافت طول فایل

                            string fullPath = Path.Combine(destinationPath, fileName);
                            Console.WriteLine($"Receiving file: {fileName} ({fileLength} bytes)");

                            using (FileStream fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                            {
                                byte[] buffer = new byte[8192];
                                int bytesRead;
                                long totalBytesRead = 0;

                                while (totalBytesRead < fileLength &&
                                       (bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    fs.Write(buffer, 0, bytesRead);
                                    totalBytesRead += bytesRead;

                                    // نمایش پراگرس بار
                                    ShowProgress(totalBytesRead, fileLength);
                                }
                            }

                            Console.WriteLine($"\nFile saved to {fullPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error receiving file: {ex.Message}");
                    }
                }
            
            });
            thread3.Start();

            void ShowProgress(long bytesTransferred, long totalBytes)
            {
                int progressBarWidth = 50; // عرض پراگرس بار
                double percentage = (double)bytesTransferred / totalBytes;
                int filledBars = (int)(percentage * progressBarWidth);

                Console.CursorLeft = 0;
                Console.Write("[");
                Console.Write(new string('#', filledBars));
                Console.Write(new string('-', progressBarWidth - filledBars));
                Console.Write($"] {percentage:P0}");
            }

            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                if (command_listener != null)
                {
                    command_listener.Stop();
                   
                    Console.WriteLine("Listener stopped on exit.");
                }
            };
        }
        static string GenerateHmac(string message, string secretKey)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey)))
            {
                byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
                return Convert.ToBase64String(hashBytes); // بازگشت رشته رمزگذاری‌شده
            }
        }
        static bool ValidateHmac(string message, string receivedHmac, string secretKey)
        {
            string calculatedHmac = GenerateHmac(message, secretKey);
            return calculatedHmac == receivedHmac;
        }

        static string ExecuteCommand(string command)
        {
            try
            {
                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"/c {command}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                return string.IsNullOrWhiteSpace(error) ? output : error;
            }
            catch (Exception ex)
            {
                return $"Error executing command: {ex.Message}";
            }
        }



    }
}