using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Manager.Avalonia.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.Manager.Avalonia.ViewModels;

public partial class AccountPickerItemViewModel : ObservableObject
{
    public AccountPickerItemViewModel(IAccountItemViewModel account)
    {
        Account = account;
        Username = account.Username;
        DisplayName = account.DisplayOrUsername;
    }

    public IAccountItemViewModel Account { get; }
    public string Username { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isSelected;
}

public partial class ArmyQueueMemberViewModel : ObservableObject
{
    public ArmyQueueMemberViewModel(string username, string displayName, string? scriptPath = null)
    {
        _username = username;
        _displayName = displayName;
        _scriptPath = scriptPath;
    }

    [ObservableProperty]
    private string _username;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string? _scriptPath;

    [ObservableProperty]
    private string _runtimeStatus = "Idle";

    [ObservableProperty]
    private int? _processId;

    public string DisplayOrUsername => string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;

    public string DisplayScriptName => string.IsNullOrWhiteSpace(ScriptPath)
        ? "(Uses Squad Script)"
        : Path.GetFileName(ScriptPath);

    [RelayCommand]
    private void BrowseScript()
    {
        var fileService = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<IFileDialogService>();
        string? script = fileService.OpenFile(ClientFileSources.SkuaScriptsDIR, "Script Files (*.cs)|*.cs|All Files (*.*)|*.*");
        if (!string.IsNullOrWhiteSpace(script))
        {
            ScriptPath = script;
        }
    }

    [RelayCommand]
    private void ClearScript()
    {
        ScriptPath = null;
    }

    partial void OnScriptPathChanged(string? value)
    {
        OnPropertyChanged(nameof(DisplayScriptName));
    }
}

public partial class ArmyQueueStepViewModel : ObservableObject
{
    public ArmyQueueStepViewModel(string name = "Squad 1")
    {
        _name = name;
        Members = new ObservableCollection<ArmyQueueMemberViewModel>();
    }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string? _stepScriptPath;

    [ObservableProperty]
    private string _stepStatus = "Pending";

    public ObservableCollection<ArmyQueueMemberViewModel> Members { get; }

    public string DisplayStepScriptName => string.IsNullOrWhiteSpace(StepScriptPath)
        ? "No Default Script"
        : Path.GetFileName(StepScriptPath);

    [RelayCommand]
    private void BrowseStepScript()
    {
        var fileService = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<IFileDialogService>();
        string? script = fileService.OpenFile(ClientFileSources.SkuaScriptsDIR, "Script Files (*.cs)|*.cs|All Files (*.*)|*.*");
        if (!string.IsNullOrWhiteSpace(script))
        {
            StepScriptPath = script;
        }
    }

    [RelayCommand]
    private void ClearStepScript()
    {
        StepScriptPath = null;
    }

    partial void OnStepScriptPathChanged(string? value)
    {
        OnPropertyChanged(nameof(DisplayStepScriptName));
    }
}

public partial class ArmyQueueViewModel : ObservableRecipient
{
    private readonly AccountManagerViewModel _accountManager;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IFileDialogService _fileService;
    private CancellationTokenSource? _queueCts;

    public ArmyQueueViewModel(
        AccountManagerViewModel accountManager,
        ISettingsService settingsService,
        IDialogService dialogService,
        IFileDialogService fileService)
    {
        _accountManager = accountManager;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _fileService = fileService;

        Steps = new ObservableCollection<ArmyQueueStepViewModel>();
        Presets = new ObservableCollection<string>();
        AvailableAccounts = new ObservableCollection<AccountPickerItemViewModel>();

        _showHelpGuide = !_settingsService.Get("ArmyQueueHelpDismissed", false);

        LoadPresetsList();
        if (Steps.Count == 0)
        {
            AddDefaultStep();
        }
    }

    public ObservableCollection<ArmyQueueStepViewModel> Steps { get; }
    public ObservableCollection<string> Presets { get; }
    public ObservableCollection<AccountPickerItemViewModel> AvailableAccounts { get; }

    [ObservableProperty]
    private ArmyQueueStepViewModel? _selectedStep;

    [ObservableProperty]
    private string _queueName = "Daily Ultras";

    [ObservableProperty]
    private string? _selectedPreset;

    [ObservableProperty]
    private int _delayBetweenStepsSeconds = 5;

