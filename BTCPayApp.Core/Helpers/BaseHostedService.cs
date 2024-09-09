using Microsoft.Extensions.Hosting;

namespace BTCPayApp.Core.Helpers;

public abstract class BaseHostedService : IHostedService, IDisposable
{
    protected CancellationTokenSource _cancellationTokenSource = new();
    protected readonly SemaphoreSlim _controlSemaphore = new(1, 1);
    private Task? _currentTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _controlSemaphore.WaitAsync(cancellationToken);
        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            await ExecuteStartAsync(CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken).Token);
        }
        finally
        {
            _controlSemaphore.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cancellationTokenSource.CancelAsync();
        await _controlSemaphore.WaitAsync(cancellationToken);
        try
        {
            await ExecuteStopAsync(_cancellationTokenSource.Token);
        }
        finally
        {
            _controlSemaphore.Release();
        }
    }

    protected abstract Task ExecuteStartAsync(CancellationToken cancellationToken);
    protected abstract Task ExecuteStopAsync(CancellationToken cancellationToken);

    public virtual void Dispose()
    {
        _cancellationTokenSource?.Dispose();
        _controlSemaphore?.Dispose();
    }
    
    protected async Task WrapInLock(Func<Task> act, CancellationToken cancellationToken)
    {
        await _controlSemaphore.WaitAsync(cancellationToken);
        try
        {
            await act();
        }
        finally
        {
            _controlSemaphore.Release();
        }
    }
}