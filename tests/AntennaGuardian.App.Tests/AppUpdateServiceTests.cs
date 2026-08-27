namespace AntennaGuardian.App.Tests;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public async Task PortableEditionDoesNotContactUpdateBackend()
    {
        var backend = new FakeUpdateBackend { IsInstalled = false };
        var service = new AppUpdateService(backend);

        await service.CheckForUpdatesAsync();

        Assert.Equal(AppUpdatePhase.Portable, service.State.Phase);
        Assert.Equal(0, backend.CheckCount);
    }

    [Fact]
    public async Task CheckReportsCurrentVersionWhenNoUpdateExists()
    {
        var backend = new FakeUpdateBackend();
        var service = new AppUpdateService(backend);

        await service.CheckForUpdatesAsync();

        Assert.Equal(AppUpdatePhase.Current, service.State.Phase);
        Assert.Contains("0.2.0", service.State.Message);
    }

    [Fact]
    public async Task AvailableUpdateCanBeDownloadedAndApplied()
    {
        var backend = new FakeUpdateBackend { AvailableVersion = "0.2.1" };
        var service = new AppUpdateService(backend);

        await service.CheckForUpdatesAsync();
        Assert.Equal(AppUpdatePhase.Available, service.State.Phase);

        await service.DownloadUpdateAsync();
        Assert.Equal(AppUpdatePhase.Ready, service.State.Phase);
        Assert.Equal(100, service.State.ProgressPercent);

        service.ApplyUpdateAndRestart();
        Assert.True(backend.ApplyCalled);
    }

    [Fact]
    public async Task NetworkFailureIsReportedWithoutEscaping()
    {
        var backend = new FakeUpdateBackend
        {
            CheckError = new HttpRequestException("network unavailable"),
        };
        var service = new AppUpdateService(backend);

        await service.CheckForUpdatesAsync();

        Assert.Equal(AppUpdatePhase.Failed, service.State.Phase);
        Assert.Contains("network unavailable", service.State.Message);
        Assert.True(service.State.CanCheck);
    }

    [Fact]
    public async Task DownloadFailureIsReportedWithoutApplyingUpdate()
    {
        var backend = new FakeUpdateBackend
        {
            AvailableVersion = "0.2.1",
            DownloadError = new IOException("disk full"),
        };
        var service = new AppUpdateService(backend);

        await service.CheckForUpdatesAsync();
        await service.DownloadUpdateAsync();

        Assert.Equal(AppUpdatePhase.Failed, service.State.Phase);
        Assert.Contains("disk full", service.State.Message);
        Assert.False(backend.ApplyCalled);
    }

    [Fact]
    public void PendingUpdateIsReadyAtStartup()
    {
        var backend = new FakeUpdateBackend { PendingVersion = "0.2.1" };

        var service = new AppUpdateService(backend);

        Assert.Equal(AppUpdatePhase.Ready, service.State.Phase);
        Assert.Equal("0.2.1", service.State.AvailableVersion);
    }

    private sealed class FakeUpdateBackend : IUpdateBackend
    {
        public bool IsInstalled { get; init; } = true;
        public string CurrentVersion { get; init; } = "0.2.0";
        public string? PendingVersion { get; init; }
        public string? AvailableVersion { get; init; }
        public Exception? CheckError { get; init; }
        public Exception? DownloadError { get; init; }
        public int CheckCount { get; private set; }
        public bool ApplyCalled { get; private set; }

        public Task<string?> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            CheckCount++;
            return CheckError is null
                ? Task.FromResult(AvailableVersion)
                : Task.FromException<string?>(CheckError);
        }

        public Task DownloadUpdateAsync(
            Action<int> progress,
            CancellationToken cancellationToken)
        {
            if (DownloadError is not null)
            {
                return Task.FromException(DownloadError);
            }
            progress(42);
            progress(100);
            return Task.CompletedTask;
        }

        public void ApplyUpdateAndRestart() => ApplyCalled = true;
    }
}
