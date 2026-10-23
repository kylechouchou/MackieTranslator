# 源码构建

目标：Windows x64，.NET Framework 4.8。发布显示版本为 1.0，程序集/文件版本为 1.0.0.0。

安装 Visual Studio 2019 或更新版本的“.NET 桌面开发”和 Windows 10 SDK。在源码目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出为 `dist/MackieTranslator/MackieTranslator.exe`。请保留输出目录的 DLL、配置和许可证文件。源码带有预编译原生 DLL，普通 UI/API 构建不需要 Rust；完整重建原生部分需 Rust MSVC 和 C++ 工具链：

```powershell
.\build.ps1 -RebuildNative
```

脚本自动查找 Windows SDK 元数据；IDE 构建时可能需要按本机 SDK 调整 csproj 中 Windows.winmd 的路径。

## 诊断构建

```powershell
.\build.ps1 -Diagnostics
```

输出 `dist/Diagnostics/MackieTranslator.exe`。测试会创建本地报告/图片并打开界面；建议在专用目录中测试，并为测试进程设置隔离的 LOCALAPPDATA，避免读写个人设置与历史。

| 参数 | 用途 |
| --- | --- |
| --language-test | 离线接口模拟 |
| --multilingual-ocr-test | 多语言 OCR 流程，本地组件及模拟 AI |
| --optimization-test | 缓存、历史、统计等 |
| --reliability-test | 历史持久化、取消条件、DPAPI |
| --ui-test --layout-only | 离线界面与状态检查 |
| --shutdown-probe capture-cycle | 划词反复启停后退出 |

`--self-test` 包含在线 Bing 请求，不能当作纯离线检查。正式构建不编译 `*Tests.cs`；诊断版不要作为普通发布包分发。构建前后不要将个人 settings.json、历史或密钥放进源码/发布目录。
