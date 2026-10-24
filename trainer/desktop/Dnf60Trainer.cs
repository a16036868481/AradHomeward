// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("阿拉德·归途 · 修改器")]
[assembly: AssemblyDescription("阿拉德·归途 Windows 原生桌面角色与物品管理工具")]
[assembly: AssemblyProduct("阿拉德·归途 · 修改器")]
[assembly: AssemblyCompany("eirros")]
[assembly: AssemblyCopyright("Copyright (c) 2026 eirros. All rights reserved.")]
[assembly: AssemblyVersion("2.6.0.15")]
[assembly: AssemblyFileVersion("2.6.0.15")]

namespace Dnf60Desktop
{
    internal static class Program
    {
        internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int n);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h,int message,IntPtr w,IntPtr l);
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            TrainerLanguage.Initialize();
            ServicePointManager.DefaultConnectionLimit=12;
            if (args.Contains("--self-check"))
            {
                try { NativeCheck.Run(); return 0; }
                catch (Exception ex) { File.WriteAllText(Path.Combine(Root,"setup","native-desktop-check-error.txt"),ex.ToString(),Encoding.UTF8);return 1; }
            }
            bool created;
            using (var instance = new Mutex(true,"Local\\AradReturn_NativeTrainer_Local",out created))
            {
                if (!created)
                {
                    foreach (var p in Process.GetProcesses())
                    {
                        try
                        {
                            if (p.Id!=Process.GetCurrentProcess().Id && String.Equals(p.MainModule.FileName,Application.ExecutablePath,StringComparison.OrdinalIgnoreCase) && p.MainWindowHandle!=IntPtr.Zero)
                            { if(args.Contains("--manage"))PostMessage(p.MainWindowHandle,0x8001,IntPtr.Zero,IntPtr.Zero);if(args.Contains("--profile-selected"))PostMessage(p.MainWindowHandle,0x8003,IntPtr.Zero,IntPtr.Zero);ShowWindow(p.MainWindowHandle,9);SetForegroundWindow(p.MainWindowHandle);break; }
                        }
                        catch { }
                    }
                    return 0;
                }
                Application.Run(new TrainerWindow(args.Contains("--manage")));
                instance.ReleaseMutex();
            }
            return 0;
        }
    }

    internal static class J
    {
        internal static Dictionary<string,object> O(object v) { return (Dictionary<string,object>)v; }
        internal static object V(Dictionary<string,object> o,string k) { object v;return o.TryGetValue(k,out v)?v:null; }
        internal static string S(Dictionary<string,object> o,string k) { return Convert.ToString(V(o,k),CultureInfo.InvariantCulture); }
        internal static int N(Dictionary<string,object> o,string k) { return Convert.ToInt32(V(o,k),CultureInfo.InvariantCulture); }
        internal static bool B(Dictionary<string,object> o,string k) { return Convert.ToBoolean(V(o,k),CultureInfo.InvariantCulture); }
        internal static List<Dictionary<string,object>> Rows(object o) { return ((IEnumerable)o).Cast<object>().Select(O).ToList(); }
        internal static Dictionary<string,object> D(params object[] args)
        { var d=new Dictionary<string,object>();for(int i=0;i<args.Length;i+=2)d.Add((string)args[i],args[i+1]);return d; }
    }

    internal sealed class LocalApi
    {
        private string token;
        internal bool HasCharacterAttributes {get;private set;}
        internal bool HasManagement {get;private set;}
        private const string Base="http://127.0.0.1:8787";
        internal object Get(string path) { return Request(path,null,false); }
        internal Dictionary<string,object> Bootstrap()
        {
            var b=J.O(Get("/api/bootstrap"));
            if(!String.Equals(J.S(b,"root"),Program.Root,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(TrainerLanguage.T("连接的修改服务不属于当前游戏目录。"));
            token=J.S(b,"token");HasCharacterAttributes=J.N(b,"apiVersion")>=2;HasManagement=J.N(b,"apiVersion")>=3;return b;
        }
        internal Dictionary<string,object> Post(string path,Dictionary<string,object> body)
        { if(String.IsNullOrEmpty(token))Bootstrap();return J.O(Request(path,body,true)); }
        private object Request(string path,Dictionary<string,object> body,bool retry)
        {
            var serializer=new JavaScriptSerializer { MaxJsonLength=16*1024*1024 };
            var req=(HttpWebRequest)WebRequest.Create(Base+path);
            req.Proxy=null;req.Timeout=body==null?18000:190000;req.ReadWriteTimeout=req.Timeout;req.KeepAlive=false;
            if(body!=null)
            {
                req.Method="POST";req.ContentType="application/json; charset=utf-8";req.Headers.Add("X-DNF60-Token",token);
                byte[] bytes=Encoding.UTF8.GetBytes(serializer.Serialize(body));req.ContentLength=bytes.Length;
                using(var s=req.GetRequestStream())s.Write(bytes,0,bytes.Length);
            }
            try
            {
                using(var response=req.GetResponse())
                using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))return serializer.DeserializeObject(reader.ReadToEnd());
            }
            catch(WebException ex)
            {
                var response=ex.Response as HttpWebResponse;
                if(response!=null)
                {
                    var code=response.StatusCode;
                    string error;
                    using(response)
                    using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))error=reader.ReadToEnd();
                    if(code==HttpStatusCode.Forbidden && body!=null && retry)
                    { string previous=token;Bootstrap();if(token!=previous)return Request(path,body,false); }
                    try { error=J.S(J.O(serializer.DeserializeObject(error)),"error"); }catch { }
                    throw new InvalidOperationException(String.IsNullOrWhiteSpace(error)?TrainerLanguage.T("游戏服务拒绝了本次操作。"):error,ex);
                }
                throw new InvalidOperationException(TrainerLanguage.T("本机游戏服务未连接。请在“服务管理”中启动游戏服务。"),ex);
            }
        }
        internal byte[] Icon(int id)
        {
            var req=(HttpWebRequest)WebRequest.Create(Base+"/icons/"+id+".png");req.Proxy=null;req.Timeout=12000;req.ReadWriteTimeout=12000;
            try { using(var response=req.GetResponse())using(var s=response.GetResponseStream())using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();} }
            catch(WebException){return null;}
        }
        internal byte[] Download(string path)
        {var req=(HttpWebRequest)WebRequest.Create(Base+path);req.Proxy=null;req.Timeout=90000;req.ReadWriteTimeout=90000;using(var r=req.GetResponse())using(var s=r.GetResponseStream())using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}}
    }

    internal static class Theme
    {
        [DllImport("user32.dll",EntryPoint="SendMessageW",CharSet=CharSet.Unicode)] private static extern IntPtr SendCue(IntPtr h,int message,IntPtr w,string text);
        internal static readonly Color Bg=Color.FromArgb(245,247,251),Side=Color.FromArgb(18,25,38),Panel=Color.White,Field=Color.White,Line=Color.FromArgb(226,232,241),Text=Color.FromArgb(32,43,60),Muted=Color.FromArgb(112,125,146),Gold=Color.FromArgb(43,105,232),Green=Color.FromArgb(29,142,108),Red=Color.FromArgb(205,68,79),Selected=Color.FromArgb(232,240,255);
        internal static readonly string[] Rarities={"普通","高级","稀有","神器","史诗","勇者","传说"};
        internal static readonly string[] Modes={"普通强化","体力增幅","精神增幅","力量增幅","智力增幅"};
        internal static Color Rarity(int n) { Color[] c={Text,Color.FromArgb(42,106,197),Color.FromArgb(126,73,193),Color.FromArgb(183,59,148),Color.FromArgb(177,113,18),Color.FromArgb(192,91,30),Color.FromArgb(174,73,31)};return n>=0&&n<c.Length?c[n]:Text; }
        internal static Label Label(string text,int size,Color color) { return TrainerDisplay.Attach(new Label {Text=text,ForeColor=color,Font=TrainerLanguage.Font(size,FontStyle.Regular),AutoSize=true,Margin=new Padding(0,0,8,0)}); }
        internal static Button Button(string text,EventHandler click,bool primary)
        {var b=new ActionButton {Text=text,Primary=primary,AutoSize=true,ForeColor=primary?Color.White:Text,Danger=text.StartsWith("删除")||text.StartsWith("清空当前角色邮箱")||text.StartsWith("移除所选契约")||text=="关闭此活动"||text=="一键关闭全部"||text.StartsWith("恢复整个数据库")||text.StartsWith("恢复当前角色")||text.StartsWith("停止游戏服务")};b.Click+=click;TrainerLanguage.Attach(b);return b;}
        internal static TextBox TextBox(string name,string value)
        {var t=new TextBox {Name=name,AccessibleName=name,Text=value,BackColor=Field,ForeColor=Text,BorderStyle=BorderStyle.FixedSingle,Margin=new Padding(0,3,0,3)};if(name.StartsWith("搜索")||name.StartsWith("查找")||name.StartsWith("筛选"))t.HandleCreated+=(s,e)=>SendCue(t.Handle,0x1501,new IntPtr(1),TrainerLanguage.T(name));TrainerLanguage.Attach(t);return t;}
        internal static NumericUpDown Number(string name,int min,int max,int value,int width)
        { return TrainerDisplay.Attach(new NumericUpDown {Name=name,AccessibleName=name,Minimum=min,Maximum=max,Value=value,Width=width,BackColor=Field,ForeColor=Text,BorderStyle=BorderStyle.FixedSingle,ThousandsSeparator=true,Margin=new Padding(0,3,5,3)}); }
        internal static ComboBox Combo(string name,int width)
        { return TrainerDisplay.Attach(new ComboBox {Name=name,AccessibleName=name,Width=width,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Field,ForeColor=Text,FlatStyle=FlatStyle.Standard,Margin=new Padding(0,3,6,3)}); }
        internal static DataGridView Grid(string name)
        {
            var g=new BufferedGrid {Name=name,AccessibleName=name,Dock=DockStyle.Fill,BackgroundColor=Field,BorderStyle=BorderStyle.None,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,AutoGenerateColumns=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=true,EnableHeadersVisualStyles=false,GridColor=Line,EditMode=DataGridViewEditMode.EditOnEnter,ClipboardCopyMode=DataGridViewClipboardCopyMode.EnableWithoutHeaderText,AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None};
            g.RowTemplate.Height=44;g.ColumnHeadersHeight=38;g.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;g.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle {BackColor=Color.FromArgb(249,250,253),ForeColor=Muted,SelectionBackColor=Color.FromArgb(249,250,253),Font=TrainerLanguage.Font(9)};
            g.DefaultCellStyle=new DataGridViewCellStyle {BackColor=Field,ForeColor=Text,SelectionBackColor=Selected,SelectionForeColor=Text,Font=TrainerLanguage.Font(9),Padding=new Padding(4,1,4,1),NullValue=""};
            g.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(251,252,254);
            g.DataError+=(s,e)=>{e.ThrowException=false;};
            g.CellPainting+=(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex>=0&&g.Columns[e.ColumnIndex] is DataGridViewImageColumn&&e.Value==null){e.PaintBackground(e.CellBounds,true);TextRenderer.DrawText(e.Graphics,TrainerLanguage.T("缺图"),g.Font,e.CellBounds,Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);e.Handled=true;}};
            TrainerDisplay.ConfigureGrid(g,name);return g;
        }
        internal static void TextColumn(DataGridView g,string name,string title,int width,bool fill,bool edit)
        { var c=new DataGridViewTextBoxColumn {Name=name,HeaderText=title,Width=width,ReadOnly=!edit,SortMode=DataGridViewColumnSortMode.NotSortable};if(fill)c.AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;g.Columns.Add(c); }
        internal static void CheckColumn(DataGridView g,string name)
        { var c=new DataGridViewCheckBoxColumn {Name=name,HeaderText=TrainerLanguage.T("选择"),Width=52,FalseValue=false,TrueValue=true,ReadOnly=false};c.HeaderCell.Style.Padding=Padding.Empty;g.Columns.Add(c);g.CurrentCellDirtyStateChanged+=(s,e)=>{if(g.IsCurrentCellDirty&&g.CurrentCell is DataGridViewCheckBoxCell)g.CommitEdit(DataGridViewDataErrorContexts.Commit);}; }
        internal static void IconColumn(DataGridView g)
        { var c=new DataGridViewImageColumn {Name="icon",HeaderText=TrainerLanguage.T("图标"),Width=52,ReadOnly=true,ImageLayout=DataGridViewImageCellLayout.Normal,SortMode=DataGridViewColumnSortMode.NotSortable};c.HeaderCell.Style.Padding=Padding.Empty;c.DefaultCellStyle.NullValue=null;g.Columns.Add(c); }
        internal static string Category(Dictionary<string,object> i)
        { switch(J.S(i,"category")){case "equipment":return "装备";case "material":return "材料";case "consumable":return "消耗品";case "quest":return "任务物品";case "avatar":return "时装";case "creature":return "宠物";default:return "其他";} }
    }
    internal sealed class BufferedGrid : DataGridView { internal BufferedGrid(){DoubleBuffered=true;} }
    internal sealed class Choice
    {
        internal int Id;internal string Text;
        internal Choice(int id,string text){Id=id;Text=text;}
        public override string ToString(){return Text;}
    }
    internal sealed class RoleEntry
    {
        internal Dictionary<string,object> Data;internal string Career;
        public override string ToString(){return J.S(Data,"name")+" · Lv."+J.N(Data,"level")+" · "+J.S(Data,"account_name");}
    }
    internal sealed class CartEntry
    {
        internal Dictionary<string,object> Item;internal int Count,Type,Level,Attribute;
        internal int Id {get{return J.N(Item,"id");}}
    }

    internal sealed partial class TrainerWindow : Form
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h,int key,ref int value,int size);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h,int message,IntPtr wParam,string text);
        private readonly LocalApi api=new LocalApi();
        private readonly ConcurrentDictionary<int,Bitmap> images=new ConcurrentDictionary<int,Bitmap>();
        private readonly Dictionary<int,CartEntry> cart=new Dictionary<int,CartEntry>();
        private readonly Dictionary<string,NumericUpDown> characterFields=new Dictionary<string,NumericUpDown>();
        private readonly List<Dictionary<string,object>> careers=new List<Dictionary<string,object>>();
        private readonly ListBox roles=new ListBox();
        private readonly TextBox roleSearch=Theme.TextBox("搜索账号或角色",""),itemSearch=Theme.TextBox("搜索物品名称或编号","");
        private readonly ComboBox category=Theme.Combo("物品类别",135),rarity=Theme.Combo("物品稀有度",125),job=Theme.Combo("基础职业",210),career=Theme.Combo("转职与觉醒",310);
        private readonly NumericUpDown gold=Theme.Number("角色金币",0,2147483647,0,180),cera=Theme.Number("商城点券",0,2147483647,0,180),level=Theme.Number("角色等级",1,60,1,90);
        private readonly DataGridView catalog=Theme.Grid("物品目录"),cartGrid=Theme.Grid("已选物品"),bag=Theme.Grid("背包与穿戴"),history=Theme.Grid("修改记录");
        private readonly Label title=Theme.Label(TrainerLanguage.T("选择要管理的角色"),19,Theme.Text),meta=Theme.Label(TrainerLanguage.T("账号、职业与等级"),9,Theme.Muted),roleState=Theme.Label(TrainerLanguage.T("未选择"),10,Theme.Muted),serviceState=Theme.Label(TrainerLanguage.T("正在连接本机服务…"),9,Theme.Muted),notice=Theme.Label(TrainerLanguage.T("所有修改前均保存角色快照。在线修改请在城镇进行。"),9,Theme.Muted),status=new Label(),pageInfo=Theme.Label("",9,Theme.Muted),cartCount=Theme.Label(TrainerLanguage.T("已选物品  0"),11,Theme.Gold),inventoryInfo=Theme.Label(TrainerLanguage.T("请选择角色"),10,Theme.Muted),careerPreview=Theme.Label("",14,Theme.Gold);
        private readonly PageDeck tabs=new PageDeck();
        private readonly Panel work=new Panel {Dock=DockStyle.Fill};
        private AdminPanel admin;
        private readonly ItemHoverService itemHover;
        private readonly Button previous,next,give;
        private readonly NumericUpDown batchCount=Theme.Number("统一物品数量",1,1000000,1,100),batchLevel=Theme.Number("发放装备强化或增幅等级",0,31,0,80),batchValue=Theme.Number("发放装备红字属性值",0,65535,0,100),bagCount=Theme.Number("背包物品数量",1,1000000,1,100),bagLevel=Theme.Number("背包装备强化或增幅等级",0,31,0,80),bagValue=Theme.Number("背包装备红字属性值",0,65535,0,100);
        private readonly ComboBox batchType=Theme.Combo("发放装备模式",135),bagType=Theme.Combo("背包装备模式",135);
        private readonly System.Windows.Forms.Timer searchTimer=new System.Windows.Forms.Timer {Interval=280},statusTimer=new System.Windows.Forms.Timer {Interval=3500};
        private Dictionary<string,object> selected;
        private bool binding,busy,statusBusy,loaded,closeAfterGameExit;
        private int page=1,pages=1,searchVersion,roleVersion,lastCatalogTotal;

        internal TrainerWindow(bool openManagement=false)
        {
            Text=TrainerLanguage.T("阿拉德·归途 · 修改器");Name="AradReturnTrainer";AccessibleName=Text;Font=TrainerLanguage.Font(9);BackColor=Theme.Bg;ForeColor=Theme.Text;
            AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);MinimumSize=new Size(1080,700);
            var area=Screen.PrimaryScreen.WorkingArea;Size=new Size(Math.Min(1320,area.Width-40),Math.Min(880,area.Height-50));StartPosition=FormStartPosition.CenterScreen;
            string icon=Path.Combine(Program.Root,"DNF60_ACT12","ACT12","DNF.ico");if(File.Exists(icon))Icon=new Icon(icon);
            previous=Theme.Button("上一页",(s,e)=>{if(page>1){page--;LoadCatalog();}},false);
            next=Theme.Button("下一页",(s,e)=>{if(page<pages){page++;LoadCatalog();}},false);
            give=Theme.Button("发放所选物品",(s,e)=>RunAction(GiveItems,TrainerLanguage.T("正在发放物品…")),true);
            itemHover=new ItemHoverService(api);startWithManagement=openManagement;BuildLayout();
            TrainerLanguage.Preserve(title);TrainerLanguage.Preserve(meta);TrainerLanguage.Preserve(inventoryInfo);TrainerLanguage.Preserve(give);give.Text=TrainerLanguage.T("发放所选物品");
            TrainerLanguage.Attach(this);TrainerLanguage.Attach(rolePicker);TrainerLanguage.Attach(cartDialog);TrainerLanguage.AttachToolTip(tips,this);
            TrainerLanguage.Changed+=RefreshLanguage;Disposed+=(sender,args)=>TrainerLanguage.Changed-=RefreshLanguage;
            itemHover.Attach(catalog,r=>ItemTipTarget.Catalogue(r.Tag as Dictionary<string,object>));
            itemHover.Attach(cartGrid,r=>ItemTipTarget.Pending(r.Tag as CartEntry));
            itemHover.Attach(bag,r=>ItemTipTarget.Inventory(r.Tag as Dictionary<string,object>));
            searchTimer.Tick+=(s,e)=>{searchTimer.Stop();page=1;LoadCatalog();};
            itemSearch.TextChanged+=(s,e)=>{if(loaded){searchTimer.Stop();searchTimer.Start();}};
            category.SelectedIndexChanged+=(s,e)=>{if(loaded){page=1;LoadCatalog();}};
            rarity.SelectedIndexChanged+=(s,e)=>{if(loaded){page=1;LoadCatalog();}};
            roles.SelectedIndexChanged+=(s,e)=>{if(!binding&&roles.SelectedItem!=null)SelectRole(J.N(((RoleEntry)roles.SelectedItem).Data,"character_id"));};
            job.SelectedIndexChanged+=(s,e)=>{if(!binding)LoadCareerChoices(-1);};
            career.SelectedIndexChanged+=(s,e)=>UpdateCareerPreview();
            statusTimer.Tick+=async(s,e)=>await RefreshStatus();
            Shown+=async(s,e)=>await Initialize();
            FormClosing+=(s,e)=>{if(busy){e.Cancel=true;SetStatus(closeAfterGameExit?TrainerLanguage.T("游戏已退出，正在完成当前保存后自动关闭…"):TrainerLanguage.T("正在完成当前操作，请稍后再关闭。"),!closeAfterGameExit);}else{statusTimer.Stop();searchTimer.Stop();}};
            Disposed+=(s,e)=>{itemHover.Dispose();if(rolePicker!=null)rolePicker.Dispose();if(cartDialog!=null)cartDialog.Dispose();tips.Dispose();foreach(var image in images.Values)image.Dispose();};
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);try{int dark=0;DwmSetWindowAttribute(Handle,20,ref dark,4);}catch { }}
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x8003){accountSwitchPending=true;if(loaded&&!busy)BeginInvoke((Action)FollowSelectedAccount);}
            if(m.Msg==0x8001&&admin!=null)OpenControl();
            if(m.Msg==0x8002)
            {
                closeAfterGameExit=true;
                SetStatus(TrainerLanguage.T("游戏已退出，正在完成当前保存后自动关闭…"),false);
                if(!busy)BeginInvoke((Action)Close);
            }
            base.WndProc(ref m);
        }
        private static FlowLayoutPanel Flow(DockStyle dock,int height)
        { return new FlowLayoutPanel {Dock=dock,Height=Math.Max(46,height),FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Padding=new Padding(0,3,0,3),BackColor=Color.Transparent}; }
        private static Panel PagePanel(){return new Panel {Dock=DockStyle.Fill,BackColor=Theme.Bg,Padding=new Padding(0)};}
        private void BuildLayout(){BuildModernLayout();}
        private void AddTab(string name,Panel panel){tabs.AddPage(name,panel);}
        private void BuildCatalog()
        {
            var panel=PagePanel();AddTab("物品目录",panel);
            var top=Flow(DockStyle.Top,45);itemSearch.Width=300;itemSearch.Margin=new Padding(0,3,8,3);itemSearch.HandleCreated+=(s,e)=>SendMessage(itemSearch.Handle,0x1501,new IntPtr(1),TrainerLanguage.T("搜索物品名称 / 编号"));top.Controls.Add(itemSearch);
            category.Items.AddRange(new object[]{new Choice(-1,"全部类别"),new Choice(0,"装备"),new Choice(1,"材料"),new Choice(2,"消耗品"),new Choice(3,"任务物品"),new Choice(4,"时装"),new Choice(5,"宠物"),new Choice(6,"其他")});category.SelectedIndex=0;
            rarity.Items.Add(new Choice(-1,"全部稀有度"));for(int i=0;i<Theme.Rarities.Length;i++)rarity.Items.Add(new Choice(i,Theme.Rarities[i]));rarity.SelectedIndex=0;
            top.Controls.Add(category);top.Controls.Add(rarity);top.Controls.Add(Theme.Button("搜索",(s,e)=>{page=1;LoadCatalog();},false));
            var choose=Flow(DockStyle.Top,35);
            choose.Controls.Add(Theme.Button("全选本页",(s,e)=>CheckPage(true),false));choose.Controls.Add(Theme.Button("选择高亮行",(s,e)=>CheckHighlighted(),false));
            var hint=Theme.Label(TrainerLanguage.T("悬停查看属性；支持多选，搜索保留已选物品"),9,Theme.Muted);hint.Margin=new Padding(12,8,0,0);choose.Controls.Add(hint);
            Theme.CheckColumn(catalog,"pick");Theme.IconColumn(catalog);Theme.TextColumn(catalog,"id",TrainerLanguage.T("编号"),82,false,false);Theme.TextColumn(catalog,"name",TrainerLanguage.T("物品名称"),200,true,false);Theme.TextColumn(catalog,"category",TrainerLanguage.T("类别"),110,false,false);Theme.TextColumn(catalog,"level",TrainerLanguage.T("等级"),57,false,false);Theme.TextColumn(catalog,"rarity",TrainerLanguage.T("稀有度"),70,false,false);
            catalog.CellValueChanged+=CatalogChecked;catalog.CellDoubleClick+=async(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex>0)await itemHover.ShowDetails(this,ItemTipTarget.Catalogue(catalog.Rows[e.RowIndex].Tag as Dictionary<string,object>),ImageFor(J.N((Dictionary<string,object>)catalog.Rows[e.RowIndex].Tag,"id")));};
            var pager=Flow(DockStyle.Bottom,38);pager.Controls.Add(previous);pageInfo.Margin=new Padding(16,9,12,0);pager.Controls.Add(pageInfo);pager.Controls.Add(next);
            var cartPanel=new Panel {Dock=DockStyle.Fill,Padding=new Padding(18,10,18,10),BackColor=Theme.Bg};
            var cartHead=Flow(DockStyle.Top,32);cartCount.Margin=new Padding(1,4,20,0);cartHead.Controls.Add(cartCount);cartHead.Controls.Add(Theme.Label(TrainerLanguage.T("装备按件发放；材料按单组数量发放"),9,Theme.Muted));
            Theme.IconColumn(cartGrid);Theme.TextColumn(cartGrid,"name",TrainerLanguage.T("已选物品"),180,true,false);Theme.TextColumn(cartGrid,"count",TrainerLanguage.T("数量"),70,false,true);
            var mode=new DataGridViewComboBoxColumn {Name="mode",HeaderText=TrainerLanguage.T("装备模式"),Width=110,FlatStyle=FlatStyle.Flat,DisplayStyle=DataGridViewComboBoxDisplayStyle.DropDownButton,ReadOnly=false};mode.Items.AddRange(Theme.Modes);cartGrid.Columns.Add(mode);
            Theme.TextColumn(cartGrid,"level",TrainerLanguage.T("等级"),88,false,true);cartGrid.Columns["level"].ToolTipText=TrainerLanguage.T("强化或增幅等级（0～31）");Theme.TextColumn(cartGrid,"attribute",TrainerLanguage.T("属性值"),90,false,true);cartGrid.Columns["attribute"].ToolTipText=TrainerLanguage.T("红字属性值（0～65535）");
            cartGrid.Columns.Add(new DataGridViewButtonColumn {Name="remove",HeaderText="",Text=TrainerLanguage.T("移除"),UseColumnTextForButtonValue=true,Width=55,FlatStyle=FlatStyle.Flat});cartGrid.RowTemplate.Height=35;
            cartGrid.CellValidating+=ValidateCartCell;cartGrid.CellEndEdit+=CartEdited;cartGrid.CellContentClick+=(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex==cartGrid.Columns["remove"].Index){cart.Remove(((CartEntry)cartGrid.Rows[e.RowIndex].Tag).Id);BindCart();SyncChecks();}};
            cartGrid.CellDoubleClick+=async(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex>=0&&(cartGrid.Columns[e.ColumnIndex].Name=="icon"||cartGrid.Columns[e.ColumnIndex].Name=="name")){var entry=cartGrid.Rows[e.RowIndex].Tag as CartEntry;if(entry!=null)await itemHover.ShowDetails(cartDialog,ItemTipTarget.Pending(entry),ImageFor(entry.Id));}};
            var settings=new Panel {Dock=DockStyle.Bottom,Height=100};
            var settingsFields=Flow(DockStyle.Top,39);FillSettings(settingsFields,batchCount,batchType,batchLevel,batchValue);
            var settingsActions=Flow(DockStyle.Bottom,39);settingsActions.Controls.Add(Theme.Button("应用到已选",(s,e)=>ApplyCartSettings(),false));settingsActions.Controls.Add(give);
            settings.Controls.Add(settingsFields);settings.Controls.Add(settingsActions);
            var note=Theme.Label(TrainerLanguage.T("强化 / 增幅共用装备等级（0～31）；未自动领取的物品保存在邮箱。"),9,Theme.Muted);note.Dock=DockStyle.Bottom;note.Height=25;note.AutoSize=false;note.TextAlign=ContentAlignment.MiddleLeft;
            cartPanel.Controls.Add(cartGrid);cartPanel.Controls.Add(cartHead);cartPanel.Controls.Add(settings);cartPanel.Controls.Add(note);
            panel.Controls.Add(catalog);panel.Controls.Add(choose);panel.Controls.Add(top);panel.Controls.Add(pager);
            ConfigureItemCart(panel,cartPanel);
        }
        private void FillSettings(FlowLayoutPanel flow,NumericUpDown quantity,ComboBox type,NumericUpDown enhancement,NumericUpDown attribute)
        {
            type.Items.AddRange(Theme.Modes);type.SelectedIndex=0;attribute.Enabled=false;
            foreach(var pair in new[]{Tuple.Create("数量",(Control)quantity),Tuple.Create("装备模式",(Control)type),Tuple.Create("等级",(Control)enhancement),Tuple.Create("属性值",(Control)attribute)})
            { var label=Theme.Label(pair.Item1,9,Theme.Muted);label.Margin=new Padding(8,8,4,0);flow.Controls.Add(label);flow.Controls.Add(pair.Item2); }
            type.SelectedIndexChanged+=(s,e)=>attribute.Enabled=type.SelectedIndex>0;
        }
        private void BuildBag()
        {
            var panel=PagePanel();AddTab("背包与穿戴",panel);
            var top=Flow(DockStyle.Top,39);inventoryInfo.Margin=new Padding(0,6,12,0);top.Controls.Add(inventoryInfo);top.Controls.Add(Theme.Button("刷新背包",(s,e)=>RunAction(async()=>await FetchRole(RequireRole()),TrainerLanguage.T("正在刷新背包…")),false));top.Controls.Add(Theme.Button("全选物品",(s,e)=>{foreach(DataGridViewRow r in bag.Rows)r.Cells["pick"].Value=true;},false));
            Theme.CheckColumn(bag,"pick");Theme.IconColumn(bag);Theme.TextColumn(bag,"id",TrainerLanguage.T("编号"),80,false,false);Theme.TextColumn(bag,"name",TrainerLanguage.T("物品名称"),220,true,false);Theme.TextColumn(bag,"space",TrainerLanguage.T("位置"),95,false,false);Theme.TextColumn(bag,"count",TrainerLanguage.T("数量"),80,false,false);Theme.TextColumn(bag,"mode",TrainerLanguage.T("强化 / 增幅"),140,false,false);Theme.TextColumn(bag,"attribute",TrainerLanguage.T("属性值"),80,false,false);
            bag.CellDoubleClick+=async(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex>0){var item=bag.Rows[e.RowIndex].Tag as Dictionary<string,object>;await itemHover.ShowDetails(this,ItemTipTarget.Inventory(item),ImageFor(J.N(item,"item_key")));}};
            var settings=new Panel {Dock=DockStyle.Bottom,Height=100};
            var fields=Flow(DockStyle.Top,39);FillSettings(fields,bagCount,bagType,bagLevel,bagValue);
            var actions=Flow(DockStyle.Bottom,39);actions.Controls.Add(Theme.Button("修改勾选物品",(s,e)=>RunAction(EditBag,TrainerLanguage.T("正在同步背包修改…")),true));settings.Controls.Add(fields);settings.Controls.Add(actions);
            var note=Theme.Label(TrainerLanguage.T("已穿戴装备也可修改；锁定的物品请先在游戏中解锁。双击物品查看说明。"),9,Theme.Muted);note.Dock=DockStyle.Bottom;note.Height=31;note.AutoSize=false;
            panel.Controls.Add(bag);panel.Controls.Add(top);panel.Controls.Add(settings);panel.Controls.Add(note);
        }
        private void BuildCareer()
        {
            var panel=PagePanel();AddTab("职业与觉醒",panel);var card=new SurfacePanel {Dock=DockStyle.Top,Height=315};panel.Controls.Add(card);
            var caption=Theme.Label(TrainerLanguage.T("选择目标职业"),14,Theme.Text);caption.Font=new Font(caption.Font,FontStyle.Bold);caption.Location=new Point(22,21);card.Controls.Add(caption);
            var a=Theme.Label(TrainerLanguage.T("基础职业"),9,Theme.Muted);a.Location=new Point(24,74);card.Controls.Add(a);job.Location=new Point(24,101);job.Width=235;card.Controls.Add(job);
            var b=Theme.Label(TrainerLanguage.T("转职 / 觉醒"),9,Theme.Muted);b.Location=new Point(291,74);card.Controls.Add(b);career.Location=new Point(291,101);career.Width=340;card.Controls.Add(career);
            careerPreview.Font=new Font(Font.FontFamily,13,FontStyle.Bold);careerPreview.Location=new Point(24,163);card.Controls.Add(careerPreview);
            var save=Theme.Button("保存职业并同步技能",(s,e)=>RunAction(async()=>{var c=career.SelectedItem as Choice;if(c==null)throw new InvalidOperationException(TrainerLanguage.T("请选择职业。"));await SaveProfile(J.D("career",c.Id));},TrainerLanguage.T("正在转换职业并同步技能…")),true);save.Location=new Point(22,214);card.Controls.Add(save);
            var help=Theme.Label(TrainerLanguage.T("切换职业会重建技能。现有装备保留，穿戴条件以游戏要求为准。"),9,Theme.Muted);help.Location=new Point(25,267);help.MaximumSize=new Size(900,35);card.Controls.Add(help);
            card.Resize+=(s,e)=>{career.Width=Math.Max(210,Math.Min(420,card.Width-320));help.MaximumSize=new Size(Math.Max(420,card.Width-48),40);};
        }
        private NumericUpDown CharacterField(string key,string caption,int min,int max,int initial)
        {
            var number=Theme.Number(caption,min,max,initial,170);characterFields.Add(key,number);return number;
        }
        private Control AttributeCard(string heading,string hint,string save,Func<Task> action,params Tuple<string,NumericUpDown>[] fields)
        {
            var card=new SurfacePanel {Dock=DockStyle.Fill,Margin=new Padding(0,0,12,12),Padding=new Padding(14)};
            var title=Theme.Label(heading,14,Theme.Text);title.Location=new Point(14,12);card.Controls.Add(title);
            int y=48;
            foreach(var pair in fields)
            {
                var label=Theme.Label(pair.Item1,9,Theme.Muted);label.Location=new Point(14,y+6);card.Controls.Add(label);
                pair.Item2.Font=new Font(Font.FontFamily,11);pair.Item2.Location=new Point(130,y);pair.Item2.Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;card.Controls.Add(pair.Item2);y+=34;
            }
            var button=Theme.Button(save,(s,e)=>RunAction(action,TrainerLanguage.T("正在保存")+heading+"…"),true);button.Location=new Point(10,y+1);card.Controls.Add(button);
            var note=Theme.Label(hint,9,Theme.Muted);note.Location=new Point(14,y+43);note.MaximumSize=new Size(420,47);card.Controls.Add(note);
            card.Resize+=(s,e)=>{foreach(var pair in fields)pair.Item2.Width=Math.Max(80,card.Width-148);note.MaximumSize=new Size(Math.Max(150,card.Width-28),47);};
            return card;
        }
        private void BuildAttributes()
        {
            var panel=PagePanel();AddTab("角色属性",panel);
            var scroll=new Panel {Dock=DockStyle.Fill,AutoScroll=true,BackColor=Theme.Bg};panel.Controls.Add(scroll);
            var grid=new TableLayoutPanel {Dock=DockStyle.Top,Height=642,ColumnCount=2,RowCount=3,BackColor=Theme.Bg,Margin=Padding.Empty};
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));for(int i=0;i<3;i++)grid.RowStyles.Add(new RowStyle(SizeType.Percent,33.333f));
            var progressionLevel=CharacterField("level","成长页角色等级",1,60,1);
            NumericUpDown spA=CharacterField("skill_points","技能栏 A 剩余 SP",0,65535,0),spB=CharacterField("skill_points2","技能栏 B 剩余 SP",0,65535,0);
            NumericUpDown maximum=CharacterField("max_fatigue","疲劳上限",0,65535,156),remaining=CharacterField("fatigue_remaining","剩余疲劳",0,65535,156);
            NumericUpDown coins=CharacterField("revival_coins","复活币数量",0,2147483647,0),points=CharacterField("win_points","胜点数量",0,2147483647,0);
            grid.Controls.Add(AttributeCard("角色金币","保存后同步到当前角色。","保存金币",()=>SaveProfile(J.D("gold",Decimal.ToInt32(gold.Value))),Tuple.Create("金币数量",gold)),0,0);
            grid.Controls.Add(AttributeCard("商城点券","点券属于所选角色的账号。","保存点券",()=>SaveProfile(J.D("cera",Decimal.ToInt32(cera.Value))),Tuple.Create("点券数量",cera)),1,0);
            grid.Controls.Add(AttributeCard("角色等级","支持 1～60 级，经验与成长属性按版本规则调整。","保存等级",()=>SaveAttributes("level"),Tuple.Create("目标等级",progressionLevel)),0,1);
            grid.Controls.Add(AttributeCard("技能点 SP","只调整剩余 SP，保留已经学习的技能。","保存技能点",()=>SaveAttributes("skill_points","skill_points2"),Tuple.Create("技能方案 A",spA),Tuple.Create("技能方案 B",spB)),1,1);
            grid.Controls.Add(AttributeCard("疲劳值","剩余疲劳不能超过上限。","保存疲劳",()=>SaveAttributes("max_fatigue","fatigue_remaining"),Tuple.Create("疲劳上限",maximum),Tuple.Create("剩余疲劳",remaining)),0,2);
            grid.Controls.Add(AttributeCard("复活币与胜点","按输入的数量保存。","保存数量",()=>SaveAttributes("revival_coins","win_points"),Tuple.Create("复活币",coins),Tuple.Create("胜点",points)),1,2);
            scroll.Controls.Add(grid);
        }
        private async Task SaveAttributes(params string[] keys)
        {
            if(keys.Any(k=>k!="level")&&!api.HasCharacterAttributes)throw new InvalidOperationException(TrainerLanguage.T("本机修改服务需要更新。请使用“一键关闭”，再“一键开始”后重试。"));
            var values=new Dictionary<string,object>();foreach(string key in keys)values[key]=Decimal.ToInt32(characterFields[key].Value);await SaveProfile(values);
        }
        private void BuildHistory()
        {
            var panel=PagePanel();AddTab("操作记录",panel);
            var top=Flow(DockStyle.Top,39);top.Controls.Add(Theme.Button("刷新记录",(s,e)=>RunAction(LoadHistory,TrainerLanguage.T("正在读取记录…")),false));top.Controls.Add(Theme.Button("打开备份目录",(s,e)=>OpenFolder(Path.Combine(Program.Root,"backups","trainer")),false));
            Theme.TextColumn(history,"time",TrainerLanguage.T("时间"),150,false,false);Theme.TextColumn(history,"action",TrainerLanguage.T("操作"),100,false,false);Theme.TextColumn(history,"role",TrainerLanguage.T("角色编号"),90,false,false);Theme.TextColumn(history,"result",TrainerLanguage.T("结果"),200,true,false);Theme.TextColumn(history,"backup",TrainerLanguage.T("修改前快照"),230,false,false);
            history.Columns["backup"].Visible=false;history.RowTemplate.Height=42;history.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0){string path=Convert.ToString(history.Rows[e.RowIndex].Cells["backup"].Value);if(File.Exists(path))Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+path+"\""){UseShellExecute=true});}};
            panel.Controls.Add(history);panel.Controls.Add(top);
            tabs.SelectedIndexChanged+=async(s,e)=>{if(loaded&&tabs.SelectedIndex==4)try{await LoadHistory();}catch(Exception ex){SetStatus(ex.Message,true);}};
        }
        private void DrawRole(object sender,DrawItemEventArgs e)
        {
            if(e.Index<0)return;var r=(RoleEntry)roles.Items[e.Index];bool pick=(e.State&DrawItemState.Selected)!=0;
            using(var b=new SolidBrush(pick?Theme.Selected:Theme.Field))e.Graphics.FillRectangle(b,e.Bounds);
            if(pick)using(var p=new Pen(Theme.Gold))e.Graphics.DrawRectangle(p,e.Bounds.X,e.Bounds.Y,e.Bounds.Width-1,e.Bounds.Height-1);
            using(var nameFont=new Font(Font.FontFamily,10,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,J.S(r.Data,"name"),nameFont,new Point(e.Bounds.X+10,e.Bounds.Y+9),Theme.Text);
            TextRenderer.DrawText(e.Graphics,"Lv."+J.N(r.Data,"level")+" "+TrainerLanguage.Content(r.Career)+"\n"+TrainerLanguage.F("账号 {0}",J.S(r.Data,"profile_name")),Font,new Point(e.Bounds.X+10,e.Bounds.Y+31),Theme.Muted);
            if(J.B(r.Data,"online"))using(var b=new SolidBrush(Theme.Green))e.Graphics.FillEllipse(b,e.Bounds.Right-15,e.Bounds.Y+15,6,6);
        }
        private async Task Initialize()
        {
            await RunActionTask(async()=>
            {
                Dictionary<string,object> boot=null;
                bool start=false;
                try { boot=await Task.Run(()=>api.Bootstrap()); }
                catch { start=true; }
                if(start)
                {
                    SetStatus(TrainerLanguage.T("正在启动本机游戏服务，请稍候…"),false);
                    await Task.Run(()=>{var p=Process.Start(new ProcessStartInfo(Path.Combine(Program.Root,"DNF60控制台.exe"),"--server-only"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Program.Root});if(p==null||!p.WaitForExit(120000)||p.ExitCode!=0)throw new InvalidOperationException(TrainerLanguage.T("服务启动未完成，请在“服务设置 → 运行与日志”查看状态。"));});
                    boot=await Task.Run(()=>api.Bootstrap());
                }
                careers.AddRange(J.Rows(boot["careers"]));
                var initialProfile=J.V(boot,"selectedProfile") as Dictionary<string,object>;selectedLocalProfileId=initialProfile==null?"":J.S(initialProfile,"id");
                foreach(var c in careers.GroupBy(x=>J.N(x,"job")).Select(x=>x.First()))job.Items.Add(new Choice(J.N(c,"job"),J.S(c,"jobName")));
                loaded=true;await LoadRoles();await LoadCatalogTask();await RefreshStatus();statusTimer.Start();admin.SetReady(api.HasManagement);
                SetStatus(TrainerLanguage.T("本机服务已连接，修改前会自动备份。"),false);
            },TrainerLanguage.T("正在连接本机服务…"));
            if(loaded)NavigateSection(startWithManagement?4:0,0);
        }
        private async void RunAction(Func<Task> action,string message){await RunActionTask(action,message);}
        private async Task RunActionTask(Func<Task> action,string message)
        {
            if(busy||closeAfterGameExit)return;busy=true;work.Enabled=false;roles.Enabled=false;roleSearch.Enabled=false;SetNavigationEnabled(false);if(cartDialog!=null&&cartDialog.Visible)cartDialog.Enabled=false;SetStatus(message,false);
            try{await action();if(status.Text.EndsWith(TrainerLanguage.T(message),StringComparison.Ordinal))SetStatus(TrainerLanguage.T("操作已完成。"),false);}
            catch(Exception ex){SetStatus(ex.Message,true);Log(ex);}
            finally{busy=false;if(!IsDisposed){work.Enabled=true;roles.Enabled=true;roleSearch.Enabled=true;SetNavigationEnabled(true);if(cartDialog!=null)cartDialog.Enabled=true;if(closeAfterGameExit)BeginInvoke((Action)Close);else if(loaded&&accountSwitchPending)BeginInvoke((Action)FollowSelectedAccount);}}
        }
        private void SetStatus(string text,bool error){if(IsDisposed)return;status.Text=(error?TrainerLanguage.T("操作未完成  ·  "):"")+TrainerLanguage.T(text);status.ForeColor=error?Theme.Red:Theme.Muted;}
        private static void Log(Exception ex){try{File.AppendAllText(Path.Combine(Program.Root,"logs","native-trainer.log"),DateTime.Now.ToString("s")+" "+ex+Environment.NewLine,Encoding.UTF8);}catch { }}
        private int RequireRole(){if(selected==null)throw new InvalidOperationException(TrainerLanguage.T("请先点击右上角选择角色。"));return J.N(selected,"character_id");}
        private string CareerName(Dictionary<string,object> r)
        {var c=careers.FirstOrDefault(x=>J.N(x,"job")==J.N(r,"job")&&J.N(x,"grow")==J.N(r,"grow_type")&&J.N(x,"awakening")==J.N(r,"awakening_type"));return c==null?"未知职业":J.S(c,"name");}
        private bool accountSwitchPending;
        private string selectedLocalProfileId="";
        private void FollowSelectedAccount()
        {
            if(!accountSwitchPending||busy||closeAfterGameExit)return;accountSwitchPending=false;
            RunAction(async()=>
            {
                var boot=await Task.Run(()=>api.Bootstrap());var p=J.V(boot,"selectedProfile") as Dictionary<string,object>;
                string next=p==null?"":J.S(p,"id");
                if(next!=selectedLocalProfileId)
                {
                    selectedLocalProfileId=next;selected=null;cart.Clear();BindCart();SyncChecks();bag.Rows.Clear();
                    title.Text=TrainerLanguage.T("请在游戏内创建或选择角色");meta.Text=p==null?TrainerLanguage.T("未选择账号"):TrainerLanguage.F("账号 {0}",J.S(p,"name"));roleState.Text=TrainerLanguage.T("未选择");
                }
                roleSearch.Text="";await LoadRoles();
            },TrainerLanguage.T("正在载入所选账号的角色…"));
        }
        private async Task LoadRoles(bool onlineOnly=false,bool chooseFirst=true)
        {
            string query=roleSearch.Text.Trim();int old=selected==null?0:J.N(selected,"character_id");
            var rows=J.Rows(await Task.Run(()=>api.Get("/api/roles?q="+Uri.EscapeDataString(query)+(onlineOnly?"&online=1":""))));
            binding=true;roles.BeginUpdate();roles.Items.Clear();foreach(var r in rows)roles.Items.Add(new RoleEntry {Data=r,Career=CareerName(r)});
            int index=rows.FindIndex(x=>J.N(x,"character_id")==old);if(index<0&&rows.Count>0)index=0;roles.SelectedIndex=index;roles.EndUpdate();binding=false;
            if(index>=0&&chooseFirst)await FetchRole(J.N(rows[index],"character_id"));else if(index<0)SetStatus(TrainerLanguage.T("没有找到匹配的角色。"),false);
        }
        private async void SelectRole(int id){if(busy)return;await RunActionTask(async()=>await FetchRole(id),TrainerLanguage.T("正在载入角色…"));if(rolePicker!=null&&rolePicker.Visible)rolePicker.Hide();}
        private async Task FetchRole(int id)
        {
            int version=++roleVersion;var r=J.O(await Task.Run(()=>api.Get("/api/role?id="+id)));if(version!=roleVersion||IsDisposed)return;
            bool changed=selected!=null&&J.N(selected,"character_id")!=id;if(changed){cart.Clear();BindCart();SyncChecks();}
            selected=r;title.Text=J.S(r,"name");meta.Text=TrainerLanguage.F("账号 {0}  ·  {1}  ·  Lv.{2}  ·  角色 {3}",J.S(r,"profile_name"),TrainerLanguage.Content(CareerName(r)),J.N(r,"level"),id);
            foreach(RoleEntry entry in roles.Items)if(J.N(entry.Data,"character_id")==id){entry.Data=r;entry.Career=CareerName(r);break;}roles.Invalidate();
            gold.Value=Math.Max(gold.Minimum,Math.Min(gold.Maximum,J.N(r,"gold")));cera.Value=Math.Max(cera.Minimum,Math.Min(cera.Maximum,J.N(r,"cera")));level.Value=Math.Max(1,Math.Min(60,J.N(r,"level")));
            foreach(var field in characterFields){int value=field.Key=="fatigue_remaining"?Math.Max(0,J.N(r,"max_fatigue")-J.N(r,"used_fatigue")):J.N(r,field.Key);field.Value.Value=Math.Max(field.Value.Minimum,Math.Min(field.Value.Maximum,value));}
            UpdateRoleState(J.B(r,"online"),J.B(r,"attached"));
            binding=true;for(int i=0;i<job.Items.Count;i++)if(((Choice)job.Items[i]).Id==J.N(r,"job"))job.SelectedIndex=i;
            int choice=careers.FindIndex(c=>J.N(c,"job")==J.N(r,"job")&&J.N(c,"grow")==J.N(r,"grow_type")&&J.N(c,"awakening")==J.N(r,"awakening_type"));LoadCareerChoices(choice);binding=false;
            var items=J.Rows(r["inventory"]);await PreloadIcons(items.Select(i=>J.N(i,"item_key")));
            if(id!=RequireRole()||IsDisposed)return;
            bag.Rows.Clear();string[] areas={"背包","时装","仓库","穿戴","其他","其他","其他","宠物"};
            foreach(var i in items)
            {
                int key=J.N(i,"item_key"),space=J.N(i,"item_space"),type=J.N(i,"enhance_type");int row=bag.Rows.Add(false,ImageFor(key),key,J.S(i,"name")+(J.N(i,"lock_state")>0?" [已锁定]":""),areas[Math.Min(space,7)]+" · "+J.N(i,"slot_index"),J.B(i,"stackable")?J.N(i,"item_count").ToString():"1 件",J.B(i,"stackable")?"—":Theme.Modes[Math.Min(type,4)]+" +"+J.N(i,"item_attr"),type>0?J.N(i,"enhance_value").ToString():"—");bag.Rows[row].Tag=i;bag.Rows[row].Cells["name"].Style.ForeColor=Theme.Rarity(J.N(i,"rarity"));
            }
            inventoryInfo.Text=TrainerLanguage.F("{0} 件物品 · {1}",items.Count,J.S(r,"name"));give.Text=TrainerLanguage.F("发放到 {0}",J.S(r,"name"));
            if(admin!=null)admin.RoleChanged();UpdateDashboard();
        }
        private void UpdateRoleState(bool online,bool attached)
        {roleState.Text=attached?TrainerLanguage.T("● 在线 · 已接入"):online?TrainerLanguage.T("● 在线 · 待接入"):TrainerLanguage.T("● 角色离线");roleState.ForeColor=attached?Theme.Green:Theme.Muted;notice.Text=attached?TrainerLanguage.T("在线城镇可即时修改。所有修改前均保存角色快照。"):online?TrainerLanguage.T("请关闭客户端，再通过桌面“一键开始”重新进入，以接入在线修改服务。"):TrainerLanguage.T("离线角色可保存数据与发放邮件，进入角色后生效。");}
        private async Task RefreshStatus()
        {
            if(statusBusy||IsDisposed)return;statusBusy=true;
            try
            {
                var data=J.O(await Task.Run(()=>api.Get("/api/status")));if(IsDisposed)return;var bridge=J.O(data["bridge"]);
                bool ready=J.B(bridge,"ready");serviceState.Text=ready?TrainerLanguage.T("● 本机服务就绪"):TrainerLanguage.T("● 等待服务启动");serviceState.ForeColor=ready?Theme.Green:Theme.Muted;
                if(selected!=null){int id=RequireRole();bool on=((IEnumerable)data["online"]).Cast<object>().Any(x=>Convert.ToInt32(x)==id),attached=((IEnumerable)bridge["characters"]).Cast<object>().Any(x=>Convert.ToInt32(x)==id);UpdateRoleState(on,attached);}
                else if(loaded&&!busy&&((IEnumerable)data["online"]).Cast<object>().Any())await LoadRoles();
            }
            catch{if(!IsDisposed){serviceState.Text=TrainerLanguage.T("● 游戏服务已停止");serviceState.ForeColor=Theme.Muted;}}
            finally{statusBusy=false;if(!IsDisposed)UpdateDashboard();}
        }
        private async Task PreloadIcons(IEnumerable<int> keys)
        {
            var needed=keys.Distinct().Where(x=>!images.ContainsKey(x)).ToArray();
            await Task.Run(()=>Parallel.ForEach(needed,new ParallelOptions {MaxDegreeOfParallelism=5},id=>{byte[] data=api.Icon(id);if(data==null)return;try{using(var stream=new MemoryStream(data))using(var src=Image.FromStream(stream)){var image=new Bitmap(src);if(!images.TryAdd(id,image))image.Dispose();}}catch { }}));
        }
        private Image ImageFor(int id){Bitmap image;return images.TryGetValue(id,out image)?image:null;}
        private async void LoadCatalog(){try{await LoadCatalogTask();}catch(Exception ex){if(!IsDisposed)SetStatus(ex.Message,true);}}
        private async Task LoadCatalogTask()
        {
            int version=++searchVersion,requestedPage=page;string q=itemSearch.Text.Trim();int c=((Choice)category.SelectedItem).Id,r=((Choice)rarity.SelectedItem).Id;string[] cats={"equipment","material","consumable","quest","avatar","creature","other"};
            string path="/api/items?lang="+Uri.EscapeDataString(TrainerLanguage.Locale)+"&q="+Uri.EscapeDataString(q)+"&page="+requestedPage+"&category="+(c<0?"":cats[c])+"&rarity="+(r<0?"":r.ToString());
            var data=J.O(await Task.Run(()=>api.Get(path)));var rows=J.Rows(data["items"]);await PreloadIcons(rows.Where(i=>J.B(i,"icon")).Select(i=>J.N(i,"id")));
            if(version!=searchVersion||IsDisposed)return;
            pages=J.N(data,"pages");lastCatalogTotal=J.N(data,"total");pageInfo.Text=TrainerLanguage.F("共 {0:N0} 项     第 {1} / {2} 页",lastCatalogTotal,requestedPage,pages);previous.Enabled=requestedPage>1;next.Enabled=requestedPage<pages;
            binding=true;catalog.Rows.Clear();
            foreach(var i in rows)
            {int id=J.N(i,"id"),rare=J.N(i,"rarity");int row=catalog.Rows.Add(cart.ContainsKey(id),ImageFor(id),id,J.S(i,"name"),Theme.Category(i),J.N(i,"level"),rare<Theme.Rarities.Length?Theme.Rarities[rare]:rare.ToString());catalog.Rows[row].Tag=i;catalog.Rows[row].Cells["name"].Style.ForeColor=Theme.Rarity(rare);if(!J.B(i,"sendable")||id<3){catalog.Rows[row].Cells["pick"].ReadOnly=true;catalog.Rows[row].DefaultCellStyle.ForeColor=Theme.Muted;catalog.Rows[row].Cells["name"].Style.ForeColor=Theme.Muted;}}
            binding=false;catalog.ClearSelection();
        }
        private void CatalogChecked(object sender,DataGridViewCellEventArgs e)
        {
            if(binding||e.RowIndex<0||e.ColumnIndex!=catalog.Columns["pick"].Index)return;
            var row=catalog.Rows[e.RowIndex];var i=(Dictionary<string,object>)row.Tag;if(i==null)return;
            int id=J.N(i,"id");bool check=Convert.ToBoolean(row.Cells["pick"].Value);
            if(check)
            {
                if(!J.B(i,"sendable")||id<3||cart.Count>=64){binding=true;row.Cells["pick"].Value=false;binding=false;if(cart.Count>=64)SetStatus(TrainerLanguage.T("每批最多选择 64 种物品。"),true);return;}
                if(!cart.ContainsKey(id))cart.Add(id,new CartEntry {Item=i,Count=Math.Min(Decimal.ToInt32(batchCount.Value),J.B(i,"stackable")?Math.Max(1,J.N(i,"stackLimit")):32),Type=batchType.SelectedIndex,Level=Decimal.ToInt32(batchLevel.Value),Attribute=Decimal.ToInt32(batchValue.Value)});
            }
            else cart.Remove(id);
            BindCart();
        }
        private void SyncChecks(){binding=true;foreach(DataGridViewRow row in catalog.Rows)if(row.Tag!=null)row.Cells["pick"].Value=cart.ContainsKey(J.N((Dictionary<string,object>)row.Tag,"id"));binding=false;}
        private void CheckPage(bool value){foreach(DataGridViewRow row in catalog.Rows)if(!row.Cells["pick"].ReadOnly)row.Cells["pick"].Value=value;}
        private void CheckHighlighted(){foreach(DataGridViewRow row in catalog.SelectedRows)if(!row.Cells["pick"].ReadOnly)row.Cells["pick"].Value=true;}
        private void BindCart()
        {
            binding=true;cartGrid.Rows.Clear();
            foreach(var entry in cart.Values)
            {
                bool stack=J.B(entry.Item,"stackable");if(stack){entry.Type=0;entry.Level=0;entry.Attribute=0;}int row=cartGrid.Rows.Add(ImageFor(entry.Id),J.S(entry.Item,"name"),entry.Count,Theme.Modes[entry.Type],entry.Level,entry.Type>0?entry.Attribute:0,"移除");cartGrid.Rows[row].Tag=entry;cartGrid.Rows[row].Cells["name"].Style.ForeColor=Theme.Rarity(J.N(entry.Item,"rarity"));
                if(stack)foreach(string col in new[]{"mode","level","attribute"}){var cell=new DataGridViewTextBoxCell();cartGrid.Rows[row].Cells[col]=cell;cell.Value="—";cell.ReadOnly=true;cell.Style.ForeColor=Theme.Muted;}
            }
            cartCount.Text=TrainerLanguage.F("已选物品  {0}",cart.Count);if(catalogSelectionSummary!=null){catalogSelectionSummary.Text=TrainerLanguage.F("已选 {0} 种物品",cart.Count);catalogReview.Enabled=cart.Count>0;}binding=false;
        }
        private void ValidateCartCell(object sender,DataGridViewCellValidatingEventArgs e)
        {
            if(binding||e.RowIndex<0||e.ColumnIndex<0||cartGrid.Rows[e.RowIndex].Tag==null||cartGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly)return;var entry=(CartEntry)cartGrid.Rows[e.RowIndex].Tag;string col=cartGrid.Columns[e.ColumnIndex].Name;int min=0,max=0,n;
            if(col=="count"){min=1;max=J.B(entry.Item,"stackable")?Math.Min(1000000,Math.Max(1,J.N(entry.Item,"stackLimit"))):32;}else if(col=="level")max=31;else if(col=="attribute")max=65535;else return;
            if(!Int32.TryParse(Convert.ToString(e.FormattedValue),out n)||n<min||n>max){e.Cancel=true;SetStatus(TrainerLanguage.T("该数值必须为 ")+min+"～"+max+TrainerLanguage.T(" 的整数。"),true);}
        }
        private void CartEdited(object sender,DataGridViewCellEventArgs e)
        {
            if(binding||e.RowIndex<0)return;var row=cartGrid.Rows[e.RowIndex];var entry=(CartEntry)row.Tag;if(entry==null)return;
            try{entry.Count=Convert.ToInt32(row.Cells["count"].Value);if(J.B(entry.Item,"stackable")){entry.Type=0;entry.Level=0;entry.Attribute=0;}else{entry.Type=Array.IndexOf(Theme.Modes,Convert.ToString(row.Cells["mode"].Value));entry.Level=Convert.ToInt32(row.Cells["level"].Value);entry.Attribute=entry.Type>0?Convert.ToInt32(row.Cells["attribute"].Value):0;}}catch(Exception ex){SetStatus(ex.Message,true);}
        }
        private void ApplyCartSettings()
        {
            if(!cartGrid.EndEdit()){SetStatus(TrainerLanguage.T("请先修正物品数量或装备等级。"),true);return;}foreach(var entry in cart.Values){entry.Count=Math.Min(Decimal.ToInt32(batchCount.Value),J.B(entry.Item,"stackable")?Math.Max(1,J.N(entry.Item,"stackLimit")):32);entry.Type=batchType.SelectedIndex;entry.Level=Decimal.ToInt32(batchLevel.Value);entry.Attribute=entry.Type>0?Decimal.ToInt32(batchValue.Value):0;}BindCart();SetStatus(TrainerLanguage.T("已应用数量与装备设置到 ")+cart.Count+TrainerLanguage.T(" 种物品。"),false);
        }
        private async Task SaveProfile(Dictionary<string,object> values)
        {int id=RequireRole();values["id"]=id;var result=await Task.Run(()=>api.Post("/api/profile",values));await FetchRole(id);SetStatus(J.S(result,"message"),false);}
        private async Task GiveItems()
        {
            int id=RequireRole();if(!cartGrid.EndEdit())throw new InvalidOperationException(TrainerLanguage.T("请先修正物品数量或装备等级。"));if(cart.Count==0)throw new InvalidOperationException(TrainerLanguage.T("请先勾选要发放的物品。"));
            var entries=cart.Values.Select(x=>J.D("id",x.Id,"count",x.Count,"enhanceType",x.Type,"enhanceLevel",x.Level,"enhanceValue",x.Attribute)).ToArray();
            var result=await Task.Run(()=>api.Post("/api/give",J.D("id",id,"items",entries)));cart.Clear();BindCart();SyncChecks();await FetchRole(id);SetStatus(J.S(result,"message"),J.B(result,"partial"));
        }
        private async Task EditBag()
        {
            int id=RequireRole();bag.EndEdit();var rows=bag.Rows.Cast<DataGridViewRow>().Where(r=>Convert.ToBoolean(r.Cells["pick"].Value)).Select(r=>(Dictionary<string,object>)r.Tag).ToArray();if(rows.Length==0)throw new InvalidOperationException(TrainerLanguage.T("请勾选要修改的背包或穿戴物品。"));
            var items=rows.Select(r=>J.D("space",J.N(r,"item_space"),"slot",J.N(r,"slot_index"),"item",J.N(r,"item_key"))).ToArray();
            var request=J.D("id",id,"items",items,"count",Decimal.ToInt32(bagCount.Value),"enhanceType",bagType.SelectedIndex,"enhanceLevel",Decimal.ToInt32(bagLevel.Value),"enhanceValue",Decimal.ToInt32(bagValue.Value));
            var result=await Task.Run(()=>api.Post("/api/inventory",request));await FetchRole(id);SetStatus(J.S(result,"message"),false);
        }
        private void LoadCareerChoices(int wanted)
        {
            if(job.SelectedItem==null)return;int id=((Choice)job.SelectedItem).Id;career.Items.Clear();
            for(int n=0;n<careers.Count;n++)if(J.N(careers[n],"job")==id)career.Items.Add(new Choice(n,J.S(careers[n],"name")+(J.N(careers[n],"awakening")>0?" · 觉醒":J.N(careers[n],"grow")>0?" · 转职":" · 初始职业")));
            if(career.Items.Count>0){career.SelectedIndex=0;for(int n=0;n<career.Items.Count;n++)if(((Choice)career.Items[n]).Id==wanted)career.SelectedIndex=n;}UpdateCareerPreview();
        }
        private void UpdateCareerPreview(){var c=career.SelectedItem as Choice;if(c==null){careerPreview.Text="";return;}var data=careers[c.Id];careerPreview.Text=TrainerLanguage.Content(J.S(data,"jobName"))+"  →  "+TrainerLanguage.Content(J.S(data,"name"));}
        private async Task LoadHistory()
        {
            var rows=J.Rows(await Task.Run(()=>api.Get("/api/history")));if(IsDisposed)return;history.Rows.Clear();
            foreach(var r in rows){string action=J.S(r,"action"),message=J.S(r,"message");var detail=J.V(r,"details") as Dictionary<string,object>;string path=detail==null?"":J.S(detail,"path");string caption=HistoryCaption(path);if(caption!=null){action=caption;if(message.StartsWith("已保存：")||message.StartsWith("已保存:"))message="操作已完成";}int id=J.N(r,"characterId");history.Rows.Add(J.S(r,"time"),action,id==0?"全局":id.ToString(),message,J.S(r,"backup"));}SetStatus(TrainerLanguage.T("双击记录可在文件夹中定位对应的角色快照。"),false);
        }
        private static string HistoryCaption(string path)
        {
            switch(path){case "/api/character/edit":return "完整角色属性";case "/api/account/gm":return "账号 GM 权限";case "/api/account/password":return "账号密码";case "/api/mail/send":return "角色邮件";case "/api/mail/clear":return "清空邮箱";case "/api/mail/send-all":return "全员邮件";case "/api/avatars/send":return "发放时装";case "/api/inventory/delete":return "删除背包物品";case "/api/quests/complete":case "/api/quests/complete-batch":return "完成任务";case "/api/premium/grant":return "发放契约";case "/api/premium/remove":return "移除契约";case "/api/dungeons/set":return "副本权限";case "/api/events/set":return "活动设置";case "/api/schedules/points":return "在线泡点";case "/api/schedules/mail":return "定时邮件";case "/api/rewards/registration":return "新账号奖励";case "/api/config/ini":return "玩法参数";case "/api/config/table":return "摆摊数据表";case "/api/channels/set":return "频道设置";case "/api/monsters/difficulty":return "怪物难度";case "/api/backups/create":return "创建存档备份";case "/api/backups/restore":return "恢复整个存档";case "/api/backups/character-restore":return "恢复角色存档";default:return null;}
        }
        private static void OpenFolder(string path){Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
        private void OpenControl(){NavigateSection(4,0);}
        private void RefreshLanguage(object sender,EventArgs args)
        {
            if(IsDisposed)return;itemHover.Hide();
            foreach(var b in navButtons)if(b!=null)b.Invalidate();roles.Invalidate();catalog.Invalidate();bag.Invalidate();cartGrid.Invalidate();history.Invalidate();
            if(selected!=null){title.Text=J.S(selected,"name");meta.Text=TrainerLanguage.F("账号 {0}  ·  {1}  ·  Lv.{2}  ·  角色 {3}",J.S(selected,"profile_name"),TrainerLanguage.Content(CareerName(selected)),J.N(selected,"level"),J.N(selected,"character_id"));give.Text=TrainerLanguage.F("发放到 {0}",J.S(selected,"name"));inventoryInfo.Text=TrainerLanguage.F("{0} 件物品 · {1}",bag.Rows.Count,J.S(selected,"name"));UpdateRoleState(J.B(selected,"online"),J.B(selected,"attached"));}
            else{title.Text=TrainerLanguage.T("选择要管理的角色");meta.Text=TrainerLanguage.T("账号、职业与等级");inventoryInfo.Text=TrainerLanguage.T("请选择角色");give.Text=TrainerLanguage.T("发放所选物品");}
            cartCount.Text=TrainerLanguage.F("已选物品  {0}",cart.Count);if(catalogSelectionSummary!=null)catalogSelectionSummary.Text=TrainerLanguage.F("已选 {0} 种物品",cart.Count);
            if(loaded)pageInfo.Text=TrainerLanguage.F("共 {0:N0} 项     第 {1} / {2} 页",lastCatalogTotal,page,pages);
            UpdateCareerPreview();UpdateDashboard();
        }
    }

    internal static class NativeCheck
    {
        internal static void Run()
        {
            var api=new LocalApi();var boot=api.Bootstrap();var flow=J.O(api.Get("/api/items?q="+Uri.EscapeDataString("流光星陨刀")));var rows=J.Rows(flow["items"]);var sword=rows.First(x=>J.N(x,"id")==27675);
            byte[] png=api.Icon(27675);int width,height;using(var stream=new MemoryStream(png))using(var image=Image.FromStream(stream)){width=image.Width;height=image.Height;}
            var roles=J.Rows(api.Get("/api/roles"));var report=J.D("passed",J.N(boot,"items")==31474&&rows.Count>0&&width>0&&roles.Count>=0,"uiTechnology","Windows Forms native controls","webViewUsed",false,"catalogItems",J.N(boot,"items"),"careers",J.Rows(boot["careers"]).Count,"itemName",J.S(sword,"name"),"iconWidth",width,"iconHeight",height,"roles",roles.Count,"time",DateTime.Now.ToString("o"));
            File.WriteAllText(Path.Combine(Program.Root,"setup","native-desktop-check.json"),new JavaScriptSerializer().Serialize(report),new UTF8Encoding(false));
        }
    }
}
