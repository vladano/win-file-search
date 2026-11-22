using System.Globalization;
using System.Text;
using FileSearch.Core;

namespace FileSearch.App.Services;

/// <summary>
/// Small thread-safe file log at %AppData%\FileSearch\logs\filesearch.log. Rotates to filesearch.old.log at 1 MB,
/// so it never grows without bound. Logging never throws. A log created with a null path discards everything.
/// </summary>
public sealed class AppLog : ISearchLogger
{
    private const long MaxBytes = 1024 * 1024;

    private readonly string? _path;
    private readonly object _gate = new();

    public AppLog(string? path) => _path = path;

    /// <summary>A log that writes nothing (used by tests and as a safe default).</summary>
    public static AppLog Disabled { get; } = new(null);

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSearch", "logs", "filesearch.log");

    public void Info(string message) => Write("INFO", message, null);

    public void Warning(string message, Exception? exception = null) => Write("WARN", message, exception);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        if (_path is null) return;

        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
          .Append(" [").Append(level).Append("] ").Append(message);
        if (exception is not null)
        {
            // Full stack for errors; one line for the many routine warnings (locked files and so on).
            sb.Append(Environment.NewLine).Append("    ")
              .Append(level == "ERROR" ? exception.ToString() : $"{exception.GetType().Name}: {exception.Message}");
        }
        sb.Append(Environment.NewLine);

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                Rotate();
                File.AppendAllText(_path, sb.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Logging must never take the application down.
        }
    }

    private void Rotate()
    {
        var info = new FileInfo(_path!);
        if (!info.Exists || info.Length < MaxBytes) return;
        var old = Path.ChangeExtension(_path!, ".old.log");
        File.Move(_path!, old, overwrite: true);
    }
}
