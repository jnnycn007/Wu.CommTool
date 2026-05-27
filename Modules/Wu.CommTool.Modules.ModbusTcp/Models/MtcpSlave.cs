namespace Wu.CommTool.Modules.ModbusTcp.Models;

/// <summary>
/// Modbus TCP 从站
/// </summary>
public partial class MtcpSlave : ObservableObject, IDisposable
{
    private static readonly ILog log = LogManager.GetLogger(typeof(MtcpSlave));
    private readonly object syncRoot = new();
    private readonly List<TcpClient> clients = [];

    private TcpListener listener;
    private CancellationTokenSource cts;

    public MtcpSlave()
    {
        RunCommand = new AsyncRelayCommand(Run);
        StopCommand = new RelayCommand(Stop);
        MessageClearCommand = new RelayCommand(MessageClear);
        ApplyHoldingRangeCommand = new RelayCommand(ApplyHoldingRange);
        ImportConfigCommand = new RelayCommand(ImportConfig);
        ExportConfigCommand = new RelayCommand(ExportConfig);
        SaveConfigCommand = new RelayCommand(SaveConfig);
        RefreshQuickImportListCommand = new RelayCommand(RefreshQuickImportList);
        QuickImportConfigCommand = new RelayCommand(QuickImportConfig);

        BuildHoldingRegisters();
        BuildInputRegisters();

        for (ushort i = 0; i < 300; i++)
        {
            Coils.Add(new MtcpCoilItem(i));
        }

        GetDefaultConfig();
        RefreshQuickImportList();
    }

    private string serverIp = "127.0.0.1";
    public string ServerIp
    {
        get => serverIp;
        set => SetProperty(ref serverIp, value);
    }

    private int serverPort = 502;
    public int ServerPort
    {
        get => serverPort;
        set => SetProperty(ref serverPort, value);
    }

    private byte slaveId = 1;
    public byte SlaveId
    {
        get => slaveId;
        set => SetProperty(ref slaveId, value);
    }

    private bool isRunning;
    public bool IsRunning
    {
        get => isRunning;
        set => SetProperty(ref isRunning, value);
    }

    private ushort holdingStartAddress = 0;
    public ushort HoldingStartAddress
    {
        get => holdingStartAddress;
        set => SetProperty(ref holdingStartAddress, value);
    }

    private ushort holdingAddressCount = 300;
    public ushort HoldingAddressCount
    {
        get => holdingAddressCount;
        set => SetProperty(ref holdingAddressCount, value);
    }

    private ObservableCollection<MtcpRegisterItem> holdingRegisters = [];
    public ObservableCollection<MtcpRegisterItem> HoldingRegisters
    {
        get => holdingRegisters;
        set => SetProperty(ref holdingRegisters, value);
    }

    private ObservableCollection<MtcpRegisterItem> inputRegisters = [];
    public ObservableCollection<MtcpRegisterItem> InputRegisters
    {
        get => inputRegisters;
        set => SetProperty(ref inputRegisters, value);
    }

    private ObservableCollection<MtcpCoilItem> coils = [];
    public ObservableCollection<MtcpCoilItem> Coils
    {
        get => coils;
        set => SetProperty(ref coils, value);
    }

    private ObservableCollection<MessageData> messages = [];
    public ObservableCollection<MessageData> Messages
    {
        get => messages;
        set => SetProperty(ref messages, value);
    }

    public IAsyncRelayCommand RunCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand MessageClearCommand { get; }
    public IRelayCommand ApplyHoldingRangeCommand { get; }
    public IRelayCommand ImportConfigCommand { get; }
    public IRelayCommand ExportConfigCommand { get; }
    public IRelayCommand SaveConfigCommand { get; }
    public IRelayCommand RefreshQuickImportListCommand { get; }
    public IRelayCommand QuickImportConfigCommand { get; }

    private readonly string configDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Configs\ModbusTcpSlaveConfig");
    private readonly string configExtension = "jsonMTS";

    private string currentConfigFullName = string.Empty;
    public string CurrentConfigFullName
    {
        get => currentConfigFullName;
        set
        {
            if (SetProperty(ref currentConfigFullName, value))
            {
                OnPropertyChanged(nameof(CurrentConfigName));
            }
        }
    }

    public string CurrentConfigName => Path.GetFileNameWithoutExtension(CurrentConfigFullName);

    private ObservableCollection<ConfigFile> configFiles = [];
    public ObservableCollection<ConfigFile> ConfigFiles
    {
        get => configFiles;
        set => SetProperty(ref configFiles, value);
    }