    [ObservableProperty]
    private int _delayBetweenLaunchesSeconds = 3;

    [ObservableProperty]
    private int _timeoutMinutes = 30;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Ready to start queue.";

    [ObservableProperty]
    private int _activeStepIndex;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _showHelpGuide;

    [ObservableProperty]
    private bool _showAccountPicker;

    [ObservableProperty]
    private string _accountPickerSearchText = string.Empty;

    [RelayCommand]
    public void ToggleHelpGuide()
    {
        ShowHelpGuide = !ShowHelpGuide;
    }

    [RelayCommand]
    public void DismissHelpGuide()
    {
        ShowHelpGuide = false;
        _settingsService.Set("ArmyQueueHelpDismissed", true);
    }

    [RelayCommand]
    public void AddStep()
    {
        ArmyQueueStepViewModel step = new($"Squad {Steps.Count + 1}");
        Steps.Add(step);
        SelectedStep = step;
    }

    [RelayCommand]
    public void RemoveStep(ArmyQueueStepViewModel? step)
    {
        step ??= SelectedStep;
        if (step is null)
            return;

        int index = Steps.IndexOf(step);
        Steps.Remove(step);
        if (SelectedStep == step)
        {
            SelectedStep = Steps.Count > 0
                ? (index < Steps.Count ? Steps[index] : Steps.Last())
                : null;
        }
    }

    [RelayCommand]
    public void MoveStepUp(ArmyQueueStepViewModel? step)
    {
        step ??= SelectedStep;
        if (step is null) return;

        int index = Steps.IndexOf(step);
        if (index > 0)
        {
            Steps.Move(index, index - 1);
            SelectedStep = step;
        }
    }

    [RelayCommand]
    public void MoveStepDown(ArmyQueueStepViewModel? step)
    {
        step ??= SelectedStep;
        if (step is null) return;

        int index = Steps.IndexOf(step);
        if (index >= 0 && index < Steps.Count - 1)
        {
            Steps.Move(index, index + 1);
            SelectedStep = step;
        }
    }

    [RelayCommand]
    public void OpenAccountPicker()
    {
        if (SelectedStep is null)
        {
            _dialogService.ShowMessageBox("Please create or select a squad first.", "Army Queue");
            return;
        }

        HashSet<string> currentMembers = new(
            SelectedStep.Members.Select(m => m.Username),
            StringComparer.OrdinalIgnoreCase);

        AvailableAccounts.Clear();
        AccountPickerSearchText = string.Empty;

        foreach (IAccountItemViewModel acc in _accountManager.Accounts)
        {
            AvailableAccounts.Add(new AccountPickerItemViewModel(acc)
            {
                IsSelected = currentMembers.Contains(acc.Username)
            });
        }

        ShowAccountPicker = true;
    }

