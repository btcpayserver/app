using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core.Helpers;

public abstract class BaseHostedService : IHostedService, IDisposable
{
    private readonly ILogger _logger;
    protected CancellationTokenSource _cancellationTokenSource = new();
    protected readonly SemaphoreSlim _controlSemaphore = new(1, 1);
    private Task? _currentTask;

    public BaseHostedService(ILogger logger)
    {
        _logger = logger;
    }
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
        _logger.LogInformation("Stopping service");
        await _cancellationTokenSource.CancelAsync();
        await _controlSemaphore.WaitAsync(cancellationToken);
        
        _logger.LogInformation("Stopping service: lock acquired");
        try
        {
            await ExecuteStopAsync(_cancellationTokenSource.Token);
            
            _logger.LogInformation("Stopped");
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