    private ConfigFile selectedConfigFile;
    public ConfigFile SelectedConfigFile
    {
        get => selectedConfigFile;
        set => SetProperty(ref selectedConfigFile, value);
    }

    private void BuildHoldingRegisters()
    {
        HoldingRegisters.Clear();
        for (int i = 0; i < HoldingAddressCount; i++)
        {
            HoldingRegisters.Add(new MtcpRegisterItem((ushort)(HoldingStartAddress + i), ReadHoldingRegisterByAddress, WriteHoldingRegisterByAddress));
        }
    }

    private ushort ReadHoldingRegisterByAddress(ushort address)
    {
        if (TryGetHoldingIndex(address, out var index))
        {
            return HoldingRegisters[index].Value;
        }
        return 0;
    }

    private void WriteHoldingRegisterByAddress(ushort address, ushort value)
    {
        if (!TryGetHoldingIndex(address, out var index))
        {
            return;
        }

        HoldingRegisters[index].Value = value;
        for (int i = 0; i <= 3; i++)
        {
            if (address >= HoldingStartAddress + i && TryGetHoldingIndex((ushort)(address - i), out var notifyIndex))
            {
                HoldingRegisters[notifyIndex].NotifyExtendedValueChanged();
            }
        }
    }

    private void BuildInputRegisters()
    {
        InputRegisters.Clear();
        for (int i = 0; i < HoldingAddressCount; i++)
        {
            InputRegisters.Add(new MtcpRegisterItem((ushort)(HoldingStartAddress + i), ReadInputRegisterByAddress, WriteInputRegisterByAddress));
        }
    }

    private ushort ReadInputRegisterByAddress(ushort address)
    {
        if (TryGetInputIndex(address, out var index))
        {
            return InputRegisters[index].Value;
        }
        return 0;
    }

    private void WriteInputRegisterByAddress(ushort address, ushort value)
    {
        if (!TryGetInputIndex(address, out var index))
        {
            return;
        }

        InputRegisters[index].Value = value;
        for (int i = 0; i <= 3; i++)
        {
            if (address >= HoldingStartAddress + i && TryGetInputIndex((ushort)(address - i), out var notifyIndex))
            {
                InputRegisters[notifyIndex].NotifyExtendedValueChanged();
            }
        }
    }

    private void ApplyHoldingRange()
    {
        if (IsRunning)
        {
            ShowErrorMessage("从站运行中，无法修改保持寄存器地址范围");
            return;
        }

        if (HoldingAddressCount == 0)
        {
            ShowErrorMessage("保持寄存器数量必须大于0");
            return;
        }

        if (HoldingStartAddress + HoldingAddressCount > ushort.MaxValue + 1)
        {
            ShowErrorMessage("保持寄存器地址范围超出 0x0000~0xFFFF");
            return;
        }

        BuildHoldingRegisters();
        BuildInputRegisters();
        ShowMessage($"寄存器范围已更新: {HoldingStartAddress} ~ {HoldingStartAddress + HoldingAddressCount - 1}");
    }

    private MtcpSlaveConfig CreateConfigSnapshot()
    {
        return new MtcpSlaveConfig
        {
            ServerIp = ServerIp,
            ServerPort = ServerPort,
            SlaveId = SlaveId,
            HoldingStartAddress = HoldingStartAddress,
            HoldingAddressCount = HoldingAddressCount,
            HoldingRegisters = HoldingRegisters.Select(x => new MtcpRegisterConfigItem
            {
                Address = x.Address,
                Value = x.Value,
                Description = x.Description
            }).ToList(),
            InputRegisters = InputRegisters.Select(x => new MtcpRegisterConfigItem
            {
                Address = x.Address,
                Value = x.Value,
                Description = x.Description
            }).ToList(),
            Coils = Coils.Select(x => new MtcpCoilConfigItem
            {
                Address = x.Address,
                Value = x.Value,
                Description = x.Description
            }).ToList()
        };
    }

    private void ApplyConfig(MtcpSlaveConfig config)
    {
        if (config == null)
        {
            return;
        }

        ServerIp = config.ServerIp;
        ServerPort = config.ServerPort;
        SlaveId = config.SlaveId;
        HoldingStartAddress = config.HoldingStartAddress;
        HoldingAddressCount = config.HoldingAddressCount == 0 ? (ushort)300 : config.HoldingAddressCount;

        BuildHoldingRegisters();
        BuildInputRegisters();

        var holdingMap = config.HoldingRegisters?.ToDictionary(x => x.Address) ?? [];
        foreach (var item in HoldingRegisters)
        {
            if (holdingMap.TryGetValue(item.Address, out var source))
            {
                item.Value = source.Value;
                item.Description = source.Description ?? string.Empty;
            }
        }

        var inputMap = config.InputRegisters?.ToDictionary(x => x.Address) ?? [];
        foreach (var item in InputRegisters)
        {
            if (inputMap.TryGetValue(item.Address, out var source))
            {
                item.Value = source.Value;
                item.Description = source.Description ?? string.Empty;
            }
        }

        if (config.Coils != null)
        {
            Coils.Clear();
            foreach (var coil in config.Coils)
            {
                Coils.Add(new MtcpCoilItem(coil.Address)
                {
                    Value = coil.Value,
                    Description = coil.Description ?? string.Empty
                });
            }
        }
    }

