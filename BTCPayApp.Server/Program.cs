
using BTCPayApp.Core;
using BTCPayApp.Desktop;
using BTCPayApp.UI;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseWebRoot("wwwroot");
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddBTCPayAppUIServices();
builder.Services.ConfigureBTCPayAppCore();
builder.Services.ConfigureBTCPayAppDesktop();

builder.Services.AddLogging();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
