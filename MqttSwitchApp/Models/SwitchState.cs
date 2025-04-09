using MqttSwitchApp.Configs;
using System.Text.RegularExpressions;

public class SwitchState
{
    private readonly Dictionary<string, bool[]> _switches = new();
    private readonly Dictionary<string, bool> _isLocked = new();
    private readonly Dictionary<string, DateTime?> _lockUntil = new();
    private readonly Dictionary<string, bool> _initialized = new();
    private readonly List<GroupConfig> _groups;  // Add this field

    public bool IsUserInitiated { get; set; } = false;

    public SwitchState(List<GroupConfig> groups)
    {
        _groups = groups;  // Initialize groups
        foreach (var group in groups)
        {
            _switches[group.Name] = new bool[group.SwitchCount];
            _isLocked[group.Name] = false;
            _lockUntil[group.Name] = null;
            _initialized[group.Name] = false;
        }
    }

    public Dictionary<string, int> GetAllStates()
    {
        return _groups.ToDictionary(g => g.Name, g => GetState(g.Name));
    }

    public Dictionary<string, bool> GetInitializationStatus()
    {
        return _groups.ToDictionary(g => g.Name, g => IsGroupInitialized(g.Name));
    }

    public void UpdateState(string groupName, int state, bool isInitialized)
    {
        if (!_switches.ContainsKey(groupName)) return;

        // Update both representations
        for (int i = 0; i < _switches[groupName].Length; i++)
        {
            _switches[groupName][i] = (state & (1 << i)) != 0;
        }

        _initialized[groupName] = isInitialized;
    }

    public bool[] GetSwitches(string groupName)
    {
        return _switches.TryGetValue(groupName, out var switches)
            ? switches
            : Array.Empty<bool>();
    }

    public bool IsGroupLocked(string groupName)
    {
        if (!_isLocked.TryGetValue(groupName, out var isLocked)) return true;
        if (!_lockUntil.TryGetValue(groupName, out var lockUntil)) return isLocked;

        return isLocked && (lockUntil == null || lockUntil > DateTime.Now);
    }

    public void SetLock(string groupName, bool isLocked, DateTime? until = null)
    {
        if (_isLocked.ContainsKey(groupName))
        {
            _isLocked[groupName] = isLocked;
            _lockUntil[groupName] = until;
        }
    }

    public bool IsGroupInitialized(string groupName)
    {
        return _initialized.TryGetValue(groupName, out var initialized) && initialized;
    }

    public int GetState(string groupName)
    {
        if (!_switches.TryGetValue(groupName, out var switches)) return 0;

        int state = 0;
        for (int i = 0; i < switches.Length; i++)
        {
            if (switches[i]) state |= (1 << i);
        }
        return state;
    }
}