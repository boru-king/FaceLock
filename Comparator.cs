// Comparator.cs —— 人脸比对核心逻辑
// 低内存设计：人脸模型（detector/landmarker/recognizer）每轮比对开始时加载、结束后立即释放；
// 底库照片解码后常驻（图像很小），摄像头按需开关。识别精度与常驻模型方案完全一致。
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using SkiaSharp;
using System.Diagnostics;
using System.Text;
using ViewFaceCore.Core;
using ViewFaceCore.Models;

internal sealed class Comparator
{
    private const int FramesPerCycle = 3;     // 每轮比对帧数（任一帧匹配即通过）
    private const int WarmupFrames = 2;       // 摄像头开启后的预热帧数（丢弃）

    private static readonly string BaseDir = AppContext.BaseDirectory;
    private static readonly string LogPath = Path.Combine(BaseDir, "facelock.log");

    private readonly List<(string Name, FaceImage Image)> _refImages = new();   // 底库图像（常驻，体积小）

    /// <summary>写日志：同时输出到控制台（若有）与 face.log 文件。</summary>
    internal static void Log(string msg)
    {
        string line = $"{DateTime.Now:HH:mm:ss} {msg}";
        try
        {
            Console.WriteLine(line);   // 控制台已关闭时 Out 绑定到 Null，安全无副作用
        }
        catch { }
        try
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { /* 日志写入失败不影响主流程 */ }
    }

