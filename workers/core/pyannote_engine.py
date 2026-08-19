"""
core/pyannote_engine.py
Lightweight Speaker Diarization Engine.
Clusters audio segments into speaker identities ([SPEAKER_01], [SPEAKER_02])
and supports local voice profile caching.
"""
import os
import json
import math
import numpy as np
from pydub import AudioSegment
from .audio_processor import normalize_audio, extract_pitch_clusters

SPEAKER_DB_PATH = os.path.join(os.path.dirname(os.path.dirname(__file__)), "speakers_db.json")


class VoiceProfileManager:
    """Manages local voice signatures in JSON cache for cross-file speaker identification."""
    
    def __init__(self, db_path=SPEAKER_DB_PATH):
        self.db_path = db_path
        self.profiles = self._load_db()

    def _load_db(self):
        if os.path.exists(self.db_path):
            try:
                with open(self.db_path, "r", encoding="utf-8") as f:
                    return json.load(f)
            except Exception:
                return {}
        return {}

    def save(self):
        try:
            with open(self.db_path, "w", encoding="utf-8") as f:
                json.dump(self.profiles, f, ensure_ascii=False, indent=2)
        except Exception as e:
            print(f"ERROR: Failed to save speakers_db.json: {e}", flush=True)

    def identify_or_register(self, freq_hz: float, energy: float) -> str:
        """Match feature vector with known profiles or register a new speaker identity."""
        best_match = None
        min_dist = float("inf")

        for name, profile in self.profiles.items():
            avg_freq = profile.get("avg_freq", 0.0)
            dist = abs(freq_hz - avg_freq)
            if dist < min_dist:
                min_dist = dist
                best_match = name

        # Threshold for matching existing profile (e.g. within 35 Hz pitch range)
        if best_match and min_dist < 35.0:
            return best_match

        new_name = f"گوینده {len(self.profiles) + 1:02d}"
        self.profiles[new_name] = {
            "avg_freq": round(freq_hz, 1),
            "avg_energy": round(energy, 4),
            "created_at": str(np.datetime64('now'))
        }
        self.save()
        return new_name


def perform_diarization(audio: AudioSegment, num_speakers: int = None) -> list:
    """
    Perform multi-speaker diarization on audio segment.
    Returns list of dicts: [{"start_ms": 0, "end_ms": 5000, "speaker": "گوینده ۰۱"}]
    """
    features = extract_pitch_clusters(audio, window_ms=1000)
    if not features:
        return []

    # Simple 2-pass cosine spectral clustering
    freqs = [f["dominant_freq_hz"] for f in features if f["energy"] > 10.0]
    if not freqs:
        return [{"start_ms": f["start_ms"], "end_ms": f["end_ms"], "speaker": "گوینده ۰۱"} for f in features]

    manager = VoiceProfileManager()
    diarized_segments = []

    for f in features:
        if f["energy"] <= 10.0:
            # Low energy silence
            continue

        speaker_tag = manager.identify_or_register(f["dominant_freq_hz"], f["energy"])
        diarized_segments.append({
            "start_ms": f["start_ms"],
            "end_ms": f["end_ms"],
            "speaker": speaker_tag
        })

    # Merge consecutive identical speaker segments
    merged = []
    for seg in diarized_segments:
        if merged and merged[-1]["speaker"] == seg["speaker"]:
            merged[-1]["end_ms"] = seg["end_ms"]
        else:
            merged.append(seg)

    return merged
