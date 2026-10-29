using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models;
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
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SettingRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly string _directory = Path.Combine(ClientFileSources.SkuaDIR, "logs");
    private readonly object _lock = new();
    private readonly StringBuilder _pending = new();
    private readonly Timer _timer;
    private string _account = "unknown";
    private string? _lastLine;
    private string? _lastLineTime;
    private int _repeats;
    private bool? _enabled;
    private DateTime _enabledReadAt;
    private bool _cleanedUp;
    private bool _disposed;

    public LogFileWriter()
    {
        _timer = new Timer(_ => Flush(), null, FlushInterval, FlushInterval);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
    }

    /// <summary>The logged-in account; lines from then on go to its file.</summary>
    public void SetAccount(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return;

        string account = InvalidFileNameChars().Replace(username.Trim().ToLowerInvariant(), "_");
        lock (_lock)
        {
            if (account == _account)
                return;

            FlushLocked();
            _account = account;
        }
    }

    public void Add(LogType type, string message)
    {
        if (_disposed || _enabled == false || string.IsNullOrWhiteSpace(message))
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
        }
    }

    public void Flush()
    {
        lock (_lock)
            FlushLocked();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Dispose();
        lock (_lock)
        {
            AppendRepeatSummary();
            FlushLocked();
        }
    }

    private void AppendRepeatSummary()
    {
        if (_repeats == 0)
            return;

        _pending.Append($"[{_lastLineTime}] (previous line repeated {_repeats} more time{(_repeats == 1 ? string.Empty : "s")})\n");
        _repeats = 0;
    }

    private void FlushLocked()
    {
        if (_pending.Length == 0)
            return;

        if (!IsEnabled())
        {
            // Off, or the setting can't be read yet: don't let the waiting lines grow without limit.
            if (_enabled == false || _pending.Length > 1_000_000)
                _pending.Clear();
            return;
        }

        string text = _pending.ToString();
        _pending.Clear();
        try
        {
            Directory.CreateDirectory(_directory);
            if (!_cleanedUp)
            {
                _cleanedUp = true;
                DeleteOldFiles();
            }

            File.AppendAllText(CurrentFile(), text, Encoding.UTF8);
        }
        catch (IOException)
        {
            // Disk full or the file is locked: drop this batch rather than fail the logger (no Trace here, it would loop back).
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string CurrentFile()
    {
        string name = $"{_account}_{DateTime.Now:yyyy-MM-dd}";
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

    // The settings service may not exist yet when the first lines arrive; until it does, lines wait in memory.
    private bool IsEnabled()
    {
        if (_enabled is not null && DateTime.Now - _enabledReadAt < SettingRefreshInterval)
            return _enabled.Value;

        try
        {
            ISettingsService? settings = Ioc.Default.GetService<ISettingsService>();
            if (settings is null)
                return false;

            _enabled = settings.Get<bool>("LogToFile");
            _enabledReadAt = DateTime.Now;
            return _enabled.Value;
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
