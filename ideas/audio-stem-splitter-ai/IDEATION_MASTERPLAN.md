# 🚀 Ideation Masterplan: Phoenix Audio Stem Splitter Engine

## Executive Overview
This document consolidates the complete evaluation, open-source research, visionary debate, and implementation roadmap for integrating **AI Vocal & Instrumental Stem Separation** into **VoiceToText Pro**.

### Directory Structure
```
ideas/audio-stem-splitter-ai/
├── 01_PROPOSAL_EVALUATION.md
├── 02_OPENSOURCE_RESEARCH.md
├── 03_THINK_TANK_WAR_ROOM.md
├── 04_IMPLEMENTATION_ROADMAP.md
├── IDEATION_MASTERPLAN.md
└── fa/
    ├── 01_ارزیابی_پیشنهاد.md
    ├── 02_تحقیقات_گیتهاب_و_متن_باز.md
    ├── 03_اتاق_فکر_نخبگان.md
    ├── 04_نقشه_راه_پیاده_سازی.md
    └── مسترپلان_ایده.md
```

---

## Technical Summary
- **Primary AI Models**: ONNX Quantized Demucs v4 (`htdemucs`) & MDX-Net.
- **IPC Architecture**: C# `PythonBridge.cs` async subprocess communication.
- **Output Formats**: Uncompressed WAV 24-bit (or MP3/FLAC) saved into user-selected directory or `output/stems/`.
- **Key Differentiator**: Direct integration with Subtitle Studio for clean music lyric transcription.
