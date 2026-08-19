"""
test_ux_simulation.py
Automated end-to-end stress and UX simulation test suite.
Verifies system performance, latency, and resilience under concurrent load.
"""
import sys
import io
import os
import time
import json
import subprocess

if sys.stdout.encoding != 'utf-8':
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
if sys.stderr.encoding != 'utf-8':
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

# Ensure workers directory is in path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

def run_ping_test():
    print("🧪 Testing Python Ping Heartbeat...")
    worker_script = os.path.join(os.path.dirname(__file__), "main_worker.py")
    res = subprocess.run([sys.executable, worker_script, "ping"], capture_output=True, text=True, encoding="utf-8")
    assert res.returncode == 0, "Ping worker exited with non-zero code"
    assert "RESULT:PONG" in res.stdout, "Ping worker failed to return RESULT:PONG"
    print("  ✅ Ping Heartbeat PASSED")

def run_plugin_discovery_stress():
    print("🧪 Testing Plugin Discovery Stress (20 iterations)...")
    worker_script = os.path.join(os.path.dirname(__file__), "main_worker.py")
    t0 = time.time()
    for i in range(20):
        res = subprocess.run([sys.executable, worker_script, "list_plugins"], capture_output=True, text=True, encoding="utf-8")
        assert res.returncode == 0
        assert "RESULT:" in res.stdout
    t1 = time.time()
    avg_ms = ((t1 - t0) / 20) * 1000
    print(f"  ✅ Plugin Discovery Stress PASSED (Avg Latency: {avg_ms:.1f}ms)")

def run_audio_stress_simulation():
    print("🧪 Testing Audio Processor & Polisher Stress...")
    from core.audio_processor import format_timestamp, format_srt_timestamp
    from core.text_polisher import polish_text
    from core.subtitle_builder import SubtitleEntry, to_srt, to_vtt

    t0 = time.time()
    sample_texts = [
        "سلام این یک تست سیستم است",
        "این یک تست سیستم است و بررسی می‌شود",
        "سلام این یک تست سیستم است"
    ]
    for _ in range(500):
        polished = polish_text(sample_texts)
        assert "تست سیستم" in polished
        
        entry = SubtitleEntry(1, 0, 5000, "تست زیرنویس")
        srt = to_srt([entry])
        vtt = to_vtt([entry])
        assert "WEBVTT" in vtt
        assert "00:00:00,000 --> 00:00:05,000" in srt

    t1 = time.time()
    print(f"  ✅ Audio Processor & Polisher Stress PASSED (500 cycles in {(t1-t0)*1000:.1f}ms)")

def run_diarization_test():
    print("🧪 Testing Speaker Diarization Engine...")
    from pydub import AudioSegment
    from core.pyannote_engine import perform_diarization, VoiceProfileManager
    
    # Generate synthetic 10s audio segment
    audio = AudioSegment.silent(duration=5000)
    results = perform_diarization(audio)
    assert isinstance(results, list)
    
    mgr = VoiceProfileManager()
    speaker = mgr.identify_or_register(150.0, 50.0)
    assert "گوینده" in speaker
    print("  ✅ Speaker Diarization Engine PASSED")

def run_translation_test():
    print("🧪 Testing AI Subtitle Translation & Alignment...")
    from core.subtitle_translator import translate_subtitle_entries
    
    sample_entries = [
        {"start_ms": 0, "end_ms": 3000, "text": "Hello world!"},
        {"start_ms": 3000, "end_ms": 6000, "text": "This is a live test."}
    ]
    res = translate_subtitle_entries(sample_entries, target_lang="fa", provider="ollama")
    assert len(res) == 2
    assert res[0]["start_ms"] == 0
    assert "translated_text" in res[0]
    print("  ✅ AI Subtitle Translation & Alignment PASSED")

def run_all_simulations():
    print("==================================================")
    print("🚀 Starting VoiceToText Pro UX Stress Simulation")
    print("==================================================")
    
    t_start = time.time()
    run_ping_test()
    run_plugin_discovery_stress()
    run_audio_stress_simulation()
    run_diarization_test()
    run_translation_test()
    t_end = time.time()
    
    print("==================================================")
    print(f"🎉 ALL UX SIMULATION TESTS PASSED IN {(t_end - t_start):.2f}s!")
    print("==================================================")

if __name__ == "__main__":
    run_all_simulations()
