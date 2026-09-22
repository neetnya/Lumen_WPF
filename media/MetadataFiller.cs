using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Lumen.Lyrics;
using Lumen.Models;

namespace Lumen.Media
{
    /// <summary>
    /// 后台元数据补全。
    ///
    /// 性能要点（修复「导入 600 首卡好几秒」）：
    ///   1. 用**线程池并行**探测，而不是单线程串行。
    ///      探测时长要开 Media Foundation，单文件约 50~100ms；
    ///      600 首串行要 30~60 秒，多线程后只需几秒。
    ///   2. 界面刷新做**节流**（最多每 400ms 一次），避免每探测完一首
    ///      就跨线程刷新整个列表。
    ///   3. 队列清空后一定补一次最终刷新。
    /// </summary>
    public sealed class MetadataFiller
    {
        /// <summary>界面刷新的最小间隔（毫秒）。</summary>
        private const int FlushIntervalMs = 400;

        /// <summary>并行探测的线程数。Media Foundation 不宜开太多。</summary>
        private static readonly int MaxParallel = Math.Max(2, Math.Min(6, Environment.ProcessorCount));

        private readonly List<Track> _queue = new List<Track>();
        private readonly object _gate = new object();
        private readonly System.Windows.Threading.Dispatcher _dispatcher;

        private int _active;
        private volatile bool _cancelled;
        private int _flushQueued;
        private DateTime _lastFlush = DateTime.UtcNow;

        /// <summary>一批元数据补完（在 UI 线程回调）。</summary>
        public event Action<IList<Track>> BatchCompleted;

        /// <summary>全部补完（在 UI 线程回调）。</summary>
        public event Action AllCompleted;

        public MetadataFiller(System.Windows.Threading.Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public int PendingCount
        {
            get { lock (_gate) return _queue.Count + _active; }
        }

        public bool IsRunning { get { return _active > 0; } }

        /// <summary>加入待补全的曲目（会跳过已补全的）。</summary>
        public void Enqueue(IEnumerable<Track> tracks)
        {
            if (tracks == null) return;

            int added = 0;
            lock (_gate)
            {
                foreach (var t in tracks)
                {
                    if (t == null || t.MetadataLoaded) continue;
                    _queue.Add(t);
                    added++;
                }
            }

            if (added == 0) return;

            // 启动若干并行工作者
            int workers = Math.Min(MaxParallel, added);
            for (int i = 0; i < workers; i++)
            {
                Interlocked.Increment(ref _active);
                ThreadPool.QueueUserWorkItem(delegate { WorkLoop(); });
            }
        }

        /// <summary>停止当前补全（重新扫描库时用）。</summary>
        public void Cancel()
        {
            _cancelled = true;
            lock (_gate) _queue.Clear();
        }

        private void WorkLoop()
        {
            try
            {
                while (!_cancelled)
                {
                    Track track;
                    lock (_gate)
                    {
                        if (_queue.Count == 0) break;
                        // 从尾部取，减少 list 搬移开销
                        int last = _queue.Count - 1;
                        track = _queue[last];
                        _queue.RemoveAt(last);
                    }

                    if (track == null) continue;
                    if (!track.MetadataLoaded) Fill(track);

                    bool empty;
                    lock (_gate) empty = _queue.Count == 0;
                    Flush(empty);
                }
            }
            catch (Exception ex)
            {
                Log.Error("元数据补全线程异常", ex);
            }
            finally
            {
                if (Interlocked.Decrement(ref _active) == 0) OnAllDone();
            }
        }

        private void Fill(Track track)
        {
            double duration = 0;
            try
            {
                if (File.Exists(track.Path)) duration = MediaScanner.ProbeDuration(track.Path);
            }
            catch (Exception ex)
            {
                Log.Warn("探测时长失败 " + track.Path + ": " + ex.Message);
            }

            int hasLyrics = 0;
            try
            {
                hasLyrics = LyricsFinder.FindLyricsFile(track.Path) != null ? 1 : 0;
            }
            catch { }

            track.Duration = duration;
            track.HasLyrics = hasLyrics;
            track.MetadataLoaded = true;
        }

        /// <summary>节流后请求一次界面刷新。</summary>
        private void Flush(bool force)
        {
            if (_dispatcher == null) return;

            if (!force)
            {
                var elapsed = (DateTime.UtcNow - _lastFlush).TotalMilliseconds;
                if (elapsed < FlushIntervalMs) return;
            }

            // 同一时刻只排一个刷新
            if (Interlocked.CompareExchange(ref _flushQueued, 1, 0) != 0) return;
            _lastFlush = DateTime.UtcNow;

            _dispatcher.BeginInvoke(new Action(delegate
            {
                Interlocked.Exchange(ref _flushQueued, 0);
                var handler = BatchCompleted;
                if (handler == null) return;
                try { handler(new List<Track>()); }
                catch (Exception ex) { Log.Warn("刷新元数据失败: " + ex.Message); }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnAllDone()
        {
            if (_cancelled || _dispatcher == null) return;

            _dispatcher.BeginInvoke(new Action(delegate
            {
                var handler = AllCompleted;
                if (handler == null) return;
                try { handler(); }
                catch (Exception ex) { Log.Warn("元数据完成回调失败: " + ex.Message); }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
