// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// The same native account pane can be hosted in the original launcher or shown
// in its own window when the legacy launcher crashes on a target computer.
internal sealed class LocalAccountWindow : Form
{
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child,IntPtr parent);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h,int index,int value);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr h,int x,int y,int w,int height,bool repaint);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h,out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h,int command);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr parent,int id);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr h);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr h,StringBuilder value,int count);
    [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr h,bool enabled);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
    private delegate void WinEvent(IntPtr hook,uint evt,IntPtr h,int obj,int child,uint thread,uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEvent callback,uint pid,uint thread,uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
    private struct Rect { public int Left,Top,Right,Bottom; }
    private readonly IntPtr host;
    private readonly int hostPid;
    private readonly Func<Dictionary<string,object>,Dictionary<string,object>> request;
    private readonly Func<string,Action<string>,Func<bool>,Task> launch;
    private readonly Color ink=Color.FromArgb(242,238,228),muted=Color.FromArgb(170,174,181),gold=Color.FromArgb(224,182,105),bg=Color.FromArgb(29,32,38);
    private readonly Panel body=new Panel(),createPane=new Panel(),listPane=new Panel();
    private readonly ListBox accounts=new ListBox();
    private readonly TextBox name=new TextBox();
    private readonly Label heading=new Label(),caption=new Label(),state=new Label();
    private readonly Label languageInfo=new Label(),nameLabel=new Label(),nameHint=new Label();
    private readonly LanguagePicker language=new LanguagePicker();
    private readonly Button create=new Button(),login=new Button(),save=new Button(),back=new Button();
    private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=500};
    private List<Dictionary<string,object>> profiles=new List<Dictionary<string,object>>();
    private bool busy,creating,loaded,closing;
    private Size hostSize=Size.Empty;
    private readonly WinEvent nativeCallback;
    private IntPtr nativeHook;
    private bool hiddenStateRecorded;
    private string statusText="账号和角色随存档保存，下次打开会记住当前选择。";
    private bool statusIsError;

    internal LocalAccountWindow(IntPtr parent,int parentPid,
        Func<Dictionary<string,object>,Dictionary<string,object>> transport,
        Func<string,Action<string>,Func<bool>,Task> start,Action visible=null)
    {
        host=parent;hostPid=parentPid;request=transport;launch=start;
        nativeCallback=(hook,evt,h,obj,child,thread,time)=>{if(obj==0&&IsLegacyField(h))HideLegacyField(h);};
        Name="AradReturnAccounts";Text="阿拉德·归途 · 选择账号";Font=new Font("Microsoft YaHei UI",9F);
        FormBorderStyle=host==IntPtr.Zero?FormBorderStyle.FixedSingle:FormBorderStyle.None;
        AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(620,369);BackColor=bg;ForeColor=ink;
        ShowInTaskbar=host==IntPtr.Zero;StartPosition=FormStartPosition.CenterScreen;MaximizeBox=false;
        if(host==IntPtr.Zero)
        {
            string icon=Path.Combine(Dnf60Control.Client,"DNF.ico");
            if(File.Exists(icon))Icon=new Icon(icon);
        }
        DoubleBuffered=true;
        heading.Font=new Font(Font.FontFamily,17F,FontStyle.Bold);heading.ForeColor=ink;
        caption.Font=Font;caption.ForeColor=muted;
        state.ForeColor=muted;state.AutoEllipsis=true;state.Name="AccountStatus";
        body.BackColor=bg;createPane.BackColor=bg;listPane.BackColor=bg;
        name.Name="LocalAccountName";name.AccessibleName="账号名称";name.MaxLength=24;name.Font=new Font(Font.FontFamily,12F);
        name.BorderStyle=BorderStyle.FixedSingle;name.BackColor=Color.FromArgb(46,50,58);name.ForeColor=ink;
        name.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;if(!busy)CreateAccount();}};
        nameLabel.Text="账号名称";nameLabel.ForeColor=ink;nameLabel.AutoSize=true;
        nameHint.Text="给这个账号起个名字，角色和进度会保存在这里。";nameHint.ForeColor=muted;nameHint.AutoSize=false;
        nameLabel.SetBounds(0,10,500,22);name.SetBounds(0,42,540,34);nameHint.SetBounds(0,89,540,24);
        createPane.Controls.AddRange(new Control[]{nameLabel,name,nameHint});
        accounts.Name="SavedLocalAccounts";accounts.AccessibleName="已保存的账号";accounts.DrawMode=DrawMode.OwnerDrawFixed;accounts.ItemHeight=61;
        accounts.BorderStyle=BorderStyle.None;accounts.BackColor=Color.FromArgb(36,40,47);accounts.ForeColor=ink;accounts.IntegralHeight=false;
        accounts.DrawItem+=(s,e)=>DrawAccount(e);accounts.SelectedIndexChanged+=(s,e)=>{login.Enabled=!busy&&accounts.SelectedIndex>=0;accounts.Invalidate();};
        accounts.DoubleClick+=(s,e)=>Login();listPane.Controls.Add(accounts);
        ConfigureButton(create,"新建账号",false,()=>ShowCreate());
        ConfigureButton(login,"选择账号登录",true,()=>Login());
        ConfigureButton(save,"创建并选中",true,()=>CreateAccount());
        ConfigureButton(back,"返回账号列表",false,()=>ShowList());
        LanguageSupport.Load(Dnf60Control.Root);
        language.Name="GameLanguage";language.AccessibleName="语言 / Language";
        language.BackColor=Color.FromArgb(46,50,58);language.ForeColor=ink;
        language.Bind(LanguageSupport.Choices);
        language.SelectedIndex=Array.FindIndex(LanguageSupport.Choices,x=>x.Id==LanguageSupport.Current);
        languageInfo.Name="GameLanguageCoverage";languageInfo.ForeColor=muted;languageInfo.Font=new Font(Font.FontFamily,8F);languageInfo.AutoEllipsis=true;
        language.SelectedIndexChanged+=(s,e)=>
        {
            if(busy||language.SelectedItem==null)return;
            string previous=LanguageSupport.Current;
            try{LanguageSupport.Select(((LanguageChoice)language.SelectedItem).Id,true);ApplyLanguage();}
            catch(Exception error){language.SelectedIndex=Array.FindIndex(LanguageSupport.Choices,x=>x.Id==previous);SetStatus(error.Message,true);}
        };
        body.Controls.AddRange(new Control[]{createPane,listPane});
        Controls.AddRange(new Control[]{heading,caption,language,languageInfo,body,state,create,login,save,back});
        body.Visible=false;create.Visible=login.Visible=save.Visible=back.Visible=false;
        Resize+=(s,e)=>LayoutPane();
        Shown+=async(s,e)=>
        {
            if(host!=IntPtr.Zero)
            {
                if(!ParentAlive()){Close();return;}
                nativeHook=SetWinEventHook(0x8000,0x8002,IntPtr.Zero,nativeCallback,(uint)hostPid,0,0);
                SetWindowLong(Handle,-16,(GetWindowLong(Handle,-16)&~unchecked((int)0x80000000))|0x40000000);
                SetParent(Handle,host);FitHost();HideLegacyFields();ShowWindow(Handle,5);
                timer.Start();
            }
            if(visible!=null)visible();
            await Work("account-list",async()=>
            {
                var saved=await Task.Run(()=>request(Data("operation","list")));
                if(!ParentAlive())throw new OperationCanceledException();
                ApplyState(saved);loaded=true;
                SetStatus("账号和角色随存档保存，下次打开会记住当前选择。");
            });
        };
        timer.Tick+=(s,e)=>{if(!ParentAlive()){closing=true;if(!busy)Close();}else{FitHost();HideLegacyFields();}};
        FormClosing+=(s,e)=>
        {
            if(!busy)return;
            e.Cancel=true;
            if(host==IntPtr.Zero&&e.CloseReason==CloseReason.UserClosing&&!closing)
            {closing=true;SetStatus("正在取消并完成收尾，请稍候…");}
        };
        FormClosed+=(s,e)=>{timer.Dispose();if(nativeHook!=IntPtr.Zero)UnhookWinEvent(nativeHook);};
        ApplyLanguage();LayoutPane();
    }

    internal bool ParentAlive()
    {
        if(host==IntPtr.Zero)return !closing;
        uint pid;return IsWindow(host)&&GetWindowThreadProcessId(host,out pid)!=0&&pid==(uint)hostPid;
    }
    private bool IsLegacyField(IntPtr field)
    {
        if(field==IntPtr.Zero||GetParent(field)!=host)return false;
        int id=GetDlgCtrlID(field);if(id!=1201&&id!=1202)return false;
        uint pid;GetWindowThreadProcessId(field,out pid);if(pid!=(uint)hostPid)return false;
        var name=new StringBuilder(32);GetClassName(field,name,name.Capacity);
        return String.Equals(name.ToString(),"Edit",StringComparison.OrdinalIgnoreCase);
    }
    private void HideLegacyField(IntPtr field)
    {
        EnableWindow(field,false);ShowWindow(field,0);
        SetWindowPos(field,IntPtr.Zero,-32000,-32000,1,1,0x0010|0x0004|0x0080);
    }
    private void HideLegacyFields()
    {
        int disabled=0;
        foreach(int id in new[]{1201,1202})
        {
            IntPtr field=GetDlgItem(host,id);
            if(!IsLegacyField(field))continue;
            HideLegacyField(field);if(!IsWindowVisible(field)&&!IsWindowEnabled(field))disabled++;
        }
        if(disabled==2&&!hiddenStateRecorded)
        {
            hiddenStateRecorded=true;
            try
            {
                Directory.CreateDirectory(Path.Combine(Dnf60Control.Root,"logs"));
                File.WriteAllText(Path.Combine(Dnf60Control.Root,"logs","account-window-state.json"),
                    new JavaScriptSerializer().Serialize(new {legacyFieldsHidden=true,legacyFieldsDisabled=true,legacyFieldCount=disabled,originalLauncherPid=hostPid,time=DateTime.Now.ToString("o")}),new UTF8Encoding(false));
            }
            catch(IOException){}
        }
    }
    private void FitHost()
    {
        Rect rect;if(!GetClientRect(host,out rect))return;
        int w=rect.Right-rect.Left,h=rect.Bottom-rect.Top;
        if(hostSize==new Size(w,h))return;hostSize=new Size(w,h);
        Rectangle area=HostPaneBounds(new Size(w,h));
        MoveWindow(Handle,area.X,area.Y,area.Width,area.Height,true);
    }
    internal static Rectangle HostPaneBounds(Size client)
    {
        int margin=Math.Max(6,client.Width*10/640),top=client.Height*108/497;
        // Cover the legacy footer up to the inner edge of the original gold
        // frame; the old 20-pixel inset exposed its links and icons below us.
        int bottom=Math.Max(6,client.Height*7/497);
        return new Rectangle(margin,top,Math.Max(1,client.Width-margin*2),Math.Max(1,client.Height-top-bottom));
    }
    private void LayoutPane()
    {
        int w=ClientSize.Width,pad=28,content=Math.Max(100,w-pad*2);
        heading.SetBounds(pad,18,Math.Max(100,content-200),34);caption.SetBounds(pad,57,content,22);
        language.SetBounds(w-pad-188,23,188,28);languageInfo.SetBounds(pad,81,content,22);
        body.SetBounds(pad,110,content,Math.Max(116,ClientSize.Height-206));
        createPane.Bounds=body.ClientRectangle;listPane.Bounds=body.ClientRectangle;accounts.Bounds=listPane.ClientRectangle;
        name.Width=content;
        nameHint.Width=content;
        int y=ClientSize.Height-83;create.SetBounds(pad,y,126,37);back.SetBounds(pad,y,140,37);
        login.SetBounds(w-pad-169,y,169,37);save.SetBounds(w-pad-169,y,169,37);
        state.SetBounds(pad,ClientSize.Height-36,content,26);
    }
    private void ApplyLanguage()
    {
        Text=LanguageSupport.Text("阿拉德·归途 · 选择账号");
        heading.Text=LanguageSupport.Text(creating?"新建账号":"选择账号");
        caption.Text=LanguageSupport.Text(creating?"只需填写一个账号名称":"选中已保存的账号，继续你的冒险");
        nameLabel.Text=LanguageSupport.Text("账号名称");name.AccessibleName=nameLabel.Text;
        accounts.AccessibleName=LanguageSupport.Text("已保存的账号");language.AccessibleName=LanguageSupport.Text("语言");
        nameHint.Text=LanguageSupport.Text("给这个账号起个名字，角色和进度会保存在这里。");
        foreach(var pair in new[]{new KeyValuePair<Button,string>(create,"新建账号"),new KeyValuePair<Button,string>(login,"选择账号登录"),new KeyValuePair<Button,string>(save,"创建并选中"),new KeyValuePair<Button,string>(back,"返回账号列表")})
        {pair.Key.Text=LanguageSupport.Text(pair.Value);pair.Key.AccessibleName=pair.Key.Text;}
        languageInfo.Text=LanguageSupport.Coverage();
        if(!busy)SetStatus(statusText,statusIsError);
        RefreshAccountNames();
        
        accounts.Invalidate();
    }
    private void ConfigureButton(Button b,string text,bool primary,Action action)
    {
        b.Text=text;b.Name=text;b.AccessibleName=text;b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=primary?0:1;
        b.FlatAppearance.BorderColor=Color.FromArgb(81,84,91);b.BackColor=primary?gold:Color.FromArgb(44,48,55);b.ForeColor=primary?Color.FromArgb(31,33,38):ink;
        b.Font=new Font(Font.FontFamily,10F,primary?FontStyle.Bold:FontStyle.Regular);b.Cursor=Cursors.Hand;b.Click+=(s,e)=>action();
    }
    private static Dictionary<string,object> Data(params object[] values)
    {var d=new Dictionary<string,object>();for(int i=0;i<values.Length;i+=2)d.Add((string)values[i],values[i+1]);return d;}
    private static string Value(Dictionary<string,object> d,string key){object v;return d.TryGetValue(key,out v)?Convert.ToString(v):"";}
    private static string ProfileName(Dictionary<string,object> profile)
    {
        string name=Value(profile,"name");
        return Value(profile,"nameLabelKey")=="defaultAccount"&&name=="我的账号"?LanguageSupport.Text("我的账号"):name;
    }
    private void RefreshAccountNames()
    {
        int selected=accounts.SelectedIndex;accounts.BeginUpdate();
        try{for(int i=0;i<profiles.Count&&i<accounts.Items.Count;++i)accounts.Items[i]=ProfileName(profiles[i]);accounts.SelectedIndex=selected;}
        finally{accounts.EndUpdate();}
    }
    private void ApplyState(Dictionary<string,object> data)
    {
        body.Visible=true;
        profiles=((IEnumerable)data["profiles"]).Cast<object>().Select(x=>(Dictionary<string,object>)x).ToList();
        accounts.Items.Clear();foreach(var p in profiles)accounts.Items.Add(ProfileName(p));
        int index=profiles.FindIndex(p=>Value(p,"id")==Value(data,"selectedId"));accounts.SelectedIndex=profiles.Count==0?-1:Math.Max(0,index);
        if(profiles.Count==0)ShowCreate();else ShowList();
    }
    private void ShowCreate()
    {
        creating=true;heading.Text=LanguageSupport.Text("新建账号");caption.Text=LanguageSupport.Text("只需填写一个账号名称");
        createPane.Visible=true;listPane.Visible=false;create.Visible=false;login.Visible=false;save.Visible=true;back.Visible=profiles.Count>0;
        AcceptButton=save;if(loaded){name.Clear();name.Focus();}
    }
    private void ShowList()
    {
        creating=false;heading.Text=LanguageSupport.Text("选择账号");caption.Text=LanguageSupport.Text("选中已保存的账号，继续你的冒险");
        createPane.Visible=false;listPane.Visible=true;create.Visible=true;login.Visible=true;save.Visible=false;back.Visible=false;
        AcceptButton=login;accounts.Focus();
    }
    private async void CreateAccount()
    {
        if(busy||!creating)return;string text=name.Text;
        await Work("account-create",async()=>{SetStatus("正在创建账号并保存存档备份…");ApplyState(await Task.Run(()=>request(Data("operation","create","name",text))));name.Clear();SetStatus("账号已创建并选中，点击“选择账号登录”开始游戏。");});
    }
    private async void Login()
    {
        if(busy||creating||accounts.SelectedIndex<0)return;string id=Value(profiles[accounts.SelectedIndex],"id");
        await Work("account-login",async()=>
        {
            await launch(id,SetStatus,ParentAlive);
            closing=true;
            if(host!=IntPtr.Zero&&ParentAlive())PostMessage(host,0x0010,IntPtr.Zero,IntPtr.Zero);
            Close();
        });
    }
    private async Task Work(string action,Func<Task> task)
    {
        if(busy)return;busy=true;body.Enabled=false;language.Enabled=false;create.Enabled=login.Enabled=save.Enabled=back.Enabled=false;
        try{Dnf60Control.AccountStage(action+"-begin","正在处理账号操作。");await task();Dnf60Control.AccountStage(action+"-done","账号操作已完成。");}
        catch(OperationCanceledException){closing=true;Close();}
        catch(Exception error){Dnf60Control.AccountStage(action+"-failed",error.Message,true);SetStatus(error.Message,true);}
        finally
        {
            busy=false;
            if(!IsDisposed){body.Enabled=true;language.Enabled=true;create.Enabled=save.Enabled=back.Enabled=true;login.Enabled=accounts.SelectedIndex>=0;if(closing)Close();}
        }
    }
    private void SetStatus(string text){SetStatus(text,false);}
    private void SetStatus(string text,bool error)
    {if(IsDisposed)return;if(InvokeRequired){BeginInvoke((Action)(()=>SetStatus(text,error)));return;}statusText=text;statusIsError=error;state.Text=LanguageSupport.Text(text);state.ForeColor=error?Color.FromArgb(255,156,146):muted;}
    private void DrawAccount(DrawItemEventArgs e)
    {
        if(e.Index<0||e.Index>=profiles.Count)return;
        bool chosen=(e.State&DrawItemState.Selected)!=0;var row=profiles[e.Index];
        using(var fill=new SolidBrush(chosen?Color.FromArgb(67,61,48):accounts.BackColor))e.Graphics.FillRectangle(fill,e.Bounds);
        if(chosen)using(var pen=new Pen(gold,1))e.Graphics.DrawRectangle(pen,e.Bounds.X,e.Bounds.Y,e.Bounds.Width-1,e.Bounds.Height-1);
        var area=new Rectangle(e.Bounds.X+15,e.Bounds.Y+8,e.Bounds.Width-30,24);
        using(var font=new Font(Font.FontFamily,11F,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,ProfileName(row),font,area,ink,TextFormatFlags.EndEllipsis);
        var chars=((IEnumerable)row["characters"]).Cast<object>().Select(x=>(Dictionary<string,object>)x).Select(x=>Value(x,"name")+"  Lv."+Value(x,"level"));
        string details=Convert.ToInt32(row["characterCount"])==0?LanguageSupport.Text("还没有角色 · 登录后可新建角色"):String.Join("  /  ",chars);
        TextRenderer.DrawText(e.Graphics,details,Font,new Rectangle(e.Bounds.X+15,e.Bounds.Y+35,e.Bounds.Width-30,20),muted,TextFormatFlags.EndEllipsis);
    }
}
