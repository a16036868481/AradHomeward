// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Diagnostics;
using System.IO;

internal static class InstallPrograms
{
#if PORTABLE
    internal const string Controller = "AradHomeward.exe";
    internal const string Trainer = "AradTrainer.exe";
    internal const string LegacyLauncher = "LegacyLauncher.exe";
    internal const string Diagnostics = "CheckStartup.exe";
#else
    internal const string Controller = "DNF60控制台.exe";
    internal const string Trainer = "归途修改器.exe";
    internal const string LegacyLauncher = "开始游戏.exe";
    internal const string Diagnostics = "启动检查.exe";
#endif

    internal static void EnsureLocalIdentity(string root, string python)
    {
        string script = Path.Combine(root, "trainer", "first_run.py");
        var info = new ProcessStartInfo(python, "-I -B \"" + script + "\"")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true
        };
        using (var process = Process.Start(info))
        {
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
                throw new InvalidOperationException("Local login identity setup timed out.");
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Local login identity setup failed. " + error.Result);
        }
    }
}
