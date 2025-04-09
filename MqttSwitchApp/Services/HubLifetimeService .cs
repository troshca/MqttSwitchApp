using Microsoft.AspNetCore.SignalR;

public class HubLifetimeService : IHostedService
{
    private readonly IHubContext<MqttHub> _hubContext;
    private readonly ModbusService _modbusService;

    public HubLifetimeService(IHubContext<MqttHub> hubContext, ModbusService modbusService)
    {
        _hubContext = hubContext;
        _modbusService = modbusService;
        _modbusService.OnReadingsUpdated += BroadcastReadings;
    }

    private async Task BroadcastReadings(string groupName, string type, ushort[] readings)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveModbusData", groupName, type, readings);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error broadcasting readings: {ex.Message}");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _modbusService.OnReadingsUpdated -= BroadcastReadings;
        return Task.CompletedTask;
    }
}