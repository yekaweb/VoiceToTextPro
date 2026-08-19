# 🎧 AI Vocal & Music Stem Separator (Phoenix Audio Separation Engine)

## 1. Core Value Proposition
The proposed feature integrates an offline, AI-driven audio source separation module into **VoiceToText Pro**. It enables users to split any song or audio file into two distinct, high-fidelity stems:
- 🎙️ **Isolated Vocals**: Pure singer/acapella audio with zero instrumental background noise.
- 🎼 **Isolated Accompaniment / Instrumental**: Pure background music/karaoke track with zero vocal interference.

This empowers content creators, podcasters, video editors, and sound engineers to perform karaoke generation, vocal cleaning for speech recognition (STT), remixing, and dubbing without relying on cloud services or external subscriptions.

---

## 2. Feasibility & Viability Score

| Metric | Score (1-10) | Rationale |
| :--- | :---: | :--- |
| **Technical Complexity** | **7 / 10** | Requires bundling ONNX/PyTorch models (Demucs v4 / MDX-Net) into Python workers without bloating binary sizes. |
| **Market / User Impact** | **9.5 / 10** | Extremely high demand for music creators, karaoke lovers, video editors, and speech transcribers. |
| **Speed to MVP** | **8.5 / 10** | Python worker pattern already exists in `workers/` directory; integration requires adding a new `stem_splitter_worker.py`. |

---

## 3. Deep Technical Architecture

```
[User Interface (WPF Tab)]
        │
        ▼ (IPC JSON Command)
[Python Bridge / stem_splitter_worker.py]
        │
        ├─► Model 1: Demucs v4 (htdemucs / ONNX hybrid)
        ├─► Model 2: MDX-Net (vocal-focused spectrogram model)
        │
        ▼ (FFmpeg Processing & Stem Export)
[Output Folder]
   ├── 🎙️ track_vocals.wav
   └── 🎼 track_instrumental.wav
```

---

## 4. AI Supercharge (10x Upgrades)

1. **Auto-Route Cleaned Vocals to Transcriber**: One-click transfer of extracted acapella directly to Whisper/Vosk for 99.9% accurate speech-to-text without music interference.
2. **Dual-Stem Realtime Visual Preview**: Interactive waveform editor allowing side-by-side playback and solo/mute toggling of vocals vs instrumental before saving.
3. **GPU / ONNX Acceleration & CPU Fallback**: Automatically leverages DirectML / CUDA for sub-10s processing, with lightweight CPU ONNX runtime fallback.
4. **Vocal Pitch & Key Shift Engine**: Built-in harmonic pitch slider to change key of the instrumental stem for karaoke practice.
5. **Noise Floor & Reverb Stripper**: Post-processing filter to remove room echo and vocal bleeding from extracted stems.
