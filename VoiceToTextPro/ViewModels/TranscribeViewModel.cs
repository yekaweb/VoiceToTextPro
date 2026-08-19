using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using VoiceToTextPro.Services;

namespace VoiceToTextPro.ViewModels
{
    public class TranscribeViewModel : ViewModelBase
    {
        private PythonBridge? _bridge;
        private string _filePath = "";
        private string _selectedLanguage = "fa-IR";
        private string _resultText = "";
        private double _progressValue;
        private string _progressPercentText = "0%";
        private string _progressStatusText = "آماده به کار";
        private bool _isTranscribing;
        private bool _canSendToSubtitle;
        private string? _currentSrtPath;

        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        public string SelectedLanguage
        {
            get => _selectedLanguage;
            set => SetProperty(ref _selectedLanguage, value);
        }

        public string ResultText
        {
            get => _resultText;
            set => SetProperty(ref _resultText, value);
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public string ProgressPercentText
        {
            get => _progressPercentText;
            set => SetProperty(ref _progressPercentText, value);
        }

        public string ProgressStatusText
        {
            get => _progressStatusText;
            set => SetProperty(ref _progressStatusText, value);
        }

        public bool IsTranscribing
        {
            get => _isTranscribing;
            set
            {
                if (SetProperty(ref _isTranscribing, value))
                {
                    OnPropertyChanged(nameof(CanStart));
                    OnPropertyChanged(nameof(CanStop));
                }
            }
        }

        public bool CanSendToSubtitle
        {
            get => _canSendToSubtitle;
            set => SetProperty(ref _canSendToSubtitle, value);
        }

        public bool CanStart => !IsTranscribing;
        public bool CanStop => IsTranscribing;

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand SendToSubtitleCommand { get; }

        public TranscribeViewModel()
        {
            StartCommand = new AsyncRelayCommand(StartTranscribeAsync, () => CanStart);
            StopCommand = new RelayCommand(StopTranscribe, () => CanStop);
            SendToSubtitleCommand = new RelayCommand(SendToSubtitle, () => CanSendToSubtitle);
        }

        public async Task StartTranscribeAsync()
        {
            if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
            {
                LoggerService.WarnLocalized("Log_TRANSCRIBE_14597a", "لطفاً یک فایل صوتی یا تصویری معتبر انتخاب کنید.", "TRANSCRIBE");
                return;
            }

            IsTranscribing = true;
            CanSendToSubtitle = false;
            ResultText = "";
            ProgressValue = 0;
            ProgressPercentText = "0%";
            ProgressStatusText = "در حال شروع موتور پایتون...";

            string outDir = AppSettings.Instance.OutputDirectory;
            Directory.CreateDirectory(outDir);

            _bridge = new PythonBridge();

            _bridge.OnProgress += (percent, msg) =>
            {
                ProgressValue = percent;
                ProgressPercentText = $"{percent:0}%";
                ProgressStatusText = msg;
            };

            _bridge.OnText += (chunkText) =>
            {
                ResultText += chunkText + "\n";
            };

            _bridge.OnPolishedText += (finalText) =>
            {
                ResultText = finalText;
                LoggerService.SuccessLocalized("Log_TRANSCRIBE_2adfad", "پالایش هوشمند متن با موفقیت انجام شد.", "TRANSCRIBE");
            };

            _bridge.OnSrtPath += (srtPath) =>
            {
                _currentSrtPath = srtPath;
                CanSendToSubtitle = true;
                LoggerService.SuccessLocalized("Log_TRANSCRIBE_478d47", "فایل زیرنویس ساخته شد: {0}", "TRANSCRIBE", srtPath);
            };

            _bridge.OnError += (err) =>
            {
                LoggerService.Error(err, "TRANSCRIBE");
            };

            LoggerService.InfoLocalized("Log_TRANSCRIBE_0a42c0", "شروع عملیات رونویسی: {0} ({1})", "TRANSCRIBE", Path.GetFileName(FilePath), SelectedLanguage);

            bool success = await _bridge.RunAsync("transcribe", FilePath, SelectedLanguage, outDir);

            IsTranscribing = false;
            if (success)
            {
                ProgressStatusText = "عملیات رونویسی با موفقیت تکمیل شد.";
                ProgressValue = 100;
                ProgressPercentText = "100%";
                LoggerService.SuccessLocalized("Log_TRANSCRIBE_d63f31", "عملیات رونویسی تمام شد.", "TRANSCRIBE");
            }
            else
            {
                ProgressStatusText = "عملیات متوقف شد یا با خطا مواجه گردید.";
            }
        }

        public void StopTranscribe()
        {
            _bridge?.KillProcess();
            IsTranscribing = false;
            ProgressStatusText = "عملیات توسط کاربر متوقف شد.";
            LoggerService.WarnLocalized("Log_TRANSCRIBE_8e6cc0", "عملیات رونویسی متوقف گردید.", "TRANSCRIBE");
        }

        public void SendToSubtitle()
        {
            if (!string.IsNullOrEmpty(_currentSrtPath) && File.Exists(_currentSrtPath))
            {
                AppEventBus.RaiseSrtReady(_currentSrtPath);
                LoggerService.InfoLocalized("Log_TRANSCRIBE_88c815", "زیرنویس به تب ویرایشگر ارسال شد: {0}", "TRANSCRIBE", _currentSrtPath);
            }
        }
    }
}
