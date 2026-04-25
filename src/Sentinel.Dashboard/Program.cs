using Sentinel.Dashboard.Components;
using Sentinel.Dashboard.Services;
using Sentinel.Dashboard.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.IO;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient("Insecure")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
    });
builder.Services.AddHttpClient(); // Cliente por defecto
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton<Sentinel.Dashboard.Services.RadioConfigService>();
builder.Services.AddSingleton<Sentinel.Dashboard.Services.FFmpegAudioService>();
builder.Services.AddSingleton<Sentinel.Dashboard.Services.NativeAudioMonitor>();
builder.Services.AddSingleton<Sentinel.Dashboard.Services.WorkerOrchestrator>();
builder.Services.AddSingleton<Sentinel.Dashboard.Services.IAlertService, Sentinel.Dashboard.Services.AlertService>();
builder.Services.AddSingleton<Sentinel.Dashboard.Services.StreamDiscoveryService>();
builder.Services.AddHostedService<Sentinel.Dashboard.Services.MonitoringSchedulerService>();
builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options => {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddScoped<Sentinel.Dashboard.Services.ThemeService>();

var app = builder.Build();

// Migración automática de base de datos al iniciar
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

// Servir la carpeta de evidencias externa para permitir descargas directas
var evidencePath = Environment.GetEnvironmentVariable("EVIDENCE_DIR");
if (string.IsNullOrEmpty(evidencePath))
{
    evidencePath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "Sentinel.Worker", "evidencia"));
}
if (!Directory.Exists(evidencePath)) Directory.CreateDirectory(evidencePath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(evidencePath),
    RequestPath = "/evidencia"
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
