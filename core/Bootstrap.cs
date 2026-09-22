using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Lumen
{
    /// <summary>
    /// 启动装配：
    /// 命令行解析 → 单实例 → 启动提示卡片 → 建主窗口 → 打开文件 → 异常兜底。
    /// </summary>
    public static class Bootstrap
    {
        public static void Run(Application app, string[] args)
        {
            // 全局异常兜底：不让播放器无声崩掉
            app.DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnTaskException;

            var options = CommandLine.Parse(args ?? new string[0]);

            Log.Info("=== Lumen 启动 === " + string.Join(" ", args ?? new string[0]));
            Log.Info("数据目录: " + Paths.DataDir);

            // 自检模式：不需要窗口，跑完直接退出
            if (options.SelfTestSeconds > 0)
            {
                int exit = SelfTest.Runner.Run(options.SelfTestSeconds);
                app.Shutdown(exit);
                return;
            }

            if (options.ShowHelp)
            {
                CommandLine.PrintHelp();
                app.Shutdown(0);
                return;
            }

            // 单实例：第二个实例把文件转交给已有窗口
            var single = new SingleInstance();
            if (!single.TryAcquire())
            {
                if (options.Files.Count > 0) single.SendFilesToExisting(options.Files);
                else single.SendActivate();
                app.Shutdown(0);
                return;
            }

            var window = new UI.MainWindow(options, single);
            app.MainWindow = window;

            // 启动提示卡片：窗口出来之前先显示
            var splash = new UI.SplashCard();
            splash.Show();
            splash.SetStatus("正在读取音乐库…");

            window.Show();
            window.InitializeAfterShow(splash);

            // --smoke：正常启动界面，若干秒后自动退出（实机冒烟验证）
            if (options.SmokeSeconds > 0)
            {
                Log.Info("冒烟模式：将在 " + options.SmokeSeconds + " 秒后自动退出");
                var smokeTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(options.SmokeSeconds)
                };
                smokeTimer.Tick += delegate
                {
                    smokeTimer.Stop();
                    Log.Info("冒烟模式：正常退出");
                    try { window.ExitApplication(); }
                    catch { app.Shutdown(0); }
                };
                smokeTimer.Start();
            }

            // 托盘常驻：关掉主窗口不退出
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("界面线程未处理异常", e.Exception);
            e.Handled = true;
            try
            {
                UI.Dialogs.ShowError(Application.Current != null ? Application.Current.MainWindow : null,
                    "出错了", e.Exception.Message);
            }
            catch { }
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Error("未处理异常", e.ExceptionObject as Exception);
        }

        private static void OnTaskException(object sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            Log.Error("后台任务异常", e.Exception);
            e.SetObserved();
        }
    }
}
