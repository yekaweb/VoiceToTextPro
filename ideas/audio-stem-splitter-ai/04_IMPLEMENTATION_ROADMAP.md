# 🗺️ Implementation Roadmap: AI Vocal & Stem Separator

## Phase 1: MVP Core Worker & IPC (Sprint 1)
- [ ] Create `workers/stem_splitter_worker.py` integrating ONNX Demucs v4 / MDX-Net.
- [ ] Add `STEM_SPLIT` command handler to `PythonBridge.cs`.
- [ ] Implement dual-file WAV export (`_vocals.wav` and `_instrumental.wav`).

## Phase 2: WPF User Interface (Sprint 2)
- [ ] Create `StemSplitterTab.xaml` with macOS Sonoma glassmorphic visual style.
- [ ] Add Drag-and-Drop file dropzone with progress bar and ETA indicator.
- [ ] Implement dual Waveform audio player controls (Mute, Solo, Volume).

## Phase 3: AI Ecosystem & Performance Boost (Sprint 3)
- [ ] Add DirectML ONNX GPU acceleration support.
- [ ] Add "Send Vocals to Transcribe" one-click action.
- [ ] Add Batch Processing queue for multiple music tracks.

---

## ⚡ Immediate First Step
Create `workers/stem_splitter_worker.py` and test ONNX model inference using PythonBridge!
