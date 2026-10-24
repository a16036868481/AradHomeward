// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Dnf60Desktop
{
    internal static class ModernDraw
    {
        internal static Color ParentColor(Control control)
        {
            for(Control parent=control.Parent;parent!=null;parent=parent.Parent)
            {var surface=parent as SurfacePanel;if(surface!=null)return surface.Fill;if(parent.BackColor.A==255)return parent.BackColor;}
            return Theme.Bg;
        }
        internal static GraphicsPath Round(RectangleF rect,float radius)
        {
            var path=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(rect.Width,rect.Height));
            if(d<=0){path.AddRectangle(rect);return path;}
            path.AddArc(rect.Left,rect.Top,d,d,180,90);path.AddArc(rect.Right-d,rect.Top,d,d,270,90);
            path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);path.AddArc(rect.Left,rect.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        internal static void Glyph(Graphics g,int kind,Rectangle box,Color color)
        {
            var old=g.Transform;g.TranslateTransform(box.X,box.Y);g.ScaleTransform(box.Width/24f,box.Height/24f);
            using(var pen=new Pen(color,1.7f)){pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
                switch(kind){
                    case 0:foreach(var r in new[]{new Rectangle(3,3,7,7),new Rectangle(14,3,7,7),new Rectangle(3,14,7,7),new Rectangle(14,14,7,7)})g.DrawRectangle(pen,r);break;
                    case 1:g.DrawEllipse(pen,8,3,8,8);g.DrawArc(pen,4,13,16,15,185,170);break;
                    case 2:g.DrawRectangle(pen,4,8,16,13);g.DrawArc(pen,8,2,8,11,180,180);g.DrawLine(pen,4,12,20,12);break;
                    case 3:g.DrawEllipse(pen,3,3,18,18);g.DrawLine(pen,8,8,8,14);g.DrawLine(pen,5,11,11,11);g.DrawEllipse(pen,15,9,1,1);g.DrawEllipse(pen,17,14,1,1);break;
                    case 4:g.DrawLine(pen,3,6,21,6);g.DrawLine(pen,3,12,21,12);g.DrawLine(pen,3,18,21,18);using(var b=new SolidBrush(Theme.Side)){g.FillRectangle(b,7,3,4,6);g.FillRectangle(b,14,9,4,6);g.FillRectangle(b,6,15,4,6);}g.DrawRectangle(pen,7,3,4,6);g.DrawRectangle(pen,14,9,4,6);g.DrawRectangle(pen,6,15,4,6);break;
                    case 5:g.DrawRectangle(pen,4,8,16,13);g.DrawRectangle(pen,3,3,18,5);g.DrawLine(pen,9,13,15,13);break;
                    case 6:g.DrawLine(pen,12,3,12,15);g.DrawLine(pen,7,11,12,16);g.DrawLine(pen,17,11,12,16);g.DrawLines(pen,new[]{new Point(4,16),new Point(4,21),new Point(20,21),new Point(20,16)});break;
                    default:g.DrawArc(pen,4,4,16,16,35,300);g.DrawLines(pen,new[]{new Point(20,4),new Point(20,10),new Point(14,10)});break;
                }
            }g.Transform=old;old.Dispose();
        }
    }

    internal class SurfacePanel : Panel
    {
        internal int Radius=12;
        internal Color Fill=Color.White,Stroke=Theme.Line;
        internal SurfacePanel(){DoubleBuffered=true;BackColor=Color.Transparent;SetStyle(ControlStyles.ResizeRedraw,true);}
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(ModernDraw.ParentColor(this));e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var p=ModernDraw.Round(new RectangleF(.5f,.5f,Width-1,Height-1),Radius))using(var b=new SolidBrush(Fill))using(var pen=new Pen(Stroke)){e.Graphics.FillPath(b,p);e.Graphics.DrawPath(pen,p);}
        }
    }

    internal class ActionButton : Button
    {
        internal bool Primary,Chip,Active,Danger;
        private bool hover,down;
        internal ActionButton()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;UseVisualStyleBackColor=false;BackColor=Color.White;Font=TrainerLanguage.Font(9);MinimumSize=new Size(72,32);Height=34;Padding=new Padding(11,0,11,0);Margin=new Padding(0,3,8,3);
        }
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;down=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){down=true;Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){down=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(ModernDraw.ParentColor(this));
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Color fill=Primary?Theme.Gold:Color.White,text=Primary?Color.White:Theme.Text,line=Primary?Theme.Gold:Theme.Line;
            if(Chip){fill=Active?Theme.Selected:Color.Transparent;text=Active?Theme.Gold:Theme.Muted;line=Color.Transparent;}
            if(Danger){text=Theme.Red;fill=Color.FromArgb(255,245,245);line=Color.FromArgb(245,214,215);}
            if(hover&&Enabled)fill=Primary?Color.FromArgb(34,87,204):Chip?Theme.Selected:Color.FromArgb(242,246,252);
            if(down&&Enabled)fill=Primary?Color.FromArgb(27,71,172):Theme.Selected;
            if(!Enabled){fill=Color.FromArgb(239,242,247);text=Color.FromArgb(164,172,184);line=Theme.Line;}
            using(var p=ModernDraw.Round(new RectangleF(.5f,.5f,Width-1,Height-1),7))using(var brush=new SolidBrush(fill))using(var pen=new Pen(line)){e.Graphics.FillPath(brush,p);if(line!=Color.Transparent)e.Graphics.DrawPath(pen,p);}
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(8,0,Width-16,Height),text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(4,4,Width-8,Height-8),text,fill);
        }
    }

    internal sealed class NavigationButton : Button
    {
        internal int Kind;internal bool Active;internal string Caption="";private bool hover;
        internal NavigationButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Height=58;Dock=DockStyle.Top;Cursor=Cursors.Hand;BackColor=Theme.Side;ForeColor=Color.White;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Side);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            if(Active||hover)using(var p=ModernDraw.Round(new RectangleF(0,3,Width-1,Height-7),9))using(var b=new SolidBrush(Active?Color.FromArgb(37,60,100):Color.FromArgb(29,41,60)))e.Graphics.FillPath(b,p);
            Color color=Active?Color.White:Color.FromArgb(176,188,208);ModernDraw.Glyph(e.Graphics,Kind,new Rectangle(15,17,23,23),color);
            using(var f=new Font(Font.FontFamily,10,Active?FontStyle.Bold:FontStyle.Regular))TextRenderer.DrawText(e.Graphics,Text,f,new Rectangle(52,9,Width-58,23),color,TextFormatFlags.VerticalCenter);
            using(var f=new Font(Font.FontFamily,8))TextRenderer.DrawText(e.Graphics,TrainerLanguage.T(Caption),f,new Rectangle(53,32,Width-58,16),Active?Color.FromArgb(177,201,240):Color.FromArgb(114,133,158),TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(Active)using(var b=new SolidBrush(Color.FromArgb(120,167,255)))e.Graphics.FillRectangle(b,0,18,3,21);
        }
    }

    internal sealed class PageDeck : Panel
    {
        private readonly List<Panel> pages=new List<Panel>();private int index=-1;
        internal event EventHandler SelectedIndexChanged;
        internal PageDeck(){Dock=DockStyle.Fill;BackColor=Theme.Bg;DoubleBuffered=true;}
        internal void AddPage(string name,Panel panel){panel.Text=name;panel.Dock=DockStyle.Fill;panel.Visible=false;pages.Add(panel);Controls.Add(panel);}
        internal Panel SelectedTab {get{return index<0?null:pages[index];}}
        internal int SelectedIndex
        {
            get{return index;}
            set{if(value<0||value>=pages.Count||value==index)return;SuspendLayout();if(index>=0)pages[index].Visible=false;index=value;pages[index].Visible=true;pages[index].BringToFront();ResumeLayout(true);if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}
        }
    }

    internal sealed class ModernTabs : TabControl
    {
        internal ModernTabs(){Dock=DockStyle.Fill;DrawMode=TabDrawMode.OwnerDrawFixed;SizeMode=TabSizeMode.Fixed;ItemSize=new Size(190,37);Font=TrainerLanguage.Font(9);Padding=new Point(12,5);SetStyle(ControlStyles.OptimizedDoubleBuffer,true);}
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var r=GetTabRect(e.Index);using(var back=new SolidBrush(Theme.Bg))e.Graphics.FillRectangle(back,r);bool active=e.Index==SelectedIndex;
            using(var p=ModernDraw.Round(new RectangleF(r.X+2,r.Y+3,r.Width-4,r.Height-5),6))using(var b=new SolidBrush(active?Color.White:Theme.Bg))e.Graphics.FillPath(b,p);
            TextRenderer.DrawText(e.Graphics,TabPages[e.Index].Text,Font,r,active?Theme.Gold:Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(active)using(var pen=new Pen(Theme.Gold,2))e.Graphics.DrawLine(pen,r.Left+18,r.Bottom-3,r.Right-18,r.Bottom-3);
        }
    }

    internal sealed class WrappingNote : Label
    {
        private bool sizing;
        internal WrappingNote(){Dock=DockStyle.Top;ForeColor=Theme.Muted;Font=TrainerLanguage.Font(9);AutoSize=false;Padding=new Padding(1,9,1,12);BackColor=Color.Transparent;}
        private void SizeText(){if(sizing||Width<10)return;sizing=true;try{Height=TextRenderer.MeasureText(Text,Font,new Size(Math.Max(10,Width-Padding.Horizontal),0),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl).Height+Padding.Vertical;}finally{sizing=false;}}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);SizeText();}
        protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);SizeText();}
    }

    internal sealed class MetricLabel : Label
    {
        private readonly List<Font> choices=new List<Font>();
        private bool fitting;
        internal MetricLabel(){AutoSize=false;AutoEllipsis=true;ForeColor=Theme.Text;for(int size=22;size>=13;size--)choices.Add(TrainerLanguage.Font(size,FontStyle.Bold));Font=choices[0];}
        private void Fit(){if(fitting||Width<15||choices==null||choices.Count==0)return;fitting=true;try{foreach(var f in choices)if(TextRenderer.MeasureText(Text,f).Width<=Width){Font=f;return;}Font=choices[choices.Count-1];}finally{fitting=false;}}
        protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Fit();}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Fit();}
        protected override void Dispose(bool disposing){if(disposing&&choices!=null)foreach(var f in choices)f.Dispose();base.Dispose(disposing);}
    }

    internal sealed class DashboardTile : SurfacePanel
    {
        internal DashboardTile(string title,string subtitle,int glyph,EventHandler clicked)
        {
            Dock=DockStyle.Fill;Margin=new Padding(0,0,12,12);Cursor=Cursors.Hand;Height=92;
            var a=Theme.Label(title,11,Theme.Text);a.Font=new Font(a.Font,FontStyle.Bold);a.Location=new Point(60,19);a.MaximumSize=new Size(240,22);
            var b=Theme.Label(subtitle,9,Theme.Muted);b.Location=new Point(61,45);b.MaximumSize=new Size(260,36);Controls.Add(a);Controls.Add(b);a.Click+=clicked;b.Click+=clicked;Click+=clicked;
            Paint+=(s,e)=>{using(var p=ModernDraw.Round(new RectangleF(17,23,31,35),8))using(var brush=new SolidBrush(Theme.Selected))e.Graphics.FillPath(brush,p);ModernDraw.Glyph(e.Graphics,glyph,new Rectangle(23,30,20,20),Theme.Gold);};
            MouseEnter+=(s,e)=>{Stroke=Color.FromArgb(169,194,244);Invalidate();};MouseLeave+=(s,e)=>{Stroke=Theme.Line;Invalidate();};
        }
    }
}
