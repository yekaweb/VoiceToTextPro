"""
core/subtitle_builder.py
Build and parse subtitle files: SRT, VTT, ASS.
"""
from dataclasses import dataclass
from core.audio_processor import format_srt_timestamp


@dataclass
class SubtitleEntry:
    index: int
    start_ms: int
    end_ms: int
    text: str

    @property
    def duration_ms(self) -> int:
        return self.end_ms - self.start_ms

    @property
    def start_srt(self) -> str:
        return format_srt_timestamp(self.start_ms)

    @property
    def end_srt(self) -> str:
        return format_srt_timestamp(self.end_ms)

    @property
    def start_vtt(self) -> str:
        return self.start_srt.replace(',', '.')

    @property
    def end_vtt(self) -> str:
        return self.end_srt.replace(',', '.')


def to_srt(entries: list[SubtitleEntry]) -> str:
    """Convert entries to SRT format string."""
    blocks = []
    for e in entries:
        blocks.append(f"{e.index}\n{e.start_srt} --> {e.end_srt}\n{e.text}\n")
    return "\n".join(blocks)


def to_vtt(entries: list[SubtitleEntry]) -> str:
    """Convert entries to WebVTT format string."""
    lines = ["WEBVTT\n"]
    for e in entries:
        lines.append(f"\n{e.index}\n{e.start_vtt} --> {e.end_vtt}\n{e.text}\n")
    return "".join(lines)


def to_ass(entries: list[SubtitleEntry], title: str = "VoiceToText Pro") -> str:
    """Convert entries to ASS (Advanced SubStation Alpha) format."""
    header = f"""[Script Info]
Title: {title}
ScriptType: v4.00+
Collisions: Normal
PlayDepth: 0

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, BackColour, Bold, Italic, Alignment
Style: Default,Arial,20,&H00FFFFFF,&H00000000,-1,0,2

[Events]
Format: Layer, Start, End, Style, Text
"""
    events = []
    for e in entries:
        s = _ms_to_ass(e.start_ms)
        end = _ms_to_ass(e.end_ms)
        events.append(f"Dialogue: 0,{s},{end},Default,{e.text}")
    return header + "\n".join(events)


def _ms_to_ass(ms: int) -> str:
    h = ms // 3_600_000
    m = (ms % 3_600_000) // 60_000
    s = (ms % 60_000) // 1000
    cs = (ms % 1000) // 10
    return f"{h}:{m:02d}:{s:02d}.{cs:02d}"


def parse_srt(content: str) -> list[SubtitleEntry]:
    """Parse an SRT string into SubtitleEntry list."""
    entries = []
    blocks = content.strip().split('\n\n')
    for block in blocks:
        lines = block.strip().splitlines()
        if len(lines) < 3:
            continue
        try:
            idx = int(lines[0].strip())
            times = lines[1].split(' --> ')
            start_ms = _srt_time_to_ms(times[0].strip())
            end_ms = _srt_time_to_ms(times[1].strip())
            text = "\n".join(lines[2:])
            entries.append(SubtitleEntry(idx, start_ms, end_ms, text))
        except Exception:
            continue
    return entries


def _srt_time_to_ms(t: str) -> int:
    t = t.replace(',', '.')
    parts = t.split(':')
    h, m = int(parts[0]), int(parts[1])
    s_ms = parts[2].split('.')
    s = int(s_ms[0])
    ms = int(s_ms[1]) if len(s_ms) > 1 else 0
    return ((h * 3600 + m * 60 + s) * 1000) + ms


def chunks_to_subtitle_entries(chunks_with_text: list[tuple]) -> list[SubtitleEntry]:
    """
    Convert (start_ms, end_ms, text) tuples to SubtitleEntry list.
    Used by the transcription worker to produce subtitle-ready data.
    """
    entries = []
    for i, (start_ms, end_ms, text) in enumerate(chunks_with_text, start=1):
        if text.strip():
            entries.append(SubtitleEntry(i, start_ms, end_ms, text.strip()))
    return entries
