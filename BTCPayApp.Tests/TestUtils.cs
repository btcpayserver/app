using Microsoft.Playwright;
using Xunit.Sdk;

namespace BTCPayApp.Tests;

public static class TestUtils
{
    
    public static  async Task AsTask(this CancellationToken cancellationToken)
    {
        // Simplification for the sake of example
        var cts = new CancellationTokenSource();

        var waitForStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // IHostApplicationLifetime event can be used instead of `cts.Token`
        var registration = cts.Token.Register(() => waitForStop.SetResult());
        await using var _ = registration.ConfigureAwait(false);

        await waitForStop.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    public static void Eventually(Action act, int ms = 20_000)
    {
        var cts = new CancellationTokenSource(ms);
        while (true)
            try
            {
                act();
                break;
            }
            catch (PlaywrightException) when (!cts.Token.IsCancellationRequested)
            {
                cts.Token.WaitHandle.WaitOne(500);
            }
            catch (XunitException) when (!cts.Token.IsCancellationRequested)
            {
                cts.Token.WaitHandle.WaitOne(500);
            }
    }

    public static async Task EventuallyAsync(Func<Task> act, int delay = 20000)
    {
        var cts = new CancellationTokenSource(delay);
        while (true)
            try
            {
                await act();
                break;
            }
            catch (PlaywrightException) when (!cts.Token.IsCancellationRequested)
            {
                var timeout = false;
                try
                {
                    await Task.Delay(500, cts.Token);
                }
                catch
                {
                    timeout = true;
                }

                if (timeout)
                    throw;
            }
            catch (XunitException) when (!cts.Token.IsCancellationRequested)
            {
                var timeout = false;
                try
                {
                    await Task.Delay(500, cts.Token);
                }
                catch
                {
                    timeout = true;
                }

                if (timeout)
                    throw;
            }
    }
}