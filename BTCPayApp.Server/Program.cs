using BTCPayApp.Core.Extensions;
using BTCPayApp.Desktop;
using BTCPayApp.UI;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddBTCPayAppUIServices();
builder.Services.ConfigureBTCPayAppCore();
builder.Services.ConfigureBTCPayAppDesktop();
builder.Services.AddLogging();
#if DEBUG
builder.Services.AddDangerousSSLSettingsForDev();
#endif
// Configure the HTTP request pipeline.
var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");
app.Run();
