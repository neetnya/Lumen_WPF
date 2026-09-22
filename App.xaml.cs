using System;
using System.Windows;

namespace Lumen
{
    /// <summary>
    /// 应用入口。真正的 Main 由 WPF 从 App.xaml 生成，
    /// 这里只负责在启动时装配 Lumen。
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Bootstrap.Run(this, e.Args);
        }
    }
}
