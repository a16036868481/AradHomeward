// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Dnf60Desktop
{
    internal enum ItemTipKind { Catalogue, Inventory, Pending, Mail }

    internal sealed class ItemTipTarget
    {
        internal int Id;
        internal ItemTipKind Kind;
        internal Dictionary<string,object> Seed,Instance;
        internal static ItemTipTarget Catalogue(Dictionary<string,object> item)
        {return item==null?null:new ItemTipTarget {Id=J.N(item,"id"),Seed=new Dictionary<string,object>(item)};}
        internal static ItemTipTarget Inventory(Dictionary<string,object> item)
        {return item==null?null:new ItemTipTarget {Id=J.N(item,"item_key"),Kind=ItemTipKind.Inventory,Seed=new Dictionary<string,object>(item),Instance=new Dictionary<string,object>(item)};}
        internal static ItemTipTarget Pending(CartEntry entry)
        {return entry==null?null:new ItemTipTarget {Id=entry.Id,Kind=ItemTipKind.Pending,Seed=new Dictionary<string,object>(entry.Item),Instance=J.D("item_count",entry.Count,"item_attr",entry.Level,"enhance_type",entry.Type,"enhance_value",entry.Attribute)};}
        internal static ItemTipTarget Mail(Dictionary<string,object> mail)
        {
            if(mail==null||J.N(mail,"item")<=0)return null;
            var instance=new Dictionary<string,object>(mail);instance["item_count"]=J.V(mail,"count");instance["item_attr"]=J.V(mail,"attr");
            return new ItemTipTarget {Id=J.N(mail,"item"),Kind=ItemTipKind.Mail,Seed=J.D("id",J.N(mail,"item")),Instance=instance};
        }
    }

    // Only immutable item definitions are cached. Instance values come from the current row.
    internal sealed class ItemDetailCache
    {
        private readonly Func<int,Dictionary<string,object>> loader;
        private readonly Dictionary<int,Task<Dictionary<string,object>>> entries=new Dictionary<int,Task<Dictionary<string,object>>>();
        private readonly Queue<int> order=new Queue<int>();
        private readonly SemaphoreSlim gate=new SemaphoreSlim(3);
        private readonly object sync=new object();
        internal ItemDetailCache(Func<int,Dictionary<string,object>> loader){this.loader=loader;}
        internal Task<Dictionary<string,object>> Get(int id)
        {
            lock(sync)
            {
                Task<Dictionary<string,object>> pending;if(entries.TryGetValue(id,out pending)&&!pending.IsFaulted&&!pending.IsCanceled)return pending;
                entries.Remove(id);
                pending=Load(id);entries.Add(id,pending);order.Enqueue(id);
                pending.ContinueWith(failed=>{lock(sync){Task<Dictionary<string,object>> current;if(entries.TryGetValue(id,out current)&&Object.ReferenceEquals(current,failed))entries.Remove(id);}},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                while(order.Count>512){int oldest=order.Dequeue();Task<Dictionary<string,object>> old;if(entries.TryGetValue(oldest,out old)&&old.IsCompleted)entries.Remove(oldest);}
                return pending;
            }
        }
        private async Task<Dictionary<string,object>> Load(int id)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var item=await Task.Run(()=>loader(id)).ConfigureAwait(false);
                if(item==null||J.N(item,"id")!=id)throw new InvalidOperationException(TrainerLanguage.T("物品详情不匹配。"));
                return item;
            }
            finally{gate.Release();}
        }
    }

    internal sealed class ItemHoverEpoch
    {
        private int version;
        internal int Advance(){return ++version;}
        internal bool Matches(int request){return request==version;}
    }

    internal sealed class ItemTipLine
    {
        internal string Text;
        internal bool Heading,Accent;
        internal ItemTipLine(string text,bool heading,bool accent){Text=text;Heading=heading;Accent=accent;}
    }
    internal sealed class ItemTipContent
    {
        internal string Name,Meta,Footer="Alt + Enter 查看完整详情";
        internal int Rarity;
        internal readonly List<ItemTipLine> Lines=new List<ItemTipLine>();
        internal string PlainText(){return Meta+Environment.NewLine+Environment.NewLine+String.Join(Environment.NewLine,Lines.Select(line=>(line.Heading?Environment.NewLine:"")+line.Text));}
    }
    internal static class ItemDetailText
    {
        private static readonly Dictionary<string,string> jobNames=new Dictionary<string,string> {{"all","所有职业"},{"swordman","鬼剑士"},{"fighter","格斗家"},{"gunner","神枪手（男）"},{"at gunner","神枪手（女）"},{"mage","魔法师"},{"priest","圣职者"},{"thief","暗夜使者"}};
        private static readonly Dictionary<string,string> slots=new Dictionary<string,string> {{"weapon","武器"},{"coat","上衣"},{"shoulder","头肩"},{"pants","下装"},{"waist","腰带"},{"shoes","鞋"},{"wrist","手镯"},{"amulet","项链"},{"ring","戒指"},{"title name","称号"}};
        private static IEnumerable<Dictionary<string,object>> Rows(object value)
        {var rows=value as IEnumerable;return rows==null||value is string?Enumerable.Empty<Dictionary<string,object>>():rows.Cast<object>().OfType<Dictionary<string,object>>();}
        private static void Line(ItemTipContent content,string text,bool accent=false){if(!String.IsNullOrWhiteSpace(text))content.Lines.Add(new ItemTipLine(text.Trim(),false,accent));}
        private static void Heading(ItemTipContent content,string text){content.Lines.Add(new ItemTipLine(text,true,false));}
        private static string Number(Dictionary<string,object> item,string key){return J.N(item,key).ToString("N0",CultureInfo.CurrentCulture);}
        private static string Enhancement(Dictionary<string,object> instance)
        {
            int type=J.N(instance,"enhance_type"),level=J.N(instance,"item_attr");
            if(type<=0)return "强化：+"+level;
            string[] attributes={"","体力","精神","力量","智力"};
            return "增幅：+"+level+"  ·  "+(type<attributes.Length?attributes[type]:"属性 "+type)+" +"+Number(instance,"enhance_value");
        }
        internal static ItemTipContent Build(Dictionary<string,object> detail,ItemTipTarget target,string error=null,Dictionary<int,string> relatedNames=null)
        {
            var item=new Dictionary<string,object>(target.Seed??J.D());if(detail!=null)foreach(var pair in detail)item[pair.Key]=pair.Value;
            string name=J.S(item,"name");if(String.IsNullOrWhiteSpace(name))name="物品 "+target.Id;
            var tooltip=J.V(item,"tooltip") as Dictionary<string,object>;
            if(tooltip!=null&&!String.IsNullOrWhiteSpace(J.S(tooltip,"name")))name=J.S(tooltip,"name");
            int rarity=J.N(item,"rarity");string rare=rarity>=0&&rarity<Theme.Rarities.Length?Theme.Rarities[rarity]:"品质 "+rarity;
            string requirement=tooltip!=null&&J.V(tooltip,"requiredLevel")!=null?"需 Lv."+J.N(tooltip,"requiredLevel"):"Lv."+J.N(item,"level");
            var content=new ItemTipContent {Name=name,Rarity=rarity,Meta=rare+" · "+Theme.Category(item)+" · "+requirement+"     编号 "+target.Id};
            bool stack=J.B(item,"stackable"),equipment=J.S(item,"category")=="equipment";
            var current=target.Instance;
            if(current!=null)
            {
                Heading(content,target.Kind==ItemTipKind.Pending?"待发放设置":target.Kind==ItemTipKind.Mail?"邮件附件属性":"当前物品属性");
                Line(content,"数量："+(stack||target.Kind==ItemTipKind.Pending?Number(current,"item_count"):"1")+(stack?"":" 件"),true);
                if(equipment&&!stack)Line(content,Enhancement(current),true);
                if(target.Kind==ItemTipKind.Inventory)
                {
                    string[] areas={"背包","时装","仓库","穿戴","扩展栏 4","账号仓库","扩展栏 6","宠物"};int space=J.N(current,"item_space");
                    Line(content,"位置："+(space>=0&&space<areas.Length?areas[space]:"物品栏 "+space)+" · 第 "+J.S(current,"slot_index")+" 格");
                }
                if(target.Kind!=ItemTipKind.Pending)
                {
                    int max=J.N(item,"durability");if(equipment&&max>0)Line(content,"耐久度："+J.S(current,"durability")+" / "+max);
                    if(equipment&&(J.B(item,"sealed")||J.N(current,"sealed")>0))Line(content,"封装："+(J.N(current,"sealed")>0?"已封装":"未封装"));
                    if(J.V(current,"lock_state")!=null)Line(content,"锁定："+(J.N(current,"lock_state")>0?"已锁定":"未锁定"));
                    int enchant=J.N(current,"enchant_card");if(enchant>0){string enchantName=null;if(relatedNames!=null)relatedNames.TryGetValue(enchant,out enchantName);Line(content,"附魔："+(String.IsNullOrEmpty(enchantName)?"卡片 "+enchant:enchantName+"（"+enchant+"）"),true);}
                    long expires=Convert.ToInt64(J.V(current,"expires_at")??0);if(expires>0&&expires<253402300800L)Line(content,"到期："+new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(expires).ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                }
            }
            string slot;if(slots.TryGetValue(J.S(item,"subtype"),out slot))Line(content,TrainerLanguage.T("部位：")+TrainerLanguage.T(slot));
            var jobs=J.V(item,"jobs") as IEnumerable;if(jobs!=null&&!(jobs is string))
            {
                var names=jobs.Cast<object>().Select(Convert.ToString).Where(job=>!String.IsNullOrWhiteSpace(job)).Select(job=>jobNames.ContainsKey(job)?jobNames[job]:job).Distinct().ToArray();
                if(names.Length>0)Line(content,"适用职业："+String.Join("、",names));
            }
            if(current==null&&equipment&&J.N(item,"durability")>0)Line(content,"最大耐久度："+J.S(item,"durability"));
            if(stack&&J.N(item,"stackLimit")>1)Line(content,"堆叠上限："+Number(item,"stackLimit"));
            if(tooltip!=null&&J.V(tooltip,"grade")!=null&&J.V(tooltip,"requiredLevel")!=null&&J.N(tooltip,"grade")!=J.N(tooltip,"requiredLevel"))Line(content,"装备等级："+J.N(tooltip,"grade"));
            if(tooltip!=null)Line(content,J.S(tooltip,"element"),true);
            var stats=Rows(tooltip!=null&&J.V(tooltip,"stats")!=null?tooltip["stats"]:J.V(item,"stats")).ToArray();
            if(stats.Length>0){Heading(content,stats.Any(stat=>J.S(stat,"value").Contains("～"))?"基础属性（品质范围）":"基础属性");foreach(var stat in stats)Line(content,J.S(stat,"name")+"："+J.S(stat,"value"));}
            var skills=tooltip==null?new Dictionary<string,object>[0]:Rows(J.V(tooltip,"skillBonuses")).ToArray();
            if(skills.Length>0){Heading(content,"技能加成");foreach(var skill in skills)Line(content,J.S(skill,"name")+"："+J.S(skill,"value"));}
            string description=(tooltip!=null&&J.V(tooltip,"description")!=null?J.S(tooltip,"description"):J.S(item,"description")).Replace("\r\n","\n").Replace("\r","\n").Trim();
            if(description.Length>0){Heading(content,"物品说明");Line(content,description);}
            if(tooltip!=null&&!String.IsNullOrWhiteSpace(J.S(tooltip,"flavor"))){Heading(content,"背景描述");Line(content,J.S(tooltip,"flavor"));}
            var avatar=J.V(item,"avatar") as Dictionary<string,object>;
            var options=avatar==null?new Dictionary<string,object>[0]:Rows(J.V(avatar,"options")).ToArray();
            if(options.Length>0){Heading(content,current!=null&&target.Kind!=ItemTipKind.Pending?"此款时装可选属性":"时装可选属性（选择一项）");foreach(var option in options)Line(content,J.S(option,"name")+"："+J.S(option,"value"));}
            if(detail!=null&&stats.Length==0&&description.Length==0&&options.Length==0)Line(content,"游戏资料未提供额外属性或说明。");
            if(error!=null){Heading(content,"详情暂未载入");Line(content,"请检查游戏服务，移开鼠标后重试。");}
            content.Name=TrainerLanguage.Content(content.Name);content.Meta=TrainerLanguage.Display(content.Meta);content.Footer=TrainerLanguage.T(content.Footer);foreach(var line in content.Lines)line.Text=TrainerLanguage.Display(line.Text);
            return content;
        }
    }

    internal sealed class ItemTipBlock
    {
        internal ItemTipLine Line;
        internal RectangleF Bounds;
    }
    internal sealed class ItemTipLayout
    {
        internal ItemTipContent Content;
        internal Size Size;
        internal float Scale,HeaderBottom;
        internal RectangleF Title,Meta,Icon,Footer;
        internal bool Truncated;
        internal readonly List<ItemTipBlock> Blocks=new List<ItemTipBlock>();
    }
    internal sealed class ItemTipRenderer : IDisposable
    {
        private Font title=TrainerLanguage.Font(11,FontStyle.Bold),normal=TrainerLanguage.Font(9),small=TrainerLanguage.Font(8),heading=TrainerLanguage.Font(8.5f,FontStyle.Bold);
        private readonly StringFormat format=new StringFormat {FormatFlags=StringFormatFlags.LineLimit,Trimming=StringTrimming.EllipsisCharacter};
        internal ItemTipRenderer(){TrainerLanguage.Changed+=RefreshFonts;}
        private void RefreshFonts(object sender,EventArgs args){title.Dispose();normal.Dispose();small.Dispose();heading.Dispose();title=TrainerLanguage.Font(11,FontStyle.Bold);normal=TrainerLanguage.Font(9);small=TrainerLanguage.Font(8);heading=TrainerLanguage.Font(8.5f,FontStyle.Bold);}
        private float Measure(Graphics graphics,string text,Font font,float width)
        {return (float)Math.Ceiling(graphics.MeasureString(text,font,new SizeF(Math.Max(1,width),100000),format).Height);}
        internal ItemTipLayout Layout(Graphics graphics,ItemTipContent content,Size max)
        {
            float scale=graphics.DpiX/96f,pad=16*scale,width=Math.Min(392*scale,max.Width),bodyWidth=width-2*pad;
            var layout=new ItemTipLayout {Content=content,Scale=scale};
            float titleHeight=Measure(graphics,content.Name,title,bodyWidth-52*scale);
            layout.Icon=new RectangleF(pad,pad,40*scale,40*scale);
            layout.Title=new RectangleF(pad+52*scale,pad,bodyWidth-52*scale,titleHeight);
            float metaHeight=Measure(graphics,content.Meta,small,bodyWidth-52*scale);
            layout.Meta=new RectangleF(layout.Title.X,layout.Title.Bottom+4*scale,layout.Title.Width,metaHeight);
            float top=Math.Max(layout.Icon.Bottom,layout.Meta.Bottom)+14*scale;layout.HeaderBottom=top;
            float footerHeight=Measure(graphics,content.Footer,small,bodyWidth)+20*scale;
            float limit=max.Height-pad-footerHeight;
            foreach(var line in content.Lines)
            {
                float before=line.Heading?9*scale:0,height=Measure(graphics,line.Text,line.Heading?heading:normal,bodyWidth);
                if(top+before+height>limit){layout.Truncated=true;break;}
                top+=before;layout.Blocks.Add(new ItemTipBlock {Line=line,Bounds=new RectangleF(pad,top,bodyWidth,height)});top+=height+3*scale;
            }
            if(layout.Truncated&&layout.Blocks.Count>0&&layout.Blocks[layout.Blocks.Count-1].Line.Heading){top=layout.Blocks[layout.Blocks.Count-1].Bounds.Top;layout.Blocks.RemoveAt(layout.Blocks.Count-1);}
            layout.Footer=new RectangleF(pad,top+10*scale,bodyWidth,footerHeight-12*scale);
            layout.Size=new Size((int)Math.Ceiling(width),Math.Min(max.Height,(int)Math.Ceiling(layout.Footer.Bottom+pad)));
            return layout;
        }
        internal void Draw(Graphics graphics,ItemTipLayout layout,Image image)
        {
            graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            graphics.Clear(Theme.Panel);float scale=layout.Scale;
            using(var border=new Pen(Theme.Line))graphics.DrawRectangle(border,0,0,layout.Size.Width-1,layout.Size.Height-1);
            using(var accent=new SolidBrush(Theme.Rarity(layout.Content.Rarity)))graphics.FillRectangle(accent,0,0,layout.Size.Width,3*scale);
            using(var fill=new SolidBrush(Theme.Bg))graphics.FillRectangle(fill,layout.Icon);
            if(image!=null){float factor=Math.Min(layout.Icon.Width/image.Width,layout.Icon.Height/image.Height);factor=Math.Min(factor,scale);float w=image.Width*factor,h=image.Height*factor;graphics.InterpolationMode=InterpolationMode.NearestNeighbor;graphics.DrawImage(image,layout.Icon.X+(layout.Icon.Width-w)/2,layout.Icon.Y+(layout.Icon.Height-h)/2,w,h);}
            DrawText(graphics,layout.Content.Name,title,Theme.Rarity(layout.Content.Rarity),layout.Title);
            DrawText(graphics,layout.Content.Meta,small,Theme.Muted,layout.Meta);
            using(var pen=new Pen(Theme.Line))graphics.DrawLine(pen,16*scale,layout.HeaderBottom-6*scale,layout.Size.Width-16*scale,layout.HeaderBottom-6*scale);
            foreach(var block in layout.Blocks)DrawText(graphics,block.Line.Text,block.Line.Heading?heading:normal,block.Line.Heading?Theme.Muted:block.Line.Accent?Theme.Gold:Theme.Text,block.Bounds);
            DrawText(graphics,(layout.Truncated?TrainerLanguage.T("更多属性 · "):"")+layout.Content.Footer,small,Theme.Muted,layout.Footer);
        }
        private void DrawText(Graphics graphics,string text,Font font,Color color,RectangleF bounds){using(var brush=new SolidBrush(color))graphics.DrawString(text,font,brush,bounds,format);}
        internal static Point Place(Point cursor,Size size,Rectangle area,int offset)
        {
            int x=cursor.X+offset,y=cursor.Y+offset;
            if(x+size.Width>area.Right)x=cursor.X-size.Width-offset;
            if(y+size.Height>area.Bottom)y=cursor.Y-size.Height-offset;
            return new Point(Math.Max(area.Left,Math.Min(x,area.Right-size.Width)),Math.Max(area.Top,Math.Min(y,area.Bottom-size.Height)));
        }
        public void Dispose(){TrainerLanguage.Changed-=RefreshFonts;title.Dispose();normal.Dispose();small.Dispose();heading.Dispose();format.Dispose();}
    }

    internal sealed class ItemHoverService : IDisposable
    {
        private readonly ItemDetailCache cache;
        private readonly ToolTip tip=new ToolTip {OwnerDraw=true,UseAnimation=false,UseFading=false,ShowAlways=false,AutoPopDelay=30000};
        private readonly System.Windows.Forms.Timer delay=new System.Windows.Forms.Timer {Interval=340};
        private readonly ItemHoverEpoch epoch=new ItemHoverEpoch();
        private readonly ItemTipRenderer renderer=new ItemTipRenderer();
        private readonly List<Binding> bindings=new List<Binding>();
        private Binding active;
        private DataGridViewRow row;
        private ItemTipLayout layout;
        private Image image;
        private bool disposed,openingDetails;
        internal ItemHoverService(LocalApi api)
        {
            cache=new ItemDetailCache(id=>J.O(api.Get("/api/item?id="+id)));
            delay.Tick+=async(s,e)=>{delay.Stop();await ShowHover();};
            tip.Popup+=(s,e)=>{if(layout!=null)e.ToolTipSize=layout.Size;};
            tip.Draw+=(s,e)=>{if(layout!=null)renderer.Draw(e.Graphics,layout,image);};
        }
        internal void Attach(DataGridView grid,Func<DataGridViewRow,ItemTipTarget> resolve)
        {
            var binding=new Binding(this,grid,resolve);bindings.Add(binding);grid.ShowCellToolTips=false;
        }
        internal void Hide()
        {
            epoch.Advance();delay.Stop();if(active!=null&&!active.Grid.IsDisposed)tip.Hide(active.Grid);active=null;row=null;layout=null;image=null;
        }
        private void Move(Binding binding,MouseEventArgs e)
        {
            if(disposed)return;
            var grid=binding.Grid;binding.BindOwner();var hit=grid.HitTest(e.X,e.Y);
            if(e.Button!=MouseButtons.None||!grid.Enabled||hit.RowIndex<0||hit.ColumnIndex<0||grid.Columns[hit.ColumnIndex] is DataGridViewButtonColumn){HideFor(binding);return;}
            if(grid.IsCurrentCellInEditMode&&grid.CurrentCell!=null&&!(grid.CurrentCell is DataGridViewCheckBoxCell)&&grid.CurrentCell.RowIndex==hit.RowIndex&&grid.CurrentCell.ColumnIndex==hit.ColumnIndex){HideFor(binding);return;}
            var hovered=grid.Rows[hit.RowIndex];if(active==binding&&row==hovered)return;
            Hide();ItemTipTarget target=binding.Resolve(hovered);if(target==null||target.Id<=0)return;
            active=binding;row=hovered;delay.Start();
        }
        private bool Valid(Binding binding,DataGridViewRow current,int request)
        {
            if(disposed||!epoch.Matches(request)||active!=binding||row!=current||binding.Grid.IsDisposed||!binding.Grid.Visible||!binding.Grid.Enabled||current.DataGridView!=binding.Grid)return false;
            Form owner=binding.Grid.FindForm();if(owner==null||(!owner.ContainsFocus&&Form.ActiveForm!=owner))return false;
            Point mouse=binding.Grid.PointToClient(Control.MousePosition);var hit=binding.Grid.HitTest(mouse.X,mouse.Y);
            return hit.RowIndex==current.Index&&hit.ColumnIndex>=0;
        }
        private async Task<Dictionary<int,string>> Related(ItemTipTarget target)
        {
            var names=new Dictionary<int,string>();int enchant=target.Instance==null?0:J.N(target.Instance,"enchant_card");
            if(enchant>0)try{names[enchant]=J.S(await cache.Get(enchant),"name");}catch{}
            return names;
        }
        private async Task ShowHover()
        {
            var binding=active;var current=row;int request=epoch.Advance();if(binding==null||current==null||!Valid(binding,current,request))return;
            var target=binding.Resolve(current);if(target==null||target.Id<=0)return;
            Dictionary<string,object> detail=null;string error=null;
            try{detail=await cache.Get(target.Id);}catch(Exception ex){error=ex.Message;}
            if(!Valid(binding,current,request))return;
            var related=error==null?await Related(target):null;if(!Valid(binding,current,request))return;
            // Resolve again after loading so edits never display an old instance snapshot.
            var fresh=binding.Resolve(current);if(fresh==null||fresh.Id!=target.Id)return;
            var content=ItemDetailText.Build(detail,fresh,error,related);Point cursor=Control.MousePosition;Rectangle screen=Screen.FromPoint(cursor).WorkingArea;
            using(var graphics=binding.Grid.CreateGraphics())layout=renderer.Layout(graphics,content,new Size(Math.Max(200,screen.Width-24),Math.Max(150,screen.Height-32)));
            image=RowImage(current);Point position=ItemTipRenderer.Place(cursor,layout.Size,screen,18);
            tip.Show(TrainerLanguage.T("物品详情"),binding.Grid,binding.Grid.PointToClient(position),30000);
        }
        private static Image RowImage(DataGridViewRow current)
        {return current!=null&&current.DataGridView!=null&&current.DataGridView.Columns.Contains("icon")?current.Cells["icon"].Value as Image:null;}
        private void HideFor(Binding binding){if(active==binding)Hide();}
        internal async Task ShowDetails(Control owner,ItemTipTarget target,Image icon=null)
        {
            if(disposed||openingDetails||target==null||target.Id<=0)return;Hide();openingDetails=true;
            try
            {
                Dictionary<string,object> detail=null;string error=null;try{detail=await cache.Get(target.Id);}catch(Exception ex){error=ex.Message;}
                var related=error==null?await Related(target):null;if(disposed||owner.IsDisposed)return;
                var content=ItemDetailText.Build(detail,target,error,related);
                using(var form=new Form {Text=TrainerLanguage.T("物品详情 · ")+content.Name,AutoScaleMode=AutoScaleMode.Dpi,AutoScaleDimensions=new SizeF(96,96),Size=new Size(640,690),MinimumSize=new Size(470,400),StartPosition=FormStartPosition.CenterParent,BackColor=Theme.Bg,ForeColor=Theme.Text,Font=owner.Font,MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false})
                {
                    var head=new Panel {Dock=DockStyle.Top,Height=85,Padding=new Padding(18)};
                    var pic=new PictureBox {Image=icon,SizeMode=PictureBoxSizeMode.CenterImage,BackColor=Theme.Field,Bounds=new Rectangle(18,18,44,44)};head.Controls.Add(pic);
                    var title=Theme.Label(content.Name,14,Theme.Rarity(content.Rarity));title.SetBounds(78,17,520,50);title.AutoSize=false;title.AutoEllipsis=true;title.Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;head.Controls.Add(title);
                    var text=new TextBox {Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BackColor=Theme.Field,ForeColor=Theme.Text,BorderStyle=BorderStyle.None,Font=TrainerLanguage.Font(10),Text=content.PlainText()};
                    var pad=new Panel {Dock=DockStyle.Fill,Padding=new Padding(20,8,20,20)};pad.Controls.Add(text);form.Controls.Add(pad);form.Controls.Add(head);TrainerDisplay.ShowDialog(form,owner);
                }
            }
            finally{openingDetails=false;}
        }
        public void Dispose()
        {
            if(disposed)return;Hide();disposed=true;foreach(var binding in bindings.ToArray())binding.Dispose();bindings.Clear();delay.Dispose();tip.Dispose();renderer.Dispose();
        }
        private sealed class Binding : IDisposable
        {
            private readonly ItemHoverService service;
            internal readonly DataGridView Grid;
            internal readonly Func<DataGridViewRow,ItemTipTarget> Resolve;
            private Form owner;
            internal Binding(ItemHoverService service,DataGridView grid,Func<DataGridViewRow,ItemTipTarget> resolve)
            {
                this.service=service;Grid=grid;Resolve=resolve;
                grid.MouseMove+=OnMove;grid.MouseLeave+=OnHide;grid.MouseDown+=OnDown;grid.MouseUp+=OnUp;grid.KeyDown+=OnKey;grid.Scroll+=OnScroll;grid.CellBeginEdit+=OnEdit;
                grid.RowsRemoved+=OnRemoved;grid.RowsAdded+=OnAdded;grid.VisibleChanged+=OnHide;grid.EnabledChanged+=OnHide;grid.Disposed+=OnDispose;
            }
            internal void BindOwner()
            {var form=Grid.FindForm();if(owner==form)return;if(owner!=null)owner.Deactivate-=OnHide;owner=form;if(owner!=null)owner.Deactivate+=OnHide;}
            private void OnMove(object sender,MouseEventArgs e){service.Move(this,e);}
            private void OnHide(object sender,EventArgs e){service.HideFor(this);}
            private void OnDown(object sender,MouseEventArgs e){service.HideFor(this);}
            private void OnUp(object sender,MouseEventArgs e){service.Move(this,new MouseEventArgs(MouseButtons.None,0,e.X,e.Y,0));}
            private void OnScroll(object sender,ScrollEventArgs e){service.HideFor(this);}
            private void OnEdit(object sender,DataGridViewCellCancelEventArgs e){service.HideFor(this);}
            private void OnRemoved(object sender,DataGridViewRowsRemovedEventArgs e){service.HideFor(this);}
            private void OnAdded(object sender,DataGridViewRowsAddedEventArgs e){service.HideFor(this);}
            private async void OnKey(object sender,KeyEventArgs e)
            {
                if(e.Alt&&e.KeyCode==Keys.Enter&&(!Grid.IsCurrentCellInEditMode||Grid.CurrentCell is DataGridViewCheckBoxCell))
                {
                    var current=service.active==this?service.row:Grid.CurrentRow;e.Handled=true;e.SuppressKeyPress=true;
                    if(current!=null)await service.ShowDetails(Grid,Resolve(current),RowImage(current));
                }
                else service.HideFor(this);
            }
            private void OnDispose(object sender,EventArgs e){Dispose();}
            public void Dispose()
            {
                service.HideFor(this);if(owner!=null){owner.Deactivate-=OnHide;owner=null;}
                Grid.MouseMove-=OnMove;Grid.MouseLeave-=OnHide;Grid.MouseDown-=OnDown;Grid.MouseUp-=OnUp;Grid.KeyDown-=OnKey;Grid.Scroll-=OnScroll;Grid.CellBeginEdit-=OnEdit;
                Grid.RowsRemoved-=OnRemoved;Grid.RowsAdded-=OnAdded;Grid.VisibleChanged-=OnHide;Grid.EnabledChanged-=OnHide;Grid.Disposed-=OnDispose;service.bindings.Remove(this);
            }
        }
    }
}
