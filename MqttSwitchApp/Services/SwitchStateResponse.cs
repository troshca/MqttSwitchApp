public class SwitchStateResponse
{
    public Dictionary<string, ushort> States { get; set; }
    public Dictionary<string, byte> Statuses { get; set; }
    public Dictionary<string, bool> Initialized { get; set; }
}