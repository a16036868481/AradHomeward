// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Dnf60Desktop
{
    internal static class TrainerDisplay
    {
        internal static T Attach<T>(T control) where T:Control
        {TrainerLanguage.Attach(control);return control;}
        internal static void FollowLanguage(Control lifetime,Action refresh)
        {EventHandler changed=(sender,args)=>{if(!lifetime.IsDisposed)refresh();};TrainerLanguage.Changed+=changed;lifetime.Disposed+=(sender,args)=>TrainerLanguage.Changed-=changed;}
        internal static void ConfigureGrid(DataGridView grid,string sourceName)
        {
            switch(sourceName)
            {
                case "物品目录":case "物品选择列表":
                    TrainerLanguage.LocalizeGrid(grid,new[]{"rarity","category"},new[]{"name"});break;
                case "已选物品":
                    TrainerLanguage.LocalizeGrid(grid,new[]{"mode"},new[]{"name"});break;
                case "背包与穿戴":
                    TrainerLanguage.LocalizeGrid(grid,null,new[]{"name","space","count","mode"});break;
                case "修改记录":
                    TrainerLanguage.LocalizeGrid(grid,new[]{"action","role","result"},null);break;
                case "时装列表":
                    TrainerLanguage.LocalizeGrid(grid,null,new[]{"name","slot","jobs"});break;
                case "背包清理列表":
                    TrainerLanguage.LocalizeGrid(grid,null,new[]{"name","space","count"});break;
                case "任务列表":
                    TrainerLanguage.LocalizeGrid(grid,new[]{"status"},new[]{"name","choice"});break;
                case "频道开关":
                    TrainerLanguage.LocalizeGrid(grid,null,new[]{"name","port"});break;
                case "玩法参数":
                    TrainerLanguage.LocalizeGrid(grid,null,new[]{"note"});break;
            }
        }
        internal static void ConfigureAdminGrid(DataGridView grid,string[] sourceHeaders)
        {
            // Public definition columns are translated for display. Authored mail,
            // player/account identity and file names remain exactly as stored.
            var privateHeaders=new HashSet<string>(new[]{"角色","角色名","角色名称","账号","账号名","账号名称","发件人","发送者","主题","标题","邮件主题","邮件标题","内容","正文","邮件正文","完整数据库备份","备份文件","原始字段","设置值"});
            var columns=sourceHeaders.Select((header,index)=>new{header,index}).Where(row=>!privateHeaders.Contains(row.header)).Select(row=>"c"+row.index).ToArray();
            TrainerLanguage.LocalizeGrid(grid,null,columns);
        }
        internal static DialogResult ShowDialog(Form form,IWin32Window owner)
        {TrainerLanguage.Attach(form);return form.ShowDialog(owner);}
        internal static DialogResult ShowDialog(CommonDialog dialog,IWin32Window owner)
        {return dialog.ShowDialog(owner);}
        internal static string HelpText()
        {string file=Path.Combine(Program.Root,"localization","trainer","help-"+TrainerLanguage.Locale+".txt");return File.Exists(file)?File.ReadAllText(file,Encoding.UTF8):TrainerLanguage.T("使用说明");}
        internal static string LogText(string raw)
        {
            return String.Join(Environment.NewLine,(raw??"").Replace("\r\n","\n").Split('\n').Select(line=>
            {
                // Translate a complete known system message after its timestamp.
                // Captured names/paths and unknown diagnostic lines stay verbatim.
                var service=Regex.Match(line,@"^(?<time>\[\d{2}:\d{2}:\d{2}(?:[.,]\d+)?\])(?<tags>(?:\[[^\]]+\])+)(?<space>\s*)(?<body>.*)$");
                if(service.Success)return service.Groups["time"].Value+Regex.Replace(service.Groups["tags"].Value,@"\[(?<label>[^\]]+)\]",m=>"["+TrainerLanguage.T(m.Groups["label"].Value)+"]")+service.Groups["space"].Value+TrainerLanguage.T(service.Groups["body"].Value);
                var match=Regex.Match(line,@"^(?<prefix>(?:\d{4}-\d{2}-\d{2}[ T][\d:.,+Z-]+(?:\s+\[[^\]]+\])?|\d{2}:\d{2}:\d{2}(?:[.,]\d+)?(?:\s+STATE\s+\S+(?:\s+[A-Za-z][A-Za-z0-9_]*=[A-Za-z0-9_.:-]+)*)?)\s+)(?<body>.*)$");
                return match.Success?match.Groups["prefix"].Value+TrainerLanguage.T(match.Groups["body"].Value):TrainerLanguage.T(line);
            }));
        }
    }
}
