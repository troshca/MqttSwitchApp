using Microsoft.AspNetCore.SignalR;
using MqttSwitchApp.Configs;
public class ModbusInitializerService : IHostedService
{
    private readonly IHubContext<MqttHub> _hubContext;
    private readonly SwitchState _switchState;
    private readonly ModbusService _modbusService;
    private readonly List<GroupConfig> _groups;

    public ModbusInitializerService(
        IHubContext<MqttHub> hubContext,
        SwitchState switchState,
        ModbusService modbusService,
        List<GroupConfig> groups)
    {
        _hubContext = hubContext;
        _switchState = switchState;
        _modbusService = modbusService;
        _groups = groups;

        _modbusService.OnSwitchStateChanged += UpdateSwitchStateFromModbus;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Task.Run(() => InitializeFromModbusAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    private async Task InitializeFromModbusAsync(CancellationToken cancellationToken)
    {
        while (_groups.Any(g => !_modbusService.IsGroupInitialized(g.Name)) &&
               !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(100, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested) return;

        foreach (var group in _groups)
        {
            var state = _modbusService.GetSwitchState(group.Name);
            UpdateSwitchStateFromModbus(group.Name, state);
        }
    }

    private async void UpdateSwitchStateFromModbus(string groupName, ushort newState)
    {
        try
        {
            var group = _groups.Find(g => g.Name == groupName);
            if (group == null) return;

            // Skip if this is a user-initiated change
            if (_switchState.IsUserInitiated)
            {
                Console.WriteLine($"User-initiated change for {groupName}, ignoring Modbus update");
                _switchState.IsUserInitiated = false;
                return;
            }

            // Skip if state hasn't changed
            var currentState = _switchState.GetState(groupName);
            if (currentState == newState) return;

            // Update the state
            _switchState.UpdateState(groupName, newState, true);
            _switchState.SetLock(groupName, false); // Unlock the group

            // Notify clients
            await _hubContext.Clients.All.SendAsync("ReceiveSwitchState", new
            {
                States = _groups.ToDictionary(g => g.Name, g => _switchState.GetState(g.Name)),
                Initialized = _groups.ToDictionary(g => g.Name, g => _switchState.IsGroupInitialized(g.Name))
            });

            Console.WriteLine($"{groupName} updated from Modbus: {Convert.ToString(newState, 2).PadLeft(group.SwitchCount, '0')}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in UpdateSwitchStateFromModbus: {ex.Message}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _modbusService.OnSwitchStateChanged -= UpdateSwitchStateFromModbus;
        return Task.CompletedTask;
    }
}