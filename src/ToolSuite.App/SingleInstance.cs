using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace ToolSuite.App;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource shutdown = new();
    private Task? listener;
    public bool IsPrimary { get; }
    public SingleInstance(string root)
    {
        var identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "|" + Path.GetFullPath(root).ToUpperInvariant())))[..24];
        pipeName = "ToolSuite-" + hash;
        mutex = new Mutex(true, @"Local\" + pipeName, out var created);
        IsPrimary = created;
    }
    public void Listen(Action activated)
    {
        listener = Task.Run(async () =>
        {
            while (!shutdown.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(shutdown.Token);
                    using var reader = new StreamReader(server);
                    if (await reader.ReadLineAsync(shutdown.Token) == "activate") activated();
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) { if (!shutdown.IsCancellationRequested) await Task.Delay(100, shutdown.Token); }
            }
        });
    }
    public async Task NotifyAsync()
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        await writer.WriteLineAsync("activate");
    }
    public void Dispose()
    {
        shutdown.Cancel();
        try { listener?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
        shutdown.Dispose();
    }
}
