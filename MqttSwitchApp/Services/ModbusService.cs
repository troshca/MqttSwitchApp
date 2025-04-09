using Microsoft.Win32;
using MqttSwitchApp.Configs;
using NModbus;
using NModbus.Serial;
using System.IO.Ports;
public class ModbusService : IDisposable, IAsyncDisposable
{
    private readonly SerialPort _serialPort;
    private readonly IModbusSerialMaster _modbusMaster;
    private readonly Mutex _mutex = new Mutex();
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly ModbusRtuRgbam _rgbamSettings;
    private readonly List<GroupConfig> _groups;
    private bool _disposed = false;
    private readonly Dictionary<string, bool> _isInitialized = new Dictionary<string, bool>();
    private readonly Dictionary<string, ushort> _switchStates = new Dictionary<string, ushort>();
    private bool _isWriting = false;
    private Task _pollReadingsTask;
    private List<Task> _pollRegisterTasks = new List<Task>(); // Отслеживаем все задачи групп

    public event Action<string, ushort> OnSwitchStateChanged;
    public event Func<string, string, ushort[], Task> OnReadingsUpdated;

    public ModbusService(ModbusRtuRgbam rgbamSettings, List<GroupConfig> groups)
    {
        _rgbamSettings = rgbamSettings;
        _groups = groups;

        foreach (var group in groups)
        {
            _isInitialized[group.Name] = false;
            _switchStates[group.Name] = 0;
        }

        _serialPort = new SerialPort
        {
            PortName = _rgbamSettings.PortName,
            BaudRate = _rgbamSettings.BaudRate,
            DataBits = _rgbamSettings.DataBits,
            Parity = _rgbamSettings.Parity,
            StopBits = _rgbamSettings.StopBits,
            ReadTimeout = _rgbamSettings.ReadTimeout,
            WriteTimeout = _rgbamSettings.WriteTimeout,
            RtsEnable = true,
            Handshake = Handshake.None
        };

        try
        {
            _serialPort.Open();
            Console.WriteLine($"Connected to Modbus on {_rgbamSettings.PortName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to Modbus: {ex.Message}");
            throw;
        }

        var factory = new ModbusFactory();
        _modbusMaster = factory.CreateRtuMaster(_serialPort);

        foreach (var group in _groups)
        {
            var task = Task.Run(() => PollModbusRegisterAsync(group, _cts.Token));
            _pollRegisterTasks.Add(task); // Добавляем задачу в список
        }

        _pollReadingsTask = Task.Run(() => PollReadingsAsync(_cts.Token));
    }

