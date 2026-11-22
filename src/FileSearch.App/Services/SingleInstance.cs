using System.IO.Pipes;
using System.Text;

namespace FileSearch.App.Services;

/// <summary>
/// Keeps one app instance per user. Later launches (e.g. from the Explorer context menu) forward
/// their arguments to the running instance, which opens a new search tab.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();
    private static readonly string PipeName = "FileSearch.Pipe." + Environment.UserName;

    public bool IsFirst { get; }

    private SingleInstance(Mutex mutex, bool isFirst)
    {
        _mutex = mutex;
        IsFirst = isFirst;
    }

    public static SingleInstance Acquire()
    {
        var mutex = new Mutex(true, @"Local\FileSearch.SingleInstance." + Environment.UserName, out var created);
        return new SingleInstance(mutex, created);
    }

    /// <summary>Sends arguments to the first instance. Returns false if it could not be reached.</summary>
    public static bool Send(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            var bytes = Encoding.UTF8.GetBytes(string.Join('\n', args));
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <param name="onError">Called for unexpected pipe errors (the server keeps running).</param>
    public void StartServer(Action<string[]> onArgs, Action<Exception>? onError = null)
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_cts.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var text = await reader.ReadToEndAsync(_cts.Token);
                    var args = text.Length == 0 ? [] : text.Split('\n');
                    onArgs(args);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    onError?.Invoke(ex);
                    await Task.Delay(200);
                }
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }
}
