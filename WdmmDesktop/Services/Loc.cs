using System;
using System.Collections.Generic;
using Windows.Globalization;

namespace WdmmDesktop.Services
{
    /// <summary>
    /// Desktop i18n. Keys match Strings/*/Resources.resw.
    /// Prefers ResourceLoader; falls back to in-memory maps (Linux/dev).
    /// Locale driven by ApplicationLanguages.PrimaryLanguageOverride / app_locale KV.
    /// </summary>
    public static class Loc
    {
        static readonly Dictionary<string, string> En = new()
        {
            ["Nav.Folders"] = "Folders",
            ["Nav.Music"] = "Music",
            ["Nav.Playlists"] = "Playlists",
            ["Nav.Manga"] = "Manga",
            ["Nav.Network"] = "File source",
            ["Nav.Tasks"] = "Tasks",
            ["Nav.About"] = "About",
            ["Nav.Settings"] = "Settings",
            ["Network.Sources"] = "Source",
            ["Network.ManageSources"] = "Manage sources…",
            ["Network.Up"] = "Up",
            ["Network.Refresh"] = "Refresh",
            ["Network.NewFolder"] = "New folder",
            ["Network.Empty"] = "No items in this folder",
            ["Network.NoSources"] = "Add a source to browse",
            ["Network.Retry"] = "Retry",
            ["Network.ColName"] = "Name",
            ["Network.ColSize"] = "Size",
            ["Network.Rename"] = "Rename",
            ["Network.Delete"] = "Delete",
            ["Settings.FileSource"] = "File source",
            ["Settings.SyncBackup"] = "Sync & backup",
            ["Settings.SyncPickSource"] = "Source",
            ["Settings.SyncRemotePath"] = "Remote path",
            ["Settings.SyncNow"] = "Sync now",
            ["Settings.Language"] = "Language",
        };

        static readonly Dictionary<string, string> Zh = new()
        {
            ["Nav.Folders"] = "文件夹",
            ["Nav.Music"] = "音乐",
            ["Nav.Playlists"] = "歌单",
            ["Nav.Manga"] = "漫画",
            ["Nav.Network"] = "文件源",
            ["Nav.Tasks"] = "任务",
            ["Nav.About"] = "关于",
            ["Nav.Settings"] = "设置",
            ["Network.Sources"] = "源",
            ["Network.ManageSources"] = "管理源…",
            ["Network.Up"] = "上级",
            ["Network.Refresh"] = "刷新",
            ["Network.NewFolder"] = "新建文件夹",
            ["Network.Empty"] = "此文件夹为空",
            ["Network.NoSources"] = "请先添加源再浏览",
            ["Network.Retry"] = "重试",
            ["Network.ColName"] = "名称",
            ["Network.ColSize"] = "大小",
            ["Network.Rename"] = "重命名",
            ["Network.Delete"] = "删除",
            ["Settings.FileSource"] = "文件源",
            ["Settings.SyncBackup"] = "同步与备份",
            ["Settings.SyncPickSource"] = "源",
            ["Settings.SyncRemotePath"] = "远端路径",
            ["Settings.SyncNow"] = "立即同步",
            ["Settings.Language"] = "界面语言",
        };

        static string _tag = "zh-CN";

        public static string CurrentTag => _tag;

        public static void ApplyLocale(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag) || tag == "system")
            {
                _tag = "zh-CN";
                try { ApplicationLanguages.PrimaryLanguageOverride = ""; } catch { }
                return;
            }
            _tag = tag.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";
            try { ApplicationLanguages.PrimaryLanguageOverride = _tag; } catch { }
        }

        public static string Get(string key)
        {
            try
            {
                var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
                var v = loader.GetString(key.Replace('.', '/'));
                if (!string.IsNullOrEmpty(v)) return v;
            }
            catch { /* ResourceLoader may be unavailable off-PRI / Linux */ }

            var map = _tag.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? En : Zh;
            return map.TryGetValue(key, out var s) ? s : key;
        }
    }
}
