// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class LanguageChoice
{
    internal string Id,Name,Font,Status;
    internal int Entries,SourceEntries,TranslatedEntries,FallbackEntries;
    public override string ToString(){return Name;}
}

internal sealed class LanguagePicker : Button
{
    private readonly ContextMenuStrip menu=new ContextMenuStrip();
    private LanguageChoice[] choices=new LanguageChoice[0];private int index=-1;
    internal event EventHandler SelectedIndexChanged;
    internal LanguageChoice[] Items {get{return choices;}}
    internal LanguageChoice SelectedItem {get{return index<0?null:choices[index];}}
    internal int SelectedIndex
    {
        get{return index;}
        set
        {
            if(value==index)return;if(value<0||value>=choices.Length)throw new ArgumentOutOfRangeException("value");
            index=value;Text=choices[index].Name;foreach(ToolStripMenuItem item in menu.Items)item.Checked=Convert.ToInt32(item.Tag)==index;
            if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);
        }
    }
    internal void Bind(LanguageChoice[] values)
    {
        choices=values;menu.Items.Clear();
        for(int i=0;i<choices.Length;++i)
        {
            int selected=i;var item=new ToolStripMenuItem(choices[i].Name) {Tag=i};
            item.Click+=(s,e)=>{SelectedIndex=selected;Focus();};menu.Items.Add(item);
        }
    }
    internal LanguagePicker()
    {
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderColor=Color.FromArgb(81,84,91);FlatAppearance.BorderSize=1;
        TextAlign=ContentAlignment.MiddleLeft;Padding=new Padding(9,0,22,0);Cursor=Cursors.Hand;
        menu.ShowImageMargin=false;menu.ShowCheckMargin=true;
        Click+=(s,e)=>menu.Show(this,new Point(0,Height));
        KeyDown+=(s,e)=>{if(e.KeyCode==Keys.F4||(e.Alt&&e.KeyCode==Keys.Down)){e.Handled=true;menu.Show(this,new Point(0,Height));}};
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);int x=Width-16,y=Height/2;
        using(var brush=new SolidBrush(ForeColor))e.Graphics.FillPolygon(brush,new[]{new Point(x-3,y-1),new Point(x+3,y-1),new Point(x,y+2)});
    }
    protected override void Dispose(bool disposing){if(disposing)menu.Dispose();base.Dispose(disposing);}
}

