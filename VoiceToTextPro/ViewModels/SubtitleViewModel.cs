using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using VoiceToTextPro.Models;
using VoiceToTextPro.Services;

namespace VoiceToTextPro.ViewModels
{
    public class SubtitleViewModel : ViewModelBase
    {
        private SubtitleEntry? _selectedEntry;
        private string _statusText = "هیچ فایل زیرنویسی بارگذاری نشده است.";
        private string _countText = "۰ ردیف";

        public ObservableCollection<SubtitleEntry> Entries { get; } = new();

        public SubtitleEntry? SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (SetProperty(ref _selectedEntry, value))
                {
                    OnPropertyChanged(nameof(HasSelection));
                }
            }
        }

        public bool HasSelection => SelectedEntry != null;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string CountText
        {
            get => _countText;
            set => SetProperty(ref _countText, value);
        }

        public ICommand AddRowCommand { get; }
        public ICommand DeleteRowCommand { get; }

        public SubtitleViewModel()
        {
            AddRowCommand = new RelayCommand(AddRow);
            DeleteRowCommand = new RelayCommand(DeleteRow, () => HasSelection);
        }

        public void LoadSrtFile(string path)
        {
            if (!File.Exists(path)) return;
            LoadFromSrtText(File.ReadAllText(path, Encoding.UTF8));
            StatusText = $"فایل بارگذاری‌شده: {Path.GetFileName(path)}";
            LoggerService.InfoLocalized("Log_SUBTITLE_EDITOR_2f9ed0", "زیرنویس بارگذاری شد: {0}", "SUBTITLE_EDITOR", Path.GetFileName(path));
        }

        public void LoadFromSrtText(string content)
        {
            Entries.Clear();
            var blocks = content.Trim().Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in blocks)
            {
                var lines = block.Trim().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                if (lines.Length < 3) continue;
                try
                {
                    int idx = int.Parse(lines[0].Trim());
                    var times = lines[1].Split(new[] { " --> " }, StringSplitOptions.None);
                    int startMs = SubtitleEntry.SrtToMs(times[0].Trim());
                    int endMs = SubtitleEntry.SrtToMs(times[1].Trim());
                    string text = string.Join("\n", lines.Skip(2));
                    Entries.Add(new SubtitleEntry { Index = idx, StartMs = startMs, EndMs = endMs, Text = text });
                }
                catch (Exception ex)
                {
                    LoggerService.WarnLocalized("Log_SUBTITLE_PARSER_313937", "خطا در خواندن بلاک زیرنویس: {0}", "SUBTITLE_PARSER", ex.Message);
                }
            }
            UpdateCount();
        }

        public void AddRow()
        {
            int lastEnd = Entries.LastOrDefault()?.EndMs ?? 0;
            int newStart = lastEnd + 500;
            int newEnd = newStart + 2000;
            var entry = new SubtitleEntry { Index = Entries.Count + 1, StartMs = newStart, EndMs = newEnd, Text = "متن زیرنویس جدید..." };
            Entries.Add(entry);
            SelectedEntry = entry;
            UpdateCount();
            LoggerService.InfoLocalized("Log_SUBTITLE_EDITOR_0acc96", "یک ردیف زیرنویس جدید اضافه شد.", "SUBTITLE_EDITOR");
        }

        public void DeleteRow()
        {
            if (SelectedEntry == null) return;
            Entries.Remove(SelectedEntry);
            Reindex();
            UpdateCount();
            LoggerService.InfoLocalized("Log_SUBTITLE_EDITOR_fe3dfe", "ردیف زیرنویس انتخابی حذف شد.", "SUBTITLE_EDITOR");
        }

        public void PerformFindReplace(string findText, string replaceText)
        {
            if (string.IsNullOrEmpty(findText)) return;
            int replacedCount = 0;
            foreach (var entry in Entries)
            {
                if (entry.Text.Contains(findText))
                {
                    entry.Text = entry.Text.Replace(findText, replaceText);
                    replacedCount++;
                }
            }
            LoggerService.SuccessLocalized("Log_SUBTITLE_EDITOR_598a2f", "تعداد {0} مورد جایگزین گردید.", "SUBTITLE_EDITOR", replacedCount);
        }

        private void Reindex()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                Entries[i].Index = i + 1;
            }
        }

        public void UpdateCount()
        {
            CountText = $"{Entries.Count} ردیف";
        }
    }
}
