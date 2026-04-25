using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Sentinel.Dashboard.Data;
using Sentinel.Dashboard.Models.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=airwitness_db;Username=postgres;Password=postgres"));
    })
    .Build();

using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

var carnaval = db.RadioStations.FirstOrDefault(s => s.Name.Contains("Carnaval"));

if (carnaval != null)
{
    var audioPath = Path.Combine(Environment.CurrentDirectory, "src", "Sentinel.Dashboard", "wwwroot", "assets", "radio-carnaval", "comercial_lider.mp3");
    
    // Si no está en el path absoluto esperado por el orquestador (que usa Environment.CurrentDirectory de la app)
    // Buscamos la ruta relativa que el orquestador usa
    var relativePath = Path.Combine(Environment.CurrentDirectory, "wwwroot", "assets", "radio-carnaval", "comercial_lider.mp3");
    
    carnaval.DefaultMasterPath = relativePath;
    db.SaveChanges();
    Console.WriteLine($"[REPARADO] Radio Carnaval vinculada a: {relativePath}");
}
else
{
    Console.WriteLine("[ERROR] No se encontró Radio Carnaval en la DB.");
}
