# 🎧 Phoenix Audio Stem Splitter Engine — Master Project Roadmap

> **Status Legend**:  
> ⬜ Not Started | 🟨 In Progress | 🟩 Completed | ⏸ Waiting | ❌ Blocked

---

## 📌 Executive Summary & Architecture Overview

The **Phoenix Audio Stem Splitter** is a high-performance, offline AI module integrated into **VoiceToText Pro**. It leverages Meta's **Demucs v4** and **MDX-Net** ONNX neural architectures to split any audio file into isolated vocal (acapella) and instrumental (karaoke) tracks.

---

## 🗺️ Master Phase Breakdown

```
Phase 1: Python Stem Separator Engine & Worker Infrastructure
Phase 2: macOS Sonoma Dark Glass WPF User Interface
Phase 3: Hardware Acceleration & STT Pipeline Integration
Phase 4: Optimization, Security Audit & Build Package
```

---

## 🚀 Phase 1: Python Stem Separator Engine & Worker Infrastructure

- **Purpose**: Build the core offline AI processing engine in Python and connect it to C# via `PythonBridge`.
- **Scope**: Python ONNX model loading, audio slicing, stem extraction, FFmpeg encoding, and JSON stdout IPC.
- **Expected Result**: Stable background worker that takes a file path and outputs `_vocals.wav` and `_instrumental.wav`.
- **Dependencies**: Python 3.10+, ONNX Runtime, SoundFile/FFmpeg.
- **Estimated Complexity**: Medium (4 days)
- **Definition of Done**: Worker processes a 3-minute MP3 file in under 15 seconds with zero memory leaks.

### 📦 Module 1.1: Python Worker Engine (`stem_splitter_worker.py`)
- **Current Status**: ⬜ Not Started
- **Purpose**: Execute Demucs v4 / MDX-Net ONNX model inference.

#### Tasks:
- [ ] ⬜ **Task 1.1.1: Python Environment & Dependencies**
  - *Subtasks*: Add `onnxruntime`, `soundfile`, `numpy`, `scipy` to `workers/requirements.txt`.
- [ ] ⬜ **Task 1.1.2: Core Stem Separation Algorithm**
  - *Subtasks*: Implement spectrogram STFT/iSTFT and ONNX model execution in `stem_splitter_worker.py`.
- [ ] ⬜ **Task 1.1.3: Real-Time Progress IPC**
  - *Subtasks*: Emit `PROG:0.45|Extracting vocals...` lines to stdout for C# progress bar binding.

### 📦 Module 1.2: C# IPC & Event Integration (`PythonBridge.cs`)
- **Current Status**: ⬜ Not Started
- **Purpose**: Connect WPF frontend commands to the stem splitter worker process.

#### Tasks:
- [ ] ⬜ **Task 1.2.1: Extend `PythonBridge.cs`**
  - *Subtasks*: Add `RunStemSplitterAsync(string inputPath, string outputDir, string modelName)` method.
- [ ] ⬜ **Task 1.2.2: Event Subscriptions**
  - *Subtasks*: Register `OnStemVocalPath` and `OnStemInstrumentalPath` event callbacks in AppEventBus.

---

## 🎨 Phase 2: macOS Sonoma Dark Glass WPF User Interface

- **Purpose**: Create a modern, high-density WPF interface tab for stem separation.
- **Scope**: XAML tab design, drag-and-drop dropzone, dual-track player controls, and progress indicators.
- **Expected Result**: Sleek `StemSplitterTab.xaml` matching the macOS Sonoma glassmorphic visual system.
- **Dependencies**: WPF Material/GlassmorphismResources.xaml, Phase 1 backend.
- **Estimated Complexity**: Medium (3 days)
- **Definition of Done**: User can drag a song onto the tab, watch progress, and play/solo/mute stems.

### 📦 Module 2.1: `StemSplitterTab.xaml` Interface Layout
- **Current Status**: ⬜ Not Started

#### Tasks:
- [ ] ⬜ **Task 2.1.1: XAML Structure & Drag-Drop Zone**
  - *Subtasks*: Design glassmorphic card with dashed dropzone icon and file selector button.
- [ ] ⬜ **Task 2.1.2: Dual Waveform Visualizer Component**
  - *Subtasks*: Implement interactive dual-track waveform preview (Vocals vs Instrumental).
- [ ] ⬜ **Task 2.1.3: Mute / Solo / Gain Controls**
  - *Subtasks*: Add volume sliders and Solo/Mute toggle buttons for each stem.

---

## ⚡ Phase 3: Hardware Acceleration & STT Pipeline Integration

- **Purpose**: Maximize GPU acceleration and bridge extracted vocal stems to Subtitle Studio.
- **Scope**: DirectML / CUDA ONNX Execution Provider selection and tab-to-tab navigation.
- **Expected Result**: Sub-10 second processing on GPUs and one-click transfer of vocal stems to Whisper STT.
- **Dependencies**: Phase 1 & Phase 2.
- **Estimated Complexity**: Medium (3 days)
- **Definition of Done**: Extracted vocals are sent directly to Subtitle Studio with 1 click.

### 📦 Module 3.1: GPU & DirectML Acceleration
- **Current Status**: ⬜ Not Started

#### Tasks:
- [ ] ⬜ **Task 3.1.1: DirectML / CUDA Detection**
  - *Subtasks*: Auto-detect GPU capabilities and select `DmlExecutionProvider` in ONNX Runtime.
- [ ] ⬜ **Task 3.1.2: One-Click STT Pipeline Bridge**
  - *Subtasks*: Add "🎙️ Send Vocals to Subtitle Studio" button to auto-load extracted acapella into TranscribeTab.

---

## 🛡️ Phase 4: Optimization, Security Audit & Build Package

- **Purpose**: Finalize memory management, security audit, localization, and Inno Setup packaging.
- **Scope**: RAM buffer cleanup, Inno Setup script update, and 8-language localization keys.
- **Expected Result**: Fully packaged installer containing the Stem Splitter module.
- **Dependencies**: Phases 1-3 completed.
- **Estimated Complexity**: Low (2 days)
- **Definition of Done**: Installer builds cleanly and all 8 languages render correct localized strings.

### 📦 Module 4.1: Localization & Installer Update
- **Current Status**: ⬜ Not Started

#### Tasks:
- [ ] ⬜ **Task 4.1.1: Localization Resource Strings**
  - *Subtasks*: Add `StemSplitter_Title`, `StemSplitter_Vocals`, `StemSplitter_Instrumental` to 8 language dictionary XAML files.
- [ ] ⬜ **Task 4.1.2: Inno Setup & Build Pipeline**
  - *Subtasks*: Update `VoiceToTextPro_Setup.iss` and release package.
