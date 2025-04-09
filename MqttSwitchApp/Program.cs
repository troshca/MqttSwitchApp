using Microsoft.Extensions.DependencyInjection;
using MqttSwitchApp.Configs;
using MqttSwitchApp.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<List<GroupConfig>>(provider =>
{
    var settingsFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".settings");
    var groups = GroupConfig.GetDefaultGroups();
    return groups;
});
builder.Services.AddSingleton<SwitchState>(provider => new SwitchState(provider.GetRequiredService<List<GroupConfig>>()));
builder.Services.AddSingleton<ModbusRtuRgbam>(provider =>
{
    var settingsFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".settings");
    if (System.IO.File.Exists(settingsFilePath))
    {
        var json = System.IO.File.ReadAllText(settingsFilePath);
        var settings = JsonSerializer.Deserialize<ModbusRtuRgbam>(json);
        return settings ?? new ModbusRtuRgbam();
    }
    return new ModbusRtuRgbam();
});
builder.Services.AddSingleton<ModbusService>(provider =>
    new ModbusService(provider.GetRequiredService<ModbusRtuRgbam>(), provider.GetRequiredService<List<GroupConfig>>()));
builder.Services.AddHostedService<MqttService>();
builder.Services.AddHostedService<ModbusInitializerService>();
builder.Services.AddHostedService<HubLifetimeService>();
builder.Services.AddControllers();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.MapHub<MqttHub>("/mqttHub");
app.MapControllers();
app.MapGet("/", async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(File.ReadAllText("wwwroot/index.html"));
});
app.MapGet("/group_readings", async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(File.ReadAllText("wwwroot/group_readings.html"));
});
app.MapGet("/settings", async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(File.ReadAllText("wwwroot/settings.html"));
});

app.Lifetime.ApplicationStopped.Register(async () =>
{
    var modbusService = app.Services.GetService<ModbusService>();
    if (modbusService != null)
    {
        await modbusService.DisposeAsync();
    }
    Console.WriteLine("Application stopped, ModbusService disposed.");
});

app.Run();