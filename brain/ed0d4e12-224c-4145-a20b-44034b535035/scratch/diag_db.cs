using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Sentinel.Dashboard.Data;
using Sentinel.Dashboard.Models.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=airwitness_db;Username=postgres;Password=postgres"));
    })
    .Build();

using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

var carnaval = db.RadioStations
    .Include(s => s.Schedules)
    .FirstOrDefault(s => s.Name.Contains("Carnaval"));

if (carnaval != null)
{
    Console.WriteLine($"ID: {carnaval.Id}");
    Console.WriteLine($"Nombre: {carnaval.Name}");
    Console.WriteLine($"URL: {carnaval.StreamUrl}");
    Console.WriteLine($"DefaultMasterPath: {carnaval.DefaultMasterPath ?? "NULL"}");
    Console.WriteLine($"Schedules: {carnaval.Schedules.Count}");
    foreach (var s in carnaval.Schedules)
    {
        Console.WriteLine($"  - {s.StartTime} to {s.EndTime} (Active: {s.IsActive})");
    }
}
else
{
    Console.WriteLine("Radio Carnaval no encontrada.");
}
