# Wu.CommTool 项目结构整理

## 1. 总体结构
- `Wu.CommTool/`：WPF 主程序（壳程序、主题、导航入口）
- `Wu.CommTool.Core/`：公共能力（枚举、扩展、模型、通用工具）
- `Modules/`：功能模块（按协议/工具拆分）

项目采用多目标框架：`net6.0-windows`、`net7.0-windows`、`net48`。

## 2. Modules 组织方式（通用约定）
每个模块基本按以下分层：
- `Models/`：协议数据、业务模型、通信处理
- `ViewModels/`：页面状态与命令
- `Views/`：WPF 页面与交互布局
- `Enums/`：枚举定义
- `DialogViewModels/`、`DialogViews/`：弹窗相关
- `*Module.cs`：Prism 模块注册与导航配置
- `GlobalUsings.cs`：模块公共 using

## 3. ModbusTcp 模块结构
`Modules/Wu.CommTool.Modules.ModbusTcp/`
- `ModbusTcpModule.cs`：导航注册
- `Views/`
  - `ModbusTcpView.xaml`：模块总入口（左侧菜单 + 区域导航）
  - `ModbusTcpCustomFrameView.xaml`：自定义帧
  - `ModbusTcpMasterView.xaml`：主站
  - `ModbusTcpSlaveView.xaml`：从站
- `ViewModels/`
  - `ModbusTcpViewModel.cs`：菜单与区域跳转
  - `ModbusTcpCustomFrameViewModel.cs`
  - `ModbusTcpMasterViewModel.cs`
  - `ModbusTcpSlaveViewModel.cs`
- `Models/`
  - `MtcpFrame.cs`：Modbus TCP 帧解析
  - `MtcpMessageData.cs`：消息展示模型
  - `MtcpMaster.cs`：主站逻辑
  - `MtcpSlave.cs`：从站逻辑（监听、解析、应答）

## 4. ModbusTcpSlave 功能说明
从站功能在 `MtcpSlave` 中实现，包含：
- TCP 监听启动/停止
- 客户端连接处理
- Modbus TCP 报文读取（MBAP + PDU）
- 功能码应答：`0x01`、`0x03`、`0x04`、`0x05`、`0x06`、`0x0F`、`0x10`
- 异常应答（非法功能码/地址/数据值/设备故障）
- 保持寄存器、输入寄存器、线圈数据维护
- 收发日志输出（可在从站页面日志页查看）
