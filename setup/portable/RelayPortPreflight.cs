// Copyright (c) 2026 eirros. All rights reserved.
// Check only this game's loopback relay; never stop an occupying application.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal static class RelayPortPreflight
{
    internal sealed class Result
    {
        internal bool Changed,AlreadyRunning;
        internal int PreviousTcp,PreviousUdp,Tcp,Udp;
        internal string BackupDirectory,Reason;
    }
    private sealed class Ini
    {
        internal readonly string Path;
        internal readonly byte[] Before;
        private readonly byte[] prefix;
        private readonly Encoding encoding;
        private string text;
        internal Ini(string path)
        {
            Path=path;Before=File.ReadAllBytes(path);int start=0;
            if(Before.Length>=2&&Before[0]==255&&Before[1]==254){encoding=Encoding.Unicode;start=2;}
            else if(Before.Length>=2&&Before[0]==254&&Before[1]==255){encoding=Encoding.BigEndianUnicode;start=2;}
            else
            {
                // The port keys are ASCII. A byte-preserving encoding keeps
                // all untouched UTF-8/legacy comments exactly as supplied.
                encoding=Encoding.GetEncoding(28591);
                if(Before.Length>=3&&Before[0]==239&&Before[1]==187&&Before[2]==191)start=3;
            }
            prefix=Before.Take(start).ToArray();text=encoding.GetString(Before,start,Before.Length-start);
        }
        internal string Get(string section,string key,string fallback)
        {
            string current="";
            foreach(string line in Regex.Split(text,@"\r\n|\n|\r"))
            {
                var header=Regex.Match(line,@"^\s*\[([^\]]+)\]");
                if(header.Success){current=header.Groups[1].Value.Trim();continue;}
                if(!String.Equals(current,section,StringComparison.OrdinalIgnoreCase))continue;
                var value=Regex.Match(line,@"^\s*"+Regex.Escape(key)+@"\s*=\s*(.*)$",RegexOptions.IgnoreCase);
                if(value.Success)return value.Groups[1].Value.Split(';')[0].Trim().Trim('"');
            }
            return fallback;
        }
        internal int Port(string section,string key,int fallback)
        {
            int value;
            if(!Int32.TryParse(Get(section,key,fallback.ToString()),out value)||value<0||value>65535)
                throw new InvalidDataException(System.IO.Path.GetFileName(Path)+" 中的 "+key+" 不是有效端口。");
            return value;
        }
        internal void Set(string section,string key,int value)
        {
            string current="";bool replaced=false,foundSection=false;int insertion=-1;
            var lines=Regex.Matches(text,@"[^\r\n]*(?:\r\n|\n|\r|$)").Cast<Match>().Where(m=>m.Length>0).Select(m=>m.Value).ToList();
            for(int i=0;i<lines.Count;i++)
            {
                string body=lines[i].TrimEnd('\r','\n');var header=Regex.Match(body,@"^\s*\[([^\]]+)\]");
                if(header.Success)
                {
                    if(String.Equals(current,section,StringComparison.OrdinalIgnoreCase))insertion=i;
                    current=header.Groups[1].Value.Trim();
                    if(String.Equals(current,section,StringComparison.OrdinalIgnoreCase)){foundSection=true;insertion=i+1;}
                    continue;
                }
                if(!String.Equals(current,section,StringComparison.OrdinalIgnoreCase))continue;
                insertion=i+1;
                var field=Regex.Match(body,@"^(?<head>\s*"+Regex.Escape(key)+@"\s*=\s*)(?<old>[^;]*?)(?<tail>[ \t]*(?:;.*)?)$",RegexOptions.IgnoreCase);
                if(field.Success){lines[i]=field.Groups["head"].Value+value+field.Groups["tail"].Value+lines[i].Substring(body.Length);replaced=true;}
            }
            if(!replaced)
            {
                string newline=text.Contains("\r\n")?"\r\n":"\n";
                if(foundSection)
                {
                    if(insertion>0&&!lines[insertion-1].EndsWith("\n")&&!lines[insertion-1].EndsWith("\r"))lines[insertion-1]+=newline;
                    lines.Insert(insertion,key+"="+value+newline);
                }
                else
                {
                    if(lines.Count>0&&!lines[lines.Count-1].EndsWith("\n")&&!lines[lines.Count-1].EndsWith("\r"))lines[lines.Count-1]+=newline;
                    lines.Add("["+section+"]"+newline+key+"="+value+newline);
                }
            }
            text=String.Concat(lines);
        }
        internal byte[] Bytes(){return prefix.Concat(encoding.GetBytes(text)).ToArray();}
    }
    private static string Hash(byte[] bytes)
    {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    private static bool OwnServerRunning(string server)
    {
        string expected=System.IO.Path.Combine(server,"a12_server.exe");
        foreach(var process in Process.GetProcessesByName("a12_server"))using(process)
        {
            try{if(!process.HasExited&&String.Equals(process.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase))return true;}
            catch(InvalidOperationException){}
            catch(System.ComponentModel.Win32Exception){throw new InvalidOperationException("检测到正在运行的游戏服务，但无法确认其目录。请正常关闭本次游戏服务后再启动。");}
        }
        return false;
    }
    private static bool CanBind(IPAddress address,int tcp,int udp,out string reason)
    {
        try
        {
            using(var stream=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp))
            using(var datagram=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp))
            {
                stream.ExclusiveAddressUse=true;datagram.ExclusiveAddressUse=true;
                stream.Bind(new IPEndPoint(address,tcp));stream.Listen(1);
                datagram.Bind(new IPEndPoint(address,udp));reason=null;return true;
            }
        }
        catch(SocketException error){reason=error.SocketErrorCode+" ("+error.NativeErrorCode+")";return false;}
    }
    private static void Replace(string path,byte[] bytes)
    {
        string temp=path+".relay-next-"+Guid.NewGuid().ToString("N");
        try{File.WriteAllBytes(temp,bytes);File.Replace(temp,path,null);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal static Result Ensure(string root,bool force=false)
    {
        root=System.IO.Path.GetFullPath(root);string server=System.IO.Path.Combine(root,"DNF60_ACT12","ACT12_Server");
        string configPath=System.IO.Path.Combine(server,"server.ini");
        if(!File.Exists(configPath))throw new FileNotFoundException("游戏服务配置缺失，请完整解压游戏文件夹。",configPath);
        string identity=Hash(Encoding.Unicode.GetBytes(root.ToUpperInvariant())).Substring(0,24);
        using(var gate=new Mutex(false,"Local\\AradReturn_Relay_"+identity))
        {
            bool acquired=false;
            try
            {
                try{acquired=gate.WaitOne(10000);}catch(AbandonedMutexException){acquired=true;}
                if(!acquired)throw new InvalidOperationException("另一启动入口正在检查游戏端口，请稍后重试。");
                var config=new Ini(configPath);string relayPath=System.IO.Path.Combine(server,"relay.ini");
                Ini relay=File.Exists(relayPath)?new Ini(relayPath):null;
                int tcp=config.Port("network","relay_tcp_port",5555),udp=config.Port("network","relay_udp_port",5555);
                if(relay!=null){tcp=relay.Port("server","this_tcp_port",tcp);udp=relay.Port("server","this_udp_port",udp);}
                var result=new Result {PreviousTcp=tcp,PreviousUdp=udp,Tcp=tcp,Udp=udp};
                if(OwnServerRunning(server)){result.AlreadyRunning=true;return result;}
                // Port zero is the native server's explicit automatic/disabled
                // setting in relay.ini; leave that deliberate setting intact.
                if(tcp==0||udp==0)return result;
                IPAddress address;
                if(!IPAddress.TryParse(config.Get("network","listen_ip","127.0.0.1"),out address)||address.AddressFamily!=AddressFamily.InterNetwork||!IPAddress.IsLoopback(address))
                    throw new InvalidDataException("单机中继端口检查要求 server.ini 的 listen_ip 使用 IPv4 本机回环地址。");
                string failure;
                bool available=CanBind(address,tcp,udp,out failure);
                if(available&&!force)return result;
                result.Reason=available?"manual relay repair":failure;int selected=0;
                foreach(int candidate in Enumerable.Range(15555,100).Concat(Enumerable.Range(27555,100)))
                    if(candidate!=tcp&&candidate!=udp&&CanBind(address,candidate,candidate,out failure)){selected=candidate;break;}
                if(selected==0)throw new IOException("未找到可用的本机中继端口："+failure+"。配置和存档未修改。");
                config.Set("network","relay_tcp_port",selected);config.Set("network","relay_udp_port",selected);
                if(relay!=null){relay.Set("server","this_tcp_port",selected);relay.Set("server","this_udp_port",selected);}
                var documents=relay==null?new[]{config}:new[]{config,relay};
                foreach(var document in documents)
                    if(!File.ReadAllBytes(document.Path).SequenceEqual(document.Before))throw new IOException("端口检查期间配置被其他程序更改，请重新启动。");
                if(OwnServerRunning(server)){result.AlreadyRunning=true;return result;}
                string backup=System.IO.Path.Combine(root,"backups","relay-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
                Directory.CreateDirectory(backup);
                foreach(var document in documents)File.WriteAllBytes(System.IO.Path.Combine(backup,System.IO.Path.GetFileName(document.Path)),document.Before);
                File.WriteAllText(System.IO.Path.Combine(backup,"COMPLETE.txt"),String.Join(Environment.NewLine,documents.Select(document=>System.IO.Path.GetFileName(document.Path)+" "+Hash(document.Before))),new UTF8Encoding(false));
                var written=new List<Ini>();
                try
                {
                    foreach(var document in documents){Replace(document.Path,document.Bytes());written.Add(document);}
                    var readConfig=new Ini(configPath);var readRelay=relay==null?null:new Ini(relayPath);
                    if(readConfig.Port("network","relay_tcp_port",0)!=selected||readConfig.Port("network","relay_udp_port",0)!=selected||
                        readRelay!=null&&(readRelay.Port("server","this_tcp_port",0)!=selected||readRelay.Port("server","this_udp_port",0)!=selected))throw new IOException("中继端口写入后校验失败。");
                }
                catch
                {
                    foreach(var document in written)try{Replace(document.Path,document.Before);}catch{}
                    throw;
                }
                result.Changed=true;result.Tcp=result.Udp=selected;result.BackupDirectory=backup;
                try
                {
                    string logs=System.IO.Path.Combine(root,"logs");Directory.CreateDirectory(logs);
                    File.AppendAllText(System.IO.Path.Combine(logs,"relay-port-preflight.log"),DateTime.Now.ToString("s")+" relay "+tcp+"/"+udp+" -> "+selected+" reason="+result.Reason+" backup="+backup+Environment.NewLine,new UTF8Encoding(false));
                }
                catch(IOException){}catch(UnauthorizedAccessException){}
                return result;
            }
            finally{if(acquired)gate.ReleaseMutex();}
        }
    }
}
