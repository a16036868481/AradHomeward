// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

internal static class LauncherMode
{
    // Healthy installations keep the original launcher. A compatibility patch
    // selects the same native account UI in its own window for affected PCs.
    internal static bool UseCompatibility(string root)
    {
        string file=Path.Combine(root,"setup","launcher-mode.json");
        if(!File.Exists(file))return false;
        var settings=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file));
        object value;
        string mode=settings!=null&&settings.TryGetValue("mode",out value)?Convert.ToString(value):"";
        if(String.Equals(mode,"compat",StringComparison.OrdinalIgnoreCase))return true;
        if(String.Equals(mode,"original",StringComparison.OrdinalIgnoreCase))return false;
        throw new InvalidDataException("启动方式配置无效，请重新覆盖启动补丁中的 setup\\launcher-mode.json。");
    }
}
