import os
import sys
import datetime
import srt

def process_whisper(video_path, model_path, out_dir):
    print("PROG:10|بارگذاری مدل Whisper...", flush=True)
    try:
        from faster_whisper import WhisperModel
    except ImportError:
        print("PROG:0|خطا: کتابخانه faster-whisper نصب نشده است. لطفا آن را نصب کنید.", flush=True)
        return

    try:
        # device="auto" to use GPU if available, fallback to CPU
        model = WhisperModel(model_path, device="cpu", compute_type="int8")
    except Exception as e:
        print(f"PROG:0|خطا در بارگذاری مدل Whisper: {e}", flush=True)
        return

    print("PROG:30|در حال استخراج زیرنویس با Whisper...", flush=True)
    
    try:
        # For a full transcription without VAD filter:
        segments, info = model.transcribe(video_path, beam_size=5)
        
        subs = []
        idx = 1
        for segment in segments:
            # Report progress just to keep UI alive (we don't know total easily unless we get duration)
            print(f"PROG:60|در حال پردازش (زمان {segment.start:.1f}s)...", flush=True)
            subs.append(
                srt.Subtitle(
                    index=idx,
                    start=datetime.timedelta(seconds=segment.start),
                    end=datetime.timedelta(seconds=segment.end),
                    content=segment.text.strip()
                )
            )
            idx += 1
            
        print("PROG:90|ذخیره زیرنویس...", flush=True)
        srt_text = srt.compose(subs)
        out_name = os.path.basename(video_path) + "_whisper.srt"
        out_path = os.path.join(out_dir, out_name)
        with open(out_path, "w", encoding="utf-8") as f:
            f.write(srt_text)
            
        print("PROG:100|پایان", flush=True)
        print(f"SRT_PATH:{out_path}", flush=True)
    except Exception as e:
        print(f"PROG:0|خطا در استخراج Whisper: {e}", flush=True)
