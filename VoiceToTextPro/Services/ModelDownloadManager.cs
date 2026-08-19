using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VoiceToTextPro.Models;

namespace VoiceToTextPro.Services
{
    public class ModelDownloadManager
    {
        private static readonly Lazy<ModelDownloadManager> _instance = new(() => new ModelDownloadManager());
        public static ModelDownloadManager Instance => _instance.Value;

        public bool IsDownloading { get; private set; }
        public AiModel? CurrentModel { get; private set; }

        public double ProgressValue { get; private set; }
        public string StatusText { get; private set; } = "آماده دانلود...";
        public bool IsIndeterminate { get; private set; }

        public event Action? OnProgressChanged;
        public event Action<bool, string>? OnDownloadCompleted;

        private CancellationTokenSource? _cts;
        private readonly ManualResetEventSlim _pauseEvent = new(true);
        public bool IsPaused => !_pauseEvent.IsSet;

        private long _lastReportTicks = 0;

        public void PauseDownload()
        {
            if (IsDownloading)
            {
                _pauseEvent.Reset();
                if (CurrentModel != null) CurrentModel.IsPaused = true;
                StatusText = $"⏸️ دانلود {CurrentModel?.Name ?? ""} متوقف شد.";
                NotifyProgressChanged(true);
            }
        }

        public void ResumeDownload()
        {
            if (IsDownloading)
            {
                _pauseEvent.Set();
                if (CurrentModel != null) CurrentModel.IsPaused = false;
                StatusText = $"▶️ ادامه دانلود {CurrentModel?.Name ?? ""}...";
                NotifyProgressChanged(true);
            }
        }

        public void TogglePause()
        {
            if (IsPaused) ResumeDownload();
            else PauseDownload();
        }

        public void CancelDownload()
        {
            if (IsDownloading)
            {
                _cts?.Cancel();
                _pauseEvent.Set(); // Unblock paused threads so cancellation bubbles up
                StatusText = $"⏹️ دانلود {CurrentModel?.Name ?? ""} لغو گردید.";
                NotifyProgressChanged(true);
            }
        }

        private static readonly string[] HF_ENDPOINTS = new[]
        {
            "https://hf-mirror.com",
            "https://huggingface.co"
        };

        private void NotifyProgressChanged(bool force = false)
        {
            long now = Environment.TickCount64;
            if (force || now - _lastReportTicks > 100) // Throttle UI updates to 10 fps
            {
                _lastReportTicks = now;
                if (CurrentModel != null)
                {
                    CurrentModel.DownloadProgress = ProgressValue;
                    CurrentModel.IsPaused = IsPaused;
                }
                OnProgressChanged?.Invoke();
            }
        }

        private string? FindAria2Executable()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localPath = Path.Combine(baseDir, "aria2c.exe");
            if (File.Exists(localPath)) return localPath;

            string toolsPath = Path.Combine(baseDir, "tools", "aria2c.exe");
            if (File.Exists(toolsPath)) return toolsPath;

            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
            if (paths != null)
            {
                foreach (var p in paths)
                {
                    string full = Path.Combine(p.Trim(), "aria2c.exe");
                    if (File.Exists(full)) return full;
                }
            }

            return null;
        }

        /// <summary>
        /// Automatically flattens single subfolder nesting created by zip extractors.
        /// </summary>
        private void FlattenExtractedDirectoryIfNeeded(string extractPath)
        {
            try
            {
                if (!Directory.Exists(extractPath)) return;

                var subFiles = Directory.GetFiles(extractPath);
                var subDirs = Directory.GetDirectories(extractPath);

                // If extractPath has 0 direct files and exactly 1 subfolder, move subfolder contents up
                if (subFiles.Length == 0 && subDirs.Length == 1)
                {
                    string nestedFolder = subDirs[0];
                    string parentDir = Path.GetDirectoryName(extractPath)!;
                    string tempMoveDir = Path.Combine(parentDir, "temp_flatten_" + Guid.NewGuid().ToString("N"));

                    Directory.Move(nestedFolder, tempMoveDir);
                    Directory.Delete(extractPath, true);
                    Directory.Move(tempMoveDir, extractPath);

                    LoggerService.InfoLocalized("Log_MODEL_MANAGER_9b2c57", "پوشه مدل با موفقیت مسطح و بهینه‌سازی گردید: {0}", "MODEL_MANAGER", extractPath);
                }
            }
            catch (Exception ex)
            {
                LoggerService.WarnLocalized("Log_MODEL_MANAGER_9cb799", "خطا در مسطح‌سازی پوشه مدل: {0}", "MODEL_MANAGER", ex.Message);
            }
        }

