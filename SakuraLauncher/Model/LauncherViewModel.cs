﻿using System;
using System.Windows;
using System.Threading;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using SakuraLibrary.Model;
using SakuraLibrary.Proto;
using SakuraLibrary.Helper;

using SakuraLauncher.Helper;

namespace SakuraLauncher.Model
{
    public class LauncherViewModel : LauncherModel
    {
        public Dictionary<string, string> failedData = new Dictionary<string, string>();

        public readonly Func<string, bool> SimpleConfirmHandler = message => App.ShowMessage(message, "操作确认", MessageBoxImage.Asterisk, MessageBoxButton.OKCancel) == MessageBoxResult.OK;
        public readonly Action<bool, string> SimpleHandler = (success, message) => App.ShowMessage(message, success ? "操作成功" : "操作失败", success ? MessageBoxImage.Information : MessageBoxImage.Error, MessageBoxButton.OK);
        public readonly Action<bool, string> SimpleFailureHandler = (success, message) =>
        {
            if (!success)
            {
                App.ShowMessage(message, "操作失败", MessageBoxImage.Error, MessageBoxButton.OK);
            }
        };

        public readonly MainWindow View;

        public LauncherViewModel(MainWindow view)
        {
            View = view;
            Dispatcher = new DispatcherWrapper(View.Dispatcher.Invoke, a => View.Dispatcher.BeginInvoke(a), View.Dispatcher.CheckAccess);

            Load();
        }

        public override void Log(Log l, bool init)
        {
            var entry = new LogModel()
            {
                Source = l.Source,
                Data = l.Data
            };
            if (l.Category == 0) // CATEGORY_FRPC
            {
                var match = LogModel.Pattern.Match(l.Data);
                if (match.Success)
                {
                    if (failedData.ContainsKey(l.Source))
                    {
                        if (View.IsVisible && !SuppressInfo)
                        {
                            string failedData_ = failedData[l.Source];
                            ThreadPool.QueueUserWorkItem(s => App.ShowMessage(failedData_, "隧道日志", MessageBoxImage.Information));
                        }
                        failedData.Remove(l.Source);
                    }
                    entry.Time = match.Groups["Time"].Value;
                    entry.Data = match.Groups["Content"].Value;
                    entry.Level = match.Groups["Level"].Value + ":" + match.Groups["Source"].Value;
                    switch (match.Groups["Level"].Value)
                    {
                    case "W":
                        entry.LevelColor = LogModel.BrushWarning;
                        break;
                    case "E":
                        entry.LevelColor = LogModel.BrushError;
                        break;
                    }
                }
                else if (!init)
                {
                    if (!failedData.ContainsKey(l.Source))
                    {
                        failedData[l.Source] = "";
                    }
                    failedData[l.Source] += l.Data + "\n";
                }
            }
            else
            {
                entry.Time = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                switch (l.Category)
                {
                case 1:
                default:
                    entry.Level = "INFO";
                    break;
                case 2:
                    entry.Level = "WARNING";
                    entry.LevelColor = LogModel.BrushWarning;
                    break;
                case 3:
                    entry.Level = "ERROR";
                    entry.LevelColor = LogModel.BrushError;
                    break;
                }
            }
            Logs.Add(entry);
            while (Logs.Count > 4096) Logs.RemoveAt(0);
        }

        public override void Load()
        {
            var settings = Properties.Settings.Default;

            View.Width = settings.Width;
            View.Height = settings.Height;

            SuppressInfo = settings.SuppressInfo;
            LogTextWrapping = settings.LogTextWrapping;
        }

        public override void Save()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Save());
                return;
            }

            var settings = Properties.Settings.Default;

            settings.Width = (int)View.Width;
            settings.Height = (int)View.Height;

            settings.SuppressInfo = SuppressInfo;
            settings.LogTextWrapping = LogTextWrapping;

            settings.Save();
            settings.Upgrade();
        }

        public override bool Refresh()
        {
            if (base.Refresh())
            {
                SwitchTab(LoggedIn ? 0 : 2);
                return true;
            }
            return false;
        }

        public ObservableCollection<LogModel> Logs { get; set; } = new ObservableCollection<LogModel>();

        [SourceBinding(nameof(CurrentTab))]
        public TabIndexTester CurrentTabTester { get; set; }

        public void SwitchTab(int id)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SwitchTab(id));
                return;
            }
            if (CurrentTab != id)
            {
                CurrentTab = id;
                View.BeginTabStoryboard("TabHideAnimation");
            }
        }
    }
}
