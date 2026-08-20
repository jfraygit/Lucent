using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Lucent;

public static class SingleInstance
{
    private const int WaitMilliseconds = 4000;

    private static readonly string Id = Identity();

    private static Mutex? _claim;

    public static bool Claim()
    {
        try
        {
            _claim = new Mutex(true, $@"Local\Lucent.{Id}", out bool first);
            return first;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public static void Release()
    {
        try
        {
            _claim?.ReleaseMutex();
            _claim?.Dispose();
            _claim = null;
        }
        catch (Exception)
        {
        }
    }

    public static bool Forward(string? url)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", $"Lucent.{Id}", PipeDirection.Out);
            pipe.Connect(WaitMilliseconds);

            AllowSetForegroundWindow(AnyProcess);

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
            writer.WriteLine(url ?? string.Empty);
            writer.Flush();

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Listen(Action<string?> onLaunch)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        $"Lucent.{Id}", PipeDirection.In, 4, PipeTransmissionMode.Byte);

                    pipe.WaitForConnection();

                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    string? line = reader.ReadLine();

                    onLaunch(string.IsNullOrWhiteSpace(line) ? null : line.Trim());
                }
                catch (Exception)
                {
                    Thread.Sleep(500);
                }
            }
        })
        {
            IsBackground = true,
            Name = "Lucent launches"
        };

        thread.Start();
    }

    private static string Identity()
    {
        string source = (Environment.ProcessPath ?? "Lucent").ToLowerInvariant() +
                        "|" + Environment.UserName.ToLowerInvariant();

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));

        return Convert.ToHexString(hash, 0, 8);
    }

    private const uint AnyProcess = unchecked((uint)-1);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