    private void GetDefaultConfig()
    {
        try
        {
            Wu.Utils.IoUtil.Exists(configDirectory);
            var filePath = Path.Combine(configDirectory, $"Default.{configExtension}");
            CurrentConfigFullName = filePath;
            if (File.Exists(filePath))
            {
                var json = Core.Common.Utils.ReadJsonFile(filePath);
                var config = JsonConvert.DeserializeObject<MtcpSlaveConfig>(json);
                if (config != null)
                {
                    ApplyConfig(config);
                    ShowMessage("读取默认配置成功");
                }
            }
            else
            {
                SaveConfigToFile(filePath);
            }
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"读取默认配置失败: {ex.Message}");
        }
    }

    private void SaveConfigToFile(string fileName)
    {
        var content = JsonConvert.SerializeObject(CreateConfigSnapshot());
        Core.Common.Utils.WriteJsonFile(fileName, content);
    }

    private void SaveConfig()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(CurrentConfigFullName))
            {
                CurrentConfigFullName = Path.Combine(configDirectory, $"Default.{configExtension}");
            }

            SaveConfigToFile(CurrentConfigFullName);
            HcGrowlExtensions.Success($"保存配置: {CurrentConfigName}");
            RefreshQuickImportList();
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"保存配置失败: {ex.Message}");
        }
    }

    private void ExportConfig()
    {
        try
        {
            Wu.Utils.IoUtil.Exists(configDirectory);
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "请选择导出配置文件...",
                Filter = $"json files(*.{configExtension})|*.{configExtension}",
                FilterIndex = 1,
                FileName = "Default",
                DefaultExt = configExtension,
                InitialDirectory = configDirectory,
                OverwritePrompt = true,
                AddExtension = true,
            };

            if (sfd.ShowDialog() != true)
                return;

            CurrentConfigFullName = sfd.FileName;
            SaveConfigToFile(sfd.FileName);
            HcGrowlExtensions.Success($"导出配置: {CurrentConfigName}");
            RefreshQuickImportList();
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"导出配置失败: {ex.Message}");
        }
    }

    private void ImportConfig()
    {
        try
        {
            Wu.Utils.IoUtil.Exists(configDirectory);
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "请选择导入配置文件...",
                Filter = $"json files(*.{configExtension})|*.{configExtension}",
                FilterIndex = 1,
                InitialDirectory = configDirectory
            };

            if (dlg.ShowDialog() != true)
                return;

            CurrentConfigFullName = dlg.FileName;
            var json = Core.Common.Utils.ReadJsonFile(dlg.FileName);
            var config = JsonConvert.DeserializeObject<MtcpSlaveConfig>(json);
            if (config == null)
            {
                ShowErrorMessage("读取配置文件失败");
                return;
            }

            ApplyConfig(config);
            HcGrowlExtensions.Success($"导入配置: {CurrentConfigName}");
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"导入配置失败: {ex.Message}");
        }
    }

    private void QuickImportConfig()
    {
        try
        {
            if (SelectedConfigFile == null)
            {
                return;
            }

            CurrentConfigFullName = SelectedConfigFile.FullName;
            var json = Core.Common.Utils.ReadJsonFile(SelectedConfigFile.FullName);
            var config = JsonConvert.DeserializeObject<MtcpSlaveConfig>(json);
            if (config == null)
            {
                ShowErrorMessage("读取配置文件失败");
                return;
            }

            ApplyConfig(config);
            HcGrowlExtensions.Success($"导入配置: {CurrentConfigName}");
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"导入配置失败: {ex.Message}");
        }
    }

    private void RefreshQuickImportList()
    {
        try
        {
            Wu.Utils.IoUtil.Exists(configDirectory);
            DirectoryInfo folder = new(configDirectory);
            var files = folder.GetFiles().Where(x => x.Extension.Equals($".{configExtension}", StringComparison.OrdinalIgnoreCase))
                .Select(item => new ConfigFile(item));

            ConfigFiles.Clear();
            foreach (var item in files)
            {
                ConfigFiles.Add(item);
            }
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"读取配置目录失败: {ex.Message}");
        }
    }

    private bool TryGetHoldingIndex(ushort address, out int index)
    {
        index = address - HoldingStartAddress;
        return address >= HoldingStartAddress && address < HoldingStartAddress + HoldingAddressCount;
    }

    private bool TryGetInputIndex(ushort address, out int index)
    {
        index = address - HoldingStartAddress;
        return address >= HoldingStartAddress && address < HoldingStartAddress + HoldingAddressCount;
    }

    private async Task Run()
    {
        if (IsRunning)
        {
            ShowMessage("从站已启动");
            return;
        }

        try
        {
            if (!System.Net.IPAddress.TryParse(ServerIp, out var ipAddress))
            {
                ShowErrorMessage("IP地址格式错误");
                return;
            }

            cts = new CancellationTokenSource();
            listener = new TcpListener(ipAddress, ServerPort);
            listener.Start();
            IsRunning = true;
            ShowMessage($"ModbusTCP从站已启动: {ServerIp}:{ServerPort} SlaveId:{SlaveId}");
            _ = Task.Run(() => AcceptLoopAsync(cts.Token));
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            IsRunning = false;
            ShowErrorMessage($"启动失败: {ex.Message}");
        }
    }

    private void Stop()
    {
        if (!IsRunning)
            return;

        try
        {
            cts?.Cancel();
            listener?.Stop();

            lock (syncRoot)
            {
                foreach (var client in clients.ToArray())
                {
                    try { client.Close(); } catch { }
                }
                clients.Clear();
            }

            IsRunning = false;
            ShowMessage("ModbusTCP从站已停止");
        }
        catch (Exception ex)
        {
            ShowErrorMessage(ex.Message);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync();
                lock (syncRoot)
                {
                    clients.Add(client);
                }

                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                ShowErrorMessage($"监听异常: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            ShowMessage($"客户端连接: {client.Client.RemoteEndPoint}");
            var stream = client.GetStream();

            while (!token.IsCancellationRequested && client.Connected)
            {
                var request = await ReadFrameAsync(stream, token);
                if (request == null)
                {
                    break;
                }

                if (request.Length < 8)
                {
                    continue;
                }

                var requestFrame = new MtcpFrame(request);
                ShowReceiveMessage(requestFrame);

                var response = ProcessRequest(request);
                if (response == null)
                {
                    continue;
                }

                await stream.WriteAsync(response, 0, response.Length, token);
                ShowSendMessage(new MtcpFrame(response));
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                ShowErrorMessage($"客户端处理异常: {ex.Message}");
            }
        }
        finally
        {
            try { client.Close(); } catch { }
            lock (syncRoot)
            {
                clients.Remove(client);
            }
            ShowMessage("客户端断开");
        }
    }

    private async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken token)
    {
        var header = new byte[7];
        var ok = await ReadExactAsync(stream, header, 0, header.Length, token);
        if (!ok)
        {
            return null;
        }

        ushort length = (ushort)((header[4] << 8) | header[5]);
        if (length == 0)
        {
            return null;
        }

        int pduLength = length - 1;
        if (pduLength < 0)
        {
            return null;
        }

        var pdu = new byte[pduLength];
        if (pduLength > 0)
        {
            ok = await ReadExactAsync(stream, pdu, 0, pduLength, token);
            if (!ok)
            {
                return null;
            }
        }

        var frame = new byte[7 + pduLength];
        Array.Copy(header, frame, 7);
        if (pduLength > 0)
        {
            Array.Copy(pdu, 0, frame, 7, pduLength);
        }
        return frame;
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken token)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, token);
            if (read <= 0)
            {
                return false;
            }
            totalRead += read;
        }

        return true;
    }

    private byte[] ProcessRequest(byte[] request)
    {
        ushort transactionId = (ushort)((request[0] << 8) | request[1]);
        ushort protocolId = (ushort)((request[2] << 8) | request[3]);
        byte unitId = request[6];

        var pdu = request.Skip(7).ToArray();
        if (pdu.Length == 0)
        {
            return null;
        }

        byte functionCode = pdu[0];

        if (protocolId != 0x0000)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, functionCode, 0x01);
        }

        if (SlaveId != 0 && unitId != SlaveId)
        {
            return null;
        }

        try
        {
            return functionCode switch
            {
                0x01 => HandleReadCoils(transactionId, protocolId, unitId, pdu),
                0x03 => HandleReadHoldingRegisters(transactionId, protocolId, unitId, pdu),
                0x04 => HandleReadInputRegisters(transactionId, protocolId, unitId, pdu),
                0x05 => HandleWriteSingleCoil(transactionId, protocolId, unitId, pdu),
                0x06 => HandleWriteSingleRegister(transactionId, protocolId, unitId, pdu),
                0x0F => HandleWriteMultipleCoils(transactionId, protocolId, unitId, pdu),
                0x10 => HandleWriteMultipleRegisters(transactionId, protocolId, unitId, pdu),
                _ => BuildExceptionResponse(transactionId, protocolId, unitId, functionCode, 0x01)
            };
        }
        catch
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, functionCode, 0x04);
        }
    }

    private byte[] HandleReadCoils(ushort transactionId, ushort protocolId, byte unitId, byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        ushort start = (ushort)((pdu[1] << 8) | pdu[2]);
        ushort quantity = (ushort)((pdu[3] << 8) | pdu[4]);
        if (quantity < 1 || quantity > 2000)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        int byteCount = (quantity + 7) / 8;
        byte[] responsePdu = new byte[2 + byteCount];
        responsePdu[0] = pdu[0];
        responsePdu[1] = (byte)byteCount;

        lock (syncRoot)
        {
            for (int i = 0; i < quantity; i++)
            {
                int address = start + i;
                bool value = address < Coils.Count && Coils[address].Value;
                if (value)
                {
                    int dataIndex = 2 + (i / 8);
                    int bitIndex = i % 8;
                    responsePdu[dataIndex] |= (byte)(1 << bitIndex);
                }
            }
        }

        return BuildResponse(transactionId, protocolId, unitId, responsePdu);
    }

    private byte[] HandleReadHoldingRegisters(ushort transactionId, ushort protocolId, byte unitId, byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        ushort start = (ushort)((pdu[1] << 8) | pdu[2]);
        ushort quantity = (ushort)((pdu[3] << 8) | pdu[4]);
        if (quantity < 1 || quantity > 125)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        if (quantity == 0)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        int endAddressInt = start + quantity - 1;
        if (endAddressInt > ushort.MaxValue)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x02);
        }

        ushort endAddress = (ushort)endAddressInt;
        if (!TryGetHoldingIndex(start, out var startIndex) || !TryGetHoldingIndex(endAddress, out _))
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x02);
        }

        byte[] responsePdu = new byte[2 + quantity * 2];
        responsePdu[0] = pdu[0];
        responsePdu[1] = (byte)(quantity * 2);

        lock (syncRoot)
        {
            for (int i = 0; i < quantity; i++)
            {
                ushort value = ReadHoldingRegisterByAddress((ushort)(start + i));
                responsePdu[2 + i * 2] = (byte)(value >> 8);
                responsePdu[3 + i * 2] = (byte)(value & 0xFF);
            }
        }

        return BuildResponse(transactionId, protocolId, unitId, responsePdu);
    }

    private byte[] HandleReadInputRegisters(ushort transactionId, ushort protocolId, byte unitId, byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        ushort start = (ushort)((pdu[1] << 8) | pdu[2]);
        ushort quantity = (ushort)((pdu[3] << 8) | pdu[4]);
        if (quantity < 1 || quantity > 125)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        int endAddressInt = start + quantity - 1;
        if (endAddressInt > ushort.MaxValue)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x02);
        }

        ushort endAddress = (ushort)endAddressInt;
        if (!TryGetInputIndex(start, out var startIndex) || !TryGetInputIndex(endAddress, out _))
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x02);
        }

        byte[] responsePdu = new byte[2 + quantity * 2];
        responsePdu[0] = pdu[0];
        responsePdu[1] = (byte)(quantity * 2);

        lock (syncRoot)
        {
            for (int i = 0; i < quantity; i++)
            {
                ushort value = ReadInputRegisterByAddress((ushort)(start + i));
                responsePdu[2 + i * 2] = (byte)(value >> 8);
                responsePdu[3 + i * 2] = (byte)(value & 0xFF);
            }
        }

        return BuildResponse(transactionId, protocolId, unitId, responsePdu);
    }

    private byte[] HandleWriteSingleCoil(ushort transactionId, ushort protocolId, byte unitId, byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        ushort address = (ushort)((pdu[1] << 8) | pdu[2]);
        ushort value = (ushort)((pdu[3] << 8) | pdu[4]);

        if (address >= Coils.Count)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x02);
        }

        if (value != 0xFF00 && value != 0x0000)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        lock (syncRoot)
        {
            Coils[address].Value = value == 0xFF00;
        }

        return BuildResponse(transactionId, protocolId, unitId, pdu.Take(5).ToArray());
    }

    private byte[] HandleWriteSingleRegister(ushort transactionId, ushort protocolId, byte unitId, byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        ushort address = (ushort)((pdu[1] << 8) | pdu[2]);
        ushort value = (ushort)((pdu[3] << 8) | pdu[4]);

        if (!TryGetHoldingIndex(address, out var index))
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x02);
        }

        lock (syncRoot)
        {
            WriteHoldingRegisterByAddress(address, value);
        }

        return BuildResponse(transactionId, protocolId, unitId, pdu.Take(5).ToArray());
    }

    private byte[] HandleWriteMultipleCoils(ushort transactionId, ushort protocolId, byte unitId, byte[] pdu)
    {
        if (pdu.Length < 6)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        ushort start = (ushort)((pdu[1] << 8) | pdu[2]);
        ushort quantity = (ushort)((pdu[3] << 8) | pdu[4]);
        byte byteCount = pdu[5];

        if (quantity < 1 || quantity > 1968)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        if (start + quantity > Coils.Count)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x02);
        }

        if (pdu.Length < 6 + byteCount)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        lock (syncRoot)
        {
            for (int i = 0; i < quantity; i++)
            {
                int dataIndex = 6 + i / 8;
                int bitIndex = i % 8;
                bool value = (pdu[dataIndex] & (1 << bitIndex)) != 0;
                Coils[start + i].Value = value;
            }
        }

        byte[] responsePdu = [pdu[0], pdu[1], pdu[2], pdu[3], pdu[4]];
        return BuildResponse(transactionId, protocolId, unitId, responsePdu);
    }

    private byte[] HandleWriteMultipleRegisters(ushort transactionId, ushort protocolId, byte unitId, byte[] pdu)
    {
        if (pdu.Length < 6)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        ushort start = (ushort)((pdu[1] << 8) | pdu[2]);
        ushort quantity = (ushort)((pdu[3] << 8) | pdu[4]);
        byte byteCount = pdu[5];

        if (quantity < 1 || quantity > 123)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        if (byteCount != quantity * 2 || pdu.Length < 6 + byteCount)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        if (quantity == 0)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x03);
        }

        int endAddressInt = start + quantity - 1;
        if (endAddressInt > ushort.MaxValue)
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x02);
        }

        ushort endAddress = (ushort)endAddressInt;
        if (!TryGetHoldingIndex(start, out var startIndex) || !TryGetHoldingIndex(endAddress, out _))
        {
            return BuildExceptionResponse(transactionId, protocolId, unitId, pdu[0], 0x02);
        }

        lock (syncRoot)
        {
            for (int i = 0; i < quantity; i++)
            {
                ushort value = (ushort)((pdu[6 + i * 2] << 8) | pdu[7 + i * 2]);
                WriteHoldingRegisterByAddress((ushort)(start + i), value);
            }
        }

        byte[] responsePdu = [pdu[0], pdu[1], pdu[2], pdu[3], pdu[4]];
        return BuildResponse(transactionId, protocolId, unitId, responsePdu);
    }

    private static byte[] BuildResponse(ushort transactionId, ushort protocolId, byte unitId, byte[] pdu)
    {
        ushort length = (ushort)(1 + pdu.Length);
        byte[] frame = new byte[7 + pdu.Length];
        frame[0] = (byte)(transactionId >> 8);
        frame[1] = (byte)(transactionId & 0xFF);
        frame[2] = (byte)(protocolId >> 8);
        frame[3] = (byte)(protocolId & 0xFF);
        frame[4] = (byte)(length >> 8);
        frame[5] = (byte)(length & 0xFF);
        frame[6] = unitId;
        Array.Copy(pdu, 0, frame, 7, pdu.Length);
        return frame;
    }

    private static byte[] BuildExceptionResponse(ushort transactionId, ushort protocolId, byte unitId, byte functionCode, byte exceptionCode)
    {
        byte[] pdu = [(byte)(functionCode | 0x80), exceptionCode];
        return BuildResponse(transactionId, protocolId, unitId, pdu);
    }

    public void ShowMessage(string message, MessageType type = MessageType.Info)
    {
        try
        {
            void action()
            {
                Messages.Add(new MessageData(message, DateTime.Now, type));
                log.Info(message);
                while (Messages.Count > 300)
                {
                    Messages.RemoveAt(0);
                }
            }

            Wu.Wpf.Utils.ExecuteFunBeginInvoke(action);
        }
        catch
        {
        }
    }

    public void ShowErrorMessage(string message) => ShowMessage(message, MessageType.Error);

    public void ShowReceiveMessage(MtcpFrame frame)
    {
        try
        {
            void action()
            {
                Messages.Add(new MtcpMessageData("", DateTime.Now, MessageType.Receive, frame));
                log.Info($"接收:{frame}");
                while (Messages.Count > 300)
                {
                    Messages.RemoveAt(0);
                }
            }

            Wu.Wpf.Utils.ExecuteFunBeginInvoke(action);
        }
        catch
        {
        }
    }

    public void ShowSendMessage(MtcpFrame frame)
    {
        try
        {
            void action()
            {
                Messages.Add(new MtcpMessageData("", DateTime.Now, MessageType.Send, frame));
                log.Info($"发送:{frame}");
                while (Messages.Count > 300)
                {
                    Messages.RemoveAt(0);
                }
            }

            Wu.Wpf.Utils.ExecuteFunBeginInvoke(action);
        }
        catch
        {
        }
    }

    private void MessageClear()
    {
        Messages.Clear();
    }

    public void Dispose()
    {
        Stop();
    }
}

