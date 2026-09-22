using System;
using System.IO;
using NAudio.Wave;

namespace Lumen.Audio
{
    /// <summary>
    /// 基于 NAudio 的播放核心。
    /// 解码：MediaFoundationReader（MP3 / AAC / M4A / WMA / MP4 容器）
    ///       + AudioFileReader（WAV / AIFF / 以及 MF 不可用时的回退）
    /// 输出：WASAPI 共享模式（WasapiOut），失败时回退 WaveOut。
    /// 播放模式：顺序播放（到底停止）/ 列表循环 / 单曲循环。
    /// </summary>
    public sealed class Player : IDisposable
    {
        public enum PlayMode
        {
            Sequential = 0,   // 顺序播放（到底停止）
            RepeatList = 1,   // 列表循环
            RepeatOne = 2     // 单曲循环
        }

        private readonly object _gate = new object();

        private IWavePlayer _output;
        private WaveStream _reader;

        /// <summary>
        /// 音量/格式转换层。
        ///
        /// 不能用 VolumeWaveProvider16 —— 它只接受 16bit PCM，而
        /// AudioFileReader / MediaFoundationReader 输出的是 32bit 浮点，
        /// 会抛 "Expecting PCM input"。WaveChannel32 能接受任意输入格式，
        /// 并统一转成 32bit 浮点交给输出设备。
        /// </summary>
        private WaveChannel32 _volumeProvider;

        private string _currentPath;

        private volatile bool _stoppingIntentionally;
        private volatile bool _disposed;
        private float _volume = 0.8f;
        private bool _muted;
        private PlayMode _mode = PlayMode.Sequential;

        /// <summary>当前曲目自然播放结束（用于自动切下一首）。</summary>
        public event Action PlaybackEnded;

        /// <summary>播放失败（解码不了 / 设备异常）。</summary>
        public event Action<string> PlaybackFailed;

        public event Action PlaybackStarted;

        // ------------------------------------------------------------------
        // 状态
        // ------------------------------------------------------------------

        public string CurrentPath { get { return _currentPath; } }

        public bool HasTrack { get { return _reader != null && _currentPath != null; } }

        public bool IsPlaying
        {
            get { return _output != null && _output.PlaybackState == PlaybackState.Playing; }
        }

        public bool IsPaused
        {
            get { return _output != null && _output.PlaybackState == PlaybackState.Paused; }
        }

        /// <summary>当前播放位置（秒）。</summary>
        public double PositionSeconds
        {
            get
            {
                var r = _reader;
                if (r == null) return 0;
                try { return r.CurrentTime.TotalSeconds; }
                catch { return 0; }
            }
        }

        /// <summary>总时长（秒）；未知返回 0。</summary>
        public double DurationSeconds
        {
            get
            {
                var r = _reader;
                if (r == null) return 0;
                try
                {
                    var total = r.TotalTime.TotalSeconds;
                    return total > 0 && !double.IsNaN(total) && !double.IsInfinity(total) ? total : 0;
                }
                catch { return 0; }
            }
        }

        public PlayMode Mode
        {
            get { return _mode; }
            set { _mode = value; }
        }

        public float Volume
        {
            get { return _volume; }
            set
            {
                _volume = Math.Max(0f, Math.Min(1f, value));
                var vp = _volumeProvider;
                if (vp != null) vp.Volume = _muted ? 0f : _volume;
            }
        }

        public bool Muted
        {
            get { return _muted; }
            set
            {
                _muted = value;
                var vp = _volumeProvider;
                if (vp != null) vp.Volume = _muted ? 0f : _volume;
            }
        }

