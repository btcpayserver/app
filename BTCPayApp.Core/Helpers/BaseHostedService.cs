using AsyncKeyedLock;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core.Helpers;

public abstract class BaseHostedService(ILogger logger) : IHostedService, IDisposable
{
    protected CancellationTokenSource CancellationTokenSource = new();
    protected readonly AsyncNonKeyedLocker ControlSemaphore = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var _ = ControlSemaphore.LockAsync(cancellationToken);
        CancellationTokenSource = new CancellationTokenSource();
        await ExecuteStartAsync(CancellationTokenSource.CreateLinkedTokenSource(CancellationTokenSource.Token, cancellationToken).Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping service");
        await CancellationTokenSource.CancelAsync();
        using var _ = await ControlSemaphore.LockAsync(cancellationToken);
        await ExecuteStopAsync(CancellationTokenSource.Token);

        logger.LogInformation("Stopped");
    }

    protected abstract Task ExecuteStartAsync(CancellationToken cancellationToken);
    protected abstract Task ExecuteStopAsync(CancellationToken cancellationToken);

    public virtual void Dispose()
    {
        CancellationTokenSource?.Dispose();
        ControlSemaphore?.Dispose();
    }

    protected async Task WrapInLock(Func<Task> act, CancellationToken cancellationToken)
    {
        using var _ = ControlSemaphore.LockAsync(cancellationToken);
        await act();
    }
}
