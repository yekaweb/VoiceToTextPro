# Phase 4: Phased Implementation Roadmap — Neural V2V Studio

## Phase 1: Engine Architecture Upgrade & Proof of Concept (Weeks 1 - 2)
- **Task 1.1**: Build `v2v_worker.py` supporting both RVC v2 inference engine and IndexTTS 2 / F5-TTS zero-shot voice cloning pipeline.
- **Task 1.2**: Implement Demucs / UVR5 background noise suppressor stage prior to neural feature extraction.
- **Task 1.3**: Integrate Faster-Whisper word-level timestamp alignment with IndexTTS 2 duration predictors.

---

## Phase 2: C# UI & Voice Profile Manager Integration (Weeks 3 - 4)
- **Task 2.1**: Update `VoiceConverterTab.xaml` UI with a dual mode selector:
  - `Studio Clean Clone (IndexTTS 2 - Zero Noise)`
  - `Direct Neural Conversion (RVC v2 - Fast)`
- **Task 2.2**: Upgrade Speaker Profile Recorder (`RecordProfile_Click`) to extract and validate 5-second clean voice reference embeddings (`.npy` / `.onnx`).
- **Task 2.3**: Add real-time spectrum visualizer showing pre-conversion noise vs post-conversion crystal-clear waveform.

---

## Phase 3: Hardware Optimization & Distribution (Weeks 5+)
- **Task 3.1**: Convert RVC v2 & IndexTTS 2 checkpoints into quantized ONNX format (`.onnx`) supporting DirectML / CPU AVX-512 execution.
- **Task 3.2**: Package pre-trained high-quality Female & Male preset voice models in `publish/workers/v2v_models/`.
- **Task 3.3**: End-to-end regression testing and benchmark latency under 3 seconds per 10-second audio chunk.

---

## Immediate Action Item (Hour 1 Execution Plan)
1. Prepare `v2v_worker.py` script architecture in `publish/workers/` with ONNX runtime loading for RVC v2 and F5-TTS / IndexTTS 2 backbones.
2. Add a clear Engine Selector UI dropdown in `VoiceConverterTab.xaml` allowing users to choose between **IndexTTS 2 (Zero Noise)** and **RVC v2 (Direct)**.
