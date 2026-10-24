// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Dnf60Desktop
{
    internal sealed class WorkspaceRoute
    {
        internal readonly string Name,Description;internal readonly int Page,Admin;
        internal WorkspaceRoute(string name,string description,int page,int admin=-1){Name=name;Description=description;Page=page;Admin=admin;}
    }

    internal sealed partial class TrainerWindow
    {
        private readonly string[] sectionNames={"概览","角色管理","物品管理","游戏玩法","服务设置","备份记录"};
        private readonly string[] sectionCaptions={"角色状态与常用操作","属性、技能与职业","道具、装备与时装","邮件、任务与奖励","频道、玩法与运行状态","存档恢复与修改记录"};
        private readonly WorkspaceRoute[][] routes={
            new[]{new WorkspaceRoute("概览","查看当前角色，快速进入常用功能。",6)},
            new[]{new WorkspaceRoute("常用属性","金币、点券、等级和 SP，按需修改并单独保存。",2),new WorkspaceRoute("职业与觉醒","选择当前版本支持的职业与觉醒阶段。",3),new WorkspaceRoute("完整属性","编辑经验、四维、副职业等完整数据，需要角色离线。",5,2)},
            new[]{new WorkspaceRoute("物品发放","搜索物品、加入清单，统一设置后批量发放。",0),new WorkspaceRoute("背包与装备","勾选已有物品，调整数量、强化或增幅。",1),new WorkspaceRoute("时装发送","挑选时装和原版属性，通过游戏邮件发送。",5,4),new WorkspaceRoute("背包清理","按名称与位置筛选物品，删除需要角色离线。",5,5)},
            new[]{new WorkspaceRoute("邮件管理","个人邮件、附件和全员邮件，发送前可预览。",5,3),new WorkspaceRoute("任务管理","查看任务条件与奖励，多选后完成任务。",5,6),new WorkspaceRoute("契约与副本","管理账号契约和副本难度权限。",5,7),new WorkspaceRoute("泡点与奖励","分别设置在线泡点、初始资源和定时邮件。",5,8),new WorkspaceRoute("活动管理","设置常用活动、自定义参数和生效时间。",5,9)},
            new[]{new WorkspaceRoute("运行与日志","统一启动、停止游戏服务，查看实时日志。",5,0),new WorkspaceRoute("频道管理","选择开放频道，保存后约 1 秒生效。",5,1),new WorkspaceRoute("假人与玩法","调整常用玩法、假人人数及详细配置。",5,10),new WorkspaceRoute("怪物难度","选择新进入副本时采用的怪物模式。",5,11)},
            new[]{new WorkspaceRoute("存档备份","创建、导出和恢复数据库备份。",5,12),new WorkspaceRoute("修改记录","查看操作结果和修改前的角色快照。",4)}
        };
        private readonly NavigationButton[] navButtons=new NavigationButton[6];
        private readonly int[] selectedSubpages=new int[6];
        private readonly FlowLayoutPanel subNavigation=new FlowLayoutPanel();
        private readonly Label workspaceTitle=Theme.Label(TrainerLanguage.T("概览"),22,Theme.Text),workspaceDescription=Theme.Label("",9,Theme.Muted),breadcrumb=Theme.Label(TrainerLanguage.T("工作台 / 概览"),9,Theme.Muted);
        private readonly Dictionary<string,Label> dashboardValues=new Dictionary<string,Label>();
        private readonly ToolTip tips=new ToolTip {AutoPopDelay=12000};
        private readonly ProgressBar activity=new ProgressBar {Style=ProgressBarStyle.Marquee,MarqueeAnimationSpeed=28,Dock=DockStyle.Bottom,Height=3,Visible=false};
        private Form rolePicker;
        private Form cartDialog;
        private Label catalogSelectionSummary;
        private Button catalogReview;
        private Button refreshPage;
        private Label dashboardHint;
        private int activeSection;
        private bool startWithManagement;
        private WorkspaceRoute CurrentRoute {get{return routes[activeSection][selectedSubpages[activeSection]];}}

        private void BuildModernLayout()
        {
            SuspendLayout();
            var sidebar=new Panel {Dock=DockStyle.Left,Width=TrainerLanguage.Locale=="zh-Hans"||TrainerLanguage.Locale=="zh-Hant"?208:242,BackColor=Theme.Side,Padding=new Padding(16,0,16,0)};
            var brand=new Panel {Dock=DockStyle.Top,Height=122};
            var logo=Theme.Label(TrainerLanguage.T("阿拉德·归途"),16,Color.FromArgb(244,191,96));logo.Font=new Font(logo.Font,FontStyle.Bold);logo.Location=new Point(10,32);
            var tag=Theme.Label(TrainerLanguage.T("本机角色与物品管理"),8,Color.FromArgb(133,153,180));tag.Location=new Point(12,72);brand.Controls.Add(logo);brand.Controls.Add(tag);
            var nav=new Panel {Dock=DockStyle.Top,Height=360};
            for(int i=5;i>=0;i--){int choice=i;var b=new NavigationButton {Kind=i,Text=sectionNames[i],Caption=sectionCaptions[i],AccessibleName=sectionNames[i]};b.Click+=(s,e)=>NavigateSection(choice,selectedSubpages[choice]);tips.SetToolTip(b,sectionNames[i]+Environment.NewLine+sectionCaptions[i]);nav.Controls.Add(b);navButtons[i]=b;}
            var foot=new Panel {Dock=DockStyle.Bottom,Height=126};serviceState.AutoSize=false;serviceState.SetBounds(8,11,174,36);serviceState.Font=new Font(Font.FontFamily,9);foot.Controls.Add(serviceState);
            var help=new LinkLabel {Text=TrainerLanguage.T("使用说明"),LinkColor=Color.FromArgb(157,176,204),ActiveLinkColor=Color.White,VisitedLinkColor=Color.FromArgb(157,176,204),AutoSize=true,Location=new Point(10,58),LinkBehavior=LinkBehavior.HoverUnderline};help.LinkClicked+=(s,e)=>AdminUi.Dialog(this,TrainerLanguage.T("使用说明"),TrainerDisplay.HelpText());foot.Controls.Add(help);
            var version=Theme.Label("ACT12  /  v"+typeof(TrainerWindow).Assembly.GetName().Version.ToString(3),8,Color.FromArgb(91,112,139));version.Location=new Point(10,89);foot.Controls.Add(version);
            sidebar.Controls.Add(nav);sidebar.Controls.Add(brand);sidebar.Controls.Add(foot);
            var main=new Panel {Dock=DockStyle.Fill,Padding=new Padding(27,17,23,8),BackColor=Theme.Bg};
            var header=new Panel {Dock=DockStyle.Top,Height=89};breadcrumb.Location=new Point(0,1);workspaceTitle.Location=new Point(-1,20);workspaceDescription.Location=new Point(1,62);workspaceDescription.AutoSize=false;workspaceDescription.AutoEllipsis=true;workspaceDescription.Size=new Size(770,21);header.Controls.Add(breadcrumb);header.Controls.Add(workspaceTitle);header.Controls.Add(workspaceDescription);
            refreshPage=Theme.Button("刷新页面",async(s,e)=>await RefreshWorkspace(),false);refreshPage.AutoSize=false;refreshPage.Size=new Size(116,34);refreshPage.Anchor=AnchorStyles.Top|AnchorStyles.Right;header.Controls.Add(refreshPage);
            header.Resize+=(s,e)=>{refreshPage.Location=new Point(Math.Max(500,header.Width-118),23);workspaceDescription.Width=Math.Max(450,header.Width-10);};
            var contextOuter=new Panel {Dock=DockStyle.Top,Height=81,Padding=new Padding(0,0,0,11)};var context=new SurfacePanel {Dock=DockStyle.Fill,Radius=10};contextOuter.Controls.Add(context);
            var avatar=new Panel {BackColor=Theme.Selected,Location=new Point(15,15),Size=new Size(40,40)};avatar.Paint+=(s,e)=>ModernDraw.Glyph(e.Graphics,1,new Rectangle(9,8,22,22),Theme.Gold);context.Controls.Add(avatar);
            title.Font=new Font(Font.FontFamily,12,FontStyle.Bold);title.Location=new Point(67,11);title.AutoSize=false;title.AutoEllipsis=true;title.Size=new Size(490,27);context.Controls.Add(title);
            meta.Location=new Point(68,39);meta.AutoSize=false;meta.AutoEllipsis=true;meta.Size=new Size(600,20);context.Controls.Add(meta);
            roleState.Location=new Point(690,22);roleState.Font=new Font(Font.FontFamily,9);context.Controls.Add(roleState);
            var chooseRole=Theme.Button("切换角色",(s,e)=>ShowRolePicker(),false);chooseRole.AutoSize=false;chooseRole.Size=new Size(148,33);context.Controls.Add(chooseRole);
            context.Resize+=(s,e)=>{chooseRole.Location=new Point(context.Width-164,18);roleState.Location=new Point(Math.Max(330,context.Width-314),27);title.Width=Math.Max(210,context.Width-350);meta.Width=Math.Max(210,context.Width-335);};
            subNavigation.Dock=DockStyle.Top;subNavigation.Height=48;subNavigation.Padding=new Padding(0,0,0,8);subNavigation.WrapContents=false;subNavigation.BackColor=Theme.Bg;
            status.Dock=DockStyle.Fill;status.TextAlign=ContentAlignment.MiddleLeft;status.AutoEllipsis=true;status.Font=new Font(Font.FontFamily,8.5f);status.ForeColor=Theme.Muted;status.Text=TrainerLanguage.T("正在连接本机服务…");status.AccessibleName=TrainerLanguage.T("操作状态");status.DoubleClick+=(s,e)=>{if(status.Text.Length>70)AdminUi.Dialog(this,TrainerLanguage.T("操作详情"),status.Text);};
            var bottom=new Panel {Dock=DockStyle.Bottom,Height=36};bottom.Controls.Add(status);bottom.Controls.Add(activity);
            work.Controls.Add(tabs);main.Controls.Add(work);main.Controls.Add(subNavigation);main.Controls.Add(contextOuter);main.Controls.Add(header);main.Controls.Add(bottom);Controls.Add(main);Controls.Add(sidebar);
            BuildCatalog();BuildBag();BuildAttributes();BuildCareer();BuildHistory();
            admin=new AdminPanel(api,RequireRole,RunActionTask,SetStatus,async()=>{if(selected!=null)await FetchRole(J.N(selected,"character_id"));},itemHover);admin.UseIntegratedNavigation();var adminHost=PagePanel();adminHost.Controls.Add(admin);AddTab("服务管理",adminHost);BuildDashboard();
            tabs.SelectedIndex=6;BuildSubNavigation();navButtons[0].Active=true;navButtons[0].Invalidate();BuildRolePicker();ResumeLayout(true);
        }

        private void BuildRolePicker()
        {
            rolePicker=new Form {Text=TrainerLanguage.T("选择角色"),Font=Font,BackColor=Theme.Bg,ForeColor=Theme.Text,StartPosition=FormStartPosition.CenterParent,Size=new Size(490,Math.Min(610,Screen.PrimaryScreen.WorkingArea.Height-60)),MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false,Padding=new Padding(20),Icon=Icon};
            var top=new Panel {Dock=DockStyle.Top,Height=111};var label=Theme.Label(TrainerLanguage.T("选择要管理的角色"),15,Theme.Text);label.Location=new Point(0,0);top.Controls.Add(label);
            roleSearch.SetBounds(0,42,283,28);top.Controls.Add(roleSearch);var search=Theme.Button("搜索",(s,e)=>RunAction(async()=>await LoadRoles(false,false),TrainerLanguage.T("正在搜索角色…")),true);search.SetBounds(291,39,100,34);search.AutoSize=false;top.Controls.Add(search);
            var online=Theme.Button("只看在线",(s,e)=>RunAction(async()=>{roleSearch.Text="";await LoadRoles(true,false);},TrainerLanguage.T("正在读取在线角色…")),false);online.Location=new Point(0,76);top.Controls.Add(online);
            roleSearch.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;RunAction(async()=>await LoadRoles(false,false),TrainerLanguage.T("正在搜索角色…"));}};
            roles.Dock=DockStyle.Fill;roles.Name="角色列表";roles.AccessibleName=TrainerLanguage.T("角色列表");roles.BorderStyle=BorderStyle.None;roles.BackColor=Theme.Field;roles.ForeColor=Theme.Text;roles.DrawMode=DrawMode.OwnerDrawFixed;roles.ItemHeight=72;roles.IntegralHeight=false;roles.DrawItem+=DrawRole;roles.DoubleClick+=(s,e)=>{if(roles.SelectedItem!=null)SelectRole(J.N(((RoleEntry)roles.SelectedItem).Data,"character_id"));};
            var bottom=new Panel {Dock=DockStyle.Bottom,Height=48};var select=Theme.Button("使用所选角色",(s,e)=>{if(roles.SelectedItem!=null)SelectRole(J.N(((RoleEntry)roles.SelectedItem).Data,"character_id"));},true);select.Dock=DockStyle.Right;bottom.Controls.Add(select);
            rolePicker.Controls.Add(roles);rolePicker.Controls.Add(top);rolePicker.Controls.Add(bottom);rolePicker.FormClosing+=(s,e)=>{if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;if(!busy)rolePicker.Hide();}};
        }
        private async void ShowRolePicker(){if(busy)return;if(tabs.SelectedIndex==5&&!admin.AllowLeave())return;int previous=selected==null?0:RequireRole();TrainerDisplay.ShowDialog(rolePicker,this);if(selected!=null&&RequireRole()!=previous&&CurrentRoute.Admin>=0&&loaded)await admin.NavigateTo(CurrentRoute.Admin,true);}
        private void SetNavigationEnabled(bool enabled){foreach(var b in navButtons)if(b!=null)b.Enabled=enabled;if(refreshPage!=null)refreshPage.Enabled=enabled;subNavigation.Enabled=enabled;activity.Visible=!enabled;}
        private void BuildSubNavigation()
        {
            subNavigation.SuspendLayout();foreach(Control c in subNavigation.Controls.Cast<Control>().ToArray()){subNavigation.Controls.Remove(c);c.Dispose();}
            for(int i=0;i<routes[activeSection].Length;i++){int pos=i;var route=routes[activeSection][i];var b=new ActionButton {Text=route.Name,Chip=true,Active=i==selectedSubpages[activeSection],AutoSize=true,BackColor=Theme.Bg,MinimumSize=new Size(98,34),AccessibleName=route.Name};b.Click+=(s,e)=>NavigateSection(activeSection,pos);subNavigation.Controls.Add(b);}subNavigation.ResumeLayout(true);
        }
        private async void NavigateSection(int group,int sub)
        {
            if(busy)return;itemHover.Hide();var target=routes[group][sub];bool same=activeSection==group&&selectedSubpages[group]==sub;
            if(tabs.SelectedIndex==5&&!same&&!admin.AllowLeave())return;
            activeSection=group;selectedSubpages[group]=sub;foreach(var b in navButtons){b.Active=b.Kind==group;b.Invalidate();}BuildSubNavigation();
            breadcrumb.Text=TrainerLanguage.F("工作台 / {0}",TrainerLanguage.T(sectionNames[group]));workspaceTitle.Text=target.Name;workspaceDescription.Text=target.Description;tabs.SelectedIndex=target.Page;
            if(target.Admin>=0&&loaded)await admin.NavigateTo(target.Admin,false);else if(target.Page==6)UpdateDashboard();
        }
        private async Task RefreshWorkspace()
        {
            if(busy)return;if(tabs.SelectedIndex==5){await admin.RefreshCurrent();return;}
            await RunActionTask(async()=>{if(selected!=null)await FetchRole(RequireRole());if(tabs.SelectedIndex==0)await LoadCatalogTask();if(tabs.SelectedIndex==4)await LoadHistory();await RefreshStatus();},TrainerLanguage.T("正在刷新页面…"));
        }
        private void ConfigureItemCart(Panel host,Panel editor)
        {
            cartDialog=new Form {Text=TrainerLanguage.T("待发放物品"),Font=Font,BackColor=Theme.Bg,ForeColor=Theme.Text,Size=new Size(930,530),MinimumSize=new Size(800,440),StartPosition=FormStartPosition.CenterParent,ShowInTaskbar=false,Icon=Icon};
            cartDialog.Controls.Add(editor);cartDialog.FormClosing+=(s,e)=>{if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;if(!busy)cartDialog.Hide();}};
            var footer=new SurfacePanel {Dock=DockStyle.Bottom,Height=58,Radius=8};
            catalogSelectionSummary=Theme.Label(TrainerLanguage.T("已选 0 种物品"),11,Theme.Text);catalogSelectionSummary.Font=new Font(catalogSelectionSummary.Font,FontStyle.Bold);catalogSelectionSummary.Location=new Point(15,18);footer.Controls.Add(catalogSelectionSummary);
            catalogReview=Theme.Button("查看清单并发放",(s,e)=>{if(cart.Count>0)TrainerDisplay.ShowDialog(cartDialog,this);},true);catalogReview.AutoSize=false;catalogReview.Size=new Size(180,34);catalogReview.Enabled=false;footer.Controls.Add(catalogReview);
            var clear=Theme.Button("清空选择",(s,e)=>{cart.Clear();BindCart();SyncChecks();},false);clear.AutoSize=false;clear.Size=new Size(125,34);footer.Controls.Add(clear);
            footer.Resize+=(s,e)=>{catalogReview.Location=new Point(footer.Width-194,12);clear.Location=new Point(footer.Width-329,12);};host.Controls.Add(footer);
        }
        private void BuildDashboard()
        {
            var panel=PagePanel();AddTab("概览",panel);var scroll=new Panel {Dock=DockStyle.Fill,AutoScroll=true,BackColor=Theme.Bg};panel.Controls.Add(scroll);
            var metrics=new TableLayoutPanel {Dock=DockStyle.Top,Height=114,ColumnCount=4,BackColor=Theme.Bg};for(int i=0;i<4;i++)metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
            string[] keys={"level","gold","cera","sp"},names={"角色等级","角色金币","商城点券","技能点 SP"},notes={"当前角色","角色持有","账号共享","方案 A / 方案 B"};
            for(int i=0;i<4;i++){var card=new SurfacePanel {Dock=DockStyle.Fill,Margin=new Padding(0,0,i==3?0:12,8)};var caption=Theme.Label(names[i],9,Theme.Muted);caption.Location=new Point(16,14);var value=new MetricLabel {Text="—"};value.Location=new Point(13,34);value.AutoSize=false;value.AutoEllipsis=true;value.Size=new Size(240,38);dashboardValues[keys[i]]=value;var hint=Theme.Label(notes[i],8,Theme.Muted);hint.Location=new Point(17,80);card.Controls.Add(caption);card.Controls.Add(value);card.Controls.Add(hint);card.Resize+=(s,e)=>value.Width=Math.Max(90,card.Width-25);metrics.Controls.Add(card,i,0);}
            var shortcutsTitle=new Panel {Dock=DockStyle.Top,Height=48};var a=Theme.Label(TrainerLanguage.T("常用操作"),12,Theme.Text);a.Font=new Font(a.Font,FontStyle.Bold);a.Location=new Point(1,17);shortcutsTitle.Controls.Add(a);
            var shortcuts=new TableLayoutPanel {Dock=DockStyle.Top,Height=216,ColumnCount=2,RowCount=2,BackColor=Theme.Bg};shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent,50));shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            shortcuts.Controls.Add(new DashboardTile(TrainerLanguage.T("修改角色属性"),TrainerLanguage.T("金币、点券、等级和技能点"),1,(s,e)=>NavigateSection(1,0)),0,0);shortcuts.Controls.Add(new DashboardTile(TrainerLanguage.T("批量发放物品"),TrainerLanguage.T("搜索图标和名称，多选并设置数量"),2,(s,e)=>NavigateSection(2,0)),1,0);shortcuts.Controls.Add(new DashboardTile(TrainerLanguage.T("管理游戏频道"),TrainerLanguage.T("查看频道，选择开放范围"),4,(s,e)=>NavigateSection(4,1)),0,1);shortcuts.Controls.Add(new DashboardTile(TrainerLanguage.T("备份与恢复存档"),TrainerLanguage.T("创建备份、导出或恢复角色"),5,(s,e)=>NavigateSection(5,0)),1,1);
            var info=new SurfacePanel {Dock=DockStyle.Top,Height=116,Padding=new Padding(18)};var infoTitle=Theme.Label(TrainerLanguage.T("本机状态"),11,Theme.Text);infoTitle.Font=new Font(infoTitle.Font,FontStyle.Bold);infoTitle.Location=new Point(18,17);info.Controls.Add(infoTitle);dashboardHint=Theme.Label(TrainerLanguage.T("正在连接本机游戏服务…"),9,Theme.Muted);dashboardHint.AutoSize=false;dashboardHint.Size=new Size(900,60);dashboardHint.Location=new Point(19,46);info.Controls.Add(dashboardHint);info.Resize+=(s,e)=>dashboardHint.Width=Math.Max(300,info.Width-35);
            scroll.Controls.Add(info);scroll.Controls.Add(shortcuts);scroll.Controls.Add(shortcutsTitle);scroll.Controls.Add(metrics);
        }
        private void UpdateDashboard()
        {
            if(dashboardValues.Count==0)return;
            if(selected!=null){dashboardValues["level"].Text="Lv. "+J.S(selected,"level");dashboardValues["gold"].Text=J.N(selected,"gold").ToString("N0");dashboardValues["cera"].Text=J.N(selected,"cera").ToString("N0");dashboardValues["sp"].Text=J.S(selected,"skill_points")+" / "+J.S(selected,"skill_points2");tips.SetToolTip(meta,meta.Text);}
            if(dashboardHint!=null)dashboardHint.Text=serviceState.Text+"    "+roleState.Text+Environment.NewLine+notice.Text+Environment.NewLine+TrainerLanguage.T("游戏内点击“结束游戏”后，自动保存并关闭修改器与后台服务。");
        }
    }
}
