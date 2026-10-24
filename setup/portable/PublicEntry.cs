// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Arad Homeward")]
[assembly: System.Reflection.AssemblyCompany("eirros")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 eirros. All rights reserved.")]
[assembly: System.Reflection.AssemblyVersion("2.6.0.23")]
internal static class PublicEntry
{
    [STAThread]
    private static int Main()
    {
        try
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app");
            string name=Path.GetFileNameWithoutExtension(Application.ExecutablePath);
            if(!Directory.Exists(root))throw new DirectoryNotFoundException("Extract the complete ZIP before starting Arad Homeward.");
            if(name!="StopAll")
            {
                var init=new ProcessStartInfo(Path.Combine(root,"runtime","python","python.exe"),"-I -B \""+Path.Combine(root,"trainer","first_run.py")+"\"")
                {WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true};
                using(var process=Process.Start(init))
                {
                    string error=process.StandardError.ReadToEnd();
                    if(!process.WaitForExit(30000)||process.ExitCode!=0)throw new InvalidOperationException("Local login setup failed. "+error);
                }
            }
            string exe=name=="AradTrainer"?"DNF60控制台.exe":name=="StopAll"?"备用关闭.exe":name=="CheckStartup"?"启动检查.exe":"阿拉德·归途.exe";
            string arguments=name=="AradTrainer"?"--gm":"";
            Process.Start(new ProcessStartInfo(Path.Combine(root,exe),arguments){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
            return 0;
        }
        catch(Exception error){MessageBox.Show(error.Message,"Arad Homeward",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 1;}
    }
}
