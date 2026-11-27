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

Run `setup/portable/build-login-backend.ps1 -SourceDirectory <registration-service-source> -OutputDirectory <backends>` with the matching registration service project to build the standard x64 login service. Build the Windows 10 compatibility service into `x64-compat` with `-Framework net10.0 -Variant x64-compat`. That executable uses the .NET apphost compatibility setting for systems without `QueueUserAPC2`. Pass `-LoginBackends <backends>` to `build.ps1` to stage the binaries and refresh their version manifest. Rebuild the portable package integrity manifest after staging.

For a Windows 7 SP1 x64 compatibility test, use a separate staging folder with the embedded Python 3.8 runtime, its matching dependencies including `async-timeout`, SQLite with `unixepoch()` support, and the x64 login service built with `-Framework net6.0 -Variant x64-compat`. Run the portable build with `-Windows7Runtime` and regenerate the package integrity manifest. On the test PC, use .NET Framework 4.8 and the Windows 7 updates and Visual C++ runtimes required by .NET 6. Check startup, local sign-in, the trainer and saving on Windows 7 before releasing this variant. [Microsoft runtime prerequisites](https://learn.microsoft.com/en-us/dotnet/core/install/windows#windows-7--81--server-2012)

Win7 SP1 64 位兼容测试使用独立的打包目录。该目录需包含嵌入式 Python 3.8、匹配的依赖（含 `async-timeout`）、支持 `unixepoch()` 的 SQLite，以及用 `-Framework net6.0 -Variant x64-compat` 构建的 64 位登录服务。构建时传入 `-Windows7Runtime` 并重新生成包校验清单。测试机需具备 .NET Framework 4.8 和 .NET 6 要求的 Win7 补丁及 Visual C++ 运行库。发布前应在 Win7 上验证启动、账号登录、修改器和存档。

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
