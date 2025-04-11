using Microsoft.AspNetCore.SignalR;

public class HubLifetimeService : IHostedService
{
    private readonly IHubContext<MqttHub> _hubContext;
    private readonly ModbusService _modbusService;

    public HubLifetimeService(IHubContext<MqttHub> hubContext, ModbusService modbusService)
    {
        _hubContext = hubContext;
        _modbusService = modbusService;
        _modbusService.OnReadingsUpdated += async (groupName, type, readings) =>
        {
            await BroadcastReadings(groupName, type, readings);
        };
    }

    private async Task BroadcastReadings(string groupName, string type, ModbusReadingData readings)
    {
        try
        {
            var data = new
            {
                GroupName = readings.GroupName,
                UpdateType = readings.UpdateType,
                Registers = readings.Registers,
                Status = readings.Status,
                State = readings.UpdateType == "status_update" && readings.Registers?.Length > 0
                    ? readings.Registers[0]
                    : (ushort?)null
            };
            await _hubContext.Clients.All.SendAsync("ReceiveModbusData", data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error broadcasting readings: {ex.Message}");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _modbusService.OnReadingsUpdated -= async (groupName, type, readings) =>
        {
            await BroadcastReadings(groupName, type, readings);
        };
        return Task.CompletedTask;
    }
}