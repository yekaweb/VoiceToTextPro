using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceToTextPro.Services
{
    public enum AudioSourceType
    {
        Microphone,
        SystemAudio
    }

    public class AudioDeviceInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public AudioSourceType SourceType { get; set; }
        public bool IsDefault { get; set; }

        public override string ToString() => Name;
    }

    public class LiveAudioCaptureService : IDisposable
    {
        private static readonly Lazy<LiveAudioCaptureService> _instance = new(() => new LiveAudioCaptureService());
        public static LiveAudioCaptureService Instance => _instance.Value;

        private IWaveIn? _captureDevice;
        private AudioSourceType _currentSourceType = AudioSourceType.SystemAudio;
        private bool _isCapturing;
        private float _lastPeakVolume;

        // Target PCM Format required by Vosk/Whisper engines: 16000 Hz, 16-bit, Mono
        public static readonly WaveFormat TargetFormat = new WaveFormat(16000, 16, 1);

        public bool IsCapturing => _isCapturing;
        public AudioSourceType CurrentSourceType => _currentSourceType;
        public float LastPeakVolume => _lastPeakVolume;

        /// <summary>
        /// Raised when 16kHz Mono 16-bit PCM audio data is available.
        /// Arguments: (byte[] pcmData, int length)
        /// </summary>
        public event Action<byte[], int>? OnPcmDataAvailable;

        /// <summary>
        /// Raised when audio volume peak changes (value between 0.0 and 1.0) for visualizer.
        /// </summary>
        public event Action<float>? OnPeakVolumeChanged;

        /// <summary>
        /// Raised when capture stops or encounters an error.
        /// </summary>
        public event Action<string?>? OnCaptureStopped;

        private LiveAudioCaptureService() { }

        /// <summary>
        /// Enumerates available input microphones on Windows.
        /// </summary>
        public List<AudioDeviceInfo> GetMicrophoneDevices()
        {
            var list = new List<AudioDeviceInfo>();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

                foreach (var device in devices)
                {
                    list.Add(new AudioDeviceInfo
                    {
                        Id = device.ID,
                        Name = device.FriendlyName,
                        SourceType = AudioSourceType.Microphone,
                        IsDefault = (defaultDevice != null && device.ID == defaultDevice.ID)
                    });
                }
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_LIVE_AUDIO_2e0038", "خطا در دریافت لیست میکروفن‌ها: {0}", "LIVE_AUDIO", ex.Message);
            }
            return list;
        }

        /// <summary>
        /// Enumerates available system output render devices (speakers/headphones).
        /// </summary>
        public List<AudioDeviceInfo> GetSystemOutputDevices()
        {
            var list = new List<AudioDeviceInfo>();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                foreach (var device in devices)
                {
                    list.Add(new AudioDeviceInfo
                    {
                        Id = device.ID,
                        Name = $"🔊 {device.FriendlyName}",
                        SourceType = AudioSourceType.SystemAudio,
                        IsDefault = (defaultDevice != null && device.ID == defaultDevice.ID)
                    });
                }
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_LIVE_AUDIO_0f7a15", "خطا در دریافت لیست خروجی‌های صدای سیستم: {0}", "LIVE_AUDIO", ex.Message);
            }
            return list;
        }

        /// <summary>
        /// Starts live audio capture from either Microphone or System Audio Output Loopback.
        /// </summary>
        public void StartCapture(AudioSourceType sourceType, string? deviceId = null)
        {
            if (_isCapturing)
            {
                StopCapture();
            }

            _currentSourceType = sourceType;
            _lastPeakVolume = 0;

            try
            {
                if (sourceType == AudioSourceType.SystemAudio)
                {
                    MMDevice? targetDevice = null;
                    using var enumerator = new MMDeviceEnumerator();

                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        targetDevice = enumerator.GetDevice(deviceId);
                    }
                    else
                    {
                        targetDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    }

                    if (targetDevice == null)
                    {
                        throw new Exception("هیچ کارت صدای فعالی برای دریافت صدای سیستم یافت نشد.");
                    }

                    var wasapiCapture = new WasapiLoopbackCapture(targetDevice);
                    wasapiCapture.DataAvailable += OnAudioDataAvailableHandler;
                    wasapiCapture.RecordingStopped += OnRecordingStoppedHandler;
                    _captureDevice = wasapiCapture;
                    wasapiCapture.StartRecording();

                    LoggerService.InfoLocalized("Log_LIVE_AUDIO_3811db", "ضبط صدای سیستم شروع شد: {0}", "LIVE_AUDIO", targetDevice.FriendlyName);
                }
                else
                {
                    // Microphone Capture
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        using var enumerator = new MMDeviceEnumerator();
                        var targetDevice = enumerator.GetDevice(deviceId);
                        var wasapiCapture = new WasapiCapture(targetDevice);
                        wasapiCapture.DataAvailable += OnAudioDataAvailableHandler;
                        wasapiCapture.RecordingStopped += OnRecordingStoppedHandler;
                        _captureDevice = wasapiCapture;
                        wasapiCapture.StartRecording();
                        LoggerService.InfoLocalized("Log_LIVE_AUDIO_a1abce", "ضبط صدای میکروفن WASAPI شروع شد: {0}", "LIVE_AUDIO", targetDevice.FriendlyName);
                    }
                    else
                    {
                        var waveIn = new WaveInEvent
                        {
                            WaveFormat = new WaveFormat(16000, 16, 1),
                            BufferMilliseconds = 50
                        };
                        waveIn.DataAvailable += OnAudioDataAvailableHandler;
                        waveIn.RecordingStopped += OnRecordingStoppedHandler;
                        _captureDevice = waveIn;
                        waveIn.StartRecording();
                        LoggerService.InfoLocalized("Log_LIVE_AUDIO_e9a959", "ضبط صدای میکروفن استاندارد (16kHz Mono) شروع شد.", "LIVE_AUDIO");
                    }
                }

                _isCapturing = true;
            }
            catch (Exception ex)
            {
                _isCapturing = false;
                LoggerService.ErrorLocalized("Log_LIVE_AUDIO_f58b21", "خطا در شروع ضبط صدا: {0}", "LIVE_AUDIO", ex.Message);
                OnCaptureStopped?.Invoke($"خطا در شروع ضبط صدا: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Stops active live audio capture.
        /// </summary>
        public void StopCapture()
        {
            if (!_isCapturing && _captureDevice == null) return;

            _isCapturing = false;
            try
            {
                _captureDevice?.StopRecording();
            }
            catch (Exception ex)
            {
                LoggerService.WarnLocalized("Log_LIVE_AUDIO_1629a4", "اشکال جزئی هنگام توقف ضبط صدا: {0}", "LIVE_AUDIO", ex.Message);
            }
            finally
            {
                _captureDevice?.Dispose();
                _captureDevice = null;
                _lastPeakVolume = 0;
                OnPeakVolumeChanged?.Invoke(0);
                LoggerService.InfoLocalized("Log_LIVE_AUDIO_9845f3", "ضبط صدا متوقف شد.", "LIVE_AUDIO");
            }
        }

        private void OnAudioDataAvailableHandler(object? sender, WaveInEventArgs e)
        {
            if (!_isCapturing || e.BytesRecorded <= 0) return;

            var inFormat = _captureDevice?.WaveFormat;
            if (inFormat == null) return;

            // Convert incoming raw buffer (Float 48kHz Stereo or PCM) to 16kHz 16-bit Mono PCM
            byte[] pcm16k = ConvertTo16kHzMonoPCM(e.Buffer, e.BytesRecorded, inFormat, out float peakVolume);

            _lastPeakVolume = peakVolume;
            OnPeakVolumeChanged?.Invoke(peakVolume);

            if (pcm16k.Length > 0)
            {
                OnPcmDataAvailable?.Invoke(pcm16k, pcm16k.Length);
            }
        }

        private void OnRecordingStoppedHandler(object? sender, StoppedEventArgs e)
        {
            _isCapturing = false;
            if (e.Exception != null)
            {
                LoggerService.ErrorLocalized("Log_LIVE_AUDIO_07d330", "ضبط صدا به علت خطا متوقف شد: {0}", "LIVE_AUDIO", e.Exception.Message);
                OnCaptureStopped?.Invoke(e.Exception.Message);
            }
            else
            {
                OnCaptureStopped?.Invoke(null);
            }
        }

        /// <summary>
        /// Converts raw audio buffer into 16kHz Mono 16-bit PCM and computes RMS peak volume.
        /// Supports IEEE 32-bit Float, 16-bit PCM, multi-channel (Stereo/Surround) and multi-sample rate.
        /// </summary>
        public static byte[] ConvertTo16kHzMonoPCM(byte[] inputBuffer, int length, WaveFormat inFormat, out float peakVolume)
        {
            peakVolume = 0f;
            if (length <= 0) return Array.Empty<byte>();

            int channels = inFormat.Channels;
            int sampleRate = inFormat.SampleRate;
            bool isFloat = inFormat.Encoding == WaveFormatEncoding.IeeeFloat || inFormat.BitsPerSample == 32;

            List<short> monoSamples = new List<short>();
            float maxAbs = 0f;

            if (isFloat)
            {
                int bytesPerFrame = channels * 4;
                int frameCount = length / bytesPerFrame;
                int step = Math.Max(1, sampleRate / 16000); // e.g. 48000 / 16000 = 3

                for (int i = 0; i < frameCount; i += step)
                {
                    int offset = i * bytesPerFrame;
                    float sum = 0f;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int sampleOffset = offset + (ch * 4);
                        if (sampleOffset + 4 <= length)
                        {
                            float sample = BitConverter.ToSingle(inputBuffer, sampleOffset);
                            sum += sample;
                        }
                    }
                    float monoFloat = sum / channels;
                    float abs = Math.Abs(monoFloat);
                    if (abs > maxAbs) maxAbs = abs;

                    short pcm16 = (short)Math.Clamp(monoFloat * 32767.0f, -32768f, 32767f);
                    monoSamples.Add(pcm16);
                }
            }
            else
            {
                // 16-bit PCM
                int bytesPerFrame = channels * 2;
                int frameCount = length / bytesPerFrame;
                int step = Math.Max(1, sampleRate / 16000);

                for (int i = 0; i < frameCount; i += step)
                {
                    int offset = i * bytesPerFrame;
                    int sum = 0;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int sampleOffset = offset + (ch * 2);
                        if (sampleOffset + 2 <= length)
                        {
                            short sample = BitConverter.ToInt16(inputBuffer, sampleOffset);
                            sum += sample;
                        }
                    }
                    short monoSample = (short)(sum / channels);
                    float abs = Math.Abs(monoSample / 32768.0f);
                    if (abs > maxAbs) maxAbs = abs;

                    monoSamples.Add(monoSample);
                }
            }

            peakVolume = Math.Clamp(maxAbs, 0f, 1f);

            // Convert List<short> to byte[] (16-bit PCM Little Endian)
            byte[] outputBytes = new byte[monoSamples.Count * 2];
            for (int i = 0; i < monoSamples.Count; i++)
            {
                short sample = monoSamples[i];
                outputBytes[i * 2] = (byte)(sample & 0xFF);
                outputBytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            return outputBytes;
        }

        public void Dispose()
        {
            StopCapture();
        }
    }
}
