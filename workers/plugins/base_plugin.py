"""
plugins/base_plugin.py
Abstract base class for all platform downloader plugins.

To add a new platform (e.g., TikTok):
  1. Create plugins/tiktok_plugin.py
  2. Inherit from BaseDownloaderPlugin
  3. Implement all abstract methods
  4. No other changes needed — plugin_manager auto-discovers it.
"""
import json
import sys
from abc import ABC, abstractmethod


class BaseDownloaderPlugin(ABC):

    # ── Identity ───────────────────────────────────────────────────────────
    @property
    @abstractmethod
    def name(self) -> str:
        """Human-readable platform name. e.g. 'YouTube'"""
        ...

    @property
    @abstractmethod
    def icon(self) -> str:
        """Emoji icon for the UI. e.g. '🎬'"""
        ...

    @property
    @abstractmethod
    def supported_domains(self) -> list[str]:
        """List of domains this plugin handles. e.g. ['youtube.com', 'youtu.be']"""
        ...

    # ── Core Methods ───────────────────────────────────────────────────────
    @abstractmethod
    def can_handle(self, url: str) -> bool:
        """Return True if this plugin can process the given URL."""
        ...

    @abstractmethod
    def get_info(self, url: str) -> dict:
        """
        Fetch metadata without downloading.
        Must return a dict with keys:
          title, duration_ms, thumbnail_url, formats (list of quality strings)
        """
        ...

    @abstractmethod
    def download(self, url: str, output_dir: str, quality: str, audio_only: bool) -> str:
        """
        Download media. Must return the absolute path of the downloaded file.
        Should call self.print_prog() for progress updates.
        """
        ...

    # ── Communication Helpers (stdout protocol) ────────────────────────────
    def print_prog(self, val: float, msg: str):
        print(f"PROG:{val:.1f}|{msg}", flush=True)

    def print_result(self, data: dict):
        print(f"RESULT:{json.dumps(data, ensure_ascii=False)}", flush=True)

    def print_error(self, msg: str):
        print(f"ERROR:{msg}", flush=True)

    # ── Shared Plugin Utilities (DRY Helpers) ──────────────────────────────
    def parse_ytdlp_progress(self, line: str) -> tuple[float, str, str] | None:
        """
        Parses yt-dlp or aria2c progress lines.
        Returns (pct, speed, eta) tuple or None if line is not a progress line.
        """
        import re
        if "[download]" in line and "%" in line:
            try:
                pct_str = line.split("%")[0].split()[-1]
                pct = float(pct_str)
                speed, eta = "?", "?"
                parts = line.split()
                for i, p in enumerate(parts):
                    if "iB/s" in p or "B/s" in p:
                        speed = p
                    if p == "ETA" and i + 1 < len(parts):
                        eta = parts[i + 1]
                return pct, speed, eta
            except Exception:
                pass

        # Parse aria2c 32-thread progress line e.g. [#200ad4 1.1MiB/15MiB(7%) CN:1 DL:2.5MiB]
        if "CN:" in line or "DL:" in line or ("(" in line and "%)" in line):
            try:
                match = re.search(r"\((\d+(?:\.\d+)?)%\)", line)
                if match:
                    pct = float(match.group(1))
                    speed = "?"
                    speed_match = re.search(r"DL:([^\s\]]+)", line)
                    if speed_match:
                        speed = speed_match.group(1) + "/s"
                    return pct, speed, "--"
            except Exception:
                pass

        return None

    def extract_download_filename(self, line: str) -> str | None:
        """
        Extracts destination file path from yt-dlp stdout lines.
        Handles Destination:, Merging formats into, and already downloaded patterns.
        """
        import os
        line = line.strip()
        if "Destination:" in line:
            fn = line.split("Destination:")[-1].strip().strip('"')
            if fn: return fn
        if "Merging formats into" in line:
            fn = line.split("Merging formats into")[-1].strip().strip('"')
            if fn: return fn
        if "has already been downloaded" in line:
            fn = line.replace("[download]", "").split("has already been downloaded")[0].strip().strip('"')
            if fn: return fn
        return None

    def find_latest_file(self, output_dir: str, start_time: float = 0.0) -> str:
        """
        Finds the most recently created file in output_dir, ignoring temporary download files (.part, .ytdl, .json).
        If start_time is provided, ONLY returns files created or modified AFTER start_time.
        """
        import os
        if not os.path.exists(output_dir):
            return ""
        
        valid_files = []
        for f in os.listdir(output_dir):
            if f.endswith(".part") or f.endswith(".ytdl") or f.endswith(".json") or f.endswith(".txt"):
                continue
            full_path = os.path.join(output_dir, f)
            if not os.path.isfile(full_path):
                continue
            ctime = os.path.getctime(full_path)
            mtime = os.path.getmtime(full_path)
            # If start_time given, enforce that file was created/modified during this download session
            if start_time > 0:
                if ctime >= (start_time - 3.0) or mtime >= (start_time - 3.0):
                    valid_files.append((full_path, max(ctime, mtime)))
            else:
                valid_files.append((full_path, max(ctime, mtime)))
        
        if not valid_files:
            return ""
            
        valid_files.sort(key=lambda x: x[1], reverse=True)
        return valid_files[0][0]

    def get_fast_downloader_args(self) -> list[str]:
        """
        Returns 32-thread Turbo Downloader arguments using IDM 32-chunk parallel streaming technology (-N 32).
        Preserves signed CDN tokens, session headers, and avoids external process errors.
        """
        return ["-N", "32", "--concurrent-fragments", "32", "--http-chunk-size", "1M", "--buffer-size", "1024k", "--no-mtime"]

