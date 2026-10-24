// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Dnf60Desktop
{
    internal sealed class FormSheet : TableLayoutPanel
    {
        internal readonly Dictionary<string,Control> Fields=new Dictionary<string,Control>();
        private readonly Dictionary<string,object> original=new Dictionary<string,object>();
        internal FormSheet()
        {
            AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;Dock=DockStyle.Top;ColumnCount=2;
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,185));ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            Padding=new Padding(14,10,14,10);BackColor=Theme.Panel;Margin=new Padding(0,5,0,12);DoubleBuffered=true;
        }
        internal Control Add(string key,string label,Control control)
        {
            int row=RowCount++;RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l=Theme.Label(label,9,Theme.Muted);l.Margin=new Padding(3,9,8,5);l.MaximumSize=new Size(174,0);
            control.Name=key;control.AccessibleName=label;control.Margin=new Padding(3,6,8,6);control.Anchor=AnchorStyles.Left|AnchorStyles.Right;
            Controls.Add(l,0,row);Controls.Add(control,1,row);Fields[key]=control;original[key]=Read(control);return control;
        }
        internal TextBox TextField(string key,string label,string value,int max,bool multi)
        {var t=Theme.TextBox(label,value);t.MaxLength=max;if(multi){t.Multiline=true;t.Height=86;t.ScrollBars=ScrollBars.Vertical;t.AcceptsReturn=true;}Add(key,label,t);return t;}
        internal NumericUpDown Number(string key,string label,decimal min,decimal max,decimal value,int decimals)
        {var n=new NumericUpDown {Minimum=min,Maximum=max,DecimalPlaces=decimals,Increment=decimals==0?1:(decimal)Math.Pow(10,-decimals),ThousandsSeparator=true,BackColor=Theme.Field,ForeColor=Theme.Text,BorderStyle=BorderStyle.FixedSingle,Width=220};n.Value=Math.Max(min,Math.Min(max,value));Add(key,label,n);return n;}
        internal CheckBox Switch(string key,string label,bool value)
        {var c=new CheckBox {Checked=value,Text=TrainerLanguage.T("启用"),AutoSize=true,ForeColor=Theme.Text};Add(key,label,c);return c;}
        internal ComboBox ChoiceField(string key,string label,IEnumerable<Choice> choices,int value)
        {var c=Theme.Combo(label,300);foreach(var v in choices)c.Items.Add(v);if(c.Items.Count>0)c.SelectedIndex=0;foreach(Choice v in c.Items)if(v.Id==value)c.SelectedItem=v;Add(key,label,c);return c;}
        internal void Set(string key,object value)
        {
            Control c=Fields[key];var n=c as NumericUpDown;var t=c as TextBox;var cb=c as CheckBox;var co=c as ComboBox;
            if(n!=null)n.Value=Math.Max(n.Minimum,Math.Min(n.Maximum,Convert.ToDecimal(value,CultureInfo.InvariantCulture)));
            else if(t!=null)t.Text=Convert.ToString(value,CultureInfo.InvariantCulture);
            else if(cb!=null)cb.Checked=Convert.ToBoolean(value);
            else if(co!=null){foreach(Choice v in co.Items)if(v.Id==Convert.ToInt32(value))co.SelectedItem=v;}
        }
        internal object Value(string key){return Read(Fields[key]);}
        internal Dictionary<string,object> Values(){return Fields.ToDictionary(p=>p.Key,p=>Read(p.Value));}
        internal Dictionary<string,object> Changes(){return Fields.Where(p=>!Equal(Read(p.Value),original[p.Key])).ToDictionary(p=>p.Key,p=>Read(p.Value));}
        internal bool Dirty {get{return Changes().Count>0;}}
        internal void Accept(){foreach(var p in Fields)original[p.Key]=Read(p.Value);}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var p=ModernDraw.Round(new RectangleF(.5f,.5f,Width-1,Height-1),10))using(var pen=new Pen(Theme.Line))e.Graphics.DrawPath(pen,p);}
        private static bool Equal(object a,object b){return Convert.ToString(a,CultureInfo.InvariantCulture)==Convert.ToString(b,CultureInfo.InvariantCulture);}
        private static object Read(Control c)
        {if(c is NumericUpDown){var n=(NumericUpDown)c;return n.DecimalPlaces==0?(object)Decimal.ToInt64(n.Value):n.Value;}if(c is TextBox)return c.Text;if(c is CheckBox)return ((CheckBox)c).Checked;if(c is ComboBox){var v=((ComboBox)c).SelectedItem as Choice;return v==null?0:v.Id;}if(c is DateTimePicker){var d=(DateTimePicker)c;return !d.Checked?0:(long)(d.Value.ToUniversalTime()-new DateTime(1970,1,1)).TotalSeconds;}return null;}
    }

    internal static class AdminUi
    {
        internal static Panel Scroll(){return new Panel {Dock=DockStyle.Fill,AutoScroll=true,BackColor=Theme.Bg,Padding=new Padding(2)};}
        internal static FlowLayoutPanel Bar(params Control[] controls)
        {var p=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,MinimumSize=new Size(10,46),WrapContents=true,Padding=new Padding(0,5,0,7)};p.Controls.AddRange(controls);return p;}
        internal static Label Note(string text){return new WrappingNote {Text=text};}
        internal static TextBox ReadText(string text)
        {return new TextBox {Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Both,WordWrap=false,Text=TrainerLanguage.T(text),BackColor=Theme.Field,ForeColor=Theme.Text,BorderStyle=BorderStyle.None,Font=TrainerLanguage.Font(10)};}
        internal static void Stack(Control host,params Control[] controls)
        {for(int i=controls.Length-1;i>=0;i--){controls[i].Dock=DockStyle.Top;host.Controls.Add(controls[i]);}}
        internal static decimal Dec(Dictionary<string,object> d,string k){object v=J.V(d,k);return v==null?0:Convert.ToDecimal(v,CultureInfo.InvariantCulture);}
        internal static object[] Array(object o){return o==null?new object[0]:((IEnumerable)o).Cast<object>().ToArray();}
        internal static string Display(object o)
        {if(o==null)return "";if(o is Dictionary<string,object>)return String.Join("  ",J.O(o).Select(pair=>{var labels=new Dictionary<string,string>{{"id","编号"},{"sender","发件人"},{"subject","邮件主题"},{"body","邮件正文"},{"gold","金币"},{"item","物品编号"},{"count","数量"},{"claimed","领取状态"},{"name","名称"},{"value","属性值"}};string label;string key=labels.TryGetValue(pair.Key,out label)?TrainerLanguage.T(label):pair.Key;bool authored=pair.Key=="sender"||pair.Key=="subject"||pair.Key=="body";return key+": "+(authored?Convert.ToString(pair.Value,CultureInfo.InvariantCulture):Display(pair.Value));}));if(o is IEnumerable && !(o is string))return String.Join(" / ",Array(o).Select(Display));if(o is bool)return TrainerLanguage.T((bool)o?"是":"否");return TrainerLanguage.Display(Convert.ToString(o,CultureInfo.InvariantCulture));}
        internal static DataGridView Grid(string name,params string[] columns)
        {var g=Theme.Grid(name);g.RowTemplate.Height=35;for(int i=0;i<columns.Length;i++)Theme.TextColumn(g,"c"+i,columns[i],i==0?100:180,i==columns.Length-1,false);TrainerDisplay.ConfigureAdminGrid(g,columns);return g;}
        internal static void Dialog(Control owner,string title,string text)
        {using(var f=new Form {Text=title,Size=new Size(780,580),StartPosition=FormStartPosition.CenterParent,BackColor=Theme.Bg,ForeColor=Theme.Text,Font=owner.Font,MinimizeBox=false,MaximizeBox=true}){var b=Theme.Button("关闭",(s,e)=>f.Close(),false);b.Dock=DockStyle.Bottom;f.Controls.Add(ReadText(text));f.Controls.Add(b);TrainerLanguage.Attach(f);TrainerDisplay.ShowDialog(f,owner);}}
        internal static string Time(object value)
        {long seconds=Convert.ToInt64(value);return seconds==0?"不限时间":new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");}
    }
}