    /// <summary>加载底库照片（不加载模型——模型按轮次即时加载/释放，降低常驻内存）。</summary>
    internal void Init()
    {
        Log("正在加载底库照片...");
        // 底库照片：targets/ 文件夹（多张，任一匹配即通过）；无该文件夹时退回单张 target.jpg
        // 路径锚定在 exe 所在目录，不受启动位置影响
        string targetsDir = Path.Combine(BaseDir, "targets");
        string[] paths = Directory.Exists(targetsDir)
            ? Directory.GetFiles(targetsDir)
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : new[] { Path.Combine(BaseDir, "target.jpg") };

        foreach (string path in paths)
        {
            try
            {
                using SKBitmap bmp = SKBitmap.Decode(path) ?? throw new InvalidOperationException("图片无法解码");
                _refImages.Add((Path.GetFileName(path), bmp.ToFaceImage()));
                Log($"底库已加载：{Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                Log($"警告：{Path.GetFileName(path)} 加载失败（{ex.Message}），已跳过。");
            }
        }
        if (_refImages.Count == 0)
        {
            throw new InvalidOperationException("没有可用的底库照片（targets/ 或 target.jpg 缺失或均无法解码）。");
        }
        Log($"底库就绪（共 {_refImages.Count} 张），每 {Settings.Current.IntervalText}比对一次。");
        Log("低内存模式：人脸模型每轮比对时加载、结束后释放。");
    }

    /// <summary>
    /// 执行一轮比对：加载模型 → 提取底库特征 → 开摄像头抓 3 帧判定 → 释放摄像头与模型。
    /// 返回汇总裁决文本（供托盘气泡提示）。
    /// </summary>
    internal string RunCycle()
    {
        bool showWindow = Settings.Current.ShowConsoleWindow;
        if (showWindow)
        {
            ConsoleWindow.Show();
        }
        string verdict;
        try
        {
            Log($"===== 开始比对（{FramesPerCycle} 帧）=====");
            var sw = Stopwatch.StartNew();
            using var detector = new FaceDetector(null);
            using var landmarker = new FaceLandmarker(null);
            using var recognizer = new FaceRecognizer(null);
            Log($"人脸模型加载完成（{sw.ElapsedMilliseconds} ms）");

            // 底库特征提取（每轮一次；图像已常驻，提取开销很小）
            var refFeatures = new List<float[]>();
            foreach (var (name, img) in _refImages)
            {
                try
                {
                    FaceInfo[] ri = detector.Detect(img);
                    if (ri.Length == 0)
                    {
                        Log($"警告：{name} 未检测到人脸，本轮跳过该底库。");
                        continue;
                    }
                    refFeatures.Add(recognizer.Extract(img, landmarker.Mark(img, ri[0])));
                }
                catch (Exception ex)
                {
                    Log($"警告：{name} 特征提取失败：{ex.Message}");
                }
            }
            if (refFeatures.Count == 0)
            {
                verdict = "底库特征提取失败，跳过本轮";
                Log(verdict);
                return verdict;
            }

#if TESTMODE
            // 内存泄漏定位实验：放置 memtest.skip 标记文件时，仅做模型加载/释放，跳过摄像头与帧处理
            if (File.Exists(Path.Combine(BaseDir, "memtest.skip")))
            {
                verdict = "memtest：仅模型加载/释放";
                Log(verdict);
                return verdict;
            }
#endif

            // DirectShow 后端优先：MSMF 后端反复开关会泄漏内存（实测 +16MB/轮），DShow 实测零泄漏
            var results = new List<bool>();
            int faceless = 0;        // 处理成功但未检测到人脸的帧数
            int readFailures = 0;    // 摄像头读帧失败数（设备层故障）
            VideoCapture? capture = null;
            try
            {
                capture = new VideoCapture(0, VideoCapture.API.DShow, Array.Empty<Tuple<CapProp, int>>());
                if (!capture.IsOpened)
                {
                    capture.Dispose();
                    capture = new VideoCapture(0, VideoCapture.API.Any, Array.Empty<Tuple<CapProp, int>>());
                    if (!capture.IsOpened)
                    {
                        verdict = "无法打开摄像头，跳过本轮";
                        Log(verdict);
                        return verdict;
                    }
                    Log("警告：DirectShow 后端不可用，本轮使用备用后端（频繁开关可能累积内存）。");
                }

                using var frame = new Mat();
                // 预热：丢弃开头几帧（改善冷启动画面质量）
                for (int w = 0; w < WarmupFrames; w++)
                {
                    capture.Read(frame);
                }

#if TESTMODE
                // 内存泄漏定位实验（二）：放置 memtest.camera 时，仅开关摄像头并读帧，跳过图像转换
                if (File.Exists(Path.Combine(BaseDir, "memtest.camera")))
                {
                    for (int i = 0; i < FramesPerCycle; i++)
                    {
                        if (i > 0)
                        {
                            Thread.Sleep(1500);
                        }
                        capture.Read(frame);
                    }
                    verdict = "memtest：仅摄像头开关与读帧";
                    Log(verdict);
                    return verdict;
                }
#endif
            for (int i = 0; i < FramesPerCycle; i++)
            {
                if (i > 0)
                {
                    Thread.Sleep(1500);
                }
                if (!capture.Read(frame) || frame.IsEmpty)
                {
                    readFailures++;
                    Log($"第 {i + 1} 帧读取失败，跳过。");
                    continue;
                }
                try
                {
                    using SKBitmap bmp = SKBitmap.Decode(MatToJpeg(frame))!;
                    FaceImage curr = bmp.ToFaceImage();
                    try
                    {
                        FaceInfo[] infos = detector.Detect(curr);
                        if (infos.Length == 0)
                        {
                            faceless++;
                            Log($"第 {i + 1} 帧未检测到人脸。");
                        }
                        else
                        {
                            FaceMarkPoint[] pts = landmarker.Mark(curr, infos[0]);
                            float[] data = recognizer.Extract(curr, pts);
                            bool isSelf = refFeatures.Any(r => recognizer.IsSelf(r, data));
                            results.Add(isSelf);
                            Log($"第 {i + 1} 帧比对：{isSelf}");
                        }
                    }
                    finally
                    {
                        // 关键：FaceImage 内部为 AllocHGlobal 非托管缓冲，struct 无终结器，
                        // 必须显式 Dispose，否则每帧泄漏一整幅图像的内存（约 6MB @1080p）
                        curr.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Log($"第 {i + 1} 帧处理出错：{ex.Message}");
                }
                }
            }
            finally
            {
                capture?.Dispose();   // 无论成败均释放摄像头会话
            }

            // 判定规则（安全优先）：
            //   任一帧匹配本人     → 放行
            //   有人脸但均不匹配   → 陌生人 → 锁屏
            //   全部帧无人脸       → 人不在场 → 锁屏（防"人走了反而不锁"）
            //   全部帧读取失败     → 摄像头故障 → 跳过（设备故障不等于未授权）
            if (results.Contains(true))
            {
                verdict = "判定：本人，不锁屏";
                Log($"判定结果：本人（有效 {results.Count} 帧中匹配），不锁屏。");
            }
            else if (results.Count == 0 && faceless == 0)
            {
                verdict = "摄像头读帧全部失败，跳过判定";
                Log(verdict);
            }
            else if (results.Count == 0)
            {
                verdict = "判定：无人在场，已锁定工作站";
                Log("判定结果：未检测到人脸（疑似无人在场），锁定工作站。");
                Native.LockWorkStation();
            }
            else
            {
                verdict = "判定：非本人，已锁定工作站";
                Log($"判定结果：非本人（{results.Count} 帧有人脸均不匹配），锁定工作站。");
                Native.LockWorkStation();
            }
            Log($"本轮完成，总耗时 {sw.ElapsedMilliseconds} ms；模型即将释放。");
        }
        catch (Exception ex)
        {
            verdict = "比对出错：" + ex.Message;
            Log(verdict);
        }
        finally
        {
            // 摄像头/模型随 using 释放；强制回收终结器队列，避免临时图像对象堆积
            // （Emgu Image 等持有非托管内存的对象依赖终结器，无 GC 压力时会延迟回收）
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            // 若显示了控制台，停留片刻便于查看后关闭
            if (showWindow)
            {
                Thread.Sleep(1500);
                ConsoleWindow.Close();
            }
        }
        return verdict;
    }

    /// <summary>帧 → JPEG 字节（临时 Emgu 图像显式释放，防非托管内存堆积）。</summary>
    private static byte[] MatToJpeg(Mat frame)
    {
        using var img = frame.ToImage<Bgr, byte>();
        return img.ToJpegData(100);
    }
}

/// <summary>按需弹出/关闭的控制台窗口（WinExe 无常驻控制台）。</summary>
internal static class ConsoleWindow
{
    private static bool _open;

    internal static bool IsOpen => _open;

    internal static void Show()
    {
        if (_open)
        {
            return;
        }
        Native.AllocConsole();
        Native.SetConsoleTitle("FaceLock - 比对进行中...");
        DisableCloseButton();                       // 防止点 × 关掉整个托盘程序
        Console.OutputEncoding = Encoding.UTF8;     // 控制台 CP=65001，中文不再乱码
        var sw = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
        Console.SetOut(sw);
        _open = true;
    }

    internal static void Close()
    {
        if (!_open)
        {
            return;
        }
        _open = false;
        Native.FreeConsole();
        Console.SetOut(TextWriter.Null);            // 解绑旧句柄，避免之后写日志抛“句柄无效”
    }

    /// <summary>禁用控制台窗口的关闭按钮（×）。</summary>
    private static void DisableCloseButton()
    {
        IntPtr hwnd = Native.GetConsoleWindow();
        IntPtr menu = Native.GetSystemMenu(hwnd, false);
        if (menu != IntPtr.Zero)
        {
            Native.EnableMenuItem(menu, 0xF060 /* SC_CLOSE */, 0x0001 /* MF_GRAYED */);
        }
    }
}
