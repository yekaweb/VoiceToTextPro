using System;

namespace VoiceToTextPro.Services
{
    public static class AppEventBus
    {
        public static event Action<string>? FileReadyForTranscription;
        public static event Action<string>? SrtReadyForSubtitleEditor;
        public static event Action<string>? MediaReadyForSubtitleEditor;

        public static void RaiseFileReady(string path) => FileReadyForTranscription?.Invoke(path);
        public static void RaiseSrtReady(string path) => SrtReadyForSubtitleEditor?.Invoke(path);
        public static void RaiseMediaReadyForSubtitle(string path) => MediaReadyForSubtitleEditor?.Invoke(path);
    }
}
