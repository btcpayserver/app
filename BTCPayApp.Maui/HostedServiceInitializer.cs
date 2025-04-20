using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Maui;

public class HostedServiceInitializer : IMauiInitializeService, IDisposable
{
    static readonly SemaphoreSlim Semaphore = new(1, 1);
    private static bool _running = false;
    public static Task? ExecutingTask;
    private IEnumerable<IHostedService>? _services;
    private ILogger<HostedServiceInitializer>? _logger;

    public HostedServiceInitializer(IEnumerable<IHostedService> services)
    {
        _services = services;
    }

    public void Initialize(IServiceProvider serviceProvider)
    {
        _services = serviceProvider.GetServices<IHostedService>();
        _logger = serviceProvider.GetService<ILogger<HostedServiceInitializer>>();
        StartDirectly().GetAwaiter().GetResult();
    }

    private Task OnStop()
    {
        return LockWaitRunExecute(async () =>
        {
            if (!_running) return;
            if (_services is not null)
            {
                foreach (var service in _services)
                {
                    await service.StopAsync(CancellationToken.None);
                }
            }

            _running = false;
            _logger?.LogInformation("Service stopped");
        });
    }

    private async Task StartDirectly()
    {
        await LockWaitRunExecute(async () =>
        {
            if (_running) return;
            foreach (var service in _services!)
            {
                await service.StartAsync(CancellationToken.None);
            }

            _running = true;
            _logger?.LogInformation("Service started");
        });
    }

    private async Task LockWaitRunExecute(Func<Task> action)
    {
        await Semaphore.WaitAsync();
        if (ExecutingTask != null)
            await ExecutingTask;
        await action();
        Semaphore.Release();
    }

    public void Dispose()
    {
        OnStop().GetAwaiter().GetResult();
    }
}
