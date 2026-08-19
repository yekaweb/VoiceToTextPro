"""
plugins/instagram_plugin.py
Instagram downloader plugin using yt-dlp.
Extracts real available format qualities and metadata.
Includes fallback mechanisms for Instagram anti-scraping blocks.
"""
import json
import os
import subprocess
from plugins.base_plugin import BaseDownloaderPlugin


class InstagramPlugin(BaseDownloaderPlugin):

    @property
    def name(self) -> str:
        return "Instagram"

    @property
    def icon(self) -> str:
        return "📸"

    @property
    def supported_domains(self) -> list[str]:
        return ["instagram.com", "www.instagram.com", "instagr.am"]

    def can_handle(self, url: str) -> bool:
        if not url or not isinstance(url, str):
            return False
        return url.startswith(("http://", "https://")) and any(d in url for d in self.supported_domains)

    def get_info(self, url: str) -> dict:
        try:
            # Try fetching json metadata
            cmd = ["yt-dlp", "--dump-json", "--no-playlist", "--no-warnings", url]
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=25)
            
            if result.returncode == 0 and result.stdout.strip():
                data = json.loads(result.stdout)
                formats_set = []
                for f in data.get("formats", []):
                    height = f.get("height")
                    vcodec = f.get("vcodec", "none")
                    if height and vcodec != "none":
                        fmt_name = f"{height}p"
                        if fmt_name not in formats_set:
                            formats_set.append(fmt_name)

                if not formats_set:
                    formats_set = ["Best Quality (اصلی)", "720p (HD)", "480p (SD)"]
                
                formats_set.append("فقط صوت (Audio MP3)")

                return {
                    "title": data.get("title", data.get("description", "Instagram Reel/Post")[:60]),
                    "duration_ms": int(data.get("duration", 0)) * 1000 if data.get("duration") else 0,
                    "thumbnail_url": data.get("thumbnail", ""),
                    "uploader": data.get("uploader", data.get("channel", "Instagram User")),
                    "formats": formats_set,
                    "platform": self.name,
                    "icon": self.icon,
                }
            return {
                "title": "پست / ریلز اینستاگرام",
                "platform": self.name,
                "icon": self.icon,
                "formats": ["Best Quality (اصلی)", "720p (HD)", "فقط صوت (Audio MP3)"]
            }
        except Exception as e:
            return {
                "title": "پست / ریلز اینستاگرام",
                "platform": self.name,
                "icon": self.icon,
                "error": str(e),
                "formats": ["Best Quality (اصلی)", "فقط صوت (Audio MP3)"]
            }

    def download(self, url: str, output_dir: str, quality: str = "Best Quality", audio_only: bool = False) -> str:
        import time
        start_time = time.time()
        os.makedirs(output_dir, exist_ok=True)
        template = os.path.join(output_dir, "%(uploader)s_%(id)s.%(ext)s")

        def build_cmd(use_browser_cookies: bool = True) -> list[str]:
            cmd = ["yt-dlp", "--no-warnings"]
            if use_browser_cookies:
                local_app_data = os.environ.get("LOCALAPPDATA", "")
                app_data = os.environ.get("APPDATA", "")
                browser_paths = {
                    "chrome": os.path.join(local_app_data, "Google", "Chrome", "User Data"),
                    "edge": os.path.join(local_app_data, "Microsoft", "Edge", "User Data"),
                    "firefox": os.path.join(app_data, "Mozilla", "Firefox", "Profiles")
                }
                for b_name, b_path in browser_paths.items():
                    if os.path.exists(b_path):
                        cmd.extend(["--cookies-from-browser", b_name])
                        break
            cmd.extend(self.get_fast_downloader_args())
            if audio_only or "صوت" in quality or "Audio" in quality:
                cmd.extend(["-x", "--audio-format", "mp3", "-o", template, url])
            else:
                cmd.extend(["-f", "best", "--merge-output-format", "mp4", "-o", template, url])
            return cmd

        def run_attempt(cmd_list: list[str]) -> tuple[int, str, list[str]]:
            self.print_prog(5, f"شروع دریافت توربو ۳۲ تکه‌ای رسانه از {self.name}...")
            process = subprocess.Popen(
                cmd_list, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                text=True, encoding="utf-8", errors="replace"
            )
            downloaded_file = ""
            error_lines = []
            for line in process.stdout:
                line = line.strip()
                prog = self.parse_ytdlp_progress(line)
                if prog:
                    pct, speed, eta = prog
                    self.print_prog(5 + pct * 0.9, f"دانلود ۳۲ تکه‌ای aria2c: {pct:.1f}% | سرعت: {speed} | باقیمانده: {eta}")
                
                fn = self.extract_download_filename(line)
                if fn and os.path.exists(fn):
                    downloaded_file = fn
                if "ERROR:" in line or "Could not copy" in line:
                    error_lines.append(line)
            process.wait()
            return process.returncode, downloaded_file, error_lines

        # Attempt 1: Direct 32-thread aria2c Turbo (no browser cookie lock)
        code, downloaded_file, error_lines = run_attempt(build_cmd(use_browser_cookies=False))
        if not downloaded_file or not os.path.exists(downloaded_file):
            downloaded_file = self.find_latest_file(output_dir, start_time=start_time)

        # Attempt 2: If Attempt 1 produced no file, retry with browser cookies safely
        if not downloaded_file or not os.path.exists(downloaded_file):
            self.print_prog(8, "درحال بررسی تاییدیه کوکی‌های مرورگر...")
            code2, downloaded_file2, error_lines2 = run_attempt(build_cmd(use_browser_cookies=True))
            if downloaded_file2 and os.path.exists(downloaded_file2):
                downloaded_file = downloaded_file2
            else:
                downloaded_file = self.find_latest_file(output_dir, start_time=start_time)
                if error_lines2:
                    filtered_errs = [e for e in error_lines2 if "Could not copy" not in e]
                    if filtered_errs:
                        error_lines = filtered_errs

        if downloaded_file and os.path.exists(downloaded_file):
            self.print_prog(100, "دانلود کامل شد!")
            self.print_result({"file": downloaded_file, "platform": self.name})
            return downloaded_file
        else:
            err_msg = error_lines[0] if error_lines else "اینستاگرام دسترسی به این رسانه را محدود کرده است. لطفاً لینک را بررسی فرمایید."
            self.print_error(err_msg)
            return ""
