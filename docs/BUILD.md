# Build notes / 构建说明

The repository contains custom launcher/session C#, native trainer C#, local management Python and the x86 C++ game-language runtime. Original game/server binaries, PVF/NPK assets, translation/font packs and the isolated Python runtime are in the release ZIP, not in Git history.

仓库包含自编启动器/会话管理 C#、原生修改器 C#、本地管理 Python 和 x86 游戏语言运行时 C++。原始游戏/服务二进制、PVF/NPK 资源、语言/字体包及独立 Python 环境随 Release 提供。

## Windows desktop components

1. Download and fully extract a release. Work on a **separate copy**, with game and trainer closed.
2. Download this source and open PowerShell in its root.
3. Build against the release's extraction folder.

```powershell
.\build.ps1 -InstallRoot 'D:\Games\AradHomeward'
```

The script uses the Windows .NET Framework 4.x C# compiler. It builds the portable controller, native trainer, English-named entries and diagnostics, copies current Python modules, and updates the language manifest's runtime hashes. It does not build the original proprietary game/server. Keep the original notices.

脚本使用 Windows .NET Framework 4.x 的 C# 编译器，生成便携控制器、原生修改器、英文入口及诊断程序，复制 Python 模块并刷新语言清单中的组件哈希。它不编译原游戏/服务器。请保留原有声明。

The release includes `a12_profiles.dll` for the original account launcher. To rebuild this helper, use an x86 Visual Studio developer prompt and compile `setup/portable/LegacyLauncherProfiles.cpp` with `/LD /O2 /MT /utf-8`, linking `shlwapi.lib` and `user32.lib`. Place the DLL at `DNF60_ACT12/ACT12/a12_profiles.dll` before refreshing the release hashes.

## Game language runtime

`setup/localization/GameLanguage.cpp` and `GameLanguage.rc` build an **x86** DLL with Visual Studio 2022 C++ Build Tools, the Windows SDK and the bundled MinHook source. Run `setup/localization/build-runtime.cmd` after adjusting its `vcvars32.bat` location if needed. The base `a12_score_base.dll`, game client and binary language/resource packs come from the release.

This runtime is tied to the bundled ACT12 binary layout. Replacing the client, changing hook addresses or mixing unrelated NPK patches requires a new compatibility review. Do not silently refresh hashes for unreviewed files. Rebuild the release resource manifest and validate all five language selections after runtime changes.

The x86 runtime also maps the cursor to the scaled game viewport. To install the rebuilt runtime and refresh its hash, supply the DLL to the portable build.

该 x86 组件同时负责放大窗口后的鼠标坐标转换。用下面的命令将重新编译的 DLL 放入完整包，并刷新对应的校验值。

```powershell
.\build.ps1 -InstallRoot 'D:\Games\AradHomeward' -ClientRuntime '.\setup\localization\a12_live_score.dll'
```

To rebuild the preview.26 resource changes against a clean preview.25 staging copy, use a Python environment with Pillow. Run `setup/localization/repair_text_packs.py <app> setup/localization/locale_text_repairs.json`, then run `localize_popup_captions.py <app>` and `localize_class_captions.py <app>` from that directory. The sprite generators read the bundled base archives and verify the previous language variants before adding captions. Refresh the runtime hashes for the resulting resource files as part of packaging.

## Layout

| Source | Responsibility |
| --- | --- |
| `setup/Dnf60Control.cs`, lifecycle/account helpers | Hidden local services, launcher integration, shutdown and backups |
| `setup/LanguageSupport.cs` | Launcher labels, validation and language sprite selection |
| `setup/portable/` | Entries, relay preflight and startup diagnostics |
| `trainer/desktop/` | Native Windows Forms trainer and localization |
| `trainer/*.py` | Local profiles, resources, database bridge and management API |
| `trainer/first_run.py` | Generate and verify the installation's unique loopback TLS identity |
| `setup/localization/` | Native text/font/interface language runtime |

The public distribution starts with a new schema-only save plus static monster XP reference data. All player/account/bot/auction/mail/history rows are empty. Certificates, passwords, protected player-name lists, credentials files, logs and backups are excluded. First run creates the required local TLS identity; credentials are generated when a user creates a local profile.

公开包使用新建的空存档结构，仅保留静态怪物经验表。玩家、账号、机器人实例、拍卖、邮件和历史记录表均为空；不发布证书、私钥、个人角色保护名单、账号凭据、日志或备份。
