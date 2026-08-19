"""
test_workers.py
Quick smoke-test for all core modules.
Run: python test_workers.py
"""
import sys
import io
import os
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

PASS = "✅"
FAIL = "❌"

def test(name, fn):
    try:
        result = fn()
        print(f"{PASS} {name}: {result}")
    except Exception as e:
        print(f"{FAIL} {name}: {e}")


# ── core/audio_processor ────────────────────────────────────────────────────
from core.audio_processor import format_timestamp, format_srt_timestamp

test("format_timestamp(0)",         lambda: format_timestamp(0) == "00:00")
test("format_timestamp(65000)",     lambda: format_timestamp(65000) == "01:05")
test("format_timestamp(3661000)",   lambda: format_timestamp(3661000) == "01:01:01")
test("format_srt_timestamp(1500)",  lambda: format_srt_timestamp(1500) == "00:00:01,500")


# ── core/text_polisher ───────────────────────────────────────────────────────
from core.text_polisher import polish_text, apply_vocab_corrections

test("polish_text empty",           lambda: polish_text([]) == "")
test("polish_text single",          lambda: polish_text(["سلام دنیا"]) == "سلام دنیا")
test("polish_text dedup",           lambda: polish_text(["سلام خوبی", "خوبی ممنون"]) == "سلام خوبی ممنون")
test("polish_text no overlap",      lambda: polish_text(["سلام", "ممنون"]) == "سلام ممنون")
test("apply_vocab_corrections",     lambda: apply_vocab_corrections("test word", {"word": "WORD"}) == "test WORD")


# ── core/subtitle_builder ────────────────────────────────────────────────────
from core.subtitle_builder import SubtitleEntry, to_srt, to_vtt, parse_srt, chunks_to_subtitle_entries

e1 = SubtitleEntry(1, 0, 3000, "سلام")
e2 = SubtitleEntry(2, 3500, 7000, "دنیا")

test("SubtitleEntry.start_srt",     lambda: e1.start_srt == "00:00:00,000")
test("SubtitleEntry.end_srt",       lambda: e1.end_srt == "00:00:03,000")
test("SubtitleEntry.duration_ms",   lambda: e1.duration_ms == 3000)

srt = to_srt([e1, e2])
test("to_srt contains index",       lambda: "1\n" in srt)
test("to_srt contains text",        lambda: "سلام" in srt)

vtt = to_vtt([e1, e2])
test("to_vtt starts WEBVTT",        lambda: vtt.startswith("WEBVTT"))
test("to_vtt uses dot not comma",   lambda: "00:00:00.000" in vtt)

parsed = parse_srt(srt)
test("parse_srt count",             lambda: len(parsed) == 2)
test("parse_srt text",              lambda: parsed[0].text == "سلام")

chunks = [(0, 3000, "سلام"), (3500, 7000, "دنیا"), (8000, 8500, "")]
entries = chunks_to_subtitle_entries(chunks)
test("chunks_to_subtitle ignores empty", lambda: len(entries) == 2)


# ── core/transcriber ─────────────────────────────────────────────────────────
from core.transcriber import latin_to_perso_arabic_az

test("az transliterator: salam",    lambda: latin_to_perso_arabic_az("salam") == "سلام")
test("az transliterator: gh→غ",     lambda: "غ" in latin_to_perso_arabic_az("sağ"))


# ── plugins/plugin_manager ───────────────────────────────────────────────────
from plugins.plugin_manager import get_manager

manager = get_manager()
manager.discover()
plugins = manager.list_all()
test("plugins discovered",          lambda: len(plugins) >= 2)
test("youtube plugin found",        lambda: any(p["name"] == "YouTube" for p in plugins))
test("instagram plugin found",      lambda: any(p["name"] == "Instagram" for p in plugins))

yt = manager.find("https://www.youtube.com/watch?v=dQw4w9WgXcQ")
test("youtube can_handle",          lambda: yt is not None and yt.name == "YouTube")

ig = manager.find("https://www.instagram.com/p/abc123/")
test("instagram can_handle",        lambda: ig is not None and ig.name == "Instagram")

unknown = manager.find("https://example.com/video")
test("unknown url returns None",    lambda: unknown is None)

print("\nAll tests complete.")
