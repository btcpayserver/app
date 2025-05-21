using _Microsoft.Android.Resource.Designer;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using BTCPayApp.Core.BTCPayServer;

namespace BTCPayApp.Maui;

[Service(Enabled = true, ForegroundServiceType = ForegroundService.TypeDataSync)]
public class HubConnectionForegroundService : Service
{
    private BTCPayConnectionManager _connectionManager;
    private const string ChannelId = "HubConnectionForegroundService";

    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
    {
        CreateNotificationChannel();
        StartForeground(1, BuildNotification());

        _connectionManager = MauiProgram.Services.GetRequiredService<BTCPayConnectionManager>();
        _connectionManager.RunningInBackground = true;

        return StartCommandResult.Sticky;
    }

    public override IBinder OnBind(Intent intent) => null;

    public override void OnDestroy()
    {
        _connectionManager.RunningInBackground = false;
        base.OnDestroy();
    }

    // https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/local-notifications?view=net-maui-8.0&pivots=devices-android
    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
#pragma warning disable CA1416
            var channel = new NotificationChannel(ChannelId, "Hub Connection Service", NotificationImportance.Default)
            {
                Description = "Keeps the server connection alive."
            };

            var notificationManager = (NotificationManager)GetSystemService(NotificationService)!;
            notificationManager.CreateNotificationChannel(channel);
#pragma warning restore CA1416
        }
    }

    private Notification BuildNotification()
    {
        return new Notification.Builder(this, ChannelId)
            .SetContentTitle("BTCPay Server")
            .SetContentText("Maintaining server connection...")
            .SetSmallIcon(ResourceConstant.Drawable.ic_notification)
            .SetOngoing(true)
            .Build();
    }
}
