using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core.Helpers;

public abstract class BaseHostedService(ILogger logger) : IHostedService, IDisposable
{
    protected CancellationTokenSource CancellationTokenSource = new();
    protected readonly SemaphoreSlim ControlSemaphore = new(1, 1);
    private Task? _currentTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ControlSemaphore.WaitAsync(cancellationToken);
        try
        {
            CancellationTokenSource = new CancellationTokenSource();
            await ExecuteStartAsync(CancellationTokenSource.CreateLinkedTokenSource(CancellationTokenSource.Token, cancellationToken).Token);
        }
        finally
        {
            ControlSemaphore.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping service");
        await CancellationTokenSource.CancelAsync();
        await ControlSemaphore.WaitAsync(cancellationToken);

        try
        {
            await ExecuteStopAsync(CancellationTokenSource.Token);

            logger.LogInformation("Stopped");
        }
        finally
        {
            ControlSemaphore.Release();
        }
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
        await ControlSemaphore.WaitAsync(cancellationToken);
        try
        {
            await act();
        }
        finally
        {
            ControlSemaphore.Release();
        }
    }
}
