using System;
using NAudio.Wave;

namespace CODMControllerProbe
{
    /// <summary>
    /// 枪声检测震动：用 WASAPI 环回采集系统输出音频（游戏声音流经默认渲染设备），
    /// 逐块计算 RMS 能量。基线采用"底噪追踪"（只信任低值）：
    ///   - 当前块能量低于底噪 → 立即更新为当前块（捕捉安静水平）；
    ///   - 高于底噪 → 底噪按每块 0.3% 缓慢上漂（环境变吵时逐步适应，但连发时不会被枪声抬满）；
    /// 瞬时能量 ≥ 底噪 × 灵敏度倍数 即判定为一次枪声爆音——连续开火时每一发都会触发
    /// （此前"滑动平均基线"会被第一枪抬高，导致第 2 发起全部漏检）。
    /// 触发 <see cref="ShotDetected"/> 回调。纯音频分析，不读取游戏内存/文件，不触碰反作弊红线。
    /// </summary>
    public sealed class ShotRumbleListener : IDisposable
    {
        private WasapiLoopbackCapture? _cap;
        private bool _running;
        private readonly object _lock = new();
        private float _floor = 0.02f;             // 底噪水平（只追踪低值，枪声不抬高它）
        private float _prevRms;                    // 上一块 RMS（强度参考保留）
        private DateTime _lastShot = DateTime.MinValue;
        private int _burstCount;                   // 300ms 内连续爆音计数（识别连发/单发）
        private readonly float _burstWindowSec = 0.30f; // 连发判定窗口
        private const float MinAbsRms = 0.004f;   // 绝对下限：低于此视为静音，不触发

        /// <summary>检测到一次枪声爆音。intensity ≥ 1，越大越响（用于响度联动）；
        /// burst = 300ms 内连续第 2 发及以上（连发，游戏内为自动/全自动开火）。</summary>
        public event Action<float, bool>? ShotDetected;

        /// <summary>当前灵敏度倍数（瞬时能量 ≥ 底噪 × 倍数 判定为枪声），1.5~8，数值越小越灵敏。</summary>
        public float Sensitivity { get; set; } = 3f;

        /// <summary>低频增强：true=低频（约 300Hz 以下）能量参与枪声判定与强度，重型枪/爆炸更易触发。</summary>
        public bool LowBoost { get; set; } = true;

        /// <summary>前台门控：非 null 时每块音频先经此判定，返回 false 则跳过（不做 RMS/触发）。
        /// 用于"仅游戏前台时检测枪声"——后台放歌/看视频不会误触发震动。判定必须快速、可抛异常兜底。</summary>
        public Func<bool>? Gate { get; set; }

        public bool Running => _running;

        public void Start()
        {
            lock (_lock)
            {
                if (_running) return;
                try
                {
                    _cap = new WasapiLoopbackCapture();
                    _cap.DataAvailable += OnData;
                    _cap.RecordingStopped += (_, _) => { };
                    _cap.StartRecording();
                    _running = true;
                }
                catch
                {
                    _cap?.Dispose();
                    _cap = null;
                    _running = false;
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_running) return;
                _running = false;
                try { _cap?.StopRecording(); } catch { }
                _cap?.Dispose();
                _cap = null;
            }
        }

