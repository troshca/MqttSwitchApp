using Microsoft.AspNetCore.Mvc;
using MqttSwitchApp.Configs;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly string _settingsFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".settings");
    private readonly List<GroupConfig> _groups;

    public SettingsController(List<GroupConfig> groups)
    {
        _groups = groups;
    }

    [HttpGet]
    public IActionResult GetSettings()
    {
        try
        {
            ModbusRtuRgbam settings;
            if (System.IO.File.Exists(_settingsFilePath))
            {
                var json = System.IO.File.ReadAllText(_settingsFilePath);
                settings = JsonSerializer.Deserialize<ModbusRtuRgbam>(json);
            }
            else
            {
                settings = new ModbusRtuRgbam();
            }

            return Ok(new
            {
                deviceAddress = settings.DeviceAddress,
                baudRate = settings.BaudRate,
                groupRegisters = settings.GroupRegisters,
                socketRegisters = settings.SocketRegisters
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading settings: {ex.Message}");
            return StatusCode(500, "Error reading settings");
        }
    }

    [HttpPost]
    public IActionResult SaveSettings([FromBody] SettingsDto settingsDto)
    {
        try
        {
            var rgbamSettings = new ModbusRtuRgbam
            {
                DeviceAddress = (byte)settingsDto.DeviceAddress,
                BaudRate = settingsDto.BaudRate,
                GroupRegisters = new ModbusRtuRgbam().GroupRegisters, // Значения по умолчанию
                SocketRegisters = new ModbusRtuRgbam().SocketRegisters // Значения по умолчанию
            };
            var json = JsonSerializer.Serialize(rgbamSettings);
            System.IO.File.WriteAllText(_settingsFilePath, json);
            return Ok();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving settings: {ex.Message}");
            return StatusCode(500, "Error saving settings");
        }
    }
}

public class SettingsDto
{
    public int DeviceAddress { get; set; }
    public int BaudRate { get; set; }
}