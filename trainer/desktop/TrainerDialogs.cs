// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Dnf60Desktop
{
    // Product prompts use product translations rather than the Windows display
    // language. Choice semantics and the caller's default button are preserved.
    internal static class TrainerDialogs
    {
        internal static DialogResult Show(string text){return Show(null,text,"阿拉德·归途 · 修改器",MessageBoxButtons.OK,MessageBoxIcon.None,MessageBoxDefaultButton.Button1);}
        internal static DialogResult Show(string text,string caption){return Show(null,text,caption,MessageBoxButtons.OK,MessageBoxIcon.None,MessageBoxDefaultButton.Button1);}
        internal static DialogResult Show(string text,string caption,MessageBoxButtons buttons){return Show(null,text,caption,buttons,MessageBoxIcon.None,MessageBoxDefaultButton.Button1);}
        internal static DialogResult Show(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon){return Show(null,text,caption,buttons,icon,MessageBoxDefaultButton.Button1);}
        internal static DialogResult Show(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon,MessageBoxDefaultButton defaultButton){return Show(null,text,caption,buttons,icon,defaultButton);}
        internal static DialogResult Show(IWin32Window owner,string text){return Show(owner,text,"阿拉德·归途 · 修改器",MessageBoxButtons.OK,MessageBoxIcon.None,MessageBoxDefaultButton.Button1);}
        internal static DialogResult Show(IWin32Window owner,string text,string caption){return Show(owner,text,caption,MessageBoxButtons.OK,MessageBoxIcon.None,MessageBoxDefaultButton.Button1);}
        internal static DialogResult Show(IWin32Window owner,string text,string caption,MessageBoxButtons buttons){return Show(owner,text,caption,buttons,MessageBoxIcon.None,MessageBoxDefaultButton.Button1);}
        internal static DialogResult Show(IWin32Window owner,string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon){return Show(owner,text,caption,buttons,icon,MessageBoxDefaultButton.Button1);}
        internal static DialogResult Show(IWin32Window owner,string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon,MessageBoxDefaultButton defaultButton)
        {
            using(var form=Create(text,caption,buttons,icon,defaultButton))return owner==null?form.ShowDialog():form.ShowDialog(owner);
        }
        private static DialogResult[] Results(MessageBoxButtons buttons)
        {
            switch(buttons)
            {
                case MessageBoxButtons.OKCancel:return new[]{DialogResult.OK,DialogResult.Cancel};
                case MessageBoxButtons.YesNo:return new[]{DialogResult.Yes,DialogResult.No};
                case MessageBoxButtons.YesNoCancel:return new[]{DialogResult.Yes,DialogResult.No,DialogResult.Cancel};
                case MessageBoxButtons.RetryCancel:return new[]{DialogResult.Retry,DialogResult.Cancel};
                case MessageBoxButtons.AbortRetryIgnore:return new[]{DialogResult.Abort,DialogResult.Retry,DialogResult.Ignore};
                default:return new[]{DialogResult.OK};
            }
        }
        private static string Caption(DialogResult result)
        {
            switch(result)
            {
                case DialogResult.Yes:return "是";case DialogResult.No:return "否";
                case DialogResult.Cancel:return "取消";case DialogResult.Retry:return "重试";
                case DialogResult.Abort:return "中止";case DialogResult.Ignore:return "忽略";
                default:return "确定";
            }
        }
        internal static Form Create(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon,MessageBoxDefaultButton defaultButton)
        {
            var available=Screen.PrimaryScreen.WorkingArea;int width=Math.Min(660,Math.Max(380,available.Width-60));
            var form=new Form {Text=TrainerLanguage.T(caption),Font=TrainerLanguage.Font(9.5f),BackColor=Theme.Bg,ForeColor=Theme.Text,StartPosition=FormStartPosition.CenterParent,ShowInTaskbar=false,MinimizeBox=false,MaximizeBox=false,FormBorderStyle=FormBorderStyle.FixedDialog,AutoScaleMode=AutoScaleMode.Dpi,KeyPreview=true,Padding=new Padding(0),ClientSize=new Size(width,210)};
            var resultList=Results(buttons);var prompt=TrainerLanguage.T(text??"");int iconWidth=icon==MessageBoxIcon.None?0:50;
            int contentWidth=width-48-iconWidth;int desired=TextRenderer.MeasureText(prompt,form.Font,new Size(contentWidth,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl|TextFormatFlags.NoPrefix).Height;
            int contentHeight=Math.Max(68,Math.Min(Math.Max(130,available.Height-210),desired+12));form.ClientSize=new Size(width,contentHeight+112);
            var body=new Panel {Dock=DockStyle.Fill,Padding=new Padding(24,22,24,10)};
            if(icon!=MessageBoxIcon.None)
            {
                Icon image=icon==MessageBoxIcon.Question?SystemIcons.Question:icon==MessageBoxIcon.Warning?SystemIcons.Warning:icon==MessageBoxIcon.Error?SystemIcons.Error:SystemIcons.Information;
                var bitmap=image.ToBitmap();var picture=new PictureBox {Image=bitmap,SizeMode=PictureBoxSizeMode.CenterImage,Dock=DockStyle.Left,Width=iconWidth};body.Controls.Add(picture);form.Disposed+=(sender,args)=>bitmap.Dispose();
            }
            var content=new TextBox {Text=prompt,Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Theme.Bg,ForeColor=Theme.Text,Dock=DockStyle.Fill,ScrollBars=desired>contentHeight?ScrollBars.Vertical:ScrollBars.None,TabStop=false,WordWrap=true};body.Controls.Add(content);content.BringToFront();
            var footer=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=68,Padding=new Padding(18,14,18,14),FlowDirection=FlowDirection.RightToLeft,WrapContents=false,BackColor=Color.White};
            var controls=new Dictionary<DialogResult,Button>();
            for(int i=resultList.Length-1;i>=0;i--)
            {
                DialogResult value=resultList[i];var button=Theme.Button(Caption(value),null,false);button.DialogResult=value;button.AutoSize=true;button.MinimumSize=new Size(94,35);button.Margin=new Padding(8,0,0,0);button.Font=TrainerLanguage.Font(9.5f);button.Text=TrainerLanguage.T(Caption(value));controls[value]=button;footer.Controls.Add(button);
            }
            int defaultIndex=defaultButton==MessageBoxDefaultButton.Button2?1:defaultButton==MessageBoxDefaultButton.Button3?2:0;defaultIndex=Math.Min(defaultIndex,resultList.Length-1);
            var accept=controls[resultList[defaultIndex]];form.AcceptButton=accept;
            DialogResult escape=resultList.Contains(DialogResult.Cancel)?DialogResult.Cancel:resultList.Contains(DialogResult.No)?DialogResult.No:resultList.Contains(DialogResult.Abort)?DialogResult.Abort:DialogResult.OK;
            form.CancelButton=controls[escape];form.Controls.Add(body);form.Controls.Add(footer);
            form.Shown+=(sender,args)=>{content.SelectionStart=0;content.SelectionLength=0;accept.Focus();};
            form.FormClosing+=(sender,args)=>{if(form.DialogResult==DialogResult.None||form.DialogResult==DialogResult.Cancel&&!resultList.Contains(DialogResult.Cancel))form.DialogResult=escape;};
            form.KeyDown+=(sender,args)=>
            {
                DialogResult key=DialogResult.None;
                if(args.KeyCode==Keys.Escape)key=escape;
                else if(args.Alt&&args.KeyCode==Keys.Y)key=DialogResult.Yes;
                else if(args.Alt&&args.KeyCode==Keys.N)key=DialogResult.No;
                else if(args.Alt&&args.KeyCode==Keys.C)key=DialogResult.Cancel;
                if(key!=DialogResult.None&&controls.ContainsKey(key)){args.Handled=true;args.SuppressKeyPress=true;controls[key].PerformClick();}
            };
            return form;
        }
    }
}
