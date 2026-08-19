"""
core/text_polisher.py
Post-processing utilities for transcribed text.
"""
import re


def polish_text(chunks: list[str]) -> str:
    """
    Merge a list of transcribed chunk texts into one clean, continuous string.
    Deduplicates overlapping words at chunk boundaries (up to 8 words).
    """
    if not chunks:
        return ""

    result = []
    for text in chunks:
        text = text.strip()
        if not text:
            continue
        if not result:
            result.append(text)
            continue

        prev_words = " ".join(result).split()
        curr_words = text.split()
        max_check = min(8, len(prev_words), len(curr_words))
        overlap = 0

        for k in range(max_check, 0, -1):
            prev_sub = [w.strip('.,!?:;«»"\'\u200c') for w in prev_words[-k:]]
            curr_sub = [w.strip('.,!?:;«»"\'\u200c') for w in curr_words[:k]]
            if prev_sub == curr_sub:
                overlap = k
                break

        if overlap > 0:
            remaining = curr_words[overlap:]
            if remaining:
                result.append(" ".join(remaining))
        else:
            result.append(text)

    full = " ".join(result)
    return " ".join(full.split())


def apply_vocab_corrections(text: str, vocab: dict) -> str:
    """
    Apply user-defined vocabulary corrections.
    vocab = {"wrong": "correct", ...}
    """
    for wrong, correct in vocab.items():
        text = re.sub(r'\b' + re.escape(wrong) + r'\b', correct, text, flags=re.IGNORECASE)
    return text


def merge_chunk_text(accumulated: str, new_text: str) -> str:
    """Simple two-text merger with overlap detection."""
    if not accumulated:
        return new_text
    if not new_text:
        return accumulated
    return polish_text([accumulated, new_text])
