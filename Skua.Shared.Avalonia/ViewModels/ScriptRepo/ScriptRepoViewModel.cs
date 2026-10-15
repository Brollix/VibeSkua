using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Models.GitHub;
using Skua.Core.Models.Scripts;
using Skua.Core.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.Shared.Avalonia.ViewModels.ScriptRepo;

public partial class ScriptRepoViewModel : BotControlViewModelBase
{
    public ScriptRepoViewModel(
        IGetScriptsService getScripts,
        IProcessService processService,
        ICustomScriptService customScripts)
        : base("Search Scripts", 969, 500)
    {
        _getScriptsService = getScripts;
        _processService = processService;
        _customScripts = customScripts;
        OpenScriptFolderCommand = new RelayCommand(_processService.OpenVSC);
    }

    protected override void OnActivated()
    {
        _getScriptsService.PropertyChanged += GetScriptsService_PropertyChanged;

        if (IsBusy || IsAnyTaskRunning())
            return;

        if (_scripts.Count == 0 || _getScriptsService.Scripts.Count == 0)
        {
            _ = LoadInitialScripts();
        }
        else
        {
            _ = RefreshScriptsList();
        }
    }

    private async Task LoadInitialScripts()
    {
        IsBusy = true;
        try
        {
            Progress<string> progress = new(ProgressHandler);
            await _getScriptsService.GetScriptsAsync(progress, default);
        }
        catch { }
        finally
        {
            IsBusy = false;
        }
        await RefreshScriptsList();
    }

    protected override void OnDeactivated()
    {
        _getScriptsService.PropertyChanged -= GetScriptsService_PropertyChanged;
    }

