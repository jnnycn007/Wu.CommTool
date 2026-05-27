namespace Wu.CommTool.Modules.ModbusTcp.ViewModels;

public class ModbusTcpSlaveViewModel : NavigationViewModel
{
    public ModbusTcpSlaveViewModel()
    {
        ExecuteCommand = new RelayCommand<string>(Execute);
        SaveCommand = new RelayCommand(() => { });
        CancelCommand = new RelayCommand(() => { });
    }

    public ModbusTcpSlaveViewModel(IContainerProvider provider) : base(provider)
    {
        ExecuteCommand = new RelayCommand<string>(Execute);
        SaveCommand = new RelayCommand(() => { });
        CancelCommand = new RelayCommand(() => { });
    }

    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
    }

    public MtcpSlave MtcpSlave { get; } = new();

    private OpenDrawers drawerState = new();
    public OpenDrawers DrawerState
    {
        get => drawerState;
        set => SetProperty(ref drawerState, value);
    }

    public string DialogHostName { get; set; } = nameof(ModbusTcpSlaveView);

    public IRelayCommand SaveCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand<string> ExecuteCommand { get; }

    private void Execute(string obj)
    {
        if (obj == "OpenLeftDrawer")
        {
            DrawerState.LeftDrawer = true;
        }
    }
}
