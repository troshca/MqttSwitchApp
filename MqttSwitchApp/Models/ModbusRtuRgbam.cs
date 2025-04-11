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
    public byte DeviceAddress { get; set; } = 16;

    // Регистры для групп (по 2 регистра на параметр)
    public Dictionary<string, ushort> GroupRegisters { get; set; } = new Dictionary<string, ushort>
    {
        { "VoltageRms", 0 },
        { "VoltageInst", 10 },
        { "FrequencyRms", 8 },
        { "FrequencyInst", 18 }
    };

    // Регистры для розеток (по 2 регистра на параметр, начиная с 60)
    public Dictionary<string, ushort> SocketRegisters { get; set; } = new Dictionary<string, ushort>
    {
        { "VoltageRms", 60 },
        { "CurrentRms", 62 },
        { "ActivePowerRms", 64 },
        { "ReactivePowerRms", 66 },
        { "FrequencyRms", 68 },
        { "VoltageInst", 70 },
        { "CurrentInst", 72 },
        { "ActivePowerInst", 74 },
        { "ReactivePowerInst", 76 },
        { "FrequencyInst", 78 }
    };

    // Регистры статусов реле
    public Dictionary<string, ushort> StatusRegisters { get; set; } = new Dictionary<string, ushort>
    {
        { "Group1Status", 3 },  // Исправлено с "Группа 1Status"
        { "Group2Status", 5 }   // Исправлено с "Группа 2Status"
    };

    // Коды статусов реле
    public Dictionary<ushort, string> StatusCodes { get; set; } = new Dictionary<ushort, string>
    {
        { 0x10, "RELAYS_NOT_INITIALIZED" },
        { 0x11, "RELAYS_INITIALIZED" },
        { 0x15, "REALYS_NEED_TO_DROP" },
        { 0x16, "RELAYS_NEED_TO_RESTORE" },
        { 0x17, "RELAYS_NEED_TO_RESTORE_WITH_OUT_LATCH" },
        { 0x20, "RELAYS_WAS_DROPPED" },
        { 0x30, "RELAYS_NEED_TO_CHANGE" },
        { 0x40, "RELAYS_NORMAL_WORK" }
    };
}

public class GroupRegister
{
    public string Name { get; set; }
    public ushort ModbusRegister { get; set; }
    public ushort StatusRegister { get; set; }
}