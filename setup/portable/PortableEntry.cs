// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("阿拉德·归途 · 启动入口")]
[assembly: System.Reflection.AssemblyCompany("eirros")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 eirros. All rights reserved.")]
[assembly: System.Reflection.AssemblyVersion("2.6.0.16")]
[assembly: System.Reflection.AssemblyFileVersion("2.6.0.16")]

internal static class PortableEntry
{
    [STAThread]
    private static int Main()
    {
        string root=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        string controller=Path.Combine(root,"DNF60控制台.exe");
        try
        {
            if(!File.Exists(controller))throw new FileNotFoundException("请先完整解压便携包，再打开本程序。",controller);
            string name=Path.GetFileNameWithoutExtension(Application.ExecutablePath);
            bool stop=name.Contains("关闭");
            if(!stop)
            {
                var relay=RelayPortPreflight.Ensure(root,name.Contains("修复"));
                if(name.Contains("修复"))
                {
                    MessageBox.Show(relay.AlreadyRunning?"本目录游戏服务正在运行，未更改端口。":relay.Changed?"中继端口已从 "+relay.PreviousTcp+" 改为 "+relay.Tcp+"。\r\n原配置已备份。请双击原来的“阿拉德·归途”开始游戏。":"当前中继端口可以使用。请双击原来的“阿拉德·归途”开始游戏。","阿拉德·归途 · 端口修复",MessageBoxButtons.OK,MessageBoxIcon.Information);
                    return 0;
                }
            }
            string action=stop?"--stop":"--start";
            Process.Start(new ProcessStartInfo(controller,action){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
            return 0;
        }
        catch(Exception ex)
        {MessageBox.Show(ex.Message,"阿拉德·归途",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 1;}
    }
}
