using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;

namespace NotnChat;

class Program
{
    static SslStream? sslStream = null;
    static bool ping;
    static readonly StringBuilder inputStream = new();
    static readonly Dictionary<string, Action<string>> commands = new()
    {
        { "help", Command_Help },
        { "status", Command_Status },
        { "changename", Command_ChangeName }
    };
    static string usingIP = "";
    static string usingPort = "";
    static string returnValue = "";

    const int STD_OUTPUT_HANDLE = -11;
    const int NAME_LENGTH = 32;
    const int MESSAGE_LENGTH = 2000;
    const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 4;
    const string RED_TEXT = "\x1B[38;5;1m";
    const string GREEN_TEXT = "\x1B[38;5;2m";
    const string GREY_TEXT = "\x1B[38;5;7m";
    const string BLUE_TEXT = "\x1B[38;5;75m";
    const string ORANGE_TEXT = "\x1B[38;5;166m";

    static List<string> ParseStringIntoList(string array)
    {
        List<string> list = new();
        array = array.Trim();
        string addition = "";
        int index = 0;
        while (index < array.Length)
        {
            if (array[index] != '\"')
                return list;
            ++index;
            while (index < array.Length && array[index] != '\"')
            {
                addition += array[index];
                ++index;
            }
            list.Add(addition);
            addition = "";
            ++index;
            while (index < array.Length && array[index] != '\"')
                ++index;
        }
        return list;
    }

    static void Command_Help(string args)
    {
        Console.WriteLine("List of commands:");
        foreach (KeyValuePair<string, Action<string>> command in commands)
            Console.WriteLine($"\\{command.Key}");
    }

    static void Command_Status(string args)
    {
        List<string> connected = ParseStringIntoList(RunCommandOnServer("\\status"));
        Console.WriteLine($"Connected to server {usingIP}:{usingPort}.\n{connected.Count} connected people.");
        foreach (string name in connected)
        {
            Console.WriteLine(name);
        }
    }

    static void Command_ChangeName(string args)
    {
        Console.WriteLine($"{RED_TEXT}coming soon!{GREY_TEXT}");
    }

    // Allow for clean certificates and self-signed certificates with an untrusted root.
    static bool VerifyCertificate(object? sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        if ((errors & SslPolicyErrors.RemoteCertificateChainErrors) != 0)
        {
            if (chain != null && chain.ChainStatus != null)
            {
                foreach (X509ChainStatus status in chain.ChainStatus)
                {
                    if (certificate != null && certificate.Subject == certificate.Issuer && status.Status == X509ChainStatusFlags.UntrustedRoot)
                        continue;
                    else if (status.Status != X509ChainStatusFlags.NoError)
                        return false;
                }
            }
            return true;
        }

        return false;
    }

    static void ClearCurrentLine()
    {
        int currentLine = Console.CursorTop;
        Console.SetCursorPosition(0, currentLine);
        Console.Write(new string(' ', Console.BufferWidth));
        Console.SetCursorPosition(Console.WindowWidth - 1, currentLine == 0 ? 0 : currentLine - 1);
    }

    static void Backspace(int count = 1)
    {
        for (int i = 0; i < count; ++i)
        {
            if (Console.CursorLeft == 0)
            {
                Console.SetCursorPosition(Console.WindowWidth - 1, Console.CursorTop - 1);
                Console.Write(" \b");
                Console.CursorLeft = Console.WindowWidth - 1;
            }
            else
                Console.Write("\b \b");
        }
    }

    static string RunCommandOnServer(string? input)
    {
        if (input == null || sslStream == null)
            return "";
        returnValue = "WAITING";
        SendToServer(input);
        while (returnValue == "WAITING")
        {

        }
        return returnValue;
    }

    static void SendToServer(string? input)
    {
        if (input == null || sslStream == null)            
            return;
        byte[] buffer = Encoding.UTF8.GetBytes(input);
        sslStream.Write(buffer, 0, buffer.Length);
    }

    static void Finish()
    {
        Console.WriteLine("The server has been closed abruptly.");
        Environment.Exit(0);
    }

