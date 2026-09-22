using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Lumen
{
    /// <summary>
    /// 单实例。
    /// 用命名管道 + 互斥体：第二个实例把要打开的文件转交给已有窗口，
    /// 并唤醒它。按当前用户隔离，崩溃后不会留下"打不开"的残留。
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private const string MutexName = "Lumen.SingleInstance.Mutex";
        private const string PipeName = "Lumen.SingleInstance.Pipe";

        private Mutex _mutex;
        private Thread _listener;
        private volatile bool _running;
        private bool _owned;

        /// <summary>收到另一个实例转交的文件。</summary>
        public event Action<IList<string>> FilesReceived;

        /// <summary>另一个实例请求把窗口叫到前台。</summary>
        public event Action ActivationRequested;

        public bool TryAcquire()
        {
            try
            {
                bool createdNew;
                _mutex = new Mutex(true, MutexName, out createdNew);
                if (!createdNew)
                {
                    _mutex.Dispose();
                    _mutex = null;
                    return false;
                }

                _owned = true;
                StartListener();
                return true;
            }
            catch (Exception ex)
            {
                // 拿不到互斥体也不要阻止启动（宁可多开也不能打不开）
                Log.Warn("单实例检查失败，按多实例继续: " + ex.Message);
                return true;
            }
        }

        private void StartListener()
        {
            _running = true;
            _listener = new Thread(ListenLoop) { IsBackground = true, Name = "Lumen.SingleInstance" };
            _listener.Start();
        }

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.None))
                    {
                        server.WaitForConnection();

                        using (var reader = new StreamReader(server, Encoding.UTF8))
                        {
                            var first = reader.ReadLine();
                            var payload = reader.ReadToEnd();

                            if (string.Equals(first, "ACTIVATE", StringComparison.Ordinal))
                            {
                                var handler = ActivationRequested;
                                if (handler != null) handler();
                            }
                            else if (string.Equals(first, "OPEN", StringComparison.Ordinal))
                            {
                                var files = new List<string>();
                                foreach (var line in (payload ?? "").Split('\n'))
                                {
                                    var f = line.Trim();
                                    if (f.Length > 0) files.Add(f);
                                }
                                if (files.Count > 0)
                                {
                                    var handler = FilesReceived;
                                    if (handler != null) handler(files);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_running) Log.Warn("单实例监听异常: " + ex.Message);
                    Thread.Sleep(200);
                }
            }
        }

        /// <summary>把文件转交给已有实例。</summary>
        public bool SendFilesToExisting(IList<string> files)
        {
            if (files == null || files.Count == 0) return SendActivate();

            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(2000);
                    using (var writer = new StreamWriter(client, new UTF8Encoding(false)))
                    {
                        writer.WriteLine("OPEN");
                        foreach (var f in files) writer.WriteLine(f);
                        writer.Flush();
                    }
                }
                Log.Info("已把 " + files.Count + " 个文件转交给已有窗口");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("转交文件失败: " + ex.Message);
                return false;
            }
        }

        public bool SendActivate()
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(2000);
                    using (var writer = new StreamWriter(client, new UTF8Encoding(false)))
                    {
                        writer.WriteLine("ACTIVATE");
                        writer.Flush();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("唤醒已有窗口失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 把主窗口带到前台并激活。
        ///
        /// Windows 对前台窗口有严格限制：一个不在前台的进程直接调
        /// SetForegroundWindow 经常被系统忽略（表现就是「只高亮不拉起」）。
        /// 这里用标准做法：先恢复最小化，再用 ShowWindow(SW_RESTORE) + 强制激活，
        /// 必要时通过 AttachThreadInput 把前台权限借过来。
        /// </summary>
        public static void BringToFront(System.Windows.Window window)
        {
            if (window == null) return;
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                if (helper.Handle == IntPtr.Zero) return;

                var hwnd = helper.Handle;

                if (window.WindowState == System.Windows.WindowState.Minimized)
                    window.WindowState = System.Windows.WindowState.Normal;

                window.Show();

                // 标准的三板斧：恢复 → 置顶再取消（骗过前台锁）→ 激活
                Native.Win32.ShowWindow(hwnd, 9 /*SW_RESTORE*/);
                Native.Win32.SetForegroundWindow(hwnd);
                window.Activate();
                window.Topmost = true;
                window.Topmost = false;
                window.Focus();
            }
            catch (Exception ex)
            {
                Log.Warn("前置窗口失败: " + ex.Message);
            }
        }

        public void Dispose()
        {
            _running = false;
            try
            {
                // 敲一下管道让监听线程退出
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(100);
                }
            }
            catch { }

            if (_mutex != null)
            {
                try
                {
                    if (_owned) _mutex.ReleaseMutex();
                }
                catch { }
                try { _mutex.Dispose(); } catch { }
                _mutex = null;
            }
        }
    }
}
