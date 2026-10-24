// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Dnf60Desktop
{
    // Read only the product's four fixed log files. Preserve UTF-8 boundaries
    // when taking a tail, rather than interpreting a partial first character
    // as evidence that the whole UTF-8 log is a legacy-encoded file.
    public static class NativeLogs
    {
        public static string Read(string root,string name)
        {
            var paths=new Dictionary<string,string>{
                {"游戏服务",Path.Combine(root,"DNF60_ACT12","ACT12_Server","server_log.txt")},
                {"在线修改服务",Path.Combine(root,"logs","trainer.log")},
                {"桌面修改器",Path.Combine(root,"logs","native-trainer.log")},
                {"一键启停",Path.Combine(root,"logs","control-"+DateTime.Now.ToString("yyyyMMdd")+".log")}
            };
            string file;if(!paths.TryGetValue(name,out file))throw new InvalidOperationException(TrainerLanguage.T("日志来源无效。"));
            if(!File.Exists(file))return "暂无日志。";
            byte[] data;bool partial;
            using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            {
                long offset=Math.Max(0,stream.Length-180000);partial=offset>0;stream.Position=offset;
                using(var buffer=new MemoryStream()){stream.CopyTo(buffer);data=buffer.ToArray();}
            }
            int start=0;if(partial){int newline=Array.IndexOf(data,(byte)10);if(newline>=0)start=newline+1;}
            string text;
            try{text=new UTF8Encoding(false,true).GetString(data,start,data.Length-start);}
            catch(DecoderFallbackException){text=Encoding.GetEncoding(936).GetString(data,start,data.Length-start);}
            var lines=text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
            return String.Join(Environment.NewLine,lines.Skip(Math.Max(0,lines.Length-600)));
        }
    }
}
