﻿using Microsoft.Win32;
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
    public event Func<string, string, ModbusReadingData, Task> OnReadingsUpdated;

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

                // Чтение состояния реле
                var registers = _modbusMaster.ReadHoldingRegisters(_rgbamSettings.DeviceAddress, group.ModbusRegister, 1);

                // Чтение статуса реле
                var statusKey = group.Name.Contains("1") ? "Group1Status" : "Group2Status";
                ushort statusValue = 0x10; // Значение по умолчанию

                if (_rgbamSettings.StatusRegisters.TryGetValue(statusKey, out var statusRegister))
                {
                    var status = _modbusMaster.ReadHoldingRegisters(_rgbamSettings.DeviceAddress, statusRegister, 1);
                    Console.WriteLine($"Raw status read for {group.Name}: 0x{status[0]:X2}"); // Логирование сырого значения
                    if (status != null && status.Length > 0)
                    {
                        statusValue = status[0];
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Status array for {group.Name} is empty or null");
                    }
                }
                else
                {
                    Console.WriteLine($"Warning: Status register for {statusKey} not found in configuration");
                }

                if (_switchStates[group.Name] != registers[0] && !_isWriting)
                {
                    _switchStates[group.Name] = registers[0];
                    Console.WriteLine($"{group.Name} updated from Modbus: {string.Join(",", Enumerable.Range(0, group.SwitchCount).Select(i => (registers[0] & (1 << i)) != 0))}");
                    OnSwitchStateChanged?.Invoke(group.Name, registers[0]);
                }

                _isInitialized[group.Name] = true;

                // Отправка обновленных данных
                if (OnReadingsUpdated != null && !_disposed)
                {
                    var readingData = new ModbusReadingData
                    {
                        GroupName = group.Name,
                        Registers = new[] { registers[0] },
                        Status = statusValue,
                        UpdateType = "status_update"
                    };
                    Console.WriteLine($"Sending data for {group.Name}: Status = 0x{statusValue:X2}, Registers = {registers[0]}");
                    await OnReadingsUpdated.Invoke(group.Name, "status_update", readingData);
                }

                Console.WriteLine($"{group.Name} read from Modbus: {registers[0]} " +
                    $"(Binary: {Convert.ToString(registers[0], 2).PadLeft(group.SwitchCount, '0')}) " +
                    $"Status: 0x{statusValue:X2}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading Modbus {group.Name} register: {ex.Message}");
                _isInitialized[group.Name] = false;

                // Отправка статуса ошибки
                if (OnReadingsUpdated != null && !_disposed)
                {
                    await OnReadingsUpdated.Invoke(group.Name, "error", new ModbusReadingData
                    {
                        GroupName = group.Name,
                        Registers = Array.Empty<ushort>(),
                        Status = 0x10, // RELAYS_NOT_INITIALIZED
                        UpdateType = "error"
                    });
                }
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

    public ushort GetRelayStatus(string groupName)
    {
        try
        {
            _mutex.WaitOne();
            var group = _groups.FirstOrDefault(g => g.Name == groupName);
            if (group == null) return 0x10; // RELAYS_NOT_INITIALIZED

            var statusRegister = _rgbamSettings.StatusRegisters[$"{groupName}Status"];
            var status = _modbusMaster.ReadHoldingRegisters(_rgbamSettings.DeviceAddress, statusRegister, 1);
            return status.Length > 0 ? status[0] : (ushort)0x10;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading relay status for {groupName}: {ex.Message}");
            return 0x10; // RELAYS_NOT_INITIALIZED
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
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
                        // Чтение статуса реле
                        var status = GetRelayStatus(group.Name);

                        int groupBase = group.ModbusOffset * groupRegisterCount;
                        var groupRegisters = ReadHoldingRegisters((ushort)groupBase, groupRegisterCount);

                        // Создаем массив для всех данных группы и розеток
                        var allRegisters = new ushort[groupRegisterCount + group.SwitchCount * socketRegisterCount + 1]; // +1 для статуса

                        // Копируем данные группы
                        Array.Copy(groupRegisters, 0, allRegisters, 0, groupRegisterCount);

                        // Копируем данные розеток
                        for (int i = 0; i < group.SwitchCount; i++)
                        {
                            int socketBase = groupBase + firstNumber + (i * socketRegisterCount);
                            var socketRegisters = ReadHoldingRegisters((ushort)socketBase, socketRegisterCount);
                            Array.Copy(socketRegisters, 0, allRegisters, groupRegisterCount + i * socketRegisterCount, socketRegisterCount);
                        }

                        // Добавляем статус в конец массива
                        allRegisters[allRegisters.Length - 1] = status;

                        if (OnReadingsUpdated != null && !_disposed)
                        {
                            await OnReadingsUpdated.Invoke(group.Name, "all", new ModbusReadingData
                            {
                                GroupName = group.Name,
                                Registers = allRegisters,
                                Status = status,
                                UpdateType = "all"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error polling group {group.Name}: {ex.Message}");
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General error in PollReadingsAsync: {ex.Message}");
            }

            await Task.Delay(30, cancellationToken);
        }
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

    private async Task SafeInvokeReadingsUpdated(string groupName, string type, ModbusReadingData readings)
    {
        try
        {
            if (OnReadingsUpdated != null && !_disposed)
            {
                foreach (var handler in OnReadingsUpdated.GetInvocationList())
                {
                    try
                    {
                        await ((Func<string, string, ModbusReadingData, Task>)handler)(groupName, type, readings);
                    }
                    catch (ObjectDisposedException)
                    {
                        OnReadingsUpdated -= (Func<string, string, ModbusReadingData, Task>)handler;
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

public class ModbusReadingData
{
    public string GroupName { get; set; }
    public ushort[] Registers { get; set; }
    public ushort Status { get; set; }
    public string UpdateType { get; set; }
}
