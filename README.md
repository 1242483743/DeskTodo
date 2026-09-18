# DeskTodo
<img width="20%" alt="待办软件图标-透明" src="https://github.com/user-attachments/assets/60f977e8-1536-49a2-8a7e-ebc04b20e2f7" />


一款简洁的 Windows 桌面待办软件。当前版本：**v1.22**。

源码与安装包均在本仓库发布，源码采用 [MIT 许可证](LICENSE)。

## 下载

打开 [Releases 页面](https://github.com/1242483743/DeskTodo/releases)，在版本下方的 **Assets** 中下载：

`DeskTodo-Setup-v1.22.exe`

直接使用软件请下载安装包。GitHub 自动生成的 `Source code (zip)` / `Source code (tar.gz)` 包含该标签对应提交的源码，不是安装包；较早的安装包发布标签可能只有介绍文件，获取当前源码请使用本仓库的 `main` 分支。

## 主要功能

- 桌面待办卡片，支持嵌入桌面、拖动、调整大小、锁定位置和大小。
- 勾选完成事项，双击编辑未完成事项；支持删除、清空已完成及撤销。
- 每条事项可独立设置间隔提醒和每日定时提醒。
- 十二套主题颜色、深色外观、自定义软件名称及事项时间显示。
- 本地数据保存、备份导出与导入、托盘开关和开机启动。

<img width="30%" alt="ChatGPT Image 2026年9月18日 20_32_51 (1)" src="https://github.com/user-attachments/assets/fbf1facc-452f-461a-a24e-e7276c797dff" />
<img width="30%" alt="ChatGPT Image 2026年9月18日 20_32_51 (2)" src="https://github.com/user-attachments/assets/01c7157d-0c2e-49e7-a2f7-a828a601e195" />
<img width="30%" alt="ChatGPT Image 2026年9月18日 20_32_52 (3)" src="https://github.com/user-attachments/assets/b0c614ee-789f-49c1-950b-aa7722c8c7b7" />


## 系统要求

- Windows 10 / 11，64 位。
- .NET Framework 4.8。

## 安装

1. 运行 `DeskTodo-Setup-v1.22.exe`。
2. 选择安装位置，默认安装到系统的 `Program Files\DeskTodo` 文件夹。
3. 下一页自行勾选开始菜单和桌面快捷方式，再点击安装。
4. 写入受保护目录时，Windows 会请求管理员权限。

更新前请保存正在编辑的内容并退出软件，再安装到原来的位置。覆盖安装会保留已有待办数据；如需更新桌面快捷方式，请勾选对应选项。开始菜单已固定的旧图标若未刷新，可取消固定后重新固定新入口。

## 数据与提醒

待办数据只保存在本机，软件无需账号，不上传待办内容。提醒需要软件持续运行，隐藏到托盘仍可提醒；退出软件或电脑关机时无法提醒。

## 问题反馈

可通过本仓库的 Issues 反馈问题。请说明软件版本、Windows 版本及复现步骤；如涉及多显示器，请附上各显示器的缩放比例。截图前请遮挡个人待办和其他隐私信息。

## 从源码编译

在 Windows 10 / 11 x64、.NET Framework 4.8 环境下使用 Windows PowerShell 5.1。项目使用系统 C# 编译器，不需要下载第三方包。

```powershell
git clone https://github.com/1242483743/DeskTodo.git
cd DeskTodo
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-setup.ps1 -AppDirectory dist
```

应用程序输出到 `dist\DeskTodo.exe`，安装包输出到 `release\DeskTodo-Setup-v1.22.exe`。也可用 Visual Studio 打开 `src\DesktopTodo.csproj`（需要 .NET Framework 4.8 开发工具）。

### 运行自检

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-setup.ps1 -AppDirectory dist -Test
```

测试会创建独立测试数据和临时窗口，输出到 `qa`；请在有可交互桌面的 Windows 会话中运行。真实 Explorer 桌面集成、不同显示器组合与 UAC 安装仍需手动验证。不要将测试数据或个人待办文件提交到仓库。

## 项目结构

```text
src/             应用源码、WPF 界面、项目文件与自检
installer/       安装器源码与界面
图标/            程序和内部界面的图标资源
build.ps1        编译应用程序
build-setup.ps1  构建安装包
build-icon.ps1   导出多尺寸透明 ICO
使用说明.md       完整使用说明
实现与验证.md     实现记录与已验证范围
LICENSE          MIT 许可证
```

`dist`、`release`、`qa`、历史备份和个人待办数据均不纳入源码版本管理。

## 许可证

源码采用 [MIT](LICENSE)，版权署名为 `1242483743`。使用、修改或分发时请保留许可证和版权声明。
