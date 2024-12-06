using System;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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
        static void Main(string[] args)
        {
            Thread thread1 = new Thread(() =>
            {
                TcpClient client = null;

                // حلقه تلاش برای اتصال به کنترلر
                while (true)
                {
                    try
                    {
                        client = new TcpClient();
                        Console.WriteLine("\nAttempting to connect to RemoteController...");
                        client.Connect(controllerIp, port); // تلاش برای اتصال
                        Console.WriteLine("Connected to RemoteController.");
                        break; // خروج از حلقه در صورت اتصال موفق
                    }
                    catch (SocketException)
                    {
                        Console.WriteLine("\nController is not available. Retrying in 5 seconds...");
                        Thread.Sleep(5000); // انتظار برای تلاش مجدد
                    }
                }

                try
                {
                    NetworkStream stream = client.GetStream();
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                    StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                    while (true)
                    {
                        try
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
                                // خواندن پیام و حذف فضای خالی یا کاراکتر اضافی
                                string message = "";
                                
                                 message = reader.ReadLine();
                                Console.WriteLine($"\nRaw message received: {message}");

                                if (!string.IsNullOrEmpty(message))
                                {
                                    if (message.StartsWith("cmd:")) // دستور
                                    {
                                        if (!flag1)
                                        {
                                            string command = message.Substring(4);
                                            Console.WriteLine($"\nCommand received: {command}");

                                            // اجرای دستور CMD
                                            string result = ExecuteCommand(command);
                                            Console.WriteLine($"\nResult: {result}");

                                            // ارسال نتیجه
                                            string[] resultLines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                                            foreach (var line in resultLines)
                                            {
                                                writer.WriteLine($"result:{line}");
                                            }
                                            writer.WriteLine("endresult");
                                            message = "";
                                            flag1 = true;
                                        }
                                        else
                                        {
                                            string command = message.Substring(5);
                                            Console.WriteLine($"\nCommand received: {command}");

                                            // اجرای دستور CMD
                                            string result = ExecuteCommand(command);
                                            Console.WriteLine($"\nResult: {result}");

                                            // ارسال نتیجه
                                            string[] resultLines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                                            foreach (var line in resultLines)
                                            {
                                                writer.WriteLine($"result:{line}");
                                            }
                                            writer.WriteLine("endresult");
                                            message = "";

                                        }
                                        
                                    }
                                    
                                    else
                                    {
                                        Console.WriteLine($"Unknown message: {message}");
                                    }
                                }

                                // تخلیه بافر برای پاک‌سازی داده‌های باقی‌مانده
                                reader.DiscardBufferedData();
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error while processing data: {ex.Message}");
                            break;
                        }

                        Thread.Sleep(5000); // تأخیر قبل از ارسال پیام بعدی
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
            });

            thread1.Start();

           
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