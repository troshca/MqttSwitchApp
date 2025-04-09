
using System.IO.Ports;

public class ModbusRtuRgbam
{
    public string PortName { get; set; } = "COM13";
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public System.IO.Ports.Parity Parity { get; set; } = System.IO.Ports.Parity.None;
    public System.IO.Ports.StopBits StopBits { get; set; } = System.IO.Ports.StopBits.One;
    public int ReadTimeout { get; set; } = 1000;
    public int WriteTimeout { get; set; } = 1000;
    public byte DeviceAddress { get; set; } = 1;

    // Регистры для групп (по 2 регистра на параметр)
    public Dictionary<string, ushort> GroupRegisters { get; set; } = new Dictionary<string, ushort>
        {
            { "VoltageRms", 0 },      // [0 + (20 * i)]
            { "VoltageInst", 10 },    // [10 + (20 * i)]
            { "FrequencyRms", 8 },    // [8 + (20 * i)]
            { "FrequencyInst", 18 }   // [18 + (20 * i)]
        };

    // Регистры для розеток (по 2 регистра на параметр, начиная с 60)
    public Dictionary<string, ushort> SocketRegisters { get; set; } = new Dictionary<string, ushort>
        {
            { "VoltageRms", 60 },      // [0 + (20 * i) + 60]
            { "CurrentRms", 62 },      // [2 + (20 * i) + 60]
            { "ActivePowerRms", 64 },  // [4 + (20 * i) + 60]
            { "ReactivePowerRms", 66 },// [6 + (20 * i) + 60]
            { "FrequencyRms", 68 },    // [8 + (20 * i) + 60]
            { "VoltageInst", 70 },     // [10 + (20 * i) + 60]
            { "CurrentInst", 72 },     // [12 + (20 * i) + 60]
            { "ActivePowerInst", 74 }, // [14 + (20 * i) + 60]
            { "ReactivePowerInst", 76 },// [16 + (20 * i) + 60]
            { "FrequencyInst", 78 }    // [18 + (20 * i) + 60]
        };
}

public class GroupRegister
{
    public string Name { get; set; }
    public ushort ModbusRegister { get; set; }
}