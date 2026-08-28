using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Velopack;
using Velopack.Sources;

namespace AntennaGuardian.App;

public enum AppUpdatePhase
{
    Idle,
    Checking,
    Current,
    Available,
    Downloading,
    Ready,
    Failed,
    Portable,
    Store,
}

public sealed record AppUpdateState(
    AppUpdatePhase Phase,
    string CurrentVersion,
    string? AvailableVersion,
    int ProgressPercent,
    string Message)
{
    public bool CanCheck => Phase is AppUpdatePhase.Idle
        or AppUpdatePhase.Current
        or AppUpdatePhase.Failed;
    public bool CanDownload => Phase == AppUpdatePhase.Available;
    public bool CanInstall => Phase == AppUpdatePhase.Ready;
}

public interface IAppUpdateService
{
    event Action<AppUpdateState>? StateChanged;

    AppUpdateState State { get; }
    Task CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task DownloadUpdateAsync(CancellationToken cancellationToken = default);
    void ApplyUpdateAndRestart();
}

internal interface IUpdateBackend
{
    bool IsInstalled { get; }
    string CurrentVersion { get; }
    string? PendingVersion { get; }
    Task<string?> CheckForUpdatesAsync(CancellationToken cancellationToken);
    Task DownloadUpdateAsync(Action<int> progress, CancellationToken cancellationToken);
    void ApplyUpdateAndRestart();
}

internal sealed class AppUpdateService : IAppUpdateService
{
    private readonly IUpdateBackend _backend;
    private readonly bool _isStorePackage;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public AppUpdateService(IUpdateBackend backend, bool? isStorePackage = null)
    {
        _backend = backend;
        _isStorePackage = isStorePackage ?? WindowsPackageIdentity.IsPackaged;
        State = _isStorePackage
            ? new AppUpdateState(
                AppUpdatePhase.Store,
                _backend.CurrentVersion,
                null,
                0,
                "Updates are managed by Microsoft Store.")
            : !_backend.IsInstalled
            ? new AppUpdateState(
                AppUpdatePhase.Portable,
                _backend.CurrentVersion,
                null,
                0,
                "Portable edition. Install AntennaGuardian to enable automatic updates.")
            : _backend.PendingVersion is { } pendingVersion
                ? new AppUpdateState(
                    AppUpdatePhase.Ready,
                    _backend.CurrentVersion,
                    pendingVersion,
                    100,
                    $"Version {pendingVersion} is ready to install.")
                : new AppUpdateState(
                    AppUpdatePhase.Idle,
                    _backend.CurrentVersion,
                    null,
                    0,
                    "Automatic updates are available for this installation.");
    }

    public event Action<AppUpdateState>? StateChanged;

    public AppUpdateState State { get; private set; }

    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (_isStorePackage || !_backend.IsInstalled || !State.CanCheck
            || !await _operationGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            SetState(State with
            {
                Phase = AppUpdatePhase.Checking,
                ProgressPercent = 0,
                Message = "Checking GitHub for updates...",
            });
            var version = await _backend.CheckForUpdatesAsync(cancellationToken);
            SetState(version is null
                ? State with
                {
                    Phase = AppUpdatePhase.Current,
                    AvailableVersion = null,
                    Message = $"Version {State.CurrentVersion} is current.",
                }
                : State with
                {
                    Phase = AppUpdatePhase.Available,
                    AvailableVersion = version,
                    Message = $"Version {version} is available.",
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(State with
            {
                Phase = AppUpdatePhase.Idle,
                Message = "Update check canceled.",
            });
        }
        catch (Exception error)
        {
            SetState(State with
            {
                Phase = AppUpdatePhase.Failed,
                Message = $"Could not check for updates: {error.Message}",
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DownloadUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!State.CanDownload || !await _operationGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            SetState(State with
            {
                Phase = AppUpdatePhase.Downloading,
                ProgressPercent = 0,
                Message = $"Downloading version {State.AvailableVersion}...",
            });
            await _backend.DownloadUpdateAsync(
                progress => SetState(State with
                {
                    ProgressPercent = Math.Clamp(progress, 0, 100),
                }),
                cancellationToken);
            SetState(State with
            {
                Phase = AppUpdatePhase.Ready,
                ProgressPercent = 100,
                Message = $"Version {State.AvailableVersion} is ready to install.",
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(State with
            {
                Phase = AppUpdatePhase.Available,
                ProgressPercent = 0,
                Message = $"Version {State.AvailableVersion} is available.",
            });
        }
        catch (Exception error)
        {
            SetState(State with
            {
                Phase = AppUpdatePhase.Failed,
                ProgressPercent = 0,
                Message = $"Could not download the update: {error.Message}",
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void ApplyUpdateAndRestart()
    {
        if (!State.CanInstall)
        {
            throw new InvalidOperationException("No downloaded update is ready to install.");
        }

        _backend.ApplyUpdateAndRestart();
    }

    private void SetState(AppUpdateState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}

internal static class WindowsPackageIdentity
{
    private const int AppModelErrorNoPackage = 15700;

    public static bool IsPackaged
    {
        get
        {
            var length = 0;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(
        ref int packageFullNameLength,
        StringBuilder? packageFullName);
}

internal sealed class StoreUpdateBackend : IUpdateBackend
{
    public bool IsInstalled => false;
    public string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
    public string? PendingVersion => null;

    public Task<string?> CheckForUpdatesAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("Updates are managed by Microsoft Store.");

    public Task DownloadUpdateAsync(Action<int> progress, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Updates are managed by Microsoft Store.");

    public void ApplyUpdateAndRestart() =>
        throw new NotSupportedException("Updates are managed by Microsoft Store.");
}

internal sealed class VelopackUpdateBackend : IUpdateBackend
{
    private const string RepositoryUrl = "https://github.com/johnfking/AntennaGuardian";
    private readonly UpdateManager _manager;
    private UpdateInfo? _availableUpdate;
    private VelopackAsset? _pendingUpdate;

    public VelopackUpdateBackend()
    {
        _manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
        _pendingUpdate = _manager.UpdatePendingRestart;
        CurrentVersion = _manager.IsInstalled
            ? _manager.CurrentVersion?.ToString() ?? "unknown"
            : Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
    }

    public bool IsInstalled => _manager.IsInstalled;
    public string CurrentVersion { get; }
    public string? PendingVersion => _pendingUpdate?.Version.ToString();

    public async Task<string?> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _availableUpdate = await _manager.CheckForUpdatesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return _availableUpdate?.TargetFullRelease.Version.ToString();
    }

    public async Task DownloadUpdateAsync(
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        var update = _availableUpdate
            ?? throw new InvalidOperationException("No update has been selected for download.");
        await _manager.DownloadUpdatesAsync(update, progress, cancellationToken);
        _pendingUpdate = update.TargetFullRelease;
    }

    public void ApplyUpdateAndRestart()
    {
        var update = _pendingUpdate
            ?? throw new InvalidOperationException("No downloaded update is ready to install.");
        _manager.ApplyUpdatesAndRestart(update);
    }
}
