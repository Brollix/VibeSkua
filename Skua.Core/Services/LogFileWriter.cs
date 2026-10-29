using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Skua.Core.Services;

/// <summary>
/// Appends the Script and Debug logs to logs/&lt;account&gt;_&lt;yyyy-MM-dd&gt;.log in the Bonxie folder, so a run left alone
/// (overnight, on another machine) can be checked afterwards. Kept small on purpose: lines are written in batches every
/// couple of seconds, a line repeated back to back is written once with a count, a day's file rolls over to a new part
/// at 5 MB, and files older than 14 days are deleted. "Save Logs to File" (LogToFile) turns it off.
/// </summary>
internal sealed partial class LogFileWriter : IDisposable
{
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private const int KeepDays = 14;
    private const int RepeatSummaryEvery = 1000;
    private const int MaxPendingChars = 1_000_000;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SettingRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly string _directory = Path.Combine(ClientFileSources.SkuaDIR, "logs");
    // _lock guards the pending lines; _writeLock serializes disk writes, which happen outside _lock.
    private readonly object _lock = new();
    private readonly object _writeLock = new();
    private readonly StringBuilder _pending = new();
    private readonly Timer _timer;
    private string _account;
    private string? _lastLine;
    private string? _lastLineTime;
    private int _repeats;
    private volatile int _enabled = -1; // -1 unknown, 0 off, 1 on
    private DateTime _enabledReadAt;
    private bool _cleanedUp;
    private bool _disposed;

    public LogFileWriter()
    {
        // The Manager never logs in; its lines get their own file instead of mixing with a client's.
        string? entry = Assembly.GetEntryAssembly()?.GetName().Name;
        _account = entry?.Contains("Manager", StringComparison.OrdinalIgnoreCase) == true ? "manager" : "unknown";

        _timer = new Timer(_ => Flush(), null, FlushInterval, FlushInterval);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
        // ProcessExit doesn't run when the process dies of an unhandled exception, and those last lines matter most.
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Dispose();
    }

    /// <summary>The logged-in account; lines from then on go to its file.</summary>
    public void SetAccount(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return;

        string account = InvalidFileNameChars().Replace(username.Trim().ToLowerInvariant(), "_");
        string previousAccount;
        string text;
        lock (_lock)
        {
            if (account == _account)
                return;

            AppendRepeatSummary();
            _lastLine = null;
            previousAccount = _account;
            text = TakePending();
            _account = account;
        }

        if (text.Length > 0 && IsEnabled())
            Write(previousAccount, text);
    }

    public void Add(LogType type, string message)
    {
        if (_disposed || _enabled == 0 || string.IsNullOrWhiteSpace(message))
            return;

        string text = message.TrimEnd();
        string time = DateTime.Now.ToString("HH:mm:ss");
        lock (_lock)
        {
            string key = type == LogType.Debug ? "[debug] " + text : text;
            if (key == _lastLine)
            {
                _lastLineTime = time;
                if (++_repeats >= RepeatSummaryEvery)
                    AppendRepeatSummary();
                return;
            }

            AppendRepeatSummary();
            _lastLine = key;
            // Lines from CoreBots already start with [HH:mm:ss]; the rest get the time added.
            _pending.Append(StartsWithTime().IsMatch(text) ? key : $"[{time}] {key}").Append('\n');
            if (_pending.Length > MaxPendingChars && _enabled != 1)
                _pending.Clear(); // the setting can't be read yet: don't let the waiting lines grow without limit
        }
    }

    public void Flush()
    {
        // Read the setting on every tick, even with nothing pending, so turning it back on takes effect.
        bool enabled = IsEnabled();
        string account;
        string text;
        lock (_lock)
        {
            if (_pending.Length == 0)
                return;

            if (!enabled)
            {
                if (_enabled == 0)
                    _pending.Clear();
                return;
            }

            account = _account;
            text = TakePending();
        }

        Write(account, text);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Dispose();
        lock (_lock)
            AppendRepeatSummary();
        Flush();
    }

    private string TakePending()
    {
        string text = _pending.ToString();
        _pending.Clear();
        return text;
    }

    private void AppendRepeatSummary()
    {
        if (_repeats == 0)
            return;

        _pending.Append($"[{_lastLineTime}] (previous line repeated {_repeats} more time{(_repeats == 1 ? string.Empty : "s")})\n");
        _repeats = 0;
    }

    private void Write(string account, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        lock (_writeLock)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(_directory);
                    if (!_cleanedUp)
                    {
                        _cleanedUp = true;
                        DeleteOldFiles();
                    }

                    // Shared, so another client (or the Manager) appending to the same file doesn't make this one fail.
                    using FileStream stream = new(CurrentFile(account), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    stream.Write(bytes);
                    return;
                }
                catch (IOException) when (attempt == 0)
                {
                    Thread.Sleep(100);
                }
                catch (IOException)
                {
                    // Disk full or still locked: drop this batch rather than fail the logger (no Trace here, it would loop back).
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }
        }
    }

    private string CurrentFile(string account)
    {
        string name = $"{account}_{DateTime.Now:yyyy-MM-dd}";
        string path = Path.Combine(_directory, name + ".log");
        for (int part = 2; File.Exists(path) && new FileInfo(path).Length >= MaxFileBytes; part++)
            path = Path.Combine(_directory, $"{name}_{part}.log");
        return path;
    }

    private void DeleteOldFiles()
    {
        DateTime cutoff = DateTime.Now.AddDays(-KeepDays);
        foreach (string file in Directory.EnumerateFiles(_directory, "*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    // Called outside _lock: the settings service takes its own lock and a cross-process mutex.
    // It may not exist yet when the first lines arrive; until it does, lines wait in memory.
    private bool IsEnabled()
    {
        if (_enabled != -1 && DateTime.Now - _enabledReadAt < SettingRefreshInterval)
            return _enabled == 1;

        try
        {
            ISettingsService? settings = Ioc.Default.GetService<ISettingsService>();
            if (settings is null)
                return false;

            _enabled = settings.Get<bool>("LogToFile") ? 1 : 0;
            _enabledReadAt = DateTime.Now;
            return _enabled == 1;
        }
        catch (InvalidOperationException)
        {
            // Ioc not configured yet.
            return false;
        }
    }

    [GeneratedRegex(@"^\[\d{2}:\d{2}:\d{2}\]")]
    private static partial Regex StartsWithTime();

    [GeneratedRegex(@"[^a-z0-9_.-]")]
    private static partial Regex InvalidFileNameChars();
}
