using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Arad Homeward service entry")]
internal static class LegacyServiceEntry
{
    [STAThread]
    private static int Main(string[] args)
    {
        string dir=AppDomain.CurrentDomain.BaseDirectory;
        string root=Path.GetFullPath(Path.Combine(dir,@"..\.."));
        string mode=args.Length==0?"--all":args[0].ToLowerInvariant();
        bool diagnostic=mode=="--diagnose" || mode=="--check";
        if(args.Length>1 || (!diagnostic && mode!="--all" && mode!="--game" && mode!="--login"))return 2;
        string file=diagnostic?Path.Combine(dir,"a12_compat_vendor.exe"):Path.Combine(root,"AradHomeward.exe");
        string command=diagnostic?mode:mode=="--login"?"--login-only":"--legacy-services";
        try
        {
            using(var child=Process.Start(new ProcessStartInfo(file,command){
                WorkingDirectory=diagnostic?dir:root,UseShellExecute=false,CreateNoWindow=true}))
            {child.WaitForExit();return child.ExitCode;}
        }
        catch(Exception e){MessageBox.Show(e.Message,"Arad Homeward");return 1;}
    }
}
