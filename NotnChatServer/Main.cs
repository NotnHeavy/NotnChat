using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace NotnChat;

class Client
{
    public SslStream? Stream;
    public string? Name;
    public string? IP;
    public bool Running;
}

class Program
{
    static readonly Dictionary<TcpClient, Client> connected = new();
    static readonly Dictionary<string, Action<string>> commands = new()
    {
        { "help", Command_Help },
        { "kick", Command_Kick },
        { "status", Command_Status },
        // todo: work on ban command once config system is working.
    };
    static readonly Dictionary<string, Action<TcpClient>> clientRequestedCommmands = new()
    {
        { "status", ClientCommand_Status }
    };
    static X509Certificate? certificate;
    static readonly char[] specials = new char[] { '$', '\\' };
    static readonly StringBuilder inputStream = new();
    static string usingIP = "";
    static string usingPort = "";

    const int STD_OUTPUT_HANDLE = -11;
    const int NAME_LENGTH = 32;
    const int MESSAGE_LENGTH = 2000;
    const ushort MIN_PORT = 30000;
    const ushort MAX_PORT = 30009;
    const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 4;
    const string RED_TEXT = "\x1B[38;5;1m";
    const string GREEN_TEXT = "\x1B[38;5;2m";
    const string GREY_TEXT = "\x1B[38;5;7m";
    const string BLUE_TEXT = "\x1B[38;5;75m";

    static void Command_Help(string args)
    {
        Console.WriteLine("List of commands:");
        foreach (KeyValuePair<string, Action<string>> command in commands)
            Console.WriteLine($"\\{command.Key}");
    }

    static void Command_Kick(string args)
    {
        args = args.Trim().ToLower();
        foreach (KeyValuePair<TcpClient, Client> client in connected)
        {
            if ((client.Value.Name != null && client.Value.Name.ToLower() == args) || client.Value.IP == args)
            {
                SendtoAllAndServer($"{BLUE_TEXT}{client.Value.Name} has been kicked from the server by the server owner.{GREY_TEXT}", client.Key);
                Kick("You have been kicked by the server owner.", client.Key, kickImmediately: false);
                Console.WriteLine($"{GREEN_TEXT}Kicked {client.Value.Name}.{GREY_TEXT}");
            }
        }
        Console.WriteLine($"{RED_TEXT}Could not find anyone with the inputted name. {GREY_TEXT}");
    }

    static void Command_Status(string args)
    {
        Console.WriteLine($"Server {usingIP}:{usingPort}.\n{connected.Count} connected people.");
        foreach (KeyValuePair<TcpClient, Client> client in connected)
            Console.WriteLine($"{client.Value.Name} from IP {client.Value.IP}.");
    }

    static void ClientCommand_Status(TcpClient client)
    {
        string clients = "";
        foreach (KeyValuePair<TcpClient, Client> connectedClient in connected)
            clients += $"\"{connectedClient.Value.Name}\" ";
        Send(clients, client);
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

    static void SendtoAllAndServer(string input, TcpClient? except = null)
    {
        WriteLineAdjusted(input);
        SendToAll(input, except);
    }
    static void SendToAll(string input, TcpClient? except = null)
    {
        foreach (TcpClient client in connected.Keys)
        {
            if (client == except)
                continue;
            Send(input, client);
        }
    }
    static void Send(string input, TcpClient client, SslStream? backupStream = null)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(input);
        if (backupStream == null)
        {
            if (connected[client].Stream == null)
                return;
#pragma warning disable CS8602 // why do i need to suppress this
            connected[client].Stream.Write(buffer, 0, buffer.Length);
#pragma warning restore CS8602
        }
        else
            backupStream.Write(buffer, 0, buffer.Length);
    }
    static void Kick(string input, TcpClient client, SslStream? backupStream = null, bool kickImmediately = true)
    {
        Send($"${input}", client, backupStream);
        if (kickImmediately)
        {
            client.Close();
            if (connected.ContainsKey(client))
                connected.Remove(client);
        }
        else
        {
            Client clientStruct = connected[client];
            clientStruct.Running = false;
        }
    }