public class MtcpSlaveConfig
{
    public string ServerIp { get; set; } = "127.0.0.1";
    public int ServerPort { get; set; } = 502;
    public byte SlaveId { get; set; } = 1;
    public ushort HoldingStartAddress { get; set; } = 0;
    public ushort HoldingAddressCount { get; set; } = 300;
    public List<MtcpRegisterConfigItem> HoldingRegisters { get; set; } = [];
    public List<MtcpRegisterConfigItem> InputRegisters { get; set; } = [];
    public List<MtcpCoilConfigItem> Coils { get; set; } = [];
}

public class MtcpRegisterConfigItem
{
    public ushort Address { get; set; }
    public ushort Value { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class MtcpCoilConfigItem
{
    public ushort Address { get; set; }
    public bool Value { get; set; }
    public string Description { get; set; } = string.Empty;
}

public partial class MtcpRegisterItem : ObservableObject
{
    private readonly Func<ushort, ushort> readRegisterCallback;
    private readonly Action<ushort, ushort> writeRegisterCallback;

    public MtcpRegisterItem(ushort address)
    {
        Address = address;
    }

    public MtcpRegisterItem(ushort address, Func<ushort, ushort> readRegisterCallback, Action<ushort, ushort> writeRegisterCallback) : this(address)
    {
        this.readRegisterCallback = readRegisterCallback;
        this.writeRegisterCallback = writeRegisterCallback;
    }

    private ushort address;
    public ushort Address
    {
        get => address;
        set => SetProperty(ref address, value);
    }

    private ushort value;
    public ushort Value
    {
        get => this.value;
        set => SetProperty(ref this.value, value);
    }

    public short Int16Value
    {
        get => unchecked((short)Value);
        set
        {
            WriteWords([unchecked((ushort)value)]);
            OnPropertyChanged(nameof(Int16Value));
        }
    }

    public ushort UInt16Value
    {
        get => Value;
        set
        {
            WriteWords([value]);
            OnPropertyChanged(nameof(UInt16Value));
        }
    }

    public string HexValue
    {
        get => $"0x{Value:X4}";
        set
        {
            var text = (value ?? string.Empty).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text[2..];
            }

            if (ushort.TryParse(text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var hex))
            {
                WriteWords([hex]);
                OnPropertyChanged(nameof(HexValue));
            }
        }
    }

    public float FloatValue
    {
        get
        {
            if (readRegisterCallback == null)
            {
                return Value;
            }

            ushort highWord = readRegisterCallback(Address);
            ushort lowWord = Address < ushort.MaxValue ? readRegisterCallback((ushort)(Address + 1)) : (ushort)0;

            byte[] bytes =
            [
                (byte)(highWord >> 8),
                (byte)(highWord & 0xFF),
                (byte)(lowWord >> 8),
                (byte)(lowWord & 0xFF)
            ];

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToSingle(bytes, 0);
        }
        set
        {
            if (writeRegisterCallback == null)
            {
                Value = (ushort)value;
                return;
            }

            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            ushort highWord = (ushort)((bytes[0] << 8) | bytes[1]);
            ushort lowWord = (ushort)((bytes[2] << 8) | bytes[3]);

            writeRegisterCallback(Address, highWord);
            if (Address < ushort.MaxValue)
            {
                writeRegisterCallback((ushort)(Address + 1), lowWord);
            }

            OnPropertyChanged(nameof(FloatValue));
        }
    }

    public int IntValue
    {
        get
        {
            if (!TryReadWords(2, out var words))
            {
                return Value;
            }

            byte[] bytes =
            [
                (byte)(words[0] >> 8),
                (byte)(words[0] & 0xFF),
                (byte)(words[1] >> 8),
                (byte)(words[1] & 0xFF)
            ];

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToInt32(bytes, 0);
        }
        set
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            WriteWords(
            [
                (ushort)((bytes[0] << 8) | bytes[1]),
                (ushort)((bytes[2] << 8) | bytes[3])
            ]);

            OnPropertyChanged(nameof(IntValue));
        }
    }

