import sys
import os
import json
import subprocess

from vosk import Model, KaldiRecognizer, SetLogLevel
SetLogLevel(-1)

def extract_audio_16k(video_path):
    cmd = [
        "ffmpeg",
        "-i", video_path,
        "-ar", "16000",
        "-ac", "1",
        "-f", "s16le",
        "-"
    ]
    process = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    return process

def process_vosk(video_path, model_path, output_dir):
    print("PROG:10|بارگذاری مدل صوتی (ممکن است طول بکشد)...", flush=True)
    try:
        model = Model(model_path)
    except Exception as e:
        print(f"PROG:0|خطا در بارگذاری مدل: {e}", flush=True)
        return
        
    rec = KaldiRecognizer(model, 16000)
    rec.SetWords(True)

    print("PROG:20|استخراج جریان صوتی...", flush=True)
    process = extract_audio_16k(video_path)

    results = []
    
    print("PROG:30|در حال رونویسی هوشمند...", flush=True)
    count = 0
    while True:
        data = process.stdout.read(4000)
        if len(data) == 0:
            break
        if rec.AcceptWaveform(data):
            res = json.loads(rec.Result())
            if "result" in res:
                results.extend(res["result"])
        
        count += 1
        if count % 100 == 0:
            # Keep progress bar alive in the 30-80% range
            p = min(80, 30 + (count // 100))
            print(f"PROG:{p}|در حال پردازش...", flush=True)
            
    res = json.loads(rec.FinalResult())
    if "result" in res:
        results.extend(res["result"])
        
    print("PROG:85|تولید فایل SRT...", flush=True)
    
    srt_entries = []
    current_sentence = []
    sentence_start = 0
    current_end = 0
    
    for word_info in results:
        w = word_info['word']
        start = word_info['start']
        end = word_info['end']
        
        if not current_sentence:
            sentence_start = start
            current_sentence.append(w)
            current_end = end
        else:
            gap = start - current_end
            # Start new sentence if gap > 1s or length >= 8 words
            if gap > 1.0 or len(current_sentence) >= 8:
                srt_entries.append((sentence_start, current_end, " ".join(current_sentence)))
                current_sentence = [w]
                sentence_start = start
                current_end = end
            else:
                current_sentence.append(w)
                current_end = end

    if current_sentence:
        srt_entries.append((sentence_start, current_end, " ".join(current_sentence)))
        
    def format_srt_time(seconds):
        h = int(seconds // 3600)
        m = int((seconds % 3600) // 60)
        s = int(seconds % 60)
        ms = int((seconds % 1) * 1000)
        return f"{h:02d}:{m:02d}:{s:02d},{ms:03d}"

    srt_content = ""
    for idx, (start, end, text) in enumerate(srt_entries):
        srt_content += f"{idx + 1}\n"
        srt_content += f"{format_srt_time(start)} --> {format_srt_time(end)}\n"
        srt_content += f"{text}\n\n"
        
    os.makedirs(output_dir, exist_ok=True)
    base = os.path.splitext(os.path.basename(video_path))[0]
    out_srt = os.path.join(output_dir, f"{base}_vosk.srt")
    
    with open(out_srt, "w", encoding="utf-8") as f:
        f.write(srt_content)
        
    print(f"SRT_PATH:{out_srt}", flush=True)
    print("PROG:100|زیرنویس با موفقیت تولید شد!", flush=True)
