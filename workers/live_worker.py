#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Universal Live Streaming Engine Worker for VoiceToText Pro
Supports BOTH Vosk models and Faster-Whisper models for real-time speech recognition.
Accepts 16kHz 16-bit Mono PCM bytes over local TCP socket and emits JSON partial/final frames.
"""

import sys
import os
import json
import socket
import argparse
import logging
import numpy as np

logging.basicConfig(level=logging.INFO, format="[LIVE_WORKER] %(asctime)s - %(levelname)s - %(message)s")


def is_whisper_model(model_path: str) -> bool:
    """Checks if the given directory contains a Faster-Whisper model."""
    if not os.path.exists(model_path):
        return False
    folder_lower = model_path.lower()
    if "faster-whisper" in folder_lower or "whisper" in folder_lower:
        return True
    try:
        files = os.listdir(model_path)
        return any(f in files for f in ["model.bin", "model.safetensors", "config.json"])
    except Exception:
        return False


def run_vosk_worker(model_path: str, conn: socket.socket):
    """Runs live streaming transcription using Vosk engine."""
    import vosk
    vosk.SetLogLevel(-1)

    logging.info(f"آماده‌سازی موتور Vosk از مسیر: {model_path}")
    model = vosk.Model(model_path)
    recognizer = vosk.KaldiRecognizer(model, 16000)
    recognizer.SetWords(True)
    logging.info("موتور Vosk آماده استریم صوتی می‌باشد.")

    last_partial = ""

    while True:
        data = conn.recv(4096)
        if not data:
            break

        if recognizer.AcceptWaveform(data):
            res = json.loads(recognizer.Result())
            text = res.get("text", "").strip()
            if text:
                payload = json.dumps({"type": "final", "text": text}, ensure_ascii=False) + "\n"
                conn.sendall(payload.encode("utf-8"))
                last_partial = ""
        else:
            pres = json.loads(recognizer.PartialResult())
            partial_text = pres.get("partial", "").strip()
            if partial_text and partial_text != last_partial:
                last_partial = partial_text
                payload = json.dumps({"type": "partial", "text": partial_text}, ensure_ascii=False) + "\n"
                conn.sendall(payload.encode("utf-8"))


def run_whisper_worker(model_path: str, conn: socket.socket):
    """Runs live streaming transcription using Faster-Whisper engine with auto language detection."""
    from faster_whisper import WhisperModel

    device = "cpu"
    compute_type = "int8"
    try:
        import ctranslate2
        if hasattr(ctranslate2, "get_cuda_device_count") and ctranslate2.get_cuda_device_count() > 0:
            device = "cuda"
            compute_type = "float16"
    except Exception:
        pass

    logging.info(f"لود مدل چندزبانه Whisper از {model_path} روی پردازنده {device.upper()} ({compute_type})...")
    
    try:
        model = WhisperModel(model_path, device=device, compute_type=compute_type)
        logging.info("مدل Whisper لود شد. آماده دریافت استریم صوتی...")
    except Exception as ex:
        logging.error(f"خطا در لود مدل Whisper: {ex}")
        payload = json.dumps({"type": "final", "text": f"[خطا در لود مدل Whisper: {ex}]"}, ensure_ascii=False) + "\n"
        conn.sendall(payload.encode("utf-8"))
        return

    pcm_buffer = bytearray()
    # 16kHz, 16-bit mono = 32000 bytes per second.
    # Process buffer every 1.2 seconds (~38400 bytes) for real-time responsiveness
    CHUNK_BYTES = 38400 
    MAX_BUFFER_BYTES = 32000 * 8 # Keep max 8 seconds of context

    while True:
        data = conn.recv(4096)
        if not data:
            break

        pcm_buffer.extend(data)

        if len(pcm_buffer) >= CHUNK_BYTES:
            # Convert raw 16-bit PCM bytes to float32 numpy array normalized to [-1.0, 1.0]
            audio_data = np.frombuffer(pcm_buffer, dtype=np.int16).astype(np.float32) / 32768.0

            try:
                # Transcribe with automatic language detection (multilingual mode)
                segments, info = model.transcribe(
                    audio_data,
                    language=None, # Auto-detect language (Persian, English, etc.)
                    beam_size=1,
                    best_of=1,
                    vad_filter=True,
                    vad_parameters=dict(min_silence_duration_ms=250)
                )

                text_parts = [segment.text.strip() for segment in segments if segment.text.strip()]
                full_text = " ".join(text_parts).strip()

                if full_text:
                    detected_lang = info.language.upper() if (info and info.language) else "AUTO"
                    logging.info(f"[Whisper Live ({detected_lang})]: {full_text}")
                    payload = json.dumps({
                        "type": "final",
                        "text": f"[{detected_lang}] {full_text}"
                    }, ensure_ascii=False) + "\n"
                    conn.sendall(payload.encode("utf-8"))
                    
                    # Reset buffer after emitting final recognized phrase
                    pcm_buffer.clear()

            except Exception as ex:
                logging.error(f"خطا در پردازش Whisper Live: {ex}")
                pcm_buffer.clear()

        if len(pcm_buffer) > MAX_BUFFER_BYTES:
            pcm_buffer = pcm_buffer[-CHUNK_BYTES:]


def run_live_worker(model_path: str, host: str = "127.0.0.1", port: int = 9876):
    if not os.path.exists(model_path):
        logging.error(f"مسیر مدل یافت نشد: {model_path}")
        sys.exit(1)

    server_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    
    try:
        server_sock.bind((host, port))
    except Exception as ex:
        logging.error(f"خطا در بایند سوکت روی پورت {port}: {ex}")
        sys.exit(1)

    server_sock.listen(1)
    logging.info(f"سوکت سرور پایتون روی {host}:{port} بایند شد. منتظر اتصال C#...")

    conn, addr = server_sock.accept()
    logging.info(f"اتصال از طرف C# برقرار شد: {addr}")

    try:
        if is_whisper_model(model_path):
            run_whisper_worker(model_path, conn)
        else:
            run_vosk_worker(model_path, conn)
    except ConnectionResetError:
        logging.warning("اتصال سوکت توسط برنامه‌ی C# قطع شد.")
    except Exception as ex:
        logging.error(f"خطای غیرمنتظره در کارگر استریم زنده: {ex}")
    finally:
        try:
            conn.close()
        except Exception:
            pass
        try:
            server_sock.close()
        except Exception:
            pass
        logging.info("کارگر زنده متوقف شد.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="VoiceToText Pro Universal Live Worker")
    parser.add_argument("--model_path", type=str, required=True, help="Path to unpacked model folder")
    parser.add_argument("--host", type=str, default="127.0.0.1", help="Binding host")
    parser.add_argument("--port", type=int, default=9876, help="Binding TCP port")
    args = parser.parse_args()

    run_live_worker(args.model_path, args.host, args.port)