    public int Int32Value
    {
        get => IntValue;
        set
        {
            IntValue = value;
            OnPropertyChanged(nameof(Int32Value));
        }
    }

    public uint UInt32Value
    {
        get
        {
            if (!TryReadWords(2, out var words))
            {
                return Value;
            }

            byte[] bytes =
            [
                (byte)(words[0] >> 8),
                (byte)(words[0] & 0xFF),
                (byte)(words[1] >> 8),
                (byte)(words[1] & 0xFF)
            ];

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToUInt32(bytes, 0);
        }
        set
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            WriteWords(
            [
                (ushort)((bytes[0] << 8) | bytes[1]),
                (ushort)((bytes[2] << 8) | bytes[3])
            ]);

            OnPropertyChanged(nameof(UInt32Value));
        }
    }

    public long LongValue
    {
        get
        {
            if (!TryReadWords(4, out var words))
            {
                return Value;
            }

            byte[] bytes =
            [
                (byte)(words[0] >> 8), (byte)(words[0] & 0xFF),
                (byte)(words[1] >> 8), (byte)(words[1] & 0xFF),
                (byte)(words[2] >> 8), (byte)(words[2] & 0xFF),
                (byte)(words[3] >> 8), (byte)(words[3] & 0xFF)
            ];

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToInt64(bytes, 0);
        }
        set
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            WriteWords(
            [
                (ushort)((bytes[0] << 8) | bytes[1]),
                (ushort)((bytes[2] << 8) | bytes[3]),
                (ushort)((bytes[4] << 8) | bytes[5]),
                (ushort)((bytes[6] << 8) | bytes[7])
            ]);

            OnPropertyChanged(nameof(LongValue));
        }
    }

    public double DoubleValue
    {
        get
        {
            if (!TryReadWords(4, out var words))
            {
                return Value;
            }

            byte[] bytes =
            [
                (byte)(words[0] >> 8), (byte)(words[0] & 0xFF),
                (byte)(words[1] >> 8), (byte)(words[1] & 0xFF),
                (byte)(words[2] >> 8), (byte)(words[2] & 0xFF),
                (byte)(words[3] >> 8), (byte)(words[3] & 0xFF)
            ];

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToDouble(bytes, 0);
        }
        set
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            WriteWords(
            [
                (ushort)((bytes[0] << 8) | bytes[1]),
                (ushort)((bytes[2] << 8) | bytes[3]),
                (ushort)((bytes[4] << 8) | bytes[5]),
                (ushort)((bytes[6] << 8) | bytes[7])
            ]);

            OnPropertyChanged(nameof(DoubleValue));
        }
    }

    private string description = string.Empty;
    public string Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }

    private bool TryReadWords(int wordCount, out ushort[] words)
    {
        words = new ushort[wordCount];
        if (readRegisterCallback == null)
        {
            return false;
        }

        for (int i = 0; i < wordCount; i++)
        {
            if (Address + i > ushort.MaxValue)
            {
                return false;
            }

            words[i] = readRegisterCallback((ushort)(Address + i));
        }

        return true;
    }

    private void WriteWords(ushort[] words)
    {
        if (writeRegisterCallback == null)
        {
            if (words.Length > 0)
            {
                Value = words[0];
            }
            return;
        }

        for (int i = 0; i < words.Length; i++)
        {
            if (Address + i > ushort.MaxValue)
            {
                return;
            }
            writeRegisterCallback((ushort)(Address + i), words[i]);
        }
    }

    public void NotifyExtendedValueChanged()
    {
        OnPropertyChanged(nameof(Int16Value));
        OnPropertyChanged(nameof(UInt16Value));
        OnPropertyChanged(nameof(HexValue));
        OnPropertyChanged(nameof(FloatValue));
        OnPropertyChanged(nameof(IntValue));
        OnPropertyChanged(nameof(Int32Value));
        OnPropertyChanged(nameof(UInt32Value));
        OnPropertyChanged(nameof(LongValue));
        OnPropertyChanged(nameof(DoubleValue));
    }
}

public partial class MtcpCoilItem : ObservableObject
{
    public MtcpCoilItem(ushort address)
    {
        Address = address;
    }

    private ushort address;
    public ushort Address
    {
        get => address;
        set => SetProperty(ref address, value);
    }

    private bool value;
    public bool Value
    {
        get => this.value;
        set => SetProperty(ref this.value, value);
    }

    private string description = string.Empty;
    public string Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }
}
