// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Dnf60Desktop
{
    internal sealed partial class AdminPanel : UserControl
    {
        private readonly LocalApi api;
        private readonly ItemHoverService itemHover;
        private readonly Func<int> selectedId;
        private readonly Func<Func<Task>,string,Task> execute;
        private readonly Action<string,bool> status;
        private readonly Func<Task> refreshRole;
        private readonly ListBox navigation=new ListBox();
        private readonly Panel content=new Panel {Dock=DockStyle.Fill,Padding=new Padding(12,2,2,2)};
        private readonly Label heading=Theme.Label(TrainerLanguage.T("服务管理"),16,Theme.Gold),roleBadge=Theme.Label("",9,Theme.Muted);
        private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=2500};
        private readonly Dictionary<int,Bitmap> icons=new Dictionary<int,Bitmap>();
        private Func<Task> pageRefresh;
        private Func<bool> isDirty;
        private bool changing,ready,loading,ticking;
        private bool integrated;
        private Control sectionHeader;
        private int current=-1,pageRole,epoch;
        private string channelSignature;
        private DataGridView channelGrid;
        private Dictionary<string,object> channelData;
        private readonly string[] pages={"运行状态与日志","频道管理","账号与完整属性","邮件管理","时装发送","背包清理","任务管理","契约与副本","泡点与初始奖励","活动管理","假人与玩法配置","怪物难度","存档备份与恢复"};
        internal AdminPanel(LocalApi api,Func<int> selectedId,Func<Func<Task>,string,Task> execute,Action<string,bool> status,Func<Task> refreshRole,ItemHoverService itemHover)
        {
            this.api=api;this.selectedId=selectedId;this.execute=execute;this.status=status;this.refreshRole=refreshRole;this.itemHover=itemHover;
            Dock=DockStyle.Fill;BackColor=Theme.Bg;ForeColor=Theme.Text;
            navigation.Dock=DockStyle.Left;navigation.Width=158;navigation.BackColor=Theme.Side;navigation.ForeColor=Theme.Text;navigation.BorderStyle=BorderStyle.None;navigation.ItemHeight=38;navigation.IntegralHeight=false;navigation.DrawMode=DrawMode.OwnerDrawFixed;navigation.AccessibleName=TrainerLanguage.T("服务管理功能");
            navigation.Items.AddRange(pages);navigation.DrawItem+=(s,e)=>{if(e.Index<0)return;using(var b=new SolidBrush(e.Index==navigation.SelectedIndex?Theme.Panel:Theme.Side))e.Graphics.FillRectangle(b,e.Bounds);TextRenderer.DrawText(e.Graphics,TrainerLanguage.T(pages[e.Index]),Font,e.Bounds,e.Index==navigation.SelectedIndex?Theme.Gold:Theme.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis);};
            navigation.SelectedIndexChanged+=async(s,e)=>{if(changing||!ready)return;int choice=navigation.SelectedIndex;if(choice==current)return;if(Dirty()&&!Confirm(TrainerLanguage.T("当前页有未保存修改。切换功能会放弃这些修改，是否继续？"))){changing=true;navigation.SelectedIndex=current;changing=false;return;}await Run(()=>LoadPage(choice),TrainerLanguage.T("正在读取")+TrainerLanguage.T(pages[choice])+"…");};
            var top=new Panel {Dock=DockStyle.Top,Height=73};sectionHeader=top;heading.Location=new Point(3,5);roleBadge.Location=new Point(3,39);top.Controls.Add(heading);top.Controls.Add(roleBadge);
            var reload=Theme.Button("刷新此页",async(s,e)=>{if(!Dirty()||Confirm(TrainerLanguage.T("刷新会放弃未保存修改，是否继续？")))await Run(()=>LoadPage(current),TrainerLanguage.T("正在刷新…"));},false);reload.Dock=DockStyle.Right;top.Controls.Add(reload);
            var right=new Panel {Dock=DockStyle.Fill};right.Controls.Add(content);right.Controls.Add(top);Controls.Add(right);Controls.Add(navigation);
            timer.Tick+=async(s,e)=>await Tick();
            VisibleChanged+=async(s,e)=>{if(!integrated&&Visible&&ready&&current<0)await Run(()=>LoadPage(0),TrainerLanguage.T("正在读取服务状态…"));};
            Disposed+=(s,e)=>{timer.Dispose();foreach(var im in icons.Values)im.Dispose();};
        }
        internal void UseIntegratedNavigation(){integrated=true;navigation.Visible=false;sectionHeader.Visible=false;content.Padding=Padding.Empty;}
        internal bool AllowLeave(){return !Dirty()||Confirm(TrainerLanguage.T("当前页有未保存的修改，是否放弃并切换？"));}
        internal async Task NavigateTo(int index,bool force)
        {if(!ready){status(TrainerLanguage.T("管理服务未连接，请重新启动本机服务。"),true);return;}if(!force&&current==index&&pageRole==RoleId())return;await Run(()=>LoadPage(index),TrainerLanguage.T("正在读取")+TrainerLanguage.T(pages[index])+"…");}
        internal async Task RefreshCurrent(){if(AllowLeave())await NavigateTo(Math.Max(0,current),true);}
        internal void SetReady(bool management){ready=management;if(!ready){content.Controls.Add(AdminUi.Note(TrainerLanguage.T("本机管理服务需要更新。请完成安装后重新一键启动。")));return;}timer.Start();if(!integrated&&Visible&&current<0)BeginInvoke((Action)(async()=>await Run(()=>LoadPage(0),TrainerLanguage.T("正在读取服务状态…"))));}
        internal void ShowManagement(){if(!integrated&&current<0&&ready)BeginInvoke((Action)(async()=>await Run(()=>LoadPage(0),TrainerLanguage.T("正在读取服务状态…"))));}
        internal void RoleChanged(){if(current>=2&&current<=7&&pageRole!=RoleId())roleBadge.Text=TrainerLanguage.T("当前角色已切换；请刷新此页后操作。");}
        private int RoleId(){try{return selectedId();}catch{return 0;}}
        private int NeedRole(){int id=RoleId();if(id<=0)throw new InvalidOperationException(TrainerLanguage.T("请先点击右上角选择角色。"));if(pageRole!=id)throw new InvalidOperationException(TrainerLanguage.T("所选角色已变化，请先刷新此页。"));return id;}
        private bool Dirty(){return isDirty!=null&&isDirty();}
        private bool Confirm(string text){return TrainerDialogs.Show(this,text,TrainerLanguage.T("阿拉德·归途 · 修改器"),MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;}
        private async Task Run(Func<Task> action,string message){if(loading)return;loading=true;try{await execute(action,message);}finally{loading=false;}}
        private Button Button(string label,Func<Task> action,bool primary)
        {return Theme.Button(label,async(s,e)=>await Run(action,TrainerLanguage.T("正在处理：")+TrainerLanguage.T(label)+"…"),primary);}
        private async Task<object> Get(string path){return await Task.Run(()=>api.Get("/api/admin?lang="+Uri.EscapeDataString(TrainerLanguage.Locale)+"&path="+Uri.EscapeDataString(path)));}
        private async Task<Dictionary<string,object>> Post(string path,Dictionary<string,object> body)
        {var r=await Task.Run(()=>api.Post("/api/admin",J.D("path",path,"body",body)));string m=J.S(r,"message");status(String.IsNullOrEmpty(m)?TrainerLanguage.T("操作已完成。"):m,false);return r;}
        private async Task LoadPage(int index)
        {
            itemHover.Hide();if(index<0)index=0;epoch++;current=index;pageRole=RoleId();isDirty=null;pageRefresh=null;channelGrid=null;
            foreach(Control c in content.Controls.Cast<Control>().ToArray()){content.Controls.Remove(c);c.Dispose();}
            heading.Text=pages[index];roleBadge.Text=pageRole>0?TrainerLanguage.T("当前角色编号 ")+pageRole+TrainerLanguage.T(" · 角色功能使用右上角所选角色"):TrainerLanguage.T("请点击右上角选择角色以使用角色功能");
            changing=true;navigation.SelectedIndex=index;changing=false;
            switch(index){case 0:await BuildRuntime();break;case 1:await BuildChannels();break;case 2:await BuildFullRole();break;case 3:await BuildMail();break;case 4:await BuildAvatars();break;case 5:await BuildInventory();break;case 6:await BuildQuests();break;case 7:await BuildPremium();break;case 8:await BuildRewards();break;case 9:await BuildEvents();break;case 10:await BuildConfigs();break;case 11:await BuildDifficulty();break;case 12:await BuildBackups();break;}
            status(TrainerLanguage.T("已载入“")+TrainerLanguage.T(pages[index])+"”。",false);
        }
        private async Task Tick()
        {
            if(!Visible||!Enabled||!ready||loading||ticking||pageRefresh==null||Dirty())return;ticking=true;
            try{await pageRefresh();}catch(Exception ex){status(ex.Message,true);}finally{ticking=false;}
        }
        private async Task ControlAction(string action)
        {
            await Task.Run(()=>{using(var p=Process.Start(new ProcessStartInfo(Path.Combine(Program.Root,"DNF60控制台.exe"),"--"+action){WorkingDirectory=Program.Root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){if(p==null||!p.WaitForExit(130000)||p.ExitCode!=0)throw new InvalidOperationException(TrainerLanguage.T("启停未完成，请查看操作日志。"));}});
        }
        private async Task BuildRuntime()
        {
            var state=AdminUi.Note("");var logs=Theme.Combo("服务日志来源",180);logs.Items.AddRange(new object[]{"游戏服务","在线修改服务","桌面修改器","一键启停"});logs.SelectedIndex=0;
            var text=AdminUi.ReadText("");content.Controls.Add(text);
            var bar=AdminUi.Bar(Button("启动游戏服务",async()=>{await ControlAction("server-only");await pageRefresh();},true),Button("启动游戏",async()=>{await ControlAction("start");},false),Button("停止游戏服务并备份",async()=>{if(Confirm(TrainerLanguage.T("将保存并关闭当前游戏和游戏服务；修改器与管理服务继续运行，可进行存档恢复。"))){await ControlAction("game-stop");await pageRefresh();}},false),Theme.Button("一键关闭全部",(sender,args)=>{if(!Confirm(TrainerLanguage.T("将保存存档并关闭游戏、服务和修改器，是否继续？")))return;Process.Start(new ProcessStartInfo(Path.Combine(Program.Root,"DNF60控制台.exe"),"--stop"){WorkingDirectory=Program.Root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});},false));
            content.Controls.Add(AdminUi.Bar(logs,Button("读取最新日志",async()=>await LoadLog(logs,text),false)));content.Controls.Add(state);content.Controls.Add(bar);
            pageRefresh=async()=>{int version=epoch;var data=J.O(await Task.Run(()=>api.Get("/api/admin/runtime")));Dictionary<string,object> overview=null;try{overview=J.O(await Get("/api/overview"));}catch{}if(version!=epoch||state.IsDisposed)return;state.Text=(J.B(data,"gameRunning")?TrainerLanguage.T("● 游戏服务运行中 · 后台模式"):TrainerLanguage.T("● 游戏服务已停止"))+"\n"+(overview==null?TrainerLanguage.T("管理接口未连接"):TrainerLanguage.T("账号 ")+J.S(overview,"accounts")+TrainerLanguage.T(" · 角色 ")+J.S(overview,"characters")+TrainerLanguage.T(" · 在线 ")+J.S(overview,"online"))+TrainerLanguage.T("\n游戏内点击“结束游戏”后，自动保存并关闭修改器与后台服务。");await LoadLog(logs,text);};
            logs.SelectedIndexChanged+=async(s,e)=>await Run(()=>LoadLog(logs,text),TrainerLanguage.T("正在读取日志…"));await pageRefresh();
        }
        private async Task LoadLog(ComboBox source,TextBox text)
        {string name=Convert.ToString(source.SelectedItem);int v=epoch;string value=await Task.Run(()=>TrainerDisplay.LogText(NativeLogs.Read(Program.Root,name)));if(v!=epoch||text.IsDisposed)return;if(value!=text.Text){text.Text=value;text.SelectionStart=text.TextLength;text.ScrollToCaret();}}
        private static string ChannelsSignature(Dictionary<string,object> d){return String.Join(",",AdminUi.Array(d["active"]).Select(Convert.ToString))+";"+J.S(d,"generation");}
        private async Task BuildChannels()
        {
            channelData=J.O(await Get("/api/channels"));channelSignature=ChannelsSignature(channelData);
            var runtime=J.O(await Task.Run(()=>api.Get("/api/admin/runtime")));
            var available=AdminUi.Array(channelData["available"]).Select(Convert.ToInt32).ToArray();var active=new HashSet<int>(AdminUi.Array(channelData["active"]).Select(Convert.ToInt32));
            var ports=AdminUi.Array(runtime["channelPorts"]).Select(Convert.ToInt32).OrderBy(x=>x).ToArray();var names=J.O(channelData["names"]);
            channelGrid=Theme.Grid("频道开关");channelGrid.RowTemplate.Height=33;Theme.CheckColumn(channelGrid,"enabled");channelGrid.Columns[0].HeaderText=TrainerLanguage.T("开放");Theme.TextColumn(channelGrid,"id",TrainerLanguage.T("频道"),65,false,false);Theme.TextColumn(channelGrid,"name",TrainerLanguage.T("频道名称"),200,true,false);Theme.TextColumn(channelGrid,"address",TrainerLanguage.T("地址"),120,false,false);Theme.TextColumn(channelGrid,"port",TrainerLanguage.T("监听端口"),95,false,false);
            for(int i=0;i<available.Length;i++){int id=available[i];channelGrid.Rows.Add(active.Contains(id),id,J.S(names,id.ToString()),"127.0.0.1",ports.Length==available.Length?ports[i].ToString():"待服务就绪");}
            isDirty=()=>channelGrid!=null&&!channelGrid.IsDisposed&&channelGrid.Rows.Cast<DataGridViewRow>().Any(r=>Convert.ToBoolean(r.Cells[0].Value)!=active.Contains(Convert.ToInt32(r.Cells[1].Value)));
            content.Controls.Add(channelGrid);content.Controls.Add(AdminUi.Bar(Button("保存频道开关",async()=>{channelGrid.EndEdit();var chosen=channelGrid.Rows.Cast<DataGridViewRow>().Where(r=>Convert.ToBoolean(r.Cells[0].Value)).Select(r=>Convert.ToInt32(r.Cells[1].Value)).ToArray();if(chosen.Length==0)throw new InvalidOperationException(TrainerLanguage.T("至少保留一个频道。"));var now=J.O(await Get("/api/channels"));if(ChannelsSignature(now)!=channelSignature)throw new InvalidOperationException(TrainerLanguage.T("频道已被其他操作修改，请刷新后再保存。"));var closing=active.Except(chosen).ToArray();if(closing.Length>0&&!Confirm(TrainerLanguage.T("将关闭频道 ")+String.Join(", ",closing)+TrainerLanguage.T("，其中的玩家会断线。是否保存？")))return;await Post("/api/channels/set",J.D("active",chosen));await LoadPage(1);},true),Button("全选",()=>{foreach(DataGridViewRow r in channelGrid.Rows)r.Cells[0].Value=true;return Task.FromResult(0);},false)));
            content.Controls.Add(AdminUi.Note(TrainerLanguage.T("勾选要开放的频道后保存，约 1 秒生效。关闭频道会断开其中的玩家。当前开放：")+String.Join("、",active)));
            pageRefresh=async()=>{var now=J.O(await Get("/api/channels"));if(current==1&&!Dirty()&&ChannelsSignature(now)!=channelSignature)await LoadPage(1);};
        }
        private async Task BuildFullRole()
        {
            int id=NeedRole();var schema=J.O(await Get("/api/character/editor"));var r=J.O(await Get("/api/character?id="+id));var values=J.O(r["editor_values"]);
            var scroll=AdminUi.Scroll();var stack=new List<Control>();var sheets=new List<FormSheet>();var basics=new FormSheet();
            basics.TextField("name",TrainerLanguage.T("角色名称"),J.S(r,"name"),30,false);
            var careers=J.Rows(schema["careers"]);int careerIndex=careers.FindIndex(c=>J.N(c,"job")==J.N(r,"job")&&J.N(c,"grow")==J.N(r,"grow_type")&&J.N(c,"awakening")==J.N(r,"awakening_type"));
            basics.ChoiceField("career",TrainerLanguage.T("职业与觉醒"),careers.Select((c,i)=>new Choice(i,J.S(c,"name"))),careerIndex);stack.Add(basics);
            foreach(var group in J.Rows(schema["fields"]).GroupBy(f=>J.S(f,"group")))
            {
                var sheet=new FormSheet();sheets.Add(sheet);stack.Add(AdminUi.Note(group.Key));stack.Add(sheet);
                foreach(var f in group)
                {
                    string key=J.S(f,"key");decimal val=AdminUi.Dec(values,key);var options=J.V(f,"options");
                    if(options!=null)sheet.ChoiceField(key,J.S(f,"label"),J.Rows(options).Select(o=>new Choice(J.N(o,"value"),J.S(o,"label"))),(int)val);
                    else{decimal scale=AdminUi.Dec(f,"scale");if(scale<=0)scale=1;int places=(int)Math.Round(Math.Log10((double)scale));sheet.Number(key,J.S(f,"label"),AdminUi.Dec(f,"minimum")/scale,AdminUi.Dec(f,"maximum")/scale,val,places);}
                }
            }
            isDirty=()=>basics.Dirty||sheets.Any(f=>f.Dirty);
            var fieldMap=sheets.SelectMany(f=>f.Fields).ToDictionary(p=>p.Key,p=>p.Value);
            var lvl=(NumericUpDown)fieldMap["level"];var exp=(NumericUpDown)fieldMap["exp"];var thresholds=AdminUi.Array(J.O(schema["progression"])["thresholds"]).Select(Convert.ToDecimal).ToArray();
            lvl.ValueChanged+=(s,e)=>{int l=(int)lvl.Value;exp.Value=l==1?0:thresholds[l-2];};
            var expert=(ComboBox)fieldMap["expert_job"];var expertLevel=(NumericUpDown)fieldMap["expert_level"];var expertExp=(NumericUpDown)fieldMap["expert_exp"];
            var experts=J.Rows(schema["experts"]);bool expertBinding=false;
            Func<Dictionary<string,object>> selectedExpert=()=>experts.FirstOrDefault(x=>J.N(x,"id")==((Choice)expert.SelectedItem).Id);
            Action syncExpert=()=>{var meta=selectedExpert();int count=meta==null?0:AdminUi.Array(J.V(meta,"bounds")).Length;expertLevel.Enabled=count>0;expertExp.Enabled=count>0;expertLevel.Maximum=Math.Max(1,count);};syncExpert();
            expert.SelectedIndexChanged+=(s,e)=>{expertBinding=true;syncExpert();expertLevel.Value=1;expertExp.Value=0;expertBinding=false;};
            expertLevel.ValueChanged+=(s,e)=>{if(expertBinding)return;var m=selectedExpert();var b=m==null?new object[0]:AdminUi.Array(J.V(m,"bounds"));int l=(int)expertLevel.Value;expertExp.Value=l<=1?0:Convert.ToDecimal(b[l-2])+1;};
            expertExp.ValueChanged+=(s,e)=>{if(expertBinding)return;var m=selectedExpert();var bounds=m==null?new object[0]:AdminUi.Array(J.V(m,"bounds"));if(bounds.Length==0)return;expertBinding=true;expertLevel.Value=Math.Min(expertLevel.Maximum,1+bounds.Count(v=>expertExp.Value>Convert.ToDecimal(v)));expertBinding=false;};
            var save=Button("保存完整角色属性",async()=>{if(J.B(r,"online"))throw new InvalidOperationException(TrainerLanguage.T("完整属性编辑需要角色离线；在线等级、SP、金币等请使用“角色属性”页。"));var changes=new Dictionary<string,object>();foreach(var f in sheets)foreach(var p in f.Changes())changes[p.Key]=p.Value;var basic=basics.Changes();if(basic.ContainsKey("career")){var c=careers[Convert.ToInt32(basic["career"])];changes["job"]=J.N(c,"job");changes["grow_type"]=J.N(c,"grow");changes["awakening_type"]=J.N(c,"awakening");if(!Confirm(TrainerLanguage.T("改变职业或觉醒可能重建技能，是否继续？")))return;}var body=J.D("id",NeedRole(),"values",changes);if(basic.ContainsKey("name"))body["name"]=basic["name"];if(changes.Count==0&&!basic.ContainsKey("name")){status(TrainerLanguage.T("没有未保存的角色修改。"),false);return;}await Post("/api/character/edit",body);await refreshRole();await LoadPage(2);},true);
            var gm=new CheckBox {Text=TrainerLanguage.T("允许账号使用 GM 指令"),Checked=J.B(r,"gm_enabled"),AutoSize=true};
            stack.Add(AdminUi.Bar(save));stack.Add(AdminUi.Note(TrainerLanguage.T("账号管理 · ")+J.S(r,"account_name")));
            stack.Add(AdminUi.Bar(gm,Button("保存 GM 权限",async()=>{NeedRole();await Post("/api/account/gm",J.D("id",J.N(r,"account_id"),"enabled",gm.Checked));},false)));
            stack.Add(AdminUi.Note(TrainerLanguage.T("本机账号在原版启动器中按名称选择，登录由程序自动完成。")));
            AdminUi.Stack(scroll,stack.ToArray());content.Controls.Add(scroll);content.Controls.Add(AdminUi.Note(TrainerLanguage.T("完整属性共 ")+J.Rows(schema["fields"]).Count+TrainerLanguage.T(" 项，仅保存改动的字段。")+(J.B(r,"online")?TrainerLanguage.T("当前角色在线；完整属性需要返回角色选择后刷新。"):TrainerLanguage.T("角色离线，可编辑。"))));
        }
    }
}
