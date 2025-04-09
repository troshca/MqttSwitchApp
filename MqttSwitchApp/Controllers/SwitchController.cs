using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MqttSwitchApp.Configs;
using System.Text.RegularExpressions;

[Route("api/[controller]")]
[ApiController]
public class SwitchController : ControllerBase
{
    private readonly ModbusService _modbusService;
    private readonly SwitchState _switchState;
    private readonly IHubContext<MqttHub> _hubContext;
    private readonly List<GroupConfig> _groups; // Add this field

    public SwitchController(ModbusService modbusService,
                          SwitchState switchState,
                          IHubContext<MqttHub> hubContext,
                          List<GroupConfig> groups) // Add this parameter
    {
        _modbusService = modbusService;
        _switchState = switchState;
        _hubContext = hubContext;
        _groups = groups; // Initialize the field
    }

    [HttpPost("update")]
    public async Task<IActionResult> UpdateSwitches([FromBody] SwitchStateDto state)
    {
        if (state == null || string.IsNullOrEmpty(state.GroupName) || state.Switches == null)
        {
            return BadRequest("Invalid switch state data.");
        }

        try
        {
            // Check if group is locked
            if (_switchState.IsGroupLocked(state.GroupName))
            {
                return StatusCode(423, $"Group {state.GroupName} is currently locked");
            }

            // Mark as user-initiated to prevent Modbus feedback loop
            _switchState.IsUserInitiated = true;

            if (_modbusService.SendSwitchState(state.GroupName, state.Switches))
            {
                // Convert bool[] to int state
                int newState = 0;
                for (int i = 0; i < state.Switches.Length; i++)
                {
                    if (state.Switches[i]) newState |= (1 << i);
                }

                // Update state
                _switchState.UpdateState(state.GroupName, newState, true);
                _switchState.SetLock(state.GroupName, false); // Unlock after successful update

                // Prepare response data
                var responseData = new
                {
                    States = new Dictionary<string, int> { { state.GroupName, newState } },
                    Initialized = new Dictionary<string, bool> { { state.GroupName, true } }
                };

                // Notify clients
                await _hubContext.Clients.All.SendAsync("ReceiveSwitchState", responseData);

                return Ok(responseData);
            }
            return StatusCode(500, $"Failed to update switches for {state.GroupName}.");
        }
        catch (Exception ex)
        {
            // Re-lock on error
            _switchState.SetLock(state.GroupName, true, DateTime.Now.AddMinutes(1));
            return StatusCode(500, $"Error updating switches: {ex.Message}");
        }
        finally
        {
            _switchState.IsUserInitiated = false;
        }
    }

    [HttpGet("state")]
    public IActionResult GetSwitchState()
    {
        try
        {
            var response = new
            {
                States = _groups.ToDictionary(g => g.Name, g => _switchState.GetState(g.Name)),
                Initialized = _groups.ToDictionary(g => g.Name, g => _switchState.IsGroupInitialized(g.Name))
            };
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error retrieving switch state: {ex.Message}");
        }
    }
}