    static void NewClient(object? data)
    {
        if (data == null)
            return;

        bool retrievedName = false;
        TcpClient client = (TcpClient)data;
        NetworkStream stream = client.GetStream();
        SslStream sslStream = new(stream, false);
        IPEndPoint? endPoint = (IPEndPoint?)client.Client.RemoteEndPoint;
        if (endPoint == null)
        {
            Kick("Failed to reach end point.", client);
            return;
        }
        string IP = endPoint.Address.ToString();
        WriteLineAdjusted($"Incoming connection from {IP}.");

        try
        {
            if (certificate == null)
                return;
            sslStream.AuthenticateAsServer(certificate, false, true);
        }
        catch (IOException)
        {
            WriteLineAdjusted($"The connection has been closed from {IP} while authenticating.");
        }
        catch (Exception exception)
        {
            WriteLineAdjusted($"Rejected connection from {IP} due to not being able to verify SSL authentication:\n{exception.Message}");
            client.Close();
            return;
        }
        while (true)
        {
            try
            {
                if (connected.ContainsKey(client) && !connected[client].Running)
                {
                    client.Close();
                    return;
                }
                byte[] buffer = new byte[MESSAGE_LENGTH];
                sslStream.Read(buffer, 0, buffer.Length);
                string message = Encoding.UTF8.GetString(buffer).Trim('\0');
                if (retrievedName) // User is sending a message.
                {
                    if (message.Length > 0 && message[0] == '\\') // Requesting for command execution.
                    {
                        foreach (KeyValuePair<string, Action<TcpClient>> command in clientRequestedCommmands)
                        {
                            if (command.Key == message[1..])
                            {
                                command.Value(client);
                                break;
                            }
                        }
                    }
                    else
                        SendtoAllAndServer($"{GREEN_TEXT}{connected[client].Name}{GREY_TEXT}: {message}", client);
                }
                else if (buffer[0] != 0) // User has just joined the chatroom.
                {
                    foreach (KeyValuePair<TcpClient, Client> name in connected)
                    {
                        if (name.Value.Name == message)
                        {
                            Kick("This username is already being used in the chatroom.", client, sslStream);
                            WriteLineAdjusted($"{IP} is trying to use the username {message}, however this username is already registered. Aborting connection...");
                            return;
                        }
                    }
                    if (message.Length > 0 && specials.Contains(message[0]))
                    {
                        Kick("Cannot use special characters ($) at the start at your name.", client, sslStream);
                        WriteLineAdjusted($"{IP} is trying to use special characters ($, \\). Aborting connection...");
                        return;
                    }
                    if (message.Length > 32)
                    {
                        Send("Your username has been truncated due to being longer than 32 characters.", client, sslStream);
                        message = message[..32];
                    }
                    if (connected.Count == 0)
                        WriteLineAdjusted("Waking up server.");
                    connected.Add(client, new Client { Name = message, Stream = sslStream, IP = IP, Running = true });
                    WriteLineAdjusted($"{connected[client].Name} ({IP}) has entered the chatroom.");
                    SendToAll($"{BLUE_TEXT}{connected[client].Name} has entered the chatroom.{GREY_TEXT}", client);
                    retrievedName = true;
                }
                else
                {
                    WriteLineAdjusted($"{connected[client].Name} ({IP}) has left the chatroom.");
                    SendToAll($"{BLUE_TEXT}{connected[client].Name} has left the chatroom.{GREY_TEXT}", client);
                    return;
                }
            }
            catch (IOException) // User has just left the chatroom.
            {
                if (connected.ContainsKey(client))
                {
                    WriteLineAdjusted($"{connected[client].Name} ({IP}) has left the chatroom.");
                    SendToAll($"{BLUE_TEXT}{connected[client].Name} has left the chatroom.{GREY_TEXT}", client);
                }
                else
                    WriteLineAdjusted($"The connection from {IP} has abruptly ended, possibly due to not being able to validate SSH.");
                connected.Remove(client);
                if (connected.Count == 0)
                    WriteLineAdjusted("Server is now hibernating.");
                return;
            }
            catch (InvalidOperationException)
            {
                WriteLineAdjusted($"The connection from {IP} is not authenticated. Closing");
                client.Close();
                return;
            }
        }
    }

    static void AcceptClients(object? data)
    {
        if (data == null)
            return;
        TcpListener server = (TcpListener)data;
        while (true)
        {
            TcpClient client = server.AcceptTcpClient();
            Thread thread = new(NewClient);
            thread.Start(client);
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

        // App introduction.
        Console.Clear();
        Console.WriteLine($"{BLUE_TEXT}-------------------------------------------------\nLaunching NotnChat server.\n-------------------------------------------------");

        // Sort out certificate.
        if (File.Exists("certificate.pfx")) // Assume default with no password. Mostly for testing purposes.
        {
            try
            {
                certificate = new("certificate.pfx");
            }
            catch (CryptographicException exception)
            {
                Console.Write($"{GREY_TEXT}Failed to authenticate default certificate (certificate.pfx): {exception.Message}");
                certificate = null;
            }
        }
        while (certificate == null)
        {
            Console.Write($"{GREY_TEXT}Please select the path to your SSL certificate. This is required for establishing SSL security.\n>>> ");
            string? certLocation = Console.ReadLine();
            Console.Write("Please provide the password to your certificate (leave blank if it doesn't require one):\n>>> ");
            string? password = Console.ReadLine();
            if (File.Exists(certLocation))
            {
                certificate = new(certLocation, password);
                Console.Clear();
                Console.Write(BLUE_TEXT);
                break;
            }
        }

        // Get network IP.
        using (Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, 0))
        {
            socket.Connect("8.8.8.8", 65535);
            IPEndPoint? endPoint = (IPEndPoint?)socket.LocalEndPoint;
            if (endPoint != null)
                usingIP = endPoint.Address.ToString();
            else
            {
                Console.WriteLine("Could not connect to the internet.");
                return 1;
            }
        }

        // Launch listener and start accepting clients.
        for (ushort port = MIN_PORT; port <= MAX_PORT; port++)
        {
            try
            {
                Console.WriteLine($"Initiating server at address {usingIP}:{port}.");
                TcpListener server = new(IPAddress.Parse(usingIP), port);
                server.Start();
                Console.WriteLine($"Now accepting clients.\nUse \"\\help\" for a list of commands.{GREY_TEXT}");
                Thread thread = new(AcceptClients);
                thread.Start(server);
                usingPort = port.ToString();
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

                    // Send a message to every client.
                    if (input.Length > MESSAGE_LENGTH - 1)
                        input = input[..(MESSAGE_LENGTH - 1)];
                    Console.WriteLine($"{RED_TEXT}You (server){GREY_TEXT}: {input}");
                    if (input.Length > 0)
                        SendToAll($"\\{input}");
                }
            }
            catch
            {
                Console.WriteLine($"Cannot host server at address {usingIP}:{port}.");
            }
        }
        Console.WriteLine($"Cannot use ports inside allocated range {MIN_PORT} - {MAX_PORT}.");
        return 1;
    }
}