        /// <summary>
        /// Strictly verifies the integrity of a model folder on disk with exact Whisper and Vosk size thresholds.
        /// </summary>
        public ModelStatus VerifyModelIntegrity(AiModel model, string targetDir)
        {
            string folderPath = Path.Combine(targetDir, model.FolderName);
            if (model.FolderName.StartsWith("piper-"))
            {
                string piperSubPath = Path.Combine(targetDir, "piper", model.FolderName);
                if (Directory.Exists(piperSubPath)) folderPath = piperSubPath;
            }

            string tempZip = Path.Combine(targetDir, model.FolderName + ".zip");

            // Check if temporary download files (.zip or .part files) exist anywhere
            if (File.Exists(tempZip)) return ModelStatus.Corrupted;
            for (int i = 0; i < 32; i++)
            {
                if (File.Exists(tempZip + $".part{i}")) return ModelStatus.Corrupted;
            }

            if (Directory.Exists(targetDir))
            {
                var leftoverParts = Directory.GetFiles(targetDir, $"{model.FolderName}*.part*");
                if (leftoverParts.Length > 0) return ModelStatus.Corrupted;
            }

            if (!Directory.Exists(folderPath))
            {
                return ModelStatus.NotInstalled;
            }

            // Auto-heal double-nested folders on disk
            FlattenExtractedDirectoryIfNeeded(folderPath);

            try
            {
                var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
                if (files.Length == 0) return ModelStatus.Corrupted;

                if (files.Any(f => f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || f.Contains(".part")))
                {
                    return ModelStatus.Corrupted;
                }

                long totalBytes = files.Sum(f => new FileInfo(f).Length);

                // Piper ONNX voice models validation
                if (model.FolderName.StartsWith("piper-") || model.Url.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
                {
                    bool hasOnnx = files.Any(f => f.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));
                    if (!hasOnnx) return ModelStatus.Corrupted;
                    long minPiperBytes = 10L * 1024 * 1024;
                    if (totalBytes >= minPiperBytes) return ModelStatus.Installed;
                    return ModelStatus.Corrupted;
                }

                // Vosk models validation
                if (!model.Url.StartsWith("huggingface:"))
                {
                    bool hasVoskModelFile = files.Any(f => f.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) ||
                                                           f.EndsWith("model.conf", StringComparison.OrdinalIgnoreCase) ||
                                                           f.EndsWith(".raw", StringComparison.OrdinalIgnoreCase) ||
                                                           f.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ||
                                                           Path.GetFileName(f).Equals("am", StringComparison.OrdinalIgnoreCase));
                    if (!hasVoskModelFile) return ModelStatus.Corrupted;

                    // Vosk models min threshold (20MB)
                    long minVoskBytes = 20 * 1024 * 1024;
                    if (totalBytes >= minVoskBytes) return ModelStatus.Installed;
                    return ModelStatus.Corrupted;
                }

                // Whisper models validation
                bool hasWeights = files.Any(f => f.EndsWith("model.bin", StringComparison.OrdinalIgnoreCase) ||
                                                 f.EndsWith("model.safetensors", StringComparison.OrdinalIgnoreCase));
                bool hasConfig = files.Any(f => f.EndsWith("config.json", StringComparison.OrdinalIgnoreCase));

                if (!hasWeights || !hasConfig)
                {
                    return ModelStatus.Corrupted;
                }

                long minBytes = 65L * 1024 * 1024;
                string fn = model.FolderName.ToLowerInvariant();

                if (fn.Contains("tiny")) minBytes = 40L * 1024 * 1024;
                else if (fn.Contains("base")) minBytes = 70L * 1024 * 1024;
                else if (fn.Contains("small")) minBytes = 200L * 1024 * 1024; // Adjusted to accommodate int8 & quantized weights (~240MB)
                else if (fn.Contains("medium")) minBytes = 700L * 1024 * 1024;
                else if (fn.Contains("turbo")) minBytes = 800L * 1024 * 1024;
                else if (fn.Contains("large")) minBytes = 1500L * 1024 * 1024;

                if (totalBytes >= minBytes)
                {
                    return ModelStatus.Installed;
                }

                return ModelStatus.Corrupted;
            }
            catch (Exception ex)
            {
                LoggerService.WarnLocalized("Log_MODEL_MANAGER_600ace", "خطا در بررسی سلامت مدل {0}: {1}", "MODEL_MANAGER", model.Name, ex.Message);
                return ModelStatus.Corrupted;
            }
        }

        public async Task<ModelStatus> DeepVerifyModelAsync(AiModel model, string targetDir)
        {
            string folderPath = Path.Combine(targetDir, model.FolderName);
            string tempZip = Path.Combine(targetDir, model.FolderName + ".zip");

            if (File.Exists(tempZip) || File.Exists(tempZip + ".part0"))
            {
                return ModelStatus.Corrupted;
            }

            if (!Directory.Exists(folderPath))
            {
                return ModelStatus.NotInstalled;
            }

            FlattenExtractedDirectoryIfNeeded(folderPath);

            try
            {
                var localFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
                if (localFiles.Length == 0) return ModelStatus.Corrupted;

                long localTotalBytes = localFiles.Sum(f => new FileInfo(f).Length);
                var client = HttpService.Client;

                if (model.Url.StartsWith("huggingface:"))
                {
                    string repo = model.Url.Replace("huggingface:", "");
                    string json = string.Empty;

                    foreach (var endpoint in HF_ENDPOINTS)
                    {
                        try
                        {
                            string apiUrl = $"{endpoint}/api/models/{repo}";
                            json = await client.GetStringAsync(apiUrl);
                            if (!string.IsNullOrEmpty(json)) break;
                        }
                        catch { }
                    }

                    if (string.IsNullOrEmpty(json)) return VerifyModelIntegrity(model, targetDir);

                    var matches = Regex.Matches(json, @"""rfilename"":\s*""([^""]+)""");
                    int expectedFileCount = 0;
                    foreach (Match match in matches)
                    {
                        string fn = match.Groups[1].Value;
                        if (!fn.StartsWith(".") && !fn.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !fn.EndsWith(".gitattributes", StringComparison.OrdinalIgnoreCase))
                        {
                            expectedFileCount++;
                        }
                    }

                    // Count matching non-hidden files locally
                    int actualLocalCount = localFiles.Count(f => {
                        string name = Path.GetFileName(f);
                        return !name.StartsWith(".") && !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".gitattributes", StringComparison.OrdinalIgnoreCase);
                    });

                    if (actualLocalCount >= expectedFileCount && VerifyModelIntegrity(model, targetDir) == ModelStatus.Installed)
                    {
                        return ModelStatus.Installed;
                    }
                    
                    // Fallback to integrity verify if file counts match valid weight & config files
                    return VerifyModelIntegrity(model, targetDir);
                }
                else
                {
                    using var req = new HttpRequestMessage(HttpMethod.Head, model.Url);
                    using var resp = await client.SendAsync(req);
                    if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentLength.HasValue)
                    {
                        long remoteZipSize = resp.Content.Headers.ContentLength.Value;
                        if (localTotalBytes >= (remoteZipSize * 0.8) && VerifyModelIntegrity(model, targetDir) == ModelStatus.Installed)
                        {
                            return ModelStatus.Installed;
                        }
                        return ModelStatus.Corrupted;
                    }
                    return VerifyModelIntegrity(model, targetDir);
                }
            }
            catch (Exception ex)
            {
                LoggerService.WarnLocalized("Log_MODEL_MANAGER_4d6901", "خطا در بررسی دقیق آنلاین مدل {0}: {1}", "MODEL_MANAGER", model.Name, ex.Message);
                return VerifyModelIntegrity(model, targetDir);
            }
        }

        public bool DeleteModel(AiModel model)
        {
            try
            {
                string targetDir = AppSettings.Load().ModelsDirectory;
                string folderPath = Path.Combine(targetDir, model.FolderName);
                string tempZip = Path.Combine(targetDir, model.FolderName + ".zip");

                if (File.Exists(tempZip)) File.Delete(tempZip);

                for (int i = 0; i < 32; i++)
                {
                    string part = tempZip + $".part{i}";
                    if (File.Exists(part)) File.Delete(part);
                }

                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, true);
                }

                if (model.FolderName.StartsWith("piper-"))
                {
                    string piperSubPath = Path.Combine(targetDir, "piper", model.FolderName);
                    if (Directory.Exists(piperSubPath)) Directory.Delete(piperSubPath, true);
                }

                model.Status = ModelStatus.NotInstalled;
                LoggerService.InfoLocalized("Log_MODEL_MANAGER_cc52df", "مدل {0} با موفقیت از دیسک حذف شد.", "MODEL_MANAGER", model.Name);
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_MODEL_MANAGER_b83b36", "خطا در حذف مدل {0}: {1}", "MODEL_MANAGER", model.Name, ex.Message);
                return false;
            }
        }

        private async Task FastDownloadFileAsync(string primaryUrl, List<string>? fallbackUrls, string destinationPath, string statusPrefix, CancellationToken ct)
        {
            string? aria2Path = FindAria2Executable();
            if (aria2Path != null)
            {
                bool success = await DownloadWithAria2Async(aria2Path, primaryUrl, destinationPath, statusPrefix, ct).ConfigureAwait(false);
                if (success) return;
            }

            var allUrls = new List<string> { primaryUrl };
            if (fallbackUrls != null) allUrls.AddRange(fallbackUrls);

            Exception? lastEx = null;
            foreach (var url in allUrls)
            {
                try
                {
                    await DownloadFileWithResumeEngineAsync(url, destinationPath, statusPrefix, ct).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    LoggerService.WarnLocalized("Log_MODEL_MANAGER_8e9234", "آدرس دانلود {0} ناموفق بود. تلاش با آدرس میرور بعدی...", "MODEL_MANAGER", url);
                }
            }

            if (lastEx != null) throw lastEx;
        }

        /// <summary>
        /// Native aria2c execution wrapper supercharged to 32 connections with 1MB chunk min split size.
        /// </summary>
        private async Task<bool> DownloadWithAria2Async(string aria2Path, string url, string destinationPath, string statusPrefix, CancellationToken ct)
        {
            try
            {
                string dir = Path.GetDirectoryName(destinationPath)!;
                string file = Path.GetFileName(destinationPath);

                var psi = new ProcessStartInfo
                {
                    FileName = aria2Path,
                    Arguments = $"-x 32 -s 32 -k 1M --min-split-size=1M --file-allocation=none --continue=true -d \"{dir}\" -o \"{file}\" \"{url}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();

                StatusText = $"{statusPrefix} (موتور ۳۲ تکه‌ای aria2c Turbo Engine)...";
                NotifyProgressChanged(true);

                var reader = proc.StandardOutput;
                var ariaRegex = new Regex(@"\[#[a-f0-9]+\s+([^\s\(]+)\/([^\s\(]+)\((\d+)%\)\s+(?:CN:\d+\s+)?DL:([^\s\]]+)", RegexOptions.IgnoreCase);
                var simplePctRegex = new Regex(@"\((\d+)%\)", RegexOptions.IgnoreCase);

                while (!proc.HasExited && !ct.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line != null)
                    {
                        var match = ariaRegex.Match(line);
                        if (match.Success)
                        {
                            string downloaded = match.Groups[1].Value;
                            string total = match.Groups[2].Value;
                            if (double.TryParse(match.Groups[3].Value, out double pct)) ProgressValue = pct;
                            string speed = match.Groups[4].Value;

                            StatusText = $"{statusPrefix} (۳۲ تکه aria2c)  |  سرعت: {speed}/s  |  حجم: {downloaded} از {total} ({ProgressValue:F0}%)";
                            NotifyProgressChanged(false);
                        }
                        else
                        {
                            var simpleMatch = simplePctRegex.Match(line);
                            if (simpleMatch.Success && double.TryParse(simpleMatch.Groups[1].Value, out double pct))
                            {
                                ProgressValue = pct;
                                StatusText = $"{statusPrefix} (۳۲ تکه aria2c)  |  پیشرفت: {pct:F0}%";
                                NotifyProgressChanged(false);
                            }
                        }
                    }
                }

                if (ct.IsCancellationRequested)
                {
                    try { proc.Kill(); } catch { }
                    return false;
                }

                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                return proc.ExitCode == 0;
            }
            catch (Exception ex)
            {
                LoggerService.WarnLocalized("Log_MODEL_MANAGER_c7f563", "اجرای aria2c با خطا مواجه شد: {0}. سوئیچ به موتور دانلود C#...", "MODEL_MANAGER", ex.Message);
                return false;
            }
        }

        private async Task DownloadFileWithResumeEngineAsync(string url, string destinationPath, string statusPrefix, CancellationToken ct)
        {
            var client = HttpService.Client;

            long totalBytes = -1L;
            bool supportsRange = false;

            try
            {
                using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
                using var headResp = await client.SendAsync(headReq, ct).ConfigureAwait(false);
                if (headResp.IsSuccessStatusCode)
                {
                    totalBytes = headResp.Content.Headers.ContentLength ?? -1L;
                    supportsRange = headResp.Headers.AcceptRanges.Contains("bytes") || headResp.Headers.Contains("Accept-Ranges");
                }
            }
            catch { }

            if (totalBytes <= 0)
            {
                try
                {
                    using var rangeReq = new HttpRequestMessage(HttpMethod.Get, url);
                    rangeReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                    using var rangeResp = await client.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    if (rangeResp.StatusCode == System.Net.HttpStatusCode.PartialContent)
                    {
                        supportsRange = true;
                        if (rangeResp.Content.Headers.ContentRange?.Length.HasValue == true)
                        {
                            totalBytes = rangeResp.Content.Headers.ContentRange.Length.Value;
                        }
                    }
                }
                catch { }
            }

            const int MIN_PARALLEL_SIZE = 4 * 1024 * 1024;
            const int NUM_SEGMENTS = 32; // 32 Threads like IDM Turbo Max

            if (supportsRange && totalBytes >= MIN_PARALLEL_SIZE)
            {
                await DownloadMultiSegmentWithResumeAsync(url, destinationPath, totalBytes, NUM_SEGMENTS, statusPrefix, ct).ConfigureAwait(false);
            }
            else
            {
                await DownloadSingleStreamWithResumeAsync(url, destinationPath, statusPrefix, ct).ConfigureAwait(false);
            }
        }

        private async Task DownloadMultiSegmentWithResumeAsync(string url, string destinationPath, long totalBytes, int numSegments, string statusPrefix, CancellationToken ct)
        {
            var client = HttpService.Client;
            long segmentSize = totalBytes / numSegments;

            long totalDownloaded = 0;
            var partFiles = new string[numSegments];

            for (int i = 0; i < numSegments; i++)
            {
                partFiles[i] = destinationPath + $".part{i}";
                if (File.Exists(partFiles[i]))
                {
                    totalDownloaded += new FileInfo(partFiles[i]).Length;
                }
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long initialDownloadedBytes = totalDownloaded;
            var tasks = new List<Task>();

            for (int i = 0; i < numSegments; i++)
            {
                int segmentIndex = i;
                long start = segmentIndex * segmentSize;
                long end = (segmentIndex == numSegments - 1) ? (totalBytes - 1) : (start + segmentSize - 1);
                long targetSegmentBytes = (end - start + 1);

                string partPath = partFiles[segmentIndex];

                tasks.Add(Task.Run(async () =>
                {
                    long existingPartBytes = 0;
                    if (File.Exists(partPath))
                    {
                        existingPartBytes = new FileInfo(partPath).Length;
                    }

                    if (existingPartBytes >= targetSegmentBytes)
                    {
                        return;
                    }

                    long rangeStart = start + existingPartBytes;

                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(rangeStart, end);

                    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();

                    using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var fileStream = new FileStream(partPath, FileMode.Append, FileAccess.Write, FileShare.Write, 524288, true);

                    byte[] buffer = new byte[262144];
                    int read;

                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        _pauseEvent.Wait(ct);
                        await fileStream.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                        long currentTotal = Interlocked.Add(ref totalDownloaded, read);

                        ProgressValue = Math.Min(100.0, (double)currentTotal / totalBytes * 100.0);
                        double elapsed = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                        double speedMb = ((currentTotal - initialDownloadedBytes) / 1024.0 / 1024.0) / elapsed;
                        double totalMb = totalBytes / 1024.0 / 1024.0;
                        double readMb = currentTotal / 1024.0 / 1024.0;

                        string resumeMsg = initialDownloadedBytes > 0 ? $" • [ادامه از {initialDownloadedBytes / 1024 / 1024}MB]" : "";
                        StatusText = $"{statusPrefix}{resumeMsg} • سرعت: {speedMb:F1} MB/s • حجم: {readMb:F1} / {totalMb:F1} MB ({ProgressValue:F1}%)";
                        
                        if (CurrentModel != null)
                        {
                            CurrentModel.DownloadSpeedText = $"{speedMb:F1} MB/s";
                            CurrentModel.DownloadProgressText = $"{readMb:F1} / {totalMb:F1} MB ({ProgressValue:F0}%)";
                        }
                        
                        NotifyProgressChanged(false);
                    }
                }, ct));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            StatusText = $"{statusPrefix}  |  در حال سرهم‌بندی فایل‌ها (Merge Parts)...";
            NotifyProgressChanged(true);

            using (var outStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1048576, true))
            {
                for (int i = 0; i < numSegments; i++)
                {
                    if (File.Exists(partFiles[i]))
                    {
                        using (var inStream = new FileStream(partFiles[i], FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, true))
                        {
                            await inStream.CopyToAsync(outStream, 1048576, ct).ConfigureAwait(false);
                        }
                        File.Delete(partFiles[i]);
                    }
                }
            }
        }

        private async Task DownloadSingleStreamWithResumeAsync(string url, string destinationPath, string statusPrefix, CancellationToken ct)
        {
            var client = HttpService.Client;

            long existingLength = 0;
            if (File.Exists(destinationPath))
            {
                existingLength = new FileInfo(destinationPath).Length;
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingLength > 0)
            {
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
            }

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                ProgressValue = 100;
                StatusText = $"{statusPrefix} (قبلاً به صورت کامل دریافت شده است)";
                NotifyProgressChanged(true);
                return;
            }

            bool isPartial = resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (!resp.IsSuccessStatusCode && !isPartial)
            {
                resp.EnsureSuccessStatusCode();
            }

            long contentLen = resp.Content.Headers.ContentLength ?? -1L;
            long totalBytes = isPartial ? (existingLength + contentLen) : (contentLen > 0 ? contentLen : -1L);

            if (existingLength > 0 && !isPartial)
            {
                existingLength = 0;
            }

            long totalRead = existingLength;
            FileMode mode = (isPartial && existingLength > 0) ? FileMode.Append : FileMode.Create;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using (var contentStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            using (var fileStream = new FileStream(destinationPath, mode, FileAccess.Write, FileShare.None, 524288, true))
            {
                byte[] buffer = new byte[524288];
                int read;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    _pauseEvent.Wait(ct);
                    await fileStream.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                    totalRead += read;

                    if (totalBytes > 0)
                    {
                        ProgressValue = (double)totalRead / totalBytes * 100.0;
                        double speedMb = ((totalRead - existingLength) / 1024.0 / 1024.0) / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                        double totalMb = totalBytes / 1024.0 / 1024.0;
                        double readMb = totalRead / 1024.0 / 1024.0;

                        string resumeMsg = existingLength > 0 ? $" • [ادامه از {existingLength / 1024 / 1024}MB]" : "";
                        StatusText = $"{statusPrefix}{resumeMsg} • سرعت: {speedMb:F1} MB/s • حجم: {readMb:F1} / {totalMb:F1} MB ({ProgressValue:F1}%)";
                        
                        if (CurrentModel != null)
                        {
                            CurrentModel.DownloadSpeedText = $"{speedMb:F1} MB/s";
                            CurrentModel.DownloadProgressText = $"{readMb:F1} / {totalMb:F1} MB ({ProgressValue:F0}%)";
                        }

                        NotifyProgressChanged(false);
                    }
                }
            }
        }

        public async Task StartDownloadAsync(AiModel model)
        {
            if (IsDownloading) return;

            IsDownloading = true;
            CurrentModel = model;
            _cts = new CancellationTokenSource();
            _pauseEvent.Set(); // Reset unpaused state

            ProgressValue = 0;
            IsIndeterminate = false;
            StatusText = $"در حال ارتباط با سرورهای پرسرعت CDN برای {model.Name}...";
            model.IsDownloading = true;
            model.IsPaused = false;
            model.DownloadProgress = 0;
            model.DownloadProgressText = "0%";
            model.DownloadSpeedText = "0 MB/s";
            NotifyProgressChanged(true);

            _ = Task.Run(async () =>
            {
                try
                {
                    string targetDir = AppSettings.Load().ModelsDirectory;
                    Directory.CreateDirectory(targetDir);

                    if (model.Url.StartsWith("huggingface:"))
                    {
                        string repo = model.Url.Replace("huggingface:", "");
                        string extractPath = Path.Combine(targetDir, model.FolderName);
                        Directory.CreateDirectory(extractPath);

                        var client = HttpService.Client;

                        StatusText = $"در حال دریافت لیست فایل‌های {model.Name} از میرور پرسرعت...";
                        NotifyProgressChanged(true);

                        string json = string.Empty;
                        string selectedEndpoint = HF_ENDPOINTS[0];

                        foreach (var endpoint in HF_ENDPOINTS)
                        {
                            try
                            {
                                string apiUrl = $"{endpoint}/api/models/{repo}";
                                json = await client.GetStringAsync(apiUrl, _cts.Token).ConfigureAwait(false);
                                if (!string.IsNullOrEmpty(json))
                                {
                                    selectedEndpoint = endpoint;
                                    break;
                                }
                            }
                            catch { }
                        }

                        if (string.IsNullOrEmpty(json)) throw new Exception("امکان ارتباط با سرورهای HuggingFace/Mirror وجود ندارد.");

                        var filesToDownload = new List<string>();
                        var matches = Regex.Matches(json, @"""rfilename"":\s*""([^""]+)""");
                        foreach (Match match in matches)
                        {
                            string file = match.Groups[1].Value;
                            if (file != ".gitattributes" && !file.EndsWith(".md"))
                            {
                                filesToDownload.Add(file);
                            }
                        }

                        if (filesToDownload.Count == 0) throw new Exception("فایلی برای این مدل یافت نشد.");

                        for (int i = 0; i < filesToDownload.Count; i++)
                        {
                            string file = filesToDownload[i];
                            string primaryUrl = $"{selectedEndpoint}/{repo}/resolve/main/{file}";
                            
                            var fallbackUrls = HF_ENDPOINTS.Where(e => e != selectedEndpoint)
                                                           .Select(e => $"{e}/{repo}/resolve/main/{file}")
                                                           .ToList();

                            string filePath = Path.Combine(extractPath, file);
                            string statusPrefix = $"در حال دریافت {file} • بخش {i + 1} از {filesToDownload.Count}";

                            await FastDownloadFileAsync(primaryUrl, fallbackUrls, filePath, statusPrefix, _cts.Token).ConfigureAwait(false);
                        }
                    }
                    else if (model.Url.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
                    {
                        string extractPath = Path.Combine(targetDir, "piper", model.FolderName);
                        Directory.CreateDirectory(extractPath);

                        string onnxFileName = Path.GetFileName(model.Url);
                        string onnxFilePath = Path.Combine(extractPath, onnxFileName);
                        string statusPrefix = $"در حال دریافت مدل گفتاری Piper: {model.Name}";

                        // 1. Download .onnx model file
                        await FastDownloadFileAsync(model.Url, null, onnxFilePath, statusPrefix, _cts.Token).ConfigureAwait(false);

                        // 2. Download accompanying .onnx.json metadata file
                        string jsonUrl = model.Url + ".json";
                        string jsonFilePath = onnxFilePath + ".json";
                        try
                        {
                            await FastDownloadFileAsync(jsonUrl, null, jsonFilePath, "در حال دریافت متادیتای مدل Piper", _cts.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            LoggerService.WarnLocalized("Log_PIPER_JSON_WARN", "متادیتای .json برای مدل piper دریافت نشد: {0}", "MODEL_MANAGER", ex.Message);
                        }
                    }
                    else
                    {
                        string tempZip = Path.Combine(targetDir, model.FolderName + ".zip");
                        string statusPrefix = $"در حال دریافت فایل فشرده {model.Name}";

                        await FastDownloadFileAsync(model.Url, null, tempZip, statusPrefix, _cts.Token).ConfigureAwait(false);

                        StatusText = $"در حال استخراج (Extract) فایل‌های {model.Name}... لطفا صبر کنید.";
                        IsIndeterminate = true;
                        NotifyProgressChanged(true);

                        string extractPath = Path.Combine(targetDir, model.FolderName);
                        if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                        Directory.CreateDirectory(extractPath);

                        ZipFile.ExtractToDirectory(tempZip, extractPath, true);
                        if (File.Exists(tempZip)) File.Delete(tempZip);

                        FlattenExtractedDirectoryIfNeeded(extractPath);
                    }

                    model.Status = VerifyModelIntegrity(model, targetDir);
                    StatusText = $"مدل {model.Name} با موفقیت نصب گردید!";
                    ProgressValue = 100;
                    IsIndeterminate = false;
                    NotifyProgressChanged(true);
                    OnDownloadCompleted?.Invoke(true, StatusText);
                }
                catch (Exception ex)
                {
                    LoggerService.ErrorLocalized("Log_MODEL_MANAGER_3e2e94", "خطا در دانلود مدل {0}: {1}", "MODEL_MANAGER", model.Name, ex.Message);
                    model.Status = ModelStatus.NotInstalled;
                    StatusText = $"خطا در دانلود: {ex.Message}";
                    ProgressValue = 0;
                    IsIndeterminate = false;
                    NotifyProgressChanged(true);
                    OnDownloadCompleted?.Invoke(false, StatusText);
                }
                finally
                {
                    IsDownloading = false;
                    model.IsDownloading = false;
                    CurrentModel = null;
                }
            });

            await Task.CompletedTask;
        }
    }
}