    private async Task PollModbusRegisterAsync(GroupConfig group, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            try
            {
                _mutex.WaitOne();
                var registers = _modbusMaster.ReadHoldingRegisters(_rgbamSettings.DeviceAddress, group.ModbusRegister, 1);
                if (_switchStates[group.Name] != registers[0] && !_isWriting)
                {
                    _switchStates[group.Name] = registers[0];
                    Console.WriteLine($"{group.Name} updated from Modbus: {string.Join(",", Enumerable.Range(0, group.SwitchCount).Select(i => (registers[0] & (1 << i)) != 0))}");
                    OnSwitchStateChanged?.Invoke(group.Name, registers[0]);
                }
                _isInitialized[group.Name] = true;
                Console.WriteLine($"{group.Name} read from Modbus: {registers[0]} (Binary: {Convert.ToString(registers[0], 2).PadLeft(group.SwitchCount, '0')})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading Modbus {group.Name} register: {ex.Message}");
                _isInitialized[group.Name] = false;
            }
            finally
            {
                _mutex.ReleaseMutex();
            }

            try
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"{group.Name} polling canceled.");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in delay for {group.Name}: {ex.Message}");
                break;
            }
        }
        Console.WriteLine($"{group.Name} polling stopped.");
    }

    private async Task PollReadingsAsync(CancellationToken cancellationToken)
    {
        const int groupRegisterCount = 20;
        const int socketRegisterCount = 20;
        const int firstNumber = 60;

        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            try
            {
                foreach (var group in _groups)
                {
                    if (cancellationToken.IsCancellationRequested || _disposed)
                        break;

                    try
                    {
                        int groupBase = group.ModbusOffset * groupRegisterCount;
                        var groupRegisters = ReadHoldingRegisters((ushort)groupBase, groupRegisterCount);
                        Console.WriteLine($"Read group registers for {group.Name}: {string.Join(", ", groupRegisters)}");

                        if (OnReadingsUpdated != null && !_disposed)
                        {
                            await SafeInvokeReadingsUpdated(group.Name, "group", groupRegisters);
                        }

                        for (int i = 0; i < group.SwitchCount; i++)
                        {
                            if (cancellationToken.IsCancellationRequested || _disposed)
                                break;

                            try
                            {
                                int socketBase = groupBase + firstNumber + (i * socketRegisterCount);
                                var socketRegisters = ReadHoldingRegisters((ushort)socketBase, socketRegisterCount);
                                Console.WriteLine($"Read socket {i + 1} registers for {group.Name}: {string.Join(", ", socketRegisters)}");

                                if (OnReadingsUpdated != null && !_disposed)
                                {
                                    await SafeInvokeReadingsUpdated(group.Name, $"socket{i}", socketRegisters);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error polling socket {i + 1} for {group.Name}: {ex.Message}");
                                // Continue with next socket instead of breaking
                                continue;
                            }

                            await Task.Delay(50, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error polling group {group.Name}: {ex.Message}");
                        // Continue with next group instead of breaking
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General error in PollReadingsAsync: {ex.Message}");
                // Don't break here - let the loop continue
            }

            try
            {
                await Task.Delay(30000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("PollReadingsAsync delay canceled.");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in delay for PollReadingsAsync: {ex.Message}");
                // Continue the loop despite delay error
            }
        }
        Console.WriteLine("PollReadingsAsync stopped.");
    }

    public bool SendSwitchState(string groupName, bool[] switches)
    {
        try
        {
            _mutex.WaitOne();
            _isWriting = true;
            ushort state = 0;
            for (int i = 0; i < switches.Length; i++)
            {
                if (switches[i]) state |= (ushort)(1 << i);
            }
            var group = _groups.FirstOrDefault(g => g.Name == groupName);
            if (group == null)
            {
                Console.WriteLine($"Group {groupName} not found.");
                return false;
            }
            _modbusMaster.WriteSingleRegister(_rgbamSettings.DeviceAddress, group.ModbusRegister, state);
            _switchStates[groupName] = state;
            OnSwitchStateChanged?.Invoke(groupName, state);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing switch state for {groupName}: {ex.Message}");
            return false;
        }
        finally
        {
            _isWriting = false;
            _mutex.ReleaseMutex();
        }
    }

    public bool IsGroupInitialized(string groupName) => _isInitialized.ContainsKey(groupName) && _isInitialized[groupName];

    public ushort GetSwitchState(string groupName)
    {
        return _switchStates.ContainsKey(groupName) ? _switchStates[groupName] : (ushort)0;
    }

    public ushort[] ReadHoldingRegisters(ushort startAddress, ushort numberOfPoints)
    {
        if (!_serialPort.IsOpen)
        {
            Console.WriteLine("Serial port is not open.");
            return new ushort[0];
        }

        try
        {
            _mutex.WaitOne();
            var registers = _modbusMaster.ReadHoldingRegisters(_rgbamSettings.DeviceAddress, startAddress, numberOfPoints);
            return registers;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading holding registers at {startAddress}: {ex.Message}");
            return new ushort[0];
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            Console.WriteLine("Disposing ModbusService...");
            _cts.Cancel();

            // Clear event subscriptions
            OnReadingsUpdated = null;
            OnSwitchStateChanged = null;

            if (_pollReadingsTask != null)
            {
                Console.WriteLine("Waiting for PollReadingsAsync to complete...");
                await _pollReadingsTask;
            }

            if (_pollRegisterTasks.Any())
            {
                Console.WriteLine("Waiting for register polling tasks...");
                await Task.WhenAll(_pollRegisterTasks);
            }

            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            _serialPort.Dispose();

            _disposed = true;
            Console.WriteLine("ModbusService disposed");
        }
    }

    private async Task SafeInvokeReadingsUpdated(string groupName, string type, ushort[] readings)
    {
        try
        {
            if (OnReadingsUpdated != null && !_disposed)
            {
                foreach (var handler in OnReadingsUpdated.GetInvocationList())
                {
                    try
                    {
                        await ((Func<string, string, ushort[], Task>)handler)(groupName, type, readings);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Remove the disposed handler
                        OnReadingsUpdated -= (Func<string, string, ushort[], Task>)handler;
                        Console.WriteLine("Removed disposed handler from OnReadingsUpdated");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error invoking readings update handler: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SafeInvokeReadingsUpdated: {ex.Message}");
        }
    }
}