    private void GetScriptsService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IGetScriptsService.Scripts))
        {
            _ = RefreshScriptsList();
        }
    }

    private readonly IGetScriptsService _getScriptsService;
    private readonly IProcessService _processService;
    private readonly ICustomScriptService _customScripts;

    [ObservableProperty]
    private bool _isManagerMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadedQuantity), nameof(OutdatedQuantity), nameof(ScriptQuantity), nameof(BotScriptQuantity))]
    private RangedObservableCollection<ScriptInfoViewModel> _scripts = new();

    [ObservableProperty]
    private RangedObservableCollection<ScriptInfoViewModel> _filteredScripts = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadedQuantity), nameof(OutdatedQuantity), nameof(ScriptQuantity), nameof(BotScriptQuantity))]
    private ScriptInfoViewModel? _selectedItem;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _progressReportMessage = string.Empty;

    [ObservableProperty]
    private string _sortBy = "Name";

    [ObservableProperty]
    private bool _sortDescending = false;

    [ObservableProperty]
    private string _filterBy = "All";

    public List<string> SortOptions { get; } = new() { "Name", "Date Created" };

    public List<string> FilterOptions { get; } = new()
    {
        "All", "Army", "Classes", "Dailies", "Evil", "Farm", "Good", "Legion", "Local", "Nation", "Other", "Rep", "Seasonal", "Story", "Ultras"
    };

    public int DownloadedQuantity => _scripts.Count(s => s.Downloaded);
    public int OutdatedQuantity => _scripts.Count(s => s.Outdated);
    public int ScriptQuantity => _scripts.Count;
    public int BotScriptQuantity => _scripts.Count;
    public bool ShowStartButton => !IsManagerMode;
    public IRelayCommand OpenScriptFolderCommand { get; }

    public string SortDirectionText => SortDescending ? "Desc" : "Asc";
    public string SortDirectionIcon => SortDescending ? "SortDescending" : "SortAscending";

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        FilterBy = "All";
    }

    private CancellationTokenSource? _searchCts;

    partial void OnIsManagerModeChanged(bool value) => OnPropertyChanged(nameof(ShowStartButton));
    
    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        CancellationToken token = _searchCts.Token;

        _ = Task.Run(async () =>
        {
            await Task.Delay(100, token);
            if (token.IsCancellationRequested) return;

            string query = (value ?? string.Empty).Trim().ToLowerInvariant();
            List<ScriptInfoViewModel> matches;

            if (string.IsNullOrWhiteSpace(query))
            {
                matches = _scripts.ToList();
            }
            else
            {
                matches = _scripts.Where(s => s.SearchIndex.Contains(query, StringComparison.Ordinal)).ToList();
            }

            if (token.IsCancellationRequested) return;

            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                FilteredScripts.ReplaceRange(matches);
                if (SelectedItem != null && !matches.Contains(SelectedItem))
                    SelectedItem = null;
            });
        }, token);
    }

    partial void OnSortByChanged(string value)
    {
        ApplySort();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        OnPropertyChanged(nameof(SortDirectionText));
        OnPropertyChanged(nameof(SortDirectionIcon));
        ApplySort();
    }

    partial void OnFilterByChanged(string value)
    {
        _ = RefreshScriptsList();
    }

    private void ApplySort()
    {
        if (_scripts.Count == 0) return;

        List<ScriptInfoViewModel> sorted = _scripts.ToList();
        if (SortBy == "Date Created")
        {
            if (SortDescending)
                sorted.Sort((a, b) => (b.Info.CreationDate ?? DateTime.MinValue).CompareTo(a.Info.CreationDate ?? DateTime.MinValue));
            else
                sorted.Sort((a, b) => (a.Info.CreationDate ?? DateTime.MinValue).CompareTo(b.Info.CreationDate ?? DateTime.MinValue));
        }
        else
        {
            if (SortDescending)
                sorted.Sort((a, b) => string.Compare(b.FileName, a.FileName, StringComparison.OrdinalIgnoreCase));
            else
                sorted.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
        }

        _scripts.ReplaceRange(sorted);
        ApplySearchFilter();
    }

    [RelayCommand]
    private void AddCustomFolder()
    {
        IFileDialogService? fileDialog = Ioc.Default.GetService<IFileDialogService>();
        if (fileDialog == null)
            return;

        string? folder = fileDialog.OpenFolder(ClientFileSources.SkuaScriptsDIR);
        if (!string.IsNullOrEmpty(folder))
        {
            ISettingsService? settings = Ioc.Default.GetService<ISettingsService>();
            settings?.Set("UserCustomScriptsFolder", folder);
            _ = RefreshScriptsList();
        }
    }

    [RelayCommand]
    private void ClearCustomFolder()
    {
        ISettingsService? settings = Ioc.Default.GetService<ISettingsService>();
        settings?.Set("UserCustomScriptsFolder", string.Empty);
        _ = RefreshScriptsList();
    }

    [RelayCommand]
    private void LoadLocalScript()
    {
        IFileDialogService? fileDialog = Ioc.Default.GetService<IFileDialogService>();
        if (fileDialog == null)
            return;

        string? path = fileDialog.OpenFile(ClientFileSources.SkuaScriptsDIR, "Skua Scripts (*.cs)|*.cs");
        if (!string.IsNullOrEmpty(path))
        {
            ISettingsService? settings = Ioc.Default.GetService<ISettingsService>();
            if (settings != null)
            {
                StringCollection? list = settings.Get<StringCollection>("UserCustomScriptsList") ?? new StringCollection();
                if (!list.Contains(path))
                {
                    list.Add(path);
                    settings.Set("UserCustomScriptsList", list);
                    _ = RefreshScriptsList();
                }
            }

            StrongReferenceMessenger.Default.Send<LoadScriptMessage, int>(new(path), (int)MessageChannels.ScriptStatus);
        }
    }

    [RelayCommand]
    private void OpenScript(ScriptInfoViewModel? script = null)
    {
        ScriptInfoViewModel? target = script ?? SelectedItem;
        if (target is null || !target.Downloaded)
            return;

        _processService.OpenVSC(target.LocalFile);
    }

    [RelayCommand]
    private async Task RefreshScripts(CancellationToken token)
    {
        IsBusy = true;
        try
        {
            await Task.Run(async () =>
            {
                Progress<string> progress = new(ProgressHandler);
                await _getScriptsService.RefreshScriptsAsync(progress, token);
            }, token);
        }
        catch (OperationCanceledException)
        {
            ProgressReportMessage = "Refresh cancelled.";
        }
        catch (Exception ex)
        {
            ProgressReportMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        await RefreshScriptsList();
    }

    private CancellationTokenSource? _updateDatesCts;

    private bool IsAnyTaskRunning()
    {
        return (_updateDatesCts != null && !_updateDatesCts.IsCancellationRequested) ||
               UpdateDatesCommand.IsRunning ||
               RefreshScriptsCommand.IsRunning ||
               DownloadAllCommand.IsRunning ||
               UpdateAllCommand.IsRunning;
    }

    [RelayCommand]
    private async Task UpdateDates(CancellationToken token)
    {
        if (IsBusy)
            return;

        _updateDatesCts?.Cancel();
        _updateDatesCts?.Dispose();
        _updateDatesCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var linkedToken = _updateDatesCts.Token;

        IsBusy = true;
        try
        {
            Progress<string> progress = new(s => ProgressReportMessage = s);
            await Task.Run(async () =>
            {
                await _getScriptsService.UpdateScriptDatesAsync(progress, linkedToken);
            }, linkedToken);
        }
        catch (OperationCanceledException)
        {
            ProgressReportMessage = "Update dates cancelled.";
        }
        catch (Exception ex)
        {
            ProgressReportMessage = $"Error: {ex.Message}";
        }
        finally
        {
            _updateDatesCts?.Dispose();
            _updateDatesCts = null;
            IsBusy = false;
        }
        await RefreshScriptsList();
    }

    private async Task RefreshScriptsList()
    {
        ISettingsService? settingsService = Ioc.Default.GetService<ISettingsService>();
        settingsService?.ReloadSettings();

        if (_getScriptsService?.Scripts != null)
        {
            List<ScriptInfoViewModel> scriptViewModels = await Task.Run(() =>
            {
                // 1. Single-pass fast dictionary of existing local files
                Dictionary<string, long> existingLocalFiles = new(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (Directory.Exists(ClientFileSources.SkuaScriptsDIR))
                    {
                        DirectoryInfo dirInfo = new(ClientFileSources.SkuaScriptsDIR);
                        foreach (FileInfo file in dirInfo.EnumerateFiles("*.cs", SearchOption.AllDirectories))
                        {
                            existingLocalFiles[Path.GetFullPath(file.FullName)] = file.Length;
                        }
                    }
                }
                catch { }

                List<ScriptInfoViewModel> viewModels = new(_getScriptsService.Scripts.Count);
                string filter = FilterBy;
                bool isAllFilter = filter == "All";

                foreach (ScriptInfo script in _getScriptsService.Scripts)
                {
                    if (!isAllFilter)
                    {
                        bool matches = false;
                        string[] parts = script.FilePath.Split('/', '\\');
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (parts[i].StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                            {
                                matches = true;
                                break;
                            }
                        }
                        if (!matches) continue;
                    }

                    if (string.IsNullOrWhiteSpace(script.Name) || script.Name.Equals("null", StringComparison.OrdinalIgnoreCase))
                        script.Name = !string.IsNullOrWhiteSpace(script.FileName) && !script.FileName.Equals("null", StringComparison.OrdinalIgnoreCase)
                            ? Path.GetFileNameWithoutExtension(script.FileName)
                            : Path.GetFileNameWithoutExtension(script.FilePath);

                    if (string.IsNullOrWhiteSpace(script.Description) || script.Description.Equals("null", StringComparison.OrdinalIgnoreCase))
                        script.Description = "No description provided.";

                    if (script.Tags == null || script.Tags.Length == 0 || (script.Tags.Length == 1 && script.Tags[0] == "null"))
                        script.Tags = new[] { "no-tags" };

                    string localPath = Path.GetFullPath(script.LocalFile);
                    bool downloaded = existingLocalFiles.TryGetValue(localPath, out long size) || (existingLocalFiles.Count == 0 && File.Exists(localPath));
                    int localSize = downloaded ? (int)(size > 0 ? size : (File.Exists(localPath) ? new FileInfo(localPath).Length : 0)) : 0;
                    script.CachedDownloaded = downloaded;
                    script.CachedLocalSize = localSize;

                    viewModels.Add(new ScriptInfoViewModel(script, null, false, downloaded, localSize));
                }

                // 2. Custom Folders and Custom Scripts
                try
                {
                    if (settingsService != null)
                    {
                        string? localFolder = settingsService.Get<string>("UserCustomScriptsFolder");
                        if (!string.IsNullOrEmpty(localFolder) && Directory.Exists(localFolder))
                        {
                            DirectoryInfo customDir = new(localFolder);
                            foreach (FileInfo file in customDir.EnumerateFiles("*.cs", SearchOption.AllDirectories))
                            {
                                if (!isAllFilter && filter != "Local") continue;

                                ScriptInfo info = new()
                                {
                                    Name = Path.GetFileNameWithoutExtension(file.Name),
                                    FileName = file.Name,
                                    FilePath = file.FullName,
                                    Description = "Local Custom Script",
                                    Tags = new[] { "Local" },
                                    CreationDate = file.CreationTime,
                                    Size = (int)file.Length,
                                    Sha256 = null,
                                    CachedDownloaded = true,
                                    CachedLocalSize = (int)file.Length
                                };
                                viewModels.Add(new ScriptInfoViewModel(info, file.FullName, isCustom: true, isDownloaded: true, localSize: (int)file.Length));
                            }
                        }

                        StringCollection? scriptList = settingsService.Get<StringCollection>("UserCustomScriptsList");
                        if (scriptList != null && scriptList.Count > 0)
                        {
                            HashSet<string> seenPaths = new(viewModels.Select(v => v.LocalFile), StringComparer.OrdinalIgnoreCase);
                            foreach (string? filePath in scriptList)
                            {
                                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) continue;
                                if (!isAllFilter && filter != "Local") continue;
                                if (seenPaths.Contains(filePath)) continue;

                                FileInfo file = new(filePath);
                                ScriptInfo info = new()
                                {
                                    Name = Path.GetFileNameWithoutExtension(file.Name),
                                    FileName = file.Name,
                                    FilePath = file.FullName,
                                    Description = "Local Single Script",
                                    Tags = new[] { "Local", "Single" },
                                    CreationDate = file.CreationTime,
                                    Size = (int)file.Length,
                                    Sha256 = null,
                                    CachedDownloaded = true,
                                    CachedLocalSize = (int)file.Length
                                };
                                seenPaths.Add(filePath);
                                viewModels.Add(new ScriptInfoViewModel(info, filePath, isCustom: true, isDownloaded: true, localSize: (int)file.Length));
                            }
                        }
                    }
                }
                catch { }

                // 3. Fast in-place sorting
                if (SortBy == "Date Created")
                {
                    if (SortDescending)
                        viewModels.Sort((a, b) => (b.Info.CreationDate ?? DateTime.MinValue).CompareTo(a.Info.CreationDate ?? DateTime.MinValue));
                    else
                        viewModels.Sort((a, b) => (a.Info.CreationDate ?? DateTime.MinValue).CompareTo(b.Info.CreationDate ?? DateTime.MinValue));
                }
                else
                {
                    if (SortDescending)
                        viewModels.Sort((a, b) => string.Compare(b.FileName, a.FileName, StringComparison.OrdinalIgnoreCase));
                    else
                        viewModels.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
                }

                return viewModels;
            });

            _scripts.ReplaceRange(scriptViewModels);
        }

        ApplySearchFilter();
        OnPropertyChanged(nameof(Scripts));
        OnPropertyChanged(nameof(DownloadedQuantity));
        OnPropertyChanged(nameof(OutdatedQuantity));
        OnPropertyChanged(nameof(ScriptQuantity));
        OnPropertyChanged(nameof(BotScriptQuantity));
        if (_updateDatesCts == null || _updateDatesCts.IsCancellationRequested)
            IsBusy = false;
    }

    public void ProgressHandler(string message)
    {
        ProgressReportMessage = message;
    }

    [RelayCommand]
    private async Task Delete(ScriptInfoViewModel? script = null)
    {
        ScriptInfoViewModel? target = script ?? SelectedItem;
        if (target is null)
            return;

        IsBusy = true;

        if (target.Info.Tags.Contains("Local"))
        {
            IDialogService? dialogService = Ioc.Default.GetService<IDialogService>();

            if (target.Info.Tags.Contains("Single"))
            {
                DialogResult? result = dialogService?.ShowMessageBox(
                    $"Do you want to permanently delete {target.FileName} from your computer, or just remove it from the Custom Scripts list?",
                    "Remove Custom Script", "Cancel", "Remove from List", "Delete File");

                if (result == null || result.Text == "Cancel" || string.IsNullOrEmpty(result.Text))
                {
                    IsBusy = false;
                    return;
                }

                if (result.Text == "Delete File")
                {
                    await _getScriptsService.DeleteScriptAsync(target.Info);
                }

                ISettingsService? settings = Ioc.Default.GetService<ISettingsService>();
                StringCollection? list = settings?.Get<StringCollection>("UserCustomScriptsList");
                if (list != null && list.Contains(target.Info.FilePath))
                {
                    list.Remove(target.Info.FilePath);
                    settings?.Set("UserCustomScriptsList", list);
                }
            }
            else
            {
                DialogResult? result = dialogService?.ShowMessageBox(
                    $"This script is inside your Custom Scripts Folder.\r\nDo you want to permanently delete {target.FileName} from your computer?",
                    "Delete Local Script", "No", "Yes");

                if (result != null && result.Text == "Yes")
                {
                    await _getScriptsService.DeleteScriptAsync(target.Info);
                }
                else
                {
                    IsBusy = false;
                    return;
                }
            }

            target.Downloaded = false;
            await RefreshScriptsList();
            IsBusy = false;
            return;
        }

        IDialogService? repoDialog = Ioc.Default.GetService<IDialogService>();
        if (repoDialog != null)
        {
            DialogResult? result = repoDialog.ShowMessageBox(
                $"Are you sure you want to delete {target.FileName} from your downloaded scripts?",
                "Delete Script", "No", "Yes");

            if (result == null || result.Text != "Yes")
            {
                IsBusy = false;
                return;
            }
        }

        ProgressReportMessage = $"Deleting {target.FileName}.";
        await _getScriptsService.DeleteScriptAsync(target.Info);
        ProgressReportMessage = $"Deleted {target.FileName}.";
        target.Downloaded = false;
        target.LocalSize = 0;
        target.Info.CachedDownloaded = false;
        target.Info.CachedLocalSize = 0;
        OnPropertyChanged(nameof(DownloadedQuantity));
        OnPropertyChanged(nameof(OutdatedQuantity));
        OnPropertyChanged(nameof(ScriptQuantity));
        OnPropertyChanged(nameof(BotScriptQuantity));
        IsBusy = false;
    }

    [RelayCommand]
    private async Task Download(ScriptInfoViewModel? script = null, CancellationToken token = default)
    {
        ScriptInfoViewModel? target = script ?? SelectedItem;
        if (target is null)
            return;

        IsBusy = true;
        try
        {
            ProgressReportMessage = $"Downloading {target.FileName}...";
            await _getScriptsService.DownloadScriptAsync(target.Info, token);
            ProgressReportMessage = $"Downloaded {target.FileName}.";
            target.Downloaded = true;
            target.LocalSize = File.Exists(target.LocalFile) ? (int)new FileInfo(target.LocalFile).Length : target.Info.Size;
            target.Info.CachedDownloaded = true;
            target.Info.CachedLocalSize = target.LocalSize;
        }
        catch (OperationCanceledException)
        {
            ProgressReportMessage = "Download cancelled.";
        }
        catch (Exception ex)
        {
            ProgressReportMessage = $"Failed to download {target.FileName}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        OnPropertyChanged(nameof(DownloadedQuantity));
        OnPropertyChanged(nameof(OutdatedQuantity));
        OnPropertyChanged(nameof(ScriptQuantity));
        OnPropertyChanged(nameof(BotScriptQuantity));
    }

    [RelayCommand]
    private async Task UpdateAll(CancellationToken token)
    {
        IsBusy = true;
        try
        {
            IProgress<string> progress = new Progress<string>(ProgressHandler);
            progress.Report("Checking for script updates...");
            await _getScriptsService.RefreshScriptsAsync(progress, token);

            progress.Report("Downloading updated scripts...");
            int count = await _getScriptsService.DownloadAllWhereAsync(s => s.Outdated, progress, token);
            ProgressReportMessage = count > 0 ? $"Updated {count} scripts." : "All scripts are up to date.";
        }
        catch (OperationCanceledException)
        {
            ProgressReportMessage = "Update cancelled.";
        }
        catch (Exception ex)
        {
            ProgressReportMessage = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        await RefreshScriptsList();
    }

    [RelayCommand]
    private async Task DownloadAll(CancellationToken token)
    {
        IsBusy = true;
        try
        {
            IProgress<string> progress = new Progress<string>(ProgressHandler);
            progress.Report("Syncing script catalog...");
            await _getScriptsService.RefreshScriptsAsync(progress, token);

            progress.Report("Downloading scripts...");
            int count = await _getScriptsService.DownloadAllWhereAsync(s => !s.Downloaded || s.Outdated, progress, token);
            ProgressReportMessage = count > 0 ? $"Downloaded {count} scripts." : "All scripts are already downloaded.";
        }
        catch (OperationCanceledException)
        {
            ProgressReportMessage = "Download cancelled.";
        }
        catch (Exception ex)
        {
            ProgressReportMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        await RefreshScriptsList();
    }

    [RelayCommand]
    public void CancelTask()
    {
        bool wasRunning = false;

        if (_updateDatesCts != null)
        {
            _updateDatesCts.Cancel();
            wasRunning = true;
        }

        if (UpdateDatesCommand.IsRunning)
        {
            UpdateDatesCommand.Cancel();
            wasRunning = true;
        }

        if (RefreshScriptsCommand.IsRunning)
        {
            RefreshScriptsCommand.Cancel();
            wasRunning = true;
        }

        if (DownloadAllCommand.IsRunning)
        {
            DownloadAllCommand.Cancel();
            wasRunning = true;
        }

        if (UpdateAllCommand.IsRunning)
        {
            UpdateAllCommand.Cancel();
            wasRunning = true;
        }

        if (DownloadCommand.IsRunning)
        {
            DownloadCommand.Cancel();
            wasRunning = true;
        }

        if (DeleteCommand.IsRunning)
        {
            DeleteCommand.Cancel();
            wasRunning = true;
        }

        if (wasRunning)
            ProgressReportMessage = "Task cancelled.";
        else
            ProgressReportMessage = string.Empty;

        IsBusy = false;
    }

    private void ApplySearchFilter()
    {
        string query = (SearchText ?? string.Empty).Trim().ToLowerInvariant();
        List<ScriptInfoViewModel> matches;

        if (string.IsNullOrWhiteSpace(query))
        {
            matches = _scripts.ToList();
        }
        else
        {
            matches = _scripts.Where(s => s.SearchIndex.Contains(query, StringComparison.Ordinal)).ToList();
        }

        FilteredScripts.ReplaceRange(matches);

        if (SelectedItem is not null && !matches.Contains(SelectedItem))
            SelectedItem = null;
    }
}