internal static class LanguageSupport
{
    internal static readonly string[] Supported={"zh-Hans","zh-Hant","en-US","ja-JP","ko-KR"};
    private static string root,selected="zh-Hans";
    private static Dictionary<string,object> manifest;
    private static Dictionary<string,string> labels=new Dictionary<string,string>();
    internal static string Current {get{return selected;}}
    internal static LanguageChoice[] Choices {get;private set;}
    private static string Folder {get{return Path.Combine(root,"localization");}}
    internal static void Load(string installation)
    {
        root=installation;
        manifest=new JavaScriptSerializer {MaxJsonLength=1024*1024}.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Folder,"manifest.json"),Encoding.UTF8));
        Choices=((IEnumerable)manifest["languages"]).Cast<object>().Select(value=>
        {
            var d=(Dictionary<string,object>)value;
            return new LanguageChoice {Id=Convert.ToString(d["id"]),Name=Convert.ToString(d["nativeName"]),Font=Convert.ToString(d["font"]),Status=Convert.ToString(d["status"]),Entries=Convert.ToInt32(d.ContainsKey("displayEntries")?d["displayEntries"]:d["entries"]),SourceEntries=d.ContainsKey("sourceEntries")?Convert.ToInt32(d["sourceEntries"]):0,TranslatedEntries=d.ContainsKey("translatedSourceEntries")?Convert.ToInt32(d["translatedSourceEntries"]):0,FallbackEntries=d.ContainsKey("fallbackSourceEntries")?Convert.ToInt32(d["fallbackSourceEntries"]):0};
        }).Where(x=>Regex.IsMatch(x.Id,"^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8}){0,3}$")&&x.Font.Length>0&&x.Font.Length<32&&!x.Font.Any(Char.IsControl)).ToArray();
        if(!Choices.Any(x=>x.Id=="zh-Hans")||Choices.Select(x=>x.Id).Distinct().Count()!=Choices.Length)throw new InvalidDataException("语言清单无效。");
        string file=Path.Combine(Folder,"selected.ini"),id="zh-Hans";
        if(File.Exists(file))foreach(string line in File.ReadAllLines(file,Encoding.Unicode))if(line.StartsWith("Language=",StringComparison.Ordinal))id=line.Substring(9).Trim();
        Select(Choices.Any(c=>c.Id==id)?id:"zh-Hans",false);
    }
    internal static void Select(string id,bool save)
    {
        if(Choices==null||!Choices.Any(c=>c.Id==id))throw new InvalidDataException("此语言尚未安装语言包。");
        string file=Path.Combine(Folder,"packs",id+".json");
        var next=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(file,Encoding.UTF8));
        if(save)
        {
            string target=Path.Combine(Folder,"selected.ini"),temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
            string font=Choices.First(x=>x.Id==id).Font;
            File.WriteAllText(temporary,"[Localization]\r\nLanguage="+id+"\r\nFont="+font+"\r\n",Encoding.Unicode);
            if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);
        }
        labels=next;selected=id;
    }
    internal static string Text(string source)
    {string target;return labels.TryGetValue(source,out target)?target:source;}
    internal static string Coverage()
    {
        var c=Choices.First(x=>x.Id==selected);
        if(selected=="zh-Hans")return Text("游戏原始中文文本；选择会自动保存。");
        if(selected!="zh-Hant"&&c.SourceEntries>0)return String.Format(Text("翻译预览：{0:N0}/{1:N0} · 中文回退 {2:N0} 条 · 下次启动生效"),c.TranslatedEntries,c.SourceEntries,c.FallbackEntries);
        return String.Format(Text("游戏文本：{0:N0} 条 · {1} · 下次进入游戏生效"),c.Entries,Text(c.Status));
    }
    private static string Hash(string file)
    {using(var stream=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    internal static void ValidateForGame(string installation)
    {
        Load(installation);
        if(selected=="zh-Hans"){ApplyClientSprites();return;}
        var hashes=(Dictionary<string,object>)manifest["runtimeHashes"];
        foreach(var pair in hashes)
        {
            string relative=pair.Key.Replace('/',Path.DirectorySeparatorChar),file=Path.GetFullPath(Path.Combine(root,relative));
            if(!file.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(file)||Hash(file)!=Convert.ToString(pair.Value))
                throw new InvalidDataException(Text("语言组件不完整，请恢复完整安装包后重试。")+" "+pair.Key);
        }
        string pack=Path.Combine(Folder,"packs",selected+".arlp");
        var packHashes=(Dictionary<string,object>)manifest["packHashes"];
        if(!File.Exists(pack)||Hash(pack)!=Convert.ToString(packHashes[selected]))throw new InvalidDataException(Text("语言包校验失败，请恢复完整安装包后重试。"));
        ApplyClientSprites();
    }
    private static string AssetPath(string relative)
    {
        string file=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!file.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid UI language asset path.");
        return file;
    }
    internal static void ApplyClientSprites()
    {
        string spec=Path.Combine(Folder,"sprites","manifest.json");
        if(!File.Exists(spec))throw new InvalidDataException(Text("语言组件不完整，请恢复完整安装包后重试。"));
        var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(spec,Encoding.UTF8));
        var archives=new List<Dictionary<string,object>> {data};
        if(data.ContainsKey("additionalArchives"))archives.AddRange(((IEnumerable)data["additionalArchives"]).Cast<object>().Select(x=>(Dictionary<string,object>)x));
        var pending=archives.Select(PrepareSpriteArchive).Where(x=>x!=null).ToArray();
        foreach(var change in pending)change();
    }
    private static Action PrepareSpriteArchive(Dictionary<string,object> data)
    {
        string target=AssetPath(Convert.ToString(data["client"])),baseFile=AssetPath(Convert.ToString(data["base"]));
        string baseHash=Convert.ToString(data["baseSha256"]);
        if(!File.Exists(baseFile)||Hash(baseFile)!=baseHash)throw new InvalidDataException("UI language base failed verification.");
        var variants=(Dictionary<string,object>)data["languages"];
        var desired=selected=="zh-Hans"?null:(Dictionary<string,object>)variants[selected];
        string expected=desired==null?baseHash:Convert.ToString(desired["clientSha256"]);
        string current=File.Exists(target)?Hash(target):"";
        if(current==expected)return null;
        bool recognized=current==baseHash||variants.Values.Cast<Dictionary<string,object>>().Any(x=>Convert.ToString(x["clientSha256"])==current||
            (x.ContainsKey("acceptedPreviousHashes")&&((IEnumerable)x["acceptedPreviousHashes"]).Cast<object>().Any(h=>Convert.ToString(h)==current)));
        if(!recognized)throw new InvalidDataException("The interface archive contains other changes; restore it before switching UI languages.");
        string game=Path.GetFullPath(Path.Combine(root,"DNF60_ACT12","ACT12","DNF.exe"));
        foreach(var process in Process.GetProcessesByName("DNF"))using(process)
        {
            try{if(String.Equals(process.MainModule.FileName,game,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请先结束当前游戏，再切换游戏界面语言。");}
            catch(System.ComponentModel.Win32Exception){throw new InvalidOperationException("无法确认游戏是否已退出，请关闭游戏后重试。");}
        }
        byte[] output=File.ReadAllBytes(baseFile);
        if(desired!=null)
        {
            var patches=desired.ContainsKey("patches")?((IEnumerable)desired["patches"]).Cast<Dictionary<string,object>>().ToArray():
                new[]{new Dictionary<string,object>{{"patch",desired["patch"]},{"patchSha256",desired["patchSha256"]},{"offset",data["offset"]},{"size",data["size"]},{"entryIndex",data["entryIndex"]}}};
            foreach(var item in patches.OrderByDescending(x=>Convert.ToInt32(x["offset"])))output=RepackUiImage(output,item);
        }
        return ()=>
        {
            string temporary=target+".language-"+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllBytes(temporary,output);
                if(Hash(temporary)!=expected)throw new InvalidDataException("Rebuilt UI archive failed verification.");
                File.Replace(temporary,target,null);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        };
    }
    private static byte[] RepackUiImage(byte[] source,Dictionary<string,object> item)
    {
            string patch=AssetPath(Convert.ToString(item["patch"]));
            if(!File.Exists(patch)||Hash(patch)!=Convert.ToString(item["patchSha256"]))throw new InvalidDataException("UI language patch failed verification.");
            byte[] replacement=File.ReadAllBytes(patch);
            int offset=Convert.ToInt32(item["offset"]),length=Convert.ToInt32(item["size"]),index=Convert.ToInt32(item["entryIndex"]);
            int count=BitConverter.ToInt32(source,16),delta=replacement.Length-length;
            if(index<0||index>=count||count<1||count>10000||offset<20+count*264||length<0||offset>source.Length-length)throw new InvalidDataException("Invalid UI archive layout.");
            byte[] output=new byte[checked(source.Length+delta)];
            Buffer.BlockCopy(source,0,output,0,offset);Buffer.BlockCopy(replacement,0,output,offset,replacement.Length);
            Buffer.BlockCopy(source,offset+length,output,offset+replacement.Length,source.Length-offset-length);
            for(int i=0;i<count;++i)
            {
                int at=20+i*264,position=BitConverter.ToInt32(source,at),size=BitConverter.ToInt32(source,at+4);
                if(i==index){if(position!=offset||size!=length)throw new InvalidDataException("UI sprite index mismatch.");Buffer.BlockCopy(BitConverter.GetBytes(replacement.Length),0,output,at+4,4);}
                else if(position>=offset+length)Buffer.BlockCopy(BitConverter.GetBytes(checked(position+delta)),0,output,at,4);
                else if(position+size>offset)throw new InvalidDataException("Overlapping UI sprite entries.");
            }
            int directoryEnd=20+count*264;
            using(var sha=SHA256.Create())Buffer.BlockCopy(sha.ComputeHash(output,0,(directoryEnd/17)*17),0,output,directoryEnd,32);
            return output;
    }
}
