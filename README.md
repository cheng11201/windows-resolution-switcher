# 屏幕分辨率切换 / Local Resolution Switcher

轻量、离线的 Windows 分辨率切换工具，使用 C# WinForms 和 Windows 原生显示 API。单个 EXE，无第三方运行依赖，使用 Windows 自带的 .NET Framework 4.x。

## 功能

- 读取当前真实分辨率、刷新率及色彩位数，选择活动显示器。
- 枚举驱动提供的模式，切换前使用 `ChangeDisplaySettingsEx(CDS_TEST)` 验证。
- 自动刷新率优先保留当前 Hz；目标分辨率不提供当前 Hz 时选择该模式可用的最高 Hz。也可以指定刷新率。
- 添加、编辑、删除预设，将任一预设设为日常桌面。
- 托盘切换菜单、关闭窗口留在托盘、可选开机启动。
- 安装或移除当前用户的桌面右键子菜单，不需要管理员权限。
- 切换后有 15 秒确认窗口；未确认则恢复原模式。独立恢复进程在超时或主进程退出后尝试恢复。
- 配置只保存在 `%LOCALAPPDATA%\LocalResolutionSwitcher`，不联网、不修改游戏文件。

## 下载与使用

从本仓库 Releases 下载 `ResolutionSwitcher.exe`，放到固定目录后双击运行。若移动 EXE，需重新点击“安装/更新桌面菜单”，已启用开机启动时也应重新设置启动选项，以更新路径。

1. 在主界面选择显示器和预设，点击“应用选中预设”。
2. 画面正常时点击“保留此设置”；否则不操作，等待自动恢复。
3. 点击“安装/更新桌面菜单”。Windows 11 使用“桌面右键 → 显示更多选项 → 屏幕分辨率切换”；Windows 10 使用经典桌面右键菜单。
4. 关闭窗口后默认留在托盘，通过托盘的“退出”或主界面的“退出软件”结束程序。

开机启动默认不启用，由用户勾选。当前实现动态应用显示模式，不将分辨率写入永久显示设置；系统重启、显示设备重连或游戏独占全屏可能改变当前模式。

## 默认预设

| 名称 | 分辨率 | 刷新率 |
| --- | --- | --- |
| 无畏契约 | 1280×960 | 自动 |
| 三角洲行动 | 1720×1080 | 自动 |
| 日常桌面 | 1920×1080 | 自动 |

这些是快捷预设，不代表显示器一定支持。工具不会创建自定义显卡时序或绕过驱动验证。驱动未提供的模式会明确报错；可先在显卡控制面板中创建并验证，再刷新本工具。

## 验证状态

2026-10-27，在 Windows 11 23H2、NVIDIA RTX 4070 Laptop GPU 的一台实机上完成以下测试：

| 项目 | 结果 |
| --- | --- |
| 读取当前模式和枚举显示模式 | 通过，枚举 461 条驱动模式记录（含重复记录） |
| 1280×960，320 Hz | Windows 测试通过，真实切换并读回匹配，随后恢复原模式 |
| 1920×1080，320 Hz | Windows 测试通过，真实切换并读回匹配，随后恢复原模式 |
| 1720×1080 | 驱动未提供，明确报告不可用 |
| 独立恢复进程超时回退 | 通过，恢复原模式且恢复进程退出 |
| 配置序列化与磁盘重新读取 | 通过 |
| 主窗口启动与控件读取 | 通过 |
| 桌面菜单注册 | 已写入当前用户注册表 |
| Explorer 中实际点击桌面子菜单触发切换 | 尚未完成人工/自动点击验收 |
| 托盘实际点击、预设编辑全流程、开机启动后登录 | 尚未完成端到端验收 |

这是初始测试版本，以上实机结果不是对所有显卡、多显示器拓扑、HDR 或独占全屏游戏的兼容保证。游戏启动和退出兼容性尚未专门测试。

## 恢复与移除

- “恢复日常桌面”应用你设定的日常预设，同样需要确认。
- “恢复上次原始模式”处理尚未完成的恢复记录。
- 若 UI 无法使用，可运行 `ResolutionSwitcher.exe --recover` 恢复待处理记录。
- 如显示器已断开或驱动拒绝恢复，请使用 Windows 显示设置恢复。
- 移除时先取消“开机启动”，点击“移除桌面菜单”，退出后删除程序目录即可。配置保留在上述本地目录，可按需自行清理。

仅写入本工具自己的桌面菜单注册表项，以及用户明确启用的开机启动项，不修改 NVIDIA 设置或 Windows 安全设置。

## 构建

在 Windows PowerShell 中，在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

产物为 `release\ResolutionSwitcher.exe`。构建脚本使用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，无需 .NET SDK 或 NuGet 包。目标为 Windows x64 和 .NET Framework 4.x；未测试 Windows ARM64。

## 诊断命令

```powershell
# 只读枚举、验证模式并生成报告，不切换分辨率
Start-Process .\release\ResolutionSwitcher.exe -ArgumentList '--probe "C:\Temp\probe.txt"' -Wait

# 实际切换当前配置的预设，再恢复；同时验证独立进程超时恢复
# 此命令会使画面短暂改变，测试时请保存正在进行的工作
Start-Process .\release\ResolutionSwitcher.exe -ArgumentList '--exercise "C:\Temp\exercise.txt"' -Wait
```

报告目录应事先存在。实际切换诊断必须在本地交互桌面上执行，不应在 CI 或远程会话中执行。GitHub Actions 仅构建，不运行会修改显示状态的测试。

## 项目结构

```text
src/ResolutionSwitcher.cs   应用、显示 API、预设、托盘、恢复进程
build.ps1                  本地构建
.github/workflows/         自动构建
LICENSE                    MIT 许可证
```

## 技术参考

- [ChangeDisplaySettingsExW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-changedisplaysettingsexw)
- [EnumDisplaySettingsW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsw)
- [当前用户级联菜单注册](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-create-cascading-menus-with-the-extendedsubcommandskey-registry-entry)

## 许可证

[MIT](LICENSE)。欢迎提交问题报告和改进建议。报告问题时不要上传私人配置、账号凭据或包含个人文件的完整桌面截图。
