using Microsoft.AspNetCore.SignalR;
using MqttSwitchApp.Configs;
using MqttSwitchApp.Services;

public class MqttHub : Hub
{
    private readonly SwitchState _switchState;
    private readonly ModbusService _modbusService;
    private readonly List<GroupConfig> _groups;
    private readonly IHubContext<MqttHub> _hubContext;
    private bool _disposed = false;

    public MqttHub(SwitchState switchState, ModbusService modbusService, List<GroupConfig> groups, IHubContext<MqttHub> hubContext)
    {
        _switchState = switchState;
        _modbusService = modbusService;
        _groups = groups;
        _hubContext = hubContext;

        _modbusService.OnReadingsUpdated += BroadcastReadings;
    }

    public async Task UpdateSwitchState(SwitchStateDto state)
    {
        if (_modbusService.SendSwitchState(state.GroupName, state.Switches))
        {
            await Clients.All.SendAsync("ReceiveSwitchState", _switchState);
        }
        else
        {
            Console.WriteLine($"Failed to update switch state for {state.GroupName}");
        }
    }

    public async Task UpdateMqttData(string data)
    {
        await Clients.All.SendAsync("ReceiveMqttData", data);
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("ReceiveSwitchState", _switchState);
        await base.OnConnectedAsync();
    }

    private async Task BroadcastReadings(string groupName, string type, ushort[] readings)
    {
        if (_disposed) return;

        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveModbusData", groupName, type, readings);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error broadcasting readings: {ex.Message}");
        }
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        _disposed = true;
        _modbusService.OnReadingsUpdated -= BroadcastReadings;
        await base.OnDisconnectedAsync(exception);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _modbusService.OnReadingsUpdated -= BroadcastReadings;
        }
    }
}