"""
core/transcriber.py
Unified transcription engine.
Supports Google STT (online) and Whisper (offline stub).
"""
import os
import time
import speech_recognition as sr
from abc import ABC, abstractmethod
from core.audio_processor import export_chunk_to_wav, normalize_audio


# ─── Azeri Transliterator ───────────────────────────────────────────────────

WORD_MAP = {
    "salam": "سلام", "sağol": "ساغ اول", "sağolun": "ساغ اولون",
    "mən": "من", "sən": "سن", "o": "او", "biz": "بیز", "siz": "سیز",
    "onlar": "اونلار", "bu": "بو", "nə": "نه", "necə": "نئجه",
    "var": "وار", "yox": "یوخ", "bəli": "بلی", "çox": "چوخ",
    "gəldi": "گلدی", "gəlir": "گلیر", "getdi": "گتدی", "gedir": "گدیر",
    "dedi": "دئدی", "deyir": "دئییر", "oldu": "اولدو", "olur": "اولور",
    "dadaş": "داداش", "baba": "بابا", "ana": "آنا", "inşallah": "انشاءالله",
}

CHAR_MAP = {
    'a': ('آ', 'ا'), 'ə': ('ا', 'ه\u200c'), 'e': ('ئـ', 'ئه\u200c'),
    'b': 'ب', 'c': 'ج', 'ç': 'چ', 'd': 'د', 'f': 'ف', 'g': 'گ',
    'ğ': 'غ', 'h': 'ه', 'x': 'خ', 'j': 'ژ', 'k': 'ک', 'q': 'ق',
    'l': 'ل', 'm': 'م', 'n': 'ن', 'p': 'پ', 'r': 'ر', 's': 'س',
    'ş': 'ش', 't': 'ت', 'v': 'و', 'y': 'ی', 'z': 'ز',
}

DIGRAPHS = {'gh': 'غ', 'kh': 'خ', 'sh': 'ش', 'ch': 'چ'}


def latin_to_perso_arabic_az(text: str) -> str:
    """Convert Azerbaijani Latin script to Perso-Arabic (Tabriz dialect)."""
    if not text:
        return text
    result = []
    for word in text.split():
        clean = word.lower().strip(".,!?:;")
        if clean in WORD_MAP:
            result.append(WORD_MAP[clean])
            continue
        w = word.lower()
        out, i = [], 0
        while i < len(w):
            # Check digraphs first
            two = w[i:i+2]
            if two in DIGRAPHS:
                out.append(DIGRAPHS[two])
                i += 2
                continue
            ch = w[i]
            mapped = CHAR_MAP.get(ch)
            if mapped is None:
                out.append(ch)
            elif isinstance(mapped, tuple):
                out.append(mapped[0] if i == 0 else mapped[1])
            else:
                out.append(mapped)
            i += 1
        result.append("".join(out))
    return " ".join(result)


# ─── Base Transcriber ────────────────────────────────────────────────────────

class BaseTranscriber(ABC):
    @abstractmethod
    def transcribe_chunk(self, chunk, lang: str) -> str:
        """Transcribe an AudioSegment chunk. Returns text or empty string."""
        ...

    @abstractmethod
    def supports_offline(self) -> bool:
        ...


# ─── Google STT ──────────────────────────────────────────────────────────────

class TranscriberGoogle(BaseTranscriber):
    def __init__(self):
        self.rec = sr.Recognizer()
        self.rec.energy_threshold = 250
        self.rec.dynamic_energy_threshold = True
        self.rec.pause_threshold = 1.0

    def supports_offline(self) -> bool:
        return False

    def transcribe_chunk(self, chunk, lang: str) -> str:
        tmp = export_chunk_to_wav(chunk)
        try:
            with sr.AudioFile(tmp) as src:
                data = self.rec.record(src)
            for attempt in range(3):
                try:
                    return self.rec.recognize_google(data, language=lang)
                except sr.UnknownValueError:
                    return ""
                except sr.RequestError:
                    if attempt < 2:
                        time.sleep(2 ** attempt)  # exponential backoff
                    else:
                        return "[CONNECTION_ERROR]"
        finally:
            try:
                os.unlink(tmp)
            except Exception:
                pass
        return ""


# ─── Whisper STT (stub – ready for Phase 7) ──────────────────────────────────

class TranscriberWhisper(BaseTranscriber):
    def supports_offline(self) -> bool:
        return True

    def transcribe_chunk(self, chunk, lang: str) -> str:
        raise NotImplementedError(
            "Whisper integration is planned for Phase 7. "
            "Install openai-whisper and implement this class."
        )


# ─── Factory ─────────────────────────────────────────────────────────────────

def get_transcriber(engine: str = "google") -> BaseTranscriber:
    if engine == "whisper":
        return TranscriberWhisper()
    return TranscriberGoogle()