        private void OnData(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded < 4) return;
            if (Gate != null)
            {
                try { if (!Gate()) return; } catch { return; } // 门控判定异常时安全跳过，不误触发
            }
            float rms = ComputeRms(e.Buffer, e.BytesRecorded);
            float eff = rms;
            if (LowBoost)
            {
                // 低频增强：一阶低通（约 300Hz 截止）后取低频能量，与宽带能量取大——
                // 重型枪/爆炸/部分消音枪低频成分突出，宽带 RMS 偏低会漏检，低频能量补上
                float low = ComputeLow(e.Buffer, e.BytesRecorded);
                if (low > rms * 1.6f) eff = low;
            }
            lock (_lock)
            {
                // 底噪追踪：只信任低值。eff 更低 → 立即采纳（捕捉安静水平）；
                // eff 更高 → 底噪每块缓慢上漂 0.3%（环境音量变化时逐步适应，枪声不会抬高底噪）
                if (eff < _floor) _floor = eff;
                else _floor = Math.Min(0.35f, _floor * 1.003f);
                if (_floor < 0.001f) _floor = 0.001f;

                float sens = Sensitivity is >= 1.5f and <= 8f ? Sensitivity : 3f;
                // 触发：瞬时能量 ≥ 底噪 × 灵敏度倍数 且 绝对水平足够（连发每一发都满足，因底噪不被枪声抬升）
                if (eff < MinAbsRms || eff < _floor * sens) { _prevRms = eff; return; }

                // 响度联动：取"相对底噪强度"与"绝对强度"较高者——消音武器 rms 低但也要给出体感明显的震动
                float intensity = Math.Max(eff / (_floor * sens), eff / 0.012f);
                if (intensity < 1f) intensity = 1f;
                _prevRms = eff;

                // 连发识别：距上次爆音 < 300ms 视为连发第 N 发；否则重新计为单发
                bool burst = (DateTime.UtcNow - _lastShot).TotalSeconds < _burstWindowSec;
                if (burst) _burstCount++;
                else _burstCount = 1;
                // 冷却：单发 90ms，连发 55ms（自动武器节奏下震动几乎连续，但不会每颗子弹单独炸一下）
                float cooldown = burst ? 0.055f : 0.09f;
                if ((DateTime.UtcNow - _lastShot).TotalSeconds >= cooldown)
                {
                    _lastShot = DateTime.UtcNow;
                    ShotDetected?.Invoke(intensity, _burstCount >= 2);
                }
            }
        }

        /// <summary>按输入格式解码并计算 RMS（支持 16-bit PCM / 32-bit float，通用处理）。</summary>
        private float ComputeRms(byte[] buf, int bytes)
        {
            var fmt = _cap?.WaveFormat;
            if (fmt == null) return 0f;
            int channels = fmt.Channels > 0 ? fmt.Channels : 1;
            long sumSq = 0;
            float fSum = 0f;
            if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
            {
                int n = bytes / 4;
                for (int i = 0; i < n; i++)
                {
                    float v = BitConverter.ToSingle(buf, i * 4);
                    fSum += v * v;
                }
                return (float)Math.Sqrt(fSum / Math.Max(1, n));
            }
            if (fmt.BitsPerSample == 16)
            {
                int n = bytes / 2;
                for (int i = 0; i < n; i++)
                {
                    short v = BitConverter.ToInt16(buf, i * 2);
                    sumSq += (long)v * v;
                }
                return (float)Math.Sqrt((double)sumSq / Math.Max(1, n)) / 32768f;
            }
            // 其他位深：按字节近似（低精度回退，不影响触发节奏）
            int nb = bytes / Math.Max(1, channels);
            for (int i = 0; i < nb; i++) sumSq += (buf[i] - 128L) * (buf[i] - 128L);
            return (float)Math.Sqrt((double)sumSq / Math.Max(1, nb)) / 128f;
        }

        /// <summary>一阶低通（截止约 300Hz @48k，比例因子 0.04）后的低频能量 RMS。
        /// 用于低频增强：重型枪/爆炸低频成分突出时，低频能量接近甚至超过宽带 RMS。</summary>
        private float ComputeLow(byte[] buf, int bytes)
        {
            var fmt = _cap?.WaveFormat;
            if (fmt == null) return 0f;
            const float k = 0.04f; // 低通系数 → 约 300Hz 截止
            double y = 0, sumSq = 0; long cnt = 0;
            if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
            {
                int n = bytes / 4;
                for (int i = 0; i < n; i++)
                {
                    float v = BitConverter.ToSingle(buf, i * 4);
                    y += k * (v - y);
                    sumSq += y * y;
                }
                cnt = n;
            }
            else if (fmt.BitsPerSample == 16)
            {
                int n = bytes / 2;
                for (int i = 0; i < n; i++)
                {
                    float v = BitConverter.ToInt16(buf, i * 2) / 32768f;
                    y += k * (v - y);
                    sumSq += y * y;
                }
                cnt = n;
            }
            if (cnt == 0) return 0f;
            return (float)Math.Sqrt(sumSq / cnt);
        }

        public void Dispose() => Stop();
    }
}