    static string ReadLineAdjusted()
    {
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                string input = inputStream.ToString();
                inputStream.Clear();
                return input;
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (inputStream.Length > 0)
                {
                    inputStream.Remove(inputStream.Length - 1, 1);
                    Backspace();
                }
            }
            else
            {
                inputStream.Append(key.KeyChar);
                Console.Write(key.KeyChar);
                if (Console.CursorLeft == Console.WindowWidth - 1 && inputStream.Length % Console.WindowWidth == 0)
                {
                    if (Console.CursorTop == Console.BufferHeight - 1)
                    {
                        Console.WriteLine();
                        Console.SetCursorPosition(0, Console.CursorTop - 1);
                    }
                    else
                        Console.SetCursorPosition(0, Console.CursorTop + 1);
                }
            }
        }
    }

    static void WriteLineAdjusted(string? input)
    {
        if (input == null)
            return;

        // Clear current input stream.
        Backspace(inputStream.Length);

        // Write text and write input stream again.
        Console.CursorLeft = 0;
        Console.WriteLine(input);
        Console.Write(inputStream);
    }

    static void ReadIncoming(object? data)
    {
        if (data == null || sslStream == null)
            return;
        TcpClient client = (TcpClient)data;
        while (true)
        {
            try
            {
                byte[] buffer = new byte[NAME_LENGTH + MESSAGE_LENGTH];
                sslStream.Read(buffer, 0, buffer.Length);
                string message = Encoding.UTF8.GetString(buffer).Trim('\0');
                if (message.Length > 0 && message[0] == '$')
                {
                    Console.WriteLine($"{RED_TEXT}Your connection has been closed: {message[1..]}{GREY_TEXT}");
                    Environment.Exit(0);
                    break;
                }
                else if (message.Length > 0 && message[0] == '\\')
                    message = $"{ORANGE_TEXT}Server{GREY_TEXT}: {message[1..]}";
                else if (buffer[0] == 0)
                {
                    Finish();
                    return;
                }

                if (returnValue == "WAITING")
                    returnValue = message;
                else
                {
                    WriteLineAdjusted(message);
                    if (ping)
                    {
                        Thread thread = new(() => Console.Beep());
                        thread.Start();
                    }
                }
            }
            catch (IOException)
            {
                Finish();
                return;
            }
        }
    }

    static int Main()
    {
        // Enable ANSI escape sequences on Windows.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            static extern IntPtr GetStdHandle(int nStdHandle);

            [DllImport("kernel32.dll")]
            static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

            [DllImport("kernel32.dll")]
            static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

            IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
            GetConsoleMode(handle, out uint flags);
            SetConsoleMode(handle, flags | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
        }

        // Configuration.
        Console.Clear();
        Console.Write($"{GREY_TEXT}Choose your name:\n>>> ");
        string? name = Console.ReadLine();
        Console.Write("Choose the IP to connect to:\n>>> ");
        string? ip = Console.ReadLine();
        Console.Write("Allow pings? (Y/N)\n>>> ");
        string? pingText = Console.ReadLine();
        if (pingText != null)
            ping = pingText.Trim().ToLower() == "y";
        if (name == null || ip == null)
            return 1;

        // Parse the IP.
        string[] ipAndPort = ip.Split(':', StringSplitOptions.RemoveEmptyEntries);
        ushort port = 30000;
        if (ipAndPort.Length > 1)
        {
            ip = ipAndPort[0];
            _ = ushort.TryParse(ipAndPort[1], out port);
        }

        //// Initiate connection.
        TcpClient client = new();
        try
        {
            IPEndPoint endPoint = new(IPAddress.Parse(ip), port);
            client.Connect(endPoint);
            usingIP = ip;
            usingPort = port.ToString();
        }
        catch (Exception)
        {
            Console.WriteLine($"Could not connect to server {ip}:{port}: either it is incorrect or no server is currently running with that port.");
            return 1;
        }

        // SSL authentication.
        NetworkStream stream = client.GetStream();
        sslStream = new(stream, false, VerifyCertificate, null);
        try
        {
            Console.Write("Specify the subject of the certificate. If you have not been directed to one, leave this blank.\n>>> ");
            string? target = Console.ReadLine();
            if (target == null)
                return 1;
            sslStream.AuthenticateAsClient(target);
        }
        catch (AuthenticationException exception)
        {
            Console.WriteLine($"Failed to authenticate SSL connection: {exception.Message}");
            return 1;
        }

        // Start sending messages!
        Thread thread = new(ReadIncoming);
        thread.Start(client);
        SendToServer(name);
        if (name.Length > 32)
            name = name[..32];
        Console.WriteLine($"{BLUE_TEXT}Connected to server {ip}:{port}!{GREY_TEXT}");
        while (true)
        {
            string? input = ReadLineAdjusted().TrimStart();
            Backspace(input.Length);
            Console.CursorLeft = 0;

            // Parse commands.
            if (input.Length > 0 && input[0] == '\\')
            {
                // Check for escape first.
                if (input.Length > 1 && input[1] == '\\')
                    input = input[1..];
                else
                {
                    // Parse command and arguments.
                    string command = "";
                    int index = 1;
                    while (index < input.Length && !char.IsWhiteSpace(input[index]))
                    {
                        command += input[index];
                        ++index;
                    }
                    while (index < input.Length && char.IsWhiteSpace(input[index]))
                        ++index;
                    input = input[index..];
                    command = command.ToLower();

                    // Match command in dictionary and execute its corresponding code.
                    Console.WriteLine(new string(' ', Console.BufferWidth));
                    Console.CursorTop -= 1;
                    if (commands.ContainsKey(command))
                    {
                        Console.WriteLine($"{GREEN_TEXT}Executing command \"{command}\".{GREY_TEXT}");
                        commands[command](input);
                    }
                    else
                        Console.WriteLine($"{RED_TEXT}The command \"{command}\" does not exist in the commands dictionary.{GREY_TEXT}");
                    continue;
                }
            }

            // Send a message to the server.
            if (input.Length > MESSAGE_LENGTH)
                input = input[..MESSAGE_LENGTH];
            Console.WriteLine($"{RED_TEXT}You ({name}){GREY_TEXT}: {input}");
            SendToServer(input);
        }
    }
}