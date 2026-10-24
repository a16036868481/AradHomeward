// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Dnf60Desktop
{
    // This class translates display text only. No account, item definition, input,
    // selected value, grid cell value, or request payload is ever rewritten here.
    internal static class TrainerLanguage
    {
        private sealed class Template
        {
            internal Regex Pattern;
            internal string Target;
        }
        private sealed class LanguageState
        {
            internal string Locale;
            internal Dictionary<string,string> Ui=new Dictionary<string,string>(StringComparer.Ordinal);
            internal Dictionary<string,string> Content=new Dictionary<string,string>(StringComparer.Ordinal);
            internal readonly Dictionary<string,string> Sources=new Dictionary<string,string>(StringComparer.Ordinal);
            internal List<Template> Templates=new List<Template>();
            internal Dictionary<int,List<KeyValuePair<string,string>>> Segments=new Dictionary<int,List<KeyValuePair<string,string>>>();
            internal readonly Dictionary<string,string> Cache=new Dictionary<string,string>(StringComparer.Ordinal);
        }
        private sealed class CaptionState
        {
            internal bool Preserved,Updating,Attached;
            internal string Source,Rendered,AccessibleSource,DescriptionSource;
            internal readonly Dictionary<object,string> ColumnSources=new Dictionary<object,string>();
            internal readonly Dictionary<object,string> ColumnRendered=new Dictionary<object,string>();
        }
        private sealed class GridState
        {
            internal readonly HashSet<string> Ui=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly HashSet<string> Content=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly HashSet<ComboBox> EditingCombos=new HashSet<ComboBox>();
            internal bool Attached;
        }
        private sealed class TipBinding
        {
            internal ToolTip Tip;
            internal WeakReference Root;
            internal readonly Dictionary<Control,KeyValuePair<string,string>> Captions=new Dictionary<Control,KeyValuePair<string,string>>();
        }
        private static readonly object sync=new object();
        private static readonly ConditionalWeakTable<Control,CaptionState> captions=new ConditionalWeakTable<Control,CaptionState>();
        private static readonly ConditionalWeakTable<DataGridView,GridState> grids=new ConditionalWeakTable<DataGridView,GridState>();
        private static readonly List<WeakReference> roots=new List<WeakReference>();
        private static readonly List<TipBinding> tips=new List<TipBinding>();
        private static readonly Regex placeholder=new Regex(@"\{(?<n>\d+)(?:,-?\d+)?(?::[^{}]*)?\}",RegexOptions.CultureInvariant);
        private static readonly string[] supported={"zh-Hans","zh-Hant","en-US","ja-JP","ko-KR"};
        private static volatile LanguageState current;
        private static string checkInstallation,checkLocale;
        private static System.Windows.Forms.Timer timer;
        private static long lastProbe;
        private static string signature;
        private static PrivateFontCollection fonts;
        private static HashSet<string> installedFonts;
        private static int fallbackRegistration;
        private static IntPtr fallbackMemory;
        private static GCHandle fallbackBytes;
        private static readonly Dictionary<string,Font> fontCache=new Dictionary<string,Font>();
        [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] private static extern int AddFontResourceEx(string file,uint flags,IntPtr reserved);
        [DllImport("gdi32.dll")] private static extern IntPtr AddFontMemResourceEx(IntPtr data,uint size,IntPtr reserved,ref uint count);
        [DllImport("user32.dll",EntryPoint="SendMessageW",CharSet=CharSet.Unicode)] private static extern IntPtr SendCue(IntPtr handle,int message,IntPtr w,string text);
        internal static event EventHandler Changed;
        internal static string Locale {get{Initialize();return current.Locale;}}
        internal static int UiEntryCount {get{Initialize();return current.Ui.Count;}}
        internal static int ContentEntryCount {get{Initialize();return current.Content.Count;}}

        internal static void Initialize()
        {
            if(current!=null)return;
            lock(sync){if(current==null){current=Load(ReadLocale());signature=FileSignature();}}
        }
        private static string Folder {get{return Path.Combine(checkInstallation??Program.Root,"localization");}}
        // Used by the isolated self-check harness. This never writes selected.ini,
        // changes saves, or starts any product/service process.
        internal static void UseForCheck(string installation,string locale)
        {
            if(String.IsNullOrWhiteSpace(installation)||!Directory.Exists(installation))throw new ArgumentException("Invalid check installation.");
            if(locale!=null&&!supported.Contains(locale))throw new ArgumentException("Unsupported check language.");
            checkInstallation=Path.GetFullPath(installation);checkLocale=locale;current=Load(locale??ReadLocale());signature=FileSignature();
        }
        private static string ReadLocale()
        {
            if(checkLocale!=null)return checkLocale;
            try
            {
                string file=Path.Combine(Folder,"selected.ini");
                using(var reader=new StreamReader(file,Encoding.UTF8,true))
                    while(!reader.EndOfStream)
                    {
                        string line=reader.ReadLine().Trim();int equals=line.IndexOf('=');
                        if(equals>0&&String.Equals(line.Substring(0,equals).Trim(),"Language",StringComparison.OrdinalIgnoreCase))
                        {string value=line.Substring(equals+1).Trim();return supported.FirstOrDefault(id=>String.Equals(id,value,StringComparison.OrdinalIgnoreCase))??"zh-Hans";}
                    }
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
            return "zh-Hans";
        }
        private static string FileSignature()
        {
            var names=new List<string>{Path.Combine(Folder,"selected.ini")};string locale=ReadLocale();
            names.Add(Path.Combine(Folder,"trainer",locale+".json"));names.Add(Path.Combine(Folder,"trainer","content-"+locale+".json"));
            names.Add(Path.Combine(Folder,"packs",locale+".arlp"));
            return String.Join(";",names.Select(name=>{var f=new FileInfo(name);return f.Exists?f.Length+":"+f.LastWriteTimeUtc.Ticks:"missing";}));
        }
        internal static bool ReloadIfChanged()
        {
            Initialize();
            // Keep confirmations and their typed/default choices stable until
            // the user closes the modal operation; retry on the next timer tick.
            if(Application.OpenForms.Cast<Form>().Any(form=>form.Visible&&form.Modal))return false;
            string next;
            try{next=FileSignature();}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
            if(next==signature)return false;
            LanguageState loaded;
            try{loaded=Load(ReadLocale());}
            catch(InvalidDataException){return false;}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
            catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
            lock(sync){current=loaded;signature=next;}
            Reapply();var changed=Changed;if(changed!=null)changed(null,EventArgs.Empty);return true;
        }
        private static LanguageState Load(string locale)
        {
            var state=new LanguageState {Locale=locale};
            ReadJson(Path.Combine(Folder,"trainer",locale+".json"),state.Ui);
            ReadPack(Path.Combine(Folder,"packs",locale+".arlp"),state.Content);
            ReadJson(Path.Combine(Folder,"trainer","content-"+locale+".json"),state.Content);
            foreach(var row in state.Ui)if(row.Key!=row.Value&&placeholder.IsMatch(row.Key))
            {
                var matches=placeholder.Matches(row.Key);var groups=new HashSet<string>();var pattern=new StringBuilder("\\A");int start=0;
                foreach(Match m in matches)
                {
                    pattern.Append(Regex.Escape(row.Key.Substring(start,m.Index-start)));string n="p"+m.Groups["n"].Value;
                    pattern.Append(groups.Add(n)?"(?<"+n+">.*?)":"\\k<"+n+">");start=m.Index+m.Length;
                }
                pattern.Append(Regex.Escape(row.Key.Substring(start))).Append("\\z");
                state.Templates.Add(new Template {Pattern=new Regex(pattern.ToString(),RegexOptions.Singleline|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(60)),Target=row.Value});
            }
            state.Templates=state.Templates.OrderByDescending(row=>row.Pattern.ToString().Length).ToList();
            // Public descriptions must prefer game semantics. The destructive
            // confirmation keyword is a UI instruction, never a stat fragment.
            foreach(var row in state.Ui.Where(pair=>pair.Key!="恢复").Concat(state.Content))
            {
                string source=row.Key;
                if(source.Length<2||source.Length>4096||source[0]=='@'||source==row.Value||placeholder.IsMatch(source)||source.IndexOf('%')>=0||!HasHan(source))continue;
                int key=(source[0]<<16)|source[1];List<KeyValuePair<string,string>> list;
                if(!state.Segments.TryGetValue(key,out list))state.Segments[key]=list=new List<KeyValuePair<string,string>>();
                list.Add(row);
            }
            foreach(var key in state.Segments.Keys.ToArray())state.Segments[key]=state.Segments[key].GroupBy(p=>p.Key,StringComparer.Ordinal).Select(g=>g.Last()).OrderByDescending(p=>p.Key.Length).ToList();
            foreach(var row in state.Ui)if(!state.Sources.ContainsKey(row.Value))state.Sources[row.Value]=row.Key;
            return state;
        }
        private static void ReadJson(string file,Dictionary<string,string> into)
        {
            if(!File.Exists(file))return;
            var serializer=new JavaScriptSerializer {MaxJsonLength=64*1024*1024,RecursionLimit=30};
            var rows=serializer.DeserializeObject(File.ReadAllText(file,Encoding.UTF8)) as Dictionary<string,object>;
            if(rows==null)throw new InvalidDataException("Invalid trainer language dictionary.");
            // Flat dictionaries are the shipped format. An explicit strings wrapper is
            // accepted for future catalogues without treating metadata as translations.
            object wrapped;if(rows.TryGetValue("strings",out wrapped)&&wrapped is Dictionary<string,object>)rows=(Dictionary<string,object>)wrapped;
            foreach(var row in rows)if(row.Value is string&&!String.IsNullOrEmpty(row.Key)&&!String.IsNullOrEmpty((string)row.Value))into[row.Key]=(string)row.Value;
        }
        private static void ReadPack(string file,Dictionary<string,string> into)
        {
            if(!File.Exists(file))return;
            using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var reader=new BinaryReader(stream,Encoding.Unicode))
            {
                if(stream.Length<12)return;byte[] magic=reader.ReadBytes(8);
                if(magic[0]!='A'||magic[1]!='R'||magic[2]!='L'||magic[3]!='P'||magic[4]!=1)return;
                uint count=reader.ReadUInt32();if(count>300000)throw new InvalidDataException("Invalid language pack entry count.");
                for(uint i=0;i<count;i++)
                {
                    if(stream.Length-stream.Position<8)throw new InvalidDataException("Truncated language pack.");
                    uint a=reader.ReadUInt32(),b=reader.ReadUInt32();
                    if(a==0||b==0||a>1000000||b>1000000||(long)(a+b)*2>stream.Length-stream.Position)throw new InvalidDataException("Invalid language pack entry.");
                    string source=Encoding.Unicode.GetString(reader.ReadBytes((int)a*2));string target=Encoding.Unicode.GetString(reader.ReadBytes((int)b*2));
                    if(!source.StartsWith("@",StringComparison.Ordinal)&&ValidDisplay(source,target))into[source]=target;
                }
            }
        }
        private static bool HasHan(string text){foreach(char c in text)if(c>='\u3400'&&c<='\u9fff')return true;return false;}
        private static bool ValidDisplay(string source,string target)
        {
            if(target.IndexOf('\uFFFD')>=0||target.IndexOf('\u2047')>=0||target.Any(value=>value>='\uE000'&&value<='\uF8FF'))return false;
            return Regex.Matches(target,@"\?\?").Count<=Regex.Matches(source,@"\?\?").Count;
        }
        private static string Exact(LanguageState state,string source)
        {string target;return state.Ui.TryGetValue(source,out target)||state.Content.TryGetValue(source,out target)?target:null;}
        private static string ContentExact(LanguageState state,string source)
        {string target;return state.Content.TryGetValue(source,out target)||source!="恢复"&&state.Ui.TryGetValue(source,out target)?target:null;}
        private static string Remember(LanguageState state,string source,string target)
        {if(source!=target)lock(state.Sources){if(state.Sources.Count<20000&&!state.Sources.ContainsKey(target))state.Sources[target]=source;}return target;}
        private static string Original(LanguageState state,string text)
        {if(text==null)return null;string source;lock(state.Sources)return state.Sources.TryGetValue(text,out source)?source:text;}

        internal static string T(string source)
        {
            if(String.IsNullOrEmpty(source))return source??"";Initialize();var state=current;if(state.Locale=="zh-Hans")return source;
            string target=Exact(state,source);if(target!=null)return Remember(state,source,target);
            if(!HasHan(source))return source;
            foreach(var template in state.Templates)
            {
                Match match;try{match=template.Pattern.Match(source);}catch(RegexMatchTimeoutException){continue;}
                if(match.Success)return Remember(state,source,placeholder.Replace(template.Target,m=>match.Groups["p"+m.Groups["n"].Value].Value));
            }
            if(source.IndexOf('\n')>=0)return Remember(state,source,String.Join("\n",source.Replace("\r\n","\n").Split('\n').Select(T)));
            string trim=source.Trim();if(trim!=source&&(target=Exact(state,trim))!=null)return Remember(state,source,source.Substring(0,source.IndexOf(trim,StringComparison.Ordinal))+target+source.Substring(source.IndexOf(trim,StringComparison.Ordinal)+trim.Length));
            return source;
        }
        internal static string F(string source,params object[] values)
        {string target=String.Format(CultureInfo.CurrentCulture,T(source),values);return Remember(current,String.Format(CultureInfo.CurrentCulture,source,values),target);}

        // Call only for public game definitions, descriptions and translated UI
        // explanations. Player/account/mail author strings must never enter this path.
        internal static string Content(string source)
        {
            if(String.IsNullOrEmpty(source))return source??"";Initialize();var state=current;if(state.Locale=="zh-Hans")return source;
            string target=ContentExact(state,source);if(target!=null)return target;
            lock(state.Cache){if(state.Cache.TryGetValue(source,out target))return target;}
            var output=new StringBuilder(source.Length+32);int offset=0;
            while(offset<source.Length)
            {
                bool found=false;List<KeyValuePair<string,string>> list;
                if(offset+1<source.Length&&state.Segments.TryGetValue((source[offset]<<16)|source[offset+1],out list))
                    foreach(var row in list)if(row.Key.Length<=source.Length-offset&&String.CompareOrdinal(source,offset,row.Key,0,row.Key.Length)==0)
                    {output.Append(row.Value);offset+=row.Key.Length;found=true;break;}
                if(!found)output.Append(source[offset++]);
            }
            target=output.ToString();
            lock(state.Cache){if(state.Cache.Count>=8192)state.Cache.Clear();state.Cache[source]=target;}
            return target;
        }
        internal static string Display(string source){string ui=T(source);return ui!=source?ui:Content(source);}

        internal static Font Font(float size,FontStyle style)
        {
            Initialize();
            lock(sync)
            {
                if(fonts==null)
                {
                    fonts=new PrivateFontCollection();string file=Path.Combine(Folder,"fonts","NotoSansCJK-Regular.ttc");
                    if(File.Exists(file)){AddFontResourceEx(file,0x10,IntPtr.Zero);fonts.AddFontFile(file);}
                    string fallback=Path.Combine(Folder,"fonts","AradReturnUI.ttf");
                    if(File.Exists(fallback))
                    {
                        fallbackRegistration=AddFontResourceEx(fallback,0x10,IntPtr.Zero);
                        if(fallbackRegistration==0)
                        {
                            // The font service may not read a sandboxed installation
                            // path. Embed it privately in this process; this changes
                            // no system fonts, policy or other application process.
                            byte[] bytes=File.ReadAllBytes(fallback);fallbackBytes=GCHandle.Alloc(bytes,GCHandleType.Pinned);uint count=0;
                            fallbackMemory=AddFontMemResourceEx(fallbackBytes.AddrOfPinnedObject(),(uint)bytes.Length,IntPtr.Zero,ref count);
                            if(fallbackMemory!=IntPtr.Zero)fallbackRegistration=(int)count;else fallbackBytes.Free();
                        }
                        fonts.AddFontFile(fallback);
                    }
                }
                if(installedFonts==null)using(var installed=new InstalledFontCollection())installedFonts=new HashSet<string>(installed.Families.Select(value=>value.Name),StringComparer.OrdinalIgnoreCase);
                string[] native=current.Locale=="ko-KR"?new[]{"Malgun Gothic","Noto Sans KR"}:
                    current.Locale=="ja-JP"?new[]{"Yu Gothic UI","Meiryo UI","MS UI Gothic"}:
                    current.Locale=="zh-Hant"?new[]{"Microsoft JhengHei UI","Microsoft JhengHei","Microsoft YaHei UI"}:
                    current.Locale=="en-US"?new[]{"Segoe UI","Arial"}:new[]{"Microsoft YaHei UI","Microsoft YaHei","Noto Sans SC"};
                // A private CFF collection is usable by GDI+ but GDI TextRenderer
                // can silently map its HFONT to SimSun. Prefer native Windows sans
                // faces for all WinForms controls so requested size stays legible.
                string available=native.FirstOrDefault(installedFonts.Contains);
                if(available!=null)return new Font(available,size,style,GraphicsUnit.Point);
                var portable=fonts.Families.FirstOrDefault(value=>value.Name=="Arad Return UI");
                if(portable!=null&&fallbackRegistration>0)return new Font(portable,size,style,GraphicsUnit.Point);
                // A damaged/missing optional asset must not request the CFF
                // family known to resolve to a different GDI face on Windows.
                return new Font("Segoe UI",size,style,GraphicsUnit.Point);
            }
        }
        internal static Font Font(float size){return Font(size,FontStyle.Regular);}
        internal static Font FallbackFont(float size,FontStyle style)
        {
            using(var initialize=Font(size,style)){}
            lock(sync){var family=fonts.Families.FirstOrDefault(value=>value.Name=="Arad Return UI");return family!=null&&fallbackRegistration>0?new Font(family,size,style,GraphicsUnit.Point):new Font("Segoe UI",size,style,GraphicsUnit.Point);}
        }
        internal static int FallbackFontRegistrationCount {get{return fallbackRegistration;}}
        internal static bool FallbackFontLoadedFromMemory {get{return fallbackMemory!=IntPtr.Zero;}}

        internal static void Preserve(Control control)
        {if(control!=null)captions.GetOrCreateValue(control).Preserved=true;}
        internal static void SetText(Control control,string source)
        {
            if(control==null||control.IsDisposed)return;Initialize();var state=captions.GetOrCreateValue(control);
            state.Source=source??"";state.Rendered=T(state.Source);state.Updating=true;
            try{control.Text=state.Rendered;}finally{state.Updating=false;}
        }
        internal static void Attach(Control root)
        {
            if(root==null||root.IsDisposed)return;Initialize();
            lock(roots)
            {
                bool covered=false;
                foreach(var weak in roots.ToArray())
                {
                    var existing=weak.Target as Control;
                    if(existing==null||existing.IsDisposed){roots.Remove(weak);continue;}
                    if(Object.ReferenceEquals(existing,root)||existing.Contains(root))covered=true;
                    else if(root.Contains(existing))roots.Remove(weak);
                }
                if(!covered)roots.Add(new WeakReference(root));
            }
            AttachTree(root);
            if(timer==null)
            {
                timer=new System.Windows.Forms.Timer {Interval=900};
                timer.Tick+=(sender,args)=>{long now=DateTime.UtcNow.Ticks;if(now-lastProbe>TimeSpan.TicksPerMillisecond*800){lastProbe=now;ReloadIfChanged();}RefreshToolTips();};timer.Start();
            }
        }
        private static void AttachTree(Control control)
        {
            if(control==null||control.IsDisposed)return;var state=captions.GetOrCreateValue(control);
            if(!state.Attached)
            {
                state.Attached=true;state.Source=Original(current,control.Text);state.AccessibleSource=Original(current,control.AccessibleName);state.DescriptionSource=Original(current,control.AccessibleDescription);
                control.ControlAdded+=(sender,args)=>AttachTree(args.Control);
                control.TextChanged+=(sender,args)=>{if(!state.Updating&&!state.Preserved&&!SkipText(control)){state.Source=Original(current,control.Text);Apply(control,state);}};
                var combo=control as ComboBox;
                if(combo!=null)
                {
                    combo.FormattingEnabled=true;
                    combo.Format+=(sender,args)=>{if(!state.Preserved&&(args.ListItem is Choice||args.ListItem is string))args.Value=Display(Convert.ToString(args.Value,CultureInfo.CurrentCulture));};
                    if(combo.DropDownStyle==ComboBoxStyle.DropDownList&&combo.DrawMode==DrawMode.Normal&&!(combo is DataGridViewComboBoxEditingControl))
                    {
                        // Native DropDownList caches inserted display strings. Paint
                        // from the raw item on each draw so a locale change never
                        // rebuilds Items or raises a selection event on dirty forms.
                        combo.DrawMode=DrawMode.OwnerDrawFixed;
                        Action size=()=>combo.ItemHeight=Math.Max(18,(int)Math.Ceiling(combo.Font.GetHeight())+4);size();
                        combo.FontChanged+=(sender,args)=>size();
                        combo.DrawItem+=(sender,args)=>
                        {
                            args.DrawBackground();
                            if(args.Index>=0&&args.Index<combo.Items.Count)
                            {
                                var box=args.Bounds;box.X+=5;box.Width=Math.Max(1,box.Width-10);
                                TextRenderer.DrawText(args.Graphics,combo.GetItemText(combo.Items[args.Index]),combo.Font,box,args.ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                            }
                            args.DrawFocusRectangle();
                        };
                    }
                }
                var list=control as ListBox;
                if(list!=null&&list.DrawMode==DrawMode.Normal)
                {list.FormattingEnabled=true;list.Format+=(sender,args)=>{if(!state.Preserved&&(args.ListItem is Choice||args.ListItem is string))args.Value=Display(Convert.ToString(args.Value,CultureInfo.CurrentCulture));};}
                var grid=control as DataGridView;
                if(grid!=null){grid.ColumnAdded+=(sender,args)=>ApplyColumns(grid,state);ApplyColumns(grid,state);}
                if(control.ContextMenuStrip!=null)ApplyMenu(control.ContextMenuStrip.Items);
            }
            Apply(control,state);foreach(Control child in control.Controls)AttachTree(child);
        }
        private static bool SkipText(Control control)
        {return control is TextBoxBase||control is UpDownBase||control is ComboBox||control is ListBox||control is DateTimePicker||control is DataGridView;}
        private static void Apply(Control control,CaptionState state)
        {
            if(control.IsDisposed||state.Preserved||state.Updating)return;state.Updating=true;
            try
            {
                if(!SkipText(control)){state.Rendered=T(state.Source);if(control.Text!=state.Rendered)control.Text=state.Rendered;}
                if(!String.IsNullOrEmpty(state.AccessibleSource))control.AccessibleName=T(state.AccessibleSource);
                if(!String.IsNullOrEmpty(state.DescriptionSource))control.AccessibleDescription=T(state.DescriptionSource);
                var currentFont=control.Font;
                string key=current.Locale+":"+currentFont.SizeInPoints.ToString(CultureInfo.InvariantCulture)+":"+(int)currentFont.Style;Font replacement;
                lock(sync){if(!fontCache.TryGetValue(key,out replacement))fontCache[key]=replacement=Font(currentFont.SizeInPoints,currentFont.Style);}
                if(control.Font.FontFamily.Name!=replacement.FontFamily.Name)control.Font=replacement;
                if(control is TextBox&&control.IsHandleCreated)
                {
                    var input=(TextBox)control;string cue=state.AccessibleSource;
                    if(!String.IsNullOrEmpty(cue)&&(cue.StartsWith("搜索",StringComparison.Ordinal)||cue.StartsWith("查找",StringComparison.Ordinal)||cue.StartsWith("筛选",StringComparison.Ordinal)))SendCue(input.Handle,0x1501,new IntPtr(1),T(cue));
                }
                var navigation=control as NavigationButton;if(navigation!=null)navigation.Invalidate();
                var grid=control as DataGridView;if(grid!=null)ApplyColumns(grid,state);
                var strip=control as ToolStrip;if(strip!=null)ApplyMenu(strip.Items);
            }
            finally{state.Updating=false;}
        }
        private static void ApplyColumns(DataGridView grid,CaptionState state)
        {
            foreach(DataGridViewColumn column in grid.Columns)
            {
                string source,rendered;
                if(!state.ColumnSources.TryGetValue(column,out source)||state.ColumnRendered.TryGetValue(column,out rendered)&&column.HeaderText!=rendered&&column.HeaderText!=source)state.ColumnSources[column]=source=Original(current,column.HeaderText);
                column.HeaderText=T(source);state.ColumnRendered[column]=column.HeaderText;var button=column as DataGridViewButtonColumn;
                if(button!=null&&button.UseColumnTextForButtonValue)
                {
                    object key=column.HeaderCell;
                    if(!state.ColumnSources.TryGetValue(key,out source)||state.ColumnRendered.TryGetValue(key,out rendered)&&button.Text!=rendered&&button.Text!=source)state.ColumnSources[key]=source=Original(current,button.Text);
                    button.Text=T(source);state.ColumnRendered[key]=button.Text;
                }
            }
            grid.Invalidate();
        }
        private static readonly Dictionary<ToolStripItem,KeyValuePair<string,string>> menuCaptions=new Dictionary<ToolStripItem,KeyValuePair<string,string>>();
        private static void ApplyMenu(ToolStripItemCollection items)
        {
            foreach(ToolStripItem item in items)
            {
                KeyValuePair<string,string> saved;if(!menuCaptions.TryGetValue(item,out saved)||item.Text!=saved.Value)saved=new KeyValuePair<string,string>(Original(current,item.Text),item.Text);
                string text=T(saved.Key);menuCaptions[item]=new KeyValuePair<string,string>(saved.Key,text);item.Text=text;
                var menu=item as ToolStripDropDownItem;if(menu!=null)ApplyMenu(menu.DropDownItems);
            }
        }
        internal static void LocalizeGrid(DataGridView grid,string[] uiColumns,string[] contentColumns)
        {
            if(grid==null)return;var state=grids.GetOrCreateValue(grid);
            if(uiColumns!=null)foreach(string key in uiColumns)state.Ui.Add(key);
            if(contentColumns!=null)foreach(string key in contentColumns)state.Content.Add(key);
            if(!state.Attached)
            {
                state.Attached=true;
                grid.CellFormatting+=(sender,args)=>
                {
                    if(args.RowIndex<0||args.ColumnIndex<0)return;
                    var column=grid.Columns[args.ColumnIndex];var comboCell=grid.Rows[args.RowIndex].Cells[args.ColumnIndex] as DataGridViewComboBoxCell;bool combo=comboCell!=null;
                    if(!combo&&!column.ReadOnly&&!grid.ReadOnly&&!grid.Rows[args.RowIndex].Cells[args.ColumnIndex].ReadOnly)return;
                    if(!state.Content.Contains(column.Name)&&!state.Ui.Contains(column.Name))return;
                    string original=args.Value as string;
                    if(comboCell!=null&&!String.IsNullOrEmpty(comboCell.ValueMember)&&!String.IsNullOrEmpty(comboCell.DisplayMember))
                        foreach(object option in comboCell.Items)
                        {object value=Member(option,comboCell.ValueMember);if(Object.Equals(value,args.Value)){original=Convert.ToString(Member(option,comboCell.DisplayMember),CultureInfo.CurrentCulture);break;}}
                    if(original==null)return;
                    if(state.Content.Contains(column.Name)){args.Value=Display(original);args.FormattingApplied=true;}
                    else if(state.Ui.Contains(column.Name)){args.Value=T(original);args.FormattingApplied=true;}
                };
                grid.EditingControlShowing+=(sender,args)=>
                {
                    var combo=args.Control as ComboBox;if(combo==null||!state.EditingCombos.Add(combo))return;
                    combo.FormattingEnabled=true;
                    combo.Format+=(formatSender,formatArgs)=>
                    {
                        if(grid.CurrentCell==null)return;string key=grid.Columns[grid.CurrentCell.ColumnIndex].Name;
                        string original=Convert.ToString(String.IsNullOrEmpty(combo.DisplayMember)?formatArgs.ListItem:Member(formatArgs.ListItem,combo.DisplayMember),CultureInfo.CurrentCulture);
                        if(state.Content.Contains(key))formatArgs.Value=Display(original);
                        else if(state.Ui.Contains(key))formatArgs.Value=T(original);
                    };
                };
            }
        }
        private static object Member(object value,string name)
        {if(value==null)return null;var property=TypeDescriptor.GetProperties(value)[name];return property==null?null:property.GetValue(value);}
        internal static void AttachToolTip(ToolTip toolTip,Control root)
        {
            if(toolTip==null||root==null)return;tips.Add(new TipBinding {Tip=toolTip,Root=new WeakReference(root)});RefreshToolTips();
        }
        private static void RefreshToolTips()
        {
            foreach(var binding in tips.ToArray())
            {
                var root=binding.Root.Target as Control;if(root==null||root.IsDisposed){tips.Remove(binding);continue;}
                try{TranslateTip(binding,root);}catch(ObjectDisposedException){tips.Remove(binding);}
            }
        }
        private static void TranslateTip(TipBinding binding,Control control)
        {
            string actual=binding.Tip.GetToolTip(control);KeyValuePair<string,string> saved;
            if(!binding.Captions.TryGetValue(control,out saved)||actual!=saved.Value)saved=new KeyValuePair<string,string>(Original(current,actual),actual);
            // Exact/template UI matching preserves embedded player data.
            string translated=T(saved.Key);if(actual!=translated)binding.Tip.SetToolTip(control,translated);
            binding.Captions[control]=new KeyValuePair<string,string>(saved.Key,translated);
            foreach(Control child in control.Controls)TranslateTip(binding,child);
        }
        private static void Reapply()
        {
            lock(roots)
            {
                foreach(var weak in roots.ToArray())
                {
                    var root=weak.Target as Control;if(root==null||root.IsDisposed){roots.Remove(weak);continue;}
                    if(root.InvokeRequired){root.BeginInvoke((Action)(()=>AttachTree(root)));continue;}
                    root.SuspendLayout();try{AttachTree(root);root.Invalidate(true);}finally{root.ResumeLayout(true);}
                }
            }
            RefreshToolTips();
        }
    }
}