        /// <summary>音频输出设备是否可用。</summary>
        public static bool HasOutputDevice()
        {
            try
            {
                return WaveOut.DeviceCount > 0;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        // 打开 / 播放 / 暂停 / 停止
        // ------------------------------------------------------------------

        /// <summary>
        /// 打开文件并开始播放。startSeconds &gt; 0 时从该位置开始。
        /// 失败会触发 PlaybackFailed 并返回 false。
        /// </summary>
        public bool Play(string path, double startSeconds)
        {
            lock (_gate)
            {
                if (_disposed) return false;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    RaiseFailed("文件不存在: " + path);
                    return false;
                }

                try
                {
                    CloseInternal();

                    _stoppingIntentionally = false;
                    _reader = OpenReader(path);
                    if (_reader == null)
                    {
                        RaiseFailed("无法解码: " + Path.GetFileName(path));
                        return false;
                    }

                    if (startSeconds > 0.05)
                    {
                        var target = TimeSpan.FromSeconds(startSeconds);
                        if (target < _reader.TotalTime) _reader.CurrentTime = target;
                    }

                    _volumeProvider = new WaveChannel32(_reader) { Volume = _muted ? 0f : _volume, PadWithZeroes = false };
                    _output = CreateOutput();
                    _output.PlaybackStopped += OnPlaybackStopped;
                    _output.Init(_volumeProvider);
                    _currentPath = path;
                    _output.Play();

                    var started = PlaybackStarted;
                    if (started != null) started();
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("播放失败: " + path, ex);
                    CloseInternal();
                    _currentPath = null;
                    RaiseFailed(Describe(ex, path));
                    return false;
                }
            }
        }

        /// <summary>只加载不播放（用于恢复上次进度）。</summary>
        public bool Load(string path, double startSeconds)
        {
            lock (_gate)
            {
                if (_disposed) return false;
                try
                {
                    CloseInternal();
                    _stoppingIntentionally = false;
                    _reader = OpenReader(path);
                    if (_reader == null) return false;
                    if (startSeconds > 0.05)
                    {
                        var target = TimeSpan.FromSeconds(startSeconds);
                        if (target < _reader.TotalTime) _reader.CurrentTime = target;
                    }
                    _volumeProvider = new WaveChannel32(_reader) { Volume = _muted ? 0f : _volume, PadWithZeroes = false };
                    _output = CreateOutput();
                    _output.PlaybackStopped += OnPlaybackStopped;
                    _output.Init(_volumeProvider);
                    _currentPath = path;
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("加载失败: " + path, ex);
                    CloseInternal();
                    return false;
                }
            }
        }

        public void Pause()
        {
            var o = _output;
            if (o == null) return;
            try { if (o.PlaybackState == PlaybackState.Playing) o.Pause(); } catch (Exception ex) { Log.Warn("暂停失败: " + ex.Message); }
        }

        public void Resume()
        {
            var o = _output;
            if (o == null) return;
            try { if (o.PlaybackState != PlaybackState.Playing) o.Play(); } catch (Exception ex) { Log.Warn("继续失败: " + ex.Message); }
        }

        public void TogglePlayPause()
        {
            if (!HasTrack) return;
            if (IsPlaying) Pause(); else Resume();
        }

        public void Stop()
        {
            lock (_gate)
            {
                _stoppingIntentionally = true;
                CloseInternal();
            }
        }

        /// <summary>跳转到指定秒数。</summary>
        public void Seek(double seconds)
        {
            var r = _reader;
            if (r == null) return;
            try
            {
                var total = r.TotalTime;
                var target = TimeSpan.FromSeconds(Math.Max(0, seconds));
                if (target > total) target = total;
                r.CurrentTime = target;
            }
            catch (Exception ex)
            {
                Log.Warn("跳转失败: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        /// <summary>
        /// 按扩展名选择解码器。MP3/AAC/M4A/WMA/MP4 走 Media Foundation，
        /// WAV/AIFF 走 NAudio 自带解析；两者都失败时互相回退。
        /// </summary>
        private static WaveStream OpenReader(string path)
        {
            var ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            bool preferManaged = ext == ".wav" || ext == ".aiff" || ext == ".aif" || ext == ".aifc";

            if (preferManaged)
            {
                try { return new AudioFileReader(path); }
                catch (Exception ex) { Log.Warn("AudioFileReader 失败，回退 MediaFoundation: " + ex.Message); }
                try { return new MediaFoundationReader(path); }
                catch (Exception ex) { Log.Error("MediaFoundation 也失败: " + ex.Message); return null; }
            }

            try { return new MediaFoundationReader(path); }
            catch (Exception ex) { Log.Warn("MediaFoundation 失败，回退 AudioFileReader: " + ex.Message); }
            try { return new AudioFileReader(path); }
            catch (Exception ex) { Log.Error("解码失败: " + ex.Message); return null; }
        }

        /// <summary>优先 WASAPI 共享模式，失败回退 WaveOut。</summary>
        private static IWavePlayer CreateOutput()
        {
            try
            {
                var wasapi = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);
                Log.Info("音频输出: WASAPI 共享模式");
                return wasapi;
            }
            catch (Exception ex)
            {
                Log.Warn("WASAPI 不可用，回退 WaveOut: " + ex.Message);
                return new WaveOutEvent { DesiredLatency = 200 };
            }
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (_stoppingIntentionally || _disposed) return;

            if (e != null && e.Exception != null)
            {
                Log.Error("播放中断", e.Exception);
                RaiseFailed(Describe(e.Exception, _currentPath));
                return;
            }

            // 自然结束
            var handler = PlaybackEnded;
            if (handler != null) handler();
        }

        private void RaiseFailed(string message)
        {
            var handler = PlaybackFailed;
            if (handler != null) handler(message);
        }

        private static string Describe(Exception ex, string path)
        {
            var name = string.IsNullOrEmpty(path) ? "" : Path.GetFileName(path) + " ";
            return name + "播放失败：" + ex.Message;
        }

        private void CloseInternal()
        {
            if (_output != null)
            {
                try
                {
                    _output.PlaybackStopped -= OnPlaybackStopped;
                    _output.Stop();
                }
                catch { }
                try { _output.Dispose(); } catch { }
                _output = null;
            }
            if (_reader != null)
            {
                try { _reader.Dispose(); } catch { }
                _reader = null;
            }
            _volumeProvider = null;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _stoppingIntentionally = true;
                CloseInternal();
                _currentPath = null;
            }
        }
    }
}