    [RelayCommand]
    public void ConfirmAccountPicker()
    {
        if (SelectedStep is null)
        {
            ShowAccountPicker = false;
            return;
        }

        var selected = AvailableAccounts.Where(a => a.IsSelected).ToList();
        var selectedUsernames = new HashSet<string>(selected.Select(s => s.Username), StringComparer.OrdinalIgnoreCase);

        // Remove members that were unchecked
        for (int i = SelectedStep.Members.Count - 1; i >= 0; i--)
        {
            if (!selectedUsernames.Contains(SelectedStep.Members[i].Username))
                SelectedStep.Members.RemoveAt(i);
        }

        // Add newly checked accounts
        foreach (var item in selected)
        {
            if (!SelectedStep.Members.Any(m => m.Username.Equals(item.Username, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedStep.Members.Add(new ArmyQueueMemberViewModel(item.Username, item.DisplayName));
            }
        }

        ShowAccountPicker = false;
    }

    [RelayCommand]
    public void CancelAccountPicker()
    {
        ShowAccountPicker = false;
    }

    [RelayCommand]
    public void SelectAllPicker()
    {
        foreach (var item in AvailableAccounts)
            item.IsSelected = true;
    }

    [RelayCommand]
    public void DeselectAllPicker()
    {
        foreach (var item in AvailableAccounts)
            item.IsSelected = false;
    }

    [RelayCommand]
    public void RemoveMember(ArmyQueueMemberViewModel? member)
    {
        if (SelectedStep != null && member != null)
        {
            SelectedStep.Members.Remove(member);
        }
    }

    [RelayCommand]
    public void ImportGroupAsStep()
    {
        SelectGroupDialogViewModel dialog = new(_accountManager.Groups);
        if (_dialogService.ShowDialog(dialog, "Import Group as Squad") == true && dialog.SelectedGroup != null)
        {
            ArmyQueueStepViewModel step = new(dialog.SelectedGroup.Name);
            foreach (IAccountItemViewModel acc in dialog.SelectedGroup.Accounts)
            {
                step.Members.Add(new ArmyQueueMemberViewModel(acc.Username, acc.DisplayOrUsername));
            }
            Steps.Add(step);
            SelectedStep = step;
        }
    }

    [RelayCommand]
    public async Task StartQueue()
    {
        if (IsRunning)
            return;

        if (Steps.Count == 0 || Steps.All(s => s.Members.Count == 0))
        {
            _dialogService.ShowMessageBox("Please add at least one squad with accounts to the queue.", "Empty Queue");
            return;
        }

        IsRunning = true;
        _queueCts = new CancellationTokenSource();
        CancellationToken token = _queueCts.Token;

        try
        {
            await RunQueueAsync(token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Army Queue was stopped.";
        }
        catch (Exception ex)
        {
            StatusText = $"Queue Error: {ex.Message}";
            _dialogService.ShowMessageBox($"Queue encountered an error: {ex.Message}", "Queue Execution Error");
        }
        finally
        {
            IsRunning = false;
            _queueCts?.Dispose();
            _queueCts = null;
        }
    }

    [RelayCommand]
    public void StopQueue()
    {
        if (!IsRunning || _queueCts == null)
            return;

        if (_dialogService.ShowMessageBox("Are you sure you want to stop the running Army Queue?", "Stop Queue", true) == true)
        {
            _queueCts?.Cancel();
            StatusText = "Stopping Army Queue...";
        }
    }

    private async Task RunQueueAsync(CancellationToken token)
    {
        int totalSteps = Steps.Count;
        ActiveStepIndex = 0;
        ProgressValue = 0;

        foreach (var s in Steps)
        {
            s.StepStatus = "Pending";
            foreach (var m in s.Members)
            {
                m.RuntimeStatus = "Waiting";
                m.ProcessId = null;
            }
        }

        for (int i = 0; i < Steps.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            ActiveStepIndex = i + 1;
            ArmyQueueStepViewModel currentStep = Steps[i];
            currentStep.StepStatus = "Running";
            StatusText = $"Running Squad {ActiveStepIndex}/{totalSteps}: '{currentStep.Name}' ({currentStep.Members.Count} accounts)...";

            List<Process> activeProcesses = new();
            Dictionary<int, ArmyQueueMemberViewModel> pidToMember = new();

            // 1. Launch all members of the squad with staggered delay
            foreach (ArmyQueueMemberViewModel member in currentStep.Members)
            {
                token.ThrowIfCancellationRequested();

                IAccountItemViewModel? acc = _accountManager.Accounts.FirstOrDefault(
                    a => a.Username.Equals(member.Username, StringComparison.OrdinalIgnoreCase));

                if (acc is null)
                {
                    member.RuntimeStatus = "Account Missing";
                    continue;
                }

                member.RuntimeStatus = "Launching...";
                string targetScript = !string.IsNullOrWhiteSpace(member.ScriptPath)
                    ? member.ScriptPath
                    : (currentStep.StepScriptPath ?? string.Empty);

                Process? proc = LaunchMemberProcess(acc, targetScript);
                if (proc != null)
                {
                    member.ProcessId = proc.Id;
                    member.RuntimeStatus = "Running";
                    activeProcesses.Add(proc);
                    pidToMember[proc.Id] = member;
                }
                else
                {
                    member.RuntimeStatus = "Launch Failed";
                }

                if (DelayBetweenLaunchesSeconds > 0 && member != currentStep.Members.Last())
                {
                    await Task.Delay(DelayBetweenLaunchesSeconds * 1000, token);
                }
            }

            // 2. Monitor processes until all exit or timeout occurs
            int timeoutSeconds = Math.Max(1, TimeoutMinutes) * 60;
            DateTime startTime = DateTime.UtcNow;

            while (activeProcesses.Any(p => !p.HasExited))
            {
                token.ThrowIfCancellationRequested();

                int completedCount = activeProcesses.Count(p => p.HasExited);
                int totalCount = activeProcesses.Count;
                TimeSpan elapsed = DateTime.UtcNow - startTime;

                if (elapsed.TotalSeconds >= timeoutSeconds)
                {
                    StatusText = $"Squad '{currentStep.Name}' timed out after {TimeoutMinutes} mins. Proceeding to next step...";
                    foreach (Process p in activeProcesses.Where(p => !p.HasExited))
                    {
                        try { p.Kill(); } catch { }
                    }
                    break;
                }

                foreach (Process p in activeProcesses)
                {
                    if (p.HasExited && pidToMember.TryGetValue(p.Id, out ArmyQueueMemberViewModel? m))
                    {
                        if (m.RuntimeStatus == "Running")
                            m.RuntimeStatus = "Finished";
                    }
                }

                StatusText = $"Squad {ActiveStepIndex}/{totalSteps} '{currentStep.Name}': {completedCount}/{totalCount} accounts finished ({Math.Round((timeoutSeconds - elapsed.TotalSeconds) / 60.0)}m remaining)";
                ProgressValue = ((i * 100.0) / totalSteps) + ((completedCount * 100.0) / (totalSteps * Math.Max(1, totalCount)));

                await Task.Delay(1000, token);
            }

            currentStep.StepStatus = "Completed";
            foreach (var m in currentStep.Members)
            {
                if (m.RuntimeStatus == "Running")
                    m.RuntimeStatus = "Finished";
            }

            ProgressValue = ((i + 1) * 100.0) / totalSteps;

            // 3. Ghost session cooldown before next squad
            if (i < Steps.Count - 1)
            {
                int cooldown = Math.Max(1, DelayBetweenStepsSeconds);
                for (int c = cooldown; c > 0; c--)
                {
                    token.ThrowIfCancellationRequested();
                    StatusText = $"Squad '{currentStep.Name}' completed! Waiting {c}s ghost session cooldown before next squad...";
                    await Task.Delay(1000, token);
                }
            }
        }

        StatusText = "All Army Queue squads completed successfully!";
        ProgressValue = 100;
        _dialogService.ShowMessageBox("All scheduled Army Queue squads have completed their runs!", "Army Queue Finished");
    }

    private Process? LaunchMemberProcess(IAccountItemViewModel acc, string scriptPath)
    {
        try
        {
            string exePath = SkuaExecutableLocator.ExecutablePath;
            string server = _settingsService.Get("LastServer", "Twilly");

            ProcessStartInfo psi = new(exePath)
            {
                WorkingDirectory = SkuaExecutableLocator.WorkingDirectory,
                ArgumentList =
                {
                    "-u",
                    acc.Username,
                    "-p",
                    acc.Password,
                    "-s",
                    server
                }
            };

            if (_settingsService.Get("syncTheme", false))
            {
                psi.ArgumentList.Add("--use-theme");
                psi.ArgumentList.Add(_settingsService.Get("CurrentTheme", "no-theme"));
            }

            if (!string.IsNullOrWhiteSpace(scriptPath))
            {
                psi.ArgumentList.Add("--run-script");
                psi.ArgumentList.Add(scriptPath);
            }

            psi.ArgumentList.Add("--exit-on-script-stop");

            Process? process = Process.Start(psi);
            if (process != null)
            {
                StrongReferenceMessenger.Default.Send(new AddProcessMessage(process, acc.DisplayOrUsername));
            }

            return process;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error launching queue account {acc.Username}: {ex.Message}");
            return null;
        }
    }

    [RelayCommand]
    public void SavePreset()
    {
        if (string.IsNullOrWhiteSpace(QueueName))
        {
            _dialogService.ShowMessageBox("Please enter a name for this Army Queue preset.", "Save Preset");
            return;
        }

        try
        {
            string configDir = ClientFileSources.SkuaDIR;
            Directory.CreateDirectory(configDir);
            string filePath = Path.Combine(configDir, "ArmyQueues.json");

            Dictionary<string, ArmyQueueConfig> configs = LoadAllConfigs();

            ArmyQueueConfig config = new()
            {
                Name = QueueName.Trim(),
                DelayBetweenStepsSeconds = DelayBetweenStepsSeconds,
                DelayBetweenLaunchesSeconds = DelayBetweenLaunchesSeconds,
                TimeoutMinutes = TimeoutMinutes,
                Steps = Steps.Select(s => new ArmyQueueStep
                {
                    Name = s.Name,
                    StepScriptPath = s.StepScriptPath,
                    Accounts = s.Members.Select(m => new ArmyQueueAccount
                    {
                        Username = m.Username,
                        DisplayName = m.DisplayName,
                        ScriptPath = m.ScriptPath
                    }).ToList()
                }).ToList()
            };

            configs[config.Name] = config;
            File.WriteAllText(filePath, JsonSerializer.Serialize(configs, new JsonSerializerOptions { WriteIndented = true }));

            LoadPresetsList();
            SelectedPreset = config.Name;
            _dialogService.ShowMessageBox($"Preset '{config.Name}' saved successfully!", "Preset Saved");
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Failed to save preset: {ex.Message}", "Save Error");
        }
    }

    [RelayCommand]
    public void LoadSelectedPreset()
    {
        if (string.IsNullOrWhiteSpace(SelectedPreset))
            return;

        try
        {
            Dictionary<string, ArmyQueueConfig> configs = LoadAllConfigs();
            if (!configs.TryGetValue(SelectedPreset, out ArmyQueueConfig? config) || config == null)
            {
                _dialogService.ShowMessageBox($"Preset '{SelectedPreset}' not found.", "Load Error");
                return;
            }

            QueueName = config.Name;
            DelayBetweenStepsSeconds = config.DelayBetweenStepsSeconds > 0 ? config.DelayBetweenStepsSeconds : 5;
            DelayBetweenLaunchesSeconds = config.DelayBetweenLaunchesSeconds > 0 ? config.DelayBetweenLaunchesSeconds : 3;
            TimeoutMinutes = config.TimeoutMinutes > 0 ? config.TimeoutMinutes : 30;

            Steps.Clear();
            foreach (ArmyQueueStep s in config.Steps)
            {
                ArmyQueueStepViewModel stepVm = new(s.Name)
                {
                    StepScriptPath = s.StepScriptPath
                };
                foreach (ArmyQueueAccount a in s.Accounts)
                {
                    stepVm.Members.Add(new ArmyQueueMemberViewModel(a.Username, a.DisplayName, a.ScriptPath));
                }
                Steps.Add(stepVm);
            }

            SelectedStep = Steps.FirstOrDefault();
            StatusText = $"Loaded preset '{config.Name}'.";
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Failed to load preset: {ex.Message}", "Load Error");
        }
    }

    [RelayCommand]
    public void DeleteSelectedPreset()
    {
        if (string.IsNullOrWhiteSpace(SelectedPreset))
            return;

        if (_dialogService.ShowMessageBox($"Are you sure you want to delete preset '{SelectedPreset}'?", "Delete Preset", true) != true)
            return;

        try
        {
            string filePath = Path.Combine(ClientFileSources.SkuaDIR, "ArmyQueues.json");
            Dictionary<string, ArmyQueueConfig> configs = LoadAllConfigs();
            configs.Remove(SelectedPreset);
            File.WriteAllText(filePath, JsonSerializer.Serialize(configs, new JsonSerializerOptions { WriteIndented = true }));

            LoadPresetsList();
            SelectedPreset = Presets.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Failed to delete preset: {ex.Message}", "Delete Error");
        }
    }

    private Dictionary<string, ArmyQueueConfig> LoadAllConfigs()
    {
        string filePath = Path.Combine(ClientFileSources.SkuaDIR, "ArmyQueues.json");
        if (!File.Exists(filePath))
            return new Dictionary<string, ArmyQueueConfig>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Dictionary<string, ArmyQueueConfig>>(json)
                ?? new Dictionary<string, ArmyQueueConfig>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, ArmyQueueConfig>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void LoadPresetsList()
    {
        Presets.Clear();
        Dictionary<string, ArmyQueueConfig> configs = LoadAllConfigs();
        foreach (string name in configs.Keys)
            Presets.Add(name);

        if (string.IsNullOrWhiteSpace(SelectedPreset) && Presets.Count > 0)
            SelectedPreset = Presets[0];
    }

    private void AddDefaultStep()
    {
        ArmyQueueStepViewModel step1 = new("Squad 1");
        Steps.Add(step1);
        SelectedStep = step1;
    }
}
