namespace MqttSwitchApp.Configs
{
    public class GroupConfig
    {
        public string Name { get; set; }
        public int SwitchCount { get; set; }
        public ushort ModbusRegister { get; set; }
        public int StartIndex { get; set; }
        public int ModbusOffset { get; set; }

        public static List<GroupConfig> GetDefaultGroups()
        {
            return new List<GroupConfig>
            {
                new GroupConfig { Name = "Группа 1", SwitchCount = 9, ModbusRegister = 2, StartIndex = 1 },
                new GroupConfig { Name = "Группа 2", SwitchCount = 9, ModbusRegister = 4, StartIndex = 10 }
            };
        }
    }
}
