"""
core/audio_processor.py
Shared audio loading and smart chunking logic.
Reused by all workers (transcriber, subtitle builder, etc.)
"""
import os
import tempfile
import time
from pathlib import Path
from pydub import AudioSegment
from pydub.silence import detect_nonsilent


def format_timestamp(ms: int) -> str:
    """Convert milliseconds to HH:MM:SS or MM:SS string."""
    s = ms // 1000
    h, m, sec = s // 3600, (s % 3600) // 60, s % 60
    return f"{h:02d}:{m:02d}:{sec:02d}" if h > 0 else f"{m:02d}:{sec:02d}"


def format_srt_timestamp(ms: int) -> str:
    """Convert ms to SRT format: HH:MM:SS,mmm"""
    h = ms // 3_600_000
    m = (ms % 3_600_000) // 60_000
    s = (ms % 60_000) // 1000
    millis = ms % 1000
    return f"{h:02d}:{m:02d}:{s:02d},{millis:03d}"


def load_audio(fp: str) -> AudioSegment:
    """Load any supported audio/video format via pydub/ffmpeg."""
    if os.path.exists(fp):
        size_mb = os.path.getsize(fp) / (1024 * 1024)
        if size_mb > 500:
            print(f"PROG:0|هشدار: حجم فایل ({size_mb:.1f}MB) بسیار زیاد است. بارگذاری حافظه ممکن است زمان‌بر باشد...", flush=True)

    fmt_map = {
        ".mp3": "mp3", ".wav": "wav", ".ogg": "ogg", ".m4a": "m4a",
        ".flac": "flac", ".wma": "wma", ".aac": "aac", ".opus": "opus",
        ".webm": "webm", ".mp4": "mp4", ".mkv": "mkv", ".avi": "avi",
        ".mov": "mov", ".wmv": "wmv", ".flv": "flv", ".3gp": "3gp",
    }
    suffix = Path(fp).suffix.lower()
    fmt = fmt_map.get(suffix)
    return AudioSegment.from_file(fp, format=fmt) if fmt else AudioSegment.from_file(fp)


def smart_chunk_audio(audio: AudioSegment, target_ms: int = 30_000, max_ms: int = 45_000):
    """
    Split audio at natural silence points.
    Returns list of (start_ms, end_ms, AudioSegment) tuples.
    """
    silence_thresh = max(-45, audio.dBFS - 14)
    nonsilent = detect_nonsilent(audio, min_silence_len=250, silence_thresh=silence_thresh)

    chunks = []
    if not nonsilent:
        # No silence detected — fixed-size fallback
        for i in range(0, len(audio), target_ms):
            end = min(len(audio), i + max_ms)
            chunks.append((i, end, audio[i:end]))
        return chunks

    cur_start = nonsilent[0][0]
    cur_end = nonsilent[0][1]

    for s, e in nonsilent[1:]:
        if (e - cur_start) <= max_ms:
            cur_end = e
        else:
            pad_s = max(0, cur_start - 800)
            pad_e = min(len(audio), cur_end + 800)
            chunks.append((pad_s, pad_e, audio[pad_s:pad_e]))
            cur_start, cur_end = s, e

    if cur_end > cur_start:
        pad_s = max(0, cur_start - 800)
        pad_e = min(len(audio), cur_end + 800)
        chunks.append((pad_s, pad_e, audio[pad_s:pad_e]))

    return chunks


def export_chunk_to_wav(chunk: AudioSegment) -> str:
    """Export a chunk to a temp WAV file. Caller must delete it."""
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
        path = tmp.name
    chunk.export(path, format="wav")
    return path


def normalize_audio(audio: AudioSegment) -> AudioSegment:
    """Normalize to mono 16kHz for STT engines."""
    return audio.set_channels(1).set_frame_rate(16000)


def extract_pitch_clusters(audio: AudioSegment, window_ms: int = 500):
    """
    Extract acoustic energy and dominant frequency clusters for speaker diarization pre-filtering.
    Returns a list of (start_ms, end_ms, energy_db, dominant_freq_hz) features.
    """
    import numpy as np

    mono = normalize_audio(audio)
    samples = np.array(mono.get_array_of_samples(), dtype=np.float32)
    sample_rate = mono.frame_rate
    window_samples = int(sample_rate * (window_ms / 1000.0))

    features = []
    total_len = len(samples)

    for start_idx in range(0, total_len, window_samples):
        end_idx = min(total_len, start_idx + window_samples)
        if end_idx - start_idx < window_samples // 2:
            break

        segment = samples[start_idx:end_idx]
        energy = float(np.sqrt(np.mean(segment**2))) if len(segment) > 0 else 0.0

        # FFT frequency estimation
        fft_data = np.abs(np.fft.rfft(segment))
        freqs = np.fft.rfftfreq(len(segment), 1.0 / sample_rate)
        dom_freq = float(freqs[np.argmax(fft_data)]) if len(fft_data) > 0 else 0.0

        start_ms = int((start_idx / sample_rate) * 1000)
        end_ms = int((end_idx / sample_rate) * 1000)
        features.append({
            "start_ms": start_ms,
            "end_ms": end_ms,
            "energy": energy,
            "dominant_freq_hz": dom_freq
        })

    return features

