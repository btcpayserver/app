using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Microsoft.Extensions.Hosting;

namespace BTCPayApp.Maui;

[Service]
public class AndroidHostedServiceForegreoundService : Service
{
    private static Func<Activity> _activityResolver;
    private IEnumerable<IHostedService> _hostedServices;

    public static Activity CurrentActivity => GetCurrentActivity();
        
    public static void SetCurrentActivityResolver(Func<Activity> activityResolver)
    {
        _activityResolver = activityResolver;
    }
    
    private static Activity GetCurrentActivity()
    {
        if (_activityResolver is null)
            throw new InvalidOperationException("Resolver for the current activity is not set. Call AndroidHostedServiceForegreoundService.SetCurrentActivityResolver somewhere in your startup code.");

        var activity = _activityResolver();
        if (activity is null)
            throw new InvalidOperationException("The configured CurrentActivityResolver returned null. " +
                                                "You need to setup the Android implementation via AndroidHostedServiceForegreoundService.SetCurrentActivityResolver(). " +
                                                "If you are using CrossCurrentActivity don't forget to initialize it, too!");

        return activity;
    }

   
    public override IBinder OnBind(Intent intent)
    {
        throw new NotImplementedException();
    }
    [return: GeneratedEnum]//we catch the actions intents to know the state of the foreground service
    public override StartCommandResult OnStartCommand(Intent intent, [GeneratedEnum] StartCommandFlags flags, int startId)
    {
        if (intent.Action == "START_SERVICE")
        {
            RegisterNotification();//Proceed to notify
            Task.WhenAll(_hostedServices.Select(service => service.StartAsync(CancellationToken.None))).GetAwaiter().GetResult();
        }
        else if (intent.Action == "STOP_SERVICE")
        {
            StopForeground(StopForegroundFlags.Detach);//Stop the service
            StopSelfResult(startId);
            
            Task.WhenAll(_hostedServices.ToList().Select(service => service.StopAsync(CancellationToken.None))).GetAwaiter().GetResult();
        }
      
        return StartCommandResult.NotSticky;
    }
    
    
    //Start and Stop Intents, set the actions for the MainActivity to get the state of the foreground service
    //Setting one action to start and one action to stop the foreground service
    public void Start(IEnumerable<IHostedService> services)
    {
        _hostedServices = services;
        Intent startService = new Intent(GetCurrentActivity(), typeof(AndroidHostedServiceForegreoundService));
        startService.SetAction("START_SERVICE");
        GetCurrentActivity().StartService(startService);
   
        
        
    }
    
    public void Stop()
    {
        Intent stopIntent = new Intent(GetCurrentActivity(), this.Class);
        stopIntent.SetAction("STOP_SERVICE");
        GetCurrentActivity().StartService(stopIntent);
    }
    
    private void RegisterNotification()
    {
        NotificationChannel channel = new NotificationChannel("ServiceChannel", "ServiceDemo", NotificationImportance.Max);
        NotificationManager manager = (NotificationManager)GetCurrentActivity().GetSystemService(Context.NotificationService);
        manager.CreateNotificationChannel(channel);
        Notification notification = new Notification.Builder(this, "ServiceChannel")
            .SetContentTitle("BTCPay App running")
            .SetSmallIcon(Resource.Drawable.abc_ab_share_pack_mtrl_alpha)
            .SetOngoing(true)
            .Build();
    
        StartForeground(100, notification);
    }

}