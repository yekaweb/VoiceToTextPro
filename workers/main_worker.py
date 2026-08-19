"""
main_worker.py
Unified Python worker entry point.
Called by C# PythonBridge with a mode argument.

Usage:
  python main_worker.py transcribe <file> <lang> <out_dir>
  python main_worker.py download <url> <out_dir> <quality> <audio_only>
  python main_worker.py info <url>
  python main_worker.py list_plugins
"""
import sys
import io
import os
import json

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

# Add workers dir to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def mode_transcribe(args):
    if len(args) < 3:
        print("PROG:0|خطا: آرگومان‌های کافی نیست.", flush=True)
        sys.exit(1)
    fp, lang, out_dir = args[0], args[1], args[2]

    from core.audio_processor import load_audio, smart_chunk_audio, normalize_audio, format_timestamp
    from core.transcriber import get_transcriber, latin_to_perso_arabic_az
    from core.text_polisher import polish_text
    from core.subtitle_builder import chunks_to_subtitle_entries, to_srt, to_vtt

    is_az_arabic = lang == "az-ARABIC"
    target_lang = "az-AZ" if is_az_arabic else lang

    print(f"PROG:5|در حال بارگذاری فایل صوتی...", flush=True)
    audio = load_audio(fp)
    dur = format_timestamp(len(audio))
    audio = normalize_audio(audio)

    print(f"PROG:10|برش هوشمند صوت...", flush=True)
    chunks = smart_chunk_audio(audio)
    total = len(chunks)

    transcriber = get_transcriber("google")
    ts_lines, raw_texts, subtitle_data = [], [], []

    for i, (start_ms, end_ms, chunk) in enumerate(chunks):
        prog = 10 + (85 * (i + 1) / total)
        ts = format_timestamp(start_ms)
        print(f"PROG:{prog:.1f}|پردازش بخش {i+1} از {total} [{ts}]", flush=True)

        text = transcriber.transcribe_chunk(chunk, target_lang)
        if text and not text.startswith("[CONNECTION"):
            if is_az_arabic:
                text = latin_to_perso_arabic_az(text)
            print(f"TEXT:{text}", flush=True)
            ts_lines.append(f"[{ts}] {text}")
            raw_texts.append(text)
            subtitle_data.append((start_ms, end_ms, text))

    polished = polish_text(raw_texts)
    os.makedirs(out_dir, exist_ok=True)
    base = os.path.splitext(os.path.basename(fp))[0]

    f1 = os.path.join(out_dir, f"{base}_timestamped.txt")
    with open(f1, "w", encoding="utf-8") as f:
        f.write("\n".join(ts_lines))

    f2 = os.path.join(out_dir, f"{base}_plain.txt")
    with open(f2, "w", encoding="utf-8") as f:
        f.write(polished)

    # Also save SRT
    entries = chunks_to_subtitle_entries(subtitle_data)
    f3 = os.path.join(out_dir, f"{base}.srt")
    with open(f3, "w", encoding="utf-8") as f:
        f.write(to_srt(entries))

    print(f"POLISHED_TEXT:{polished}", flush=True)
    print(f"SRT_PATH:{f3}", flush=True)
    print(f"PROG:100|پردازش کامل شد! فایل‌ها در {out_dir} ذخیره شدند.", flush=True)


import urllib.parse


def is_valid_url(url: str) -> bool:
    if not url or not isinstance(url, str):
        return False
    try:
        parsed = urllib.parse.urlparse(url.strip())
        return parsed.scheme in ('http', 'https') and bool(parsed.netloc)
    except Exception:
        return False


def mode_info(args):
    if not args:
        print("ERROR:URL required", flush=True)
        sys.exit(1)
    url = args[0]
    if not is_valid_url(url):
        print(f"RESULT:{json.dumps({'error': 'آدرس اینترنتی معتبر نیست (باید با http:// یا https:// شروع شود)'}, ensure_ascii=False)}", flush=True)
        return
    from plugins.plugin_manager import get_manager
    manager = get_manager()
    manager.discover()
    plugin = manager.find(url)
    if not plugin:
        print(f"RESULT:{json.dumps({'error': 'No plugin found for this URL'})}", flush=True)
        return
    info = plugin.get_info(url)
    print(f"RESULT:{json.dumps(info, ensure_ascii=False)}", flush=True)


def mode_download(args):
    if len(args) < 2:
        print("ERROR:URL and output_dir required", flush=True)
        sys.exit(1)
    url = args[0]
    out_dir = args[1]
    if not is_valid_url(url):
        print("ERROR:آدرس اینترنتی نامعتبر است.", flush=True)
        sys.exit(1)
    quality = args[2] if len(args) > 2 else "720p"
    audio_only = args[3].lower() == "true" if len(args) > 3 else False

    from plugins.plugin_manager import get_manager
    manager = get_manager()
    manager.discover()
    plugin = manager.find(url)
    if not plugin:
        print("ERROR:پلتفرم شناسایی نشد.", flush=True)
        sys.exit(1)
    plugin.download(url, out_dir, quality, audio_only)


def mode_list_plugins(args):
    from plugins.plugin_manager import get_manager
    manager = get_manager()
    manager.discover()
    plugins = manager.list_all()
    print(f"RESULT:{json.dumps(plugins, ensure_ascii=False)}", flush=True)


def mode_subtitle(args):
    if len(args) < 3:
        print("PROG:0|خطا: آرگومان‌های کافی نیست.", flush=True)
        sys.exit(1)
    fp, model_path, out_dir = args[0], args[1], args[2]
    
    import os
    is_whisper = False
    if "whisper" in model_path.lower() or os.path.exists(os.path.join(model_path, "config.json")):
        is_whisper = True
        
    if is_whisper:
        from core.whisper_engine import process_whisper
        process_whisper(fp, model_path, out_dir)
    else:
        from core.vosk_engine import process_vosk
        process_vosk(fp, model_path, out_dir)


def mode_transcribe_chunk(args):
    if len(args) < 4:
        print("ERROR:Arguments required: <file> <start_ms> <end_ms> <lang>", flush=True)
        sys.exit(1)
    
    fp, start_ms, end_ms, lang = args[0], int(args[1]), int(args[2]), args[3]
    
    from core.audio_processor import load_audio, normalize_audio
    from core.transcriber import get_transcriber, latin_to_perso_arabic_az
    
    print("PROG:20|در حال بارگذاری صدا...", flush=True)
    try:
        audio = load_audio(fp)
        transcriber = get_transcriber("google")
        
        is_az_arabic = lang == "az-ARABIC"
        target_lang = "az-AZ" if is_az_arabic else lang
        
        print("PROG:50|پردازش هوشمند چندمرحله‌ای...", flush=True)
        # Multi-pass chunking strategy to guarantee recognition success:
        # Pass 1: 400ms generous acoustic safety margin (captures complete starting/ending words)
        # Pass 2: 600ms wide margin (for slow speech / long phonemes)
        # Pass 3: 200ms tight margin
        # Pass 4: 0ms exact boundary fallback
        paddings = [400, 600, 200, 0]
        text = ""
        
        for pad in paddings:
            p_start = max(0, start_ms - pad)
            p_end = min(len(audio), end_ms + pad)
            chunk = audio[p_start:p_end]
            chunk = normalize_audio(chunk)
            
            res = transcriber.transcribe_chunk(chunk, target_lang)
            if res and not res.startswith("[CONNECTION"):
                text = res
                break
            elif res == "[CONNECTION_ERROR]":
                text = res
                break
        
        if text and not text.startswith("[CONNECTION"):
            if is_az_arabic:
                text = latin_to_perso_arabic_az(text)
            print(f"CORRECTED_TEXT:{text}", flush=True)
        else:
            print(f"CORRECTED_TEXT:خطا در ارتباط با سرور یا صدای نامفهوم.", flush=True)
            
        print("PROG:100|عملیات پایان یافت.", flush=True)
    except Exception as e:
        print(f"ERROR:Chunk transcribe failed: {str(e)}", flush=True)


def mode_diarize(args):
    if not args:
        print("ERROR:File path required", flush=True)
        sys.exit(1)
    fp = args[0]
    from core.audio_processor import load_audio
    from core.pyannote_engine import perform_diarization

    print("PROG:20|در حال بارگذاری فایل صوتی...", flush=True)
    audio = load_audio(fp)

    print("PROG:60|در حال تفکیک خودکار گویندگان...", flush=True)
    results = perform_diarization(audio)

    print(f"RESULT:{json.dumps(results, ensure_ascii=False)}", flush=True)
    print("PROG:100|تفکیک گویندگان پایان یافت.", flush=True)


def mode_translate(args):
    if not args:
        print("ERROR:Subtitle JSON or text required", flush=True)
        sys.exit(1)
    json_data = args[0]
    target_lang = args[1] if len(args) > 1 else "fa"
    provider = args[2] if len(args) > 2 else "ollama"

    from core.subtitle_translator import translate_subtitle_entries

    print("PROG:30|در حال آماده‌سازی خطوط زیرنویس برای ترجمه بافت‌محور...", flush=True)
    try:
        entries = json.loads(json_data)
        print("PROG:60|در حال ارتباط با مدل هوش مصنوعی...", flush=True)
        translated = translate_subtitle_entries(entries, target_lang=target_lang, provider=provider)
        print(f"RESULT:{json.dumps(translated, ensure_ascii=False)}", flush=True)
        print("PROG:100|ترجمه هوشمند با موفقیت پایان یافت.", flush=True)
    except Exception as e:
        print(f"ERROR:Translation failed: {str(e)}", flush=True)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("ERROR:Mode required (transcribe|download|info|list_plugins|diarize|translate)", flush=True)
        sys.exit(1)

    mode = sys.argv[1].lower()
    rest = sys.argv[2:]

    if mode == "transcribe":
        mode_transcribe(rest)
    elif mode == "transcribe_chunk":
        mode_transcribe_chunk(rest)
    elif mode == "diarize":
        mode_diarize(rest)
    elif mode == "translate":
        mode_translate(rest)
    elif mode == "subtitle":
        mode_subtitle(rest)
    elif mode == "info":
        mode_info(rest)
    elif mode == "download":
        mode_download(rest)
    elif mode == "list_plugins":
        mode_list_plugins(rest)
    elif mode == "ping":
        print("RESULT:PONG", flush=True)
    else:
        print(f"ERROR:Unknown mode: {mode}", flush=True)
        sys.exit(1)
