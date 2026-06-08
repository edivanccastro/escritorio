"""
EspiaDesk Screen Capture Module
- Captura de tela com PIL ImageGrab
- Compressão JPEG configurável
- Detecção de regiões alteradas (dirty rects)
- Suporte a múltiplos monitores
"""
import io
import zlib
import threading
import time
import numpy as np
from PIL import ImageGrab, Image
import tkinter as tk


class ScreenCapture:
    """Captura e comprime frames da tela."""

    def __init__(self, quality: int = 60, fps: int = 20):
        self.quality = quality
        self.fps = fps
        self.running = False
        self._thread: threading.Thread | None = None
        self._callback = None
        self._prev_frame: np.ndarray | None = None
        self._lock = threading.Lock()
        self._monitor = None

    def get_screen_info(self) -> dict:
        """Retorna informações sobre a resolução da tela."""
        root = tk.Tk()
        root.withdraw()
        w = root.winfo_screenwidth()
        h = root.winfo_screenheight()
        root.destroy()
        return {"width": w, "height": h}

    def capture_frame(self, bbox=None) -> bytes:
        """Captura um frame e retorna como JPEG comprimido."""
        img = ImageGrab.grab(bbox=bbox, all_screens=False)
        buf = io.BytesIO()
        img.save(buf, format='JPEG', quality=self.quality, optimize=True)
        return buf.getvalue()

    def capture_frame_png(self, bbox=None) -> bytes:
        """Captura como PNG (sem perdas, maior)."""
        img = ImageGrab.grab(bbox=bbox, all_screens=False)
        buf = io.BytesIO()
        img.save(buf, format='PNG', compress_level=6)
        return buf.getvalue()

    def capture_diff(self, bbox=None):
        """
        Captura frame e compara com frame anterior.
        Retorna (full_frame_bytes, dirty_rects).
        dirty_rects: lista de (x, y, w, h, jpeg_bytes)
        """
        img = ImageGrab.grab(bbox=bbox, all_screens=False)
        arr = np.array(img)

        full_buf = io.BytesIO()
        img.save(full_buf, format='JPEG', quality=self.quality, optimize=True)
        full_bytes = full_buf.getvalue()

        if self._prev_frame is None or arr.shape != self._prev_frame.shape:
            self._prev_frame = arr
            return full_bytes, []

        diff = np.any(arr != self._prev_frame, axis=2)
        changed_rows = np.where(diff.any(axis=1))[0]
        changed_cols = np.where(diff.any(axis=0))[0]

        dirty_rects = []
        if len(changed_rows) > 0 and len(changed_cols) > 0:
            y1, y2 = changed_rows[0], changed_rows[-1] + 1
            x1, x2 = changed_cols[0], changed_cols[-1] + 1

            region = img.crop((x1, y1, x2, y2))
            region_buf = io.BytesIO()
            region.save(region_buf, format='JPEG', quality=self.quality)
            dirty_rects.append({
                'x': int(x1), 'y': int(y1),
                'w': int(x2 - x1), 'h': int(y2 - y1),
                'data': region_buf.getvalue()
            })

        self._prev_frame = arr
        return full_bytes, dirty_rects

    def start(self, callback):
        """Inicia captura contínua em thread separada."""
        self._callback = callback
        self.running = True
        self._thread = threading.Thread(target=self._loop, daemon=True)
        self._thread.start()

    def stop(self):
        """Para a captura."""
        self.running = False
        if self._thread:
            self._thread.join(timeout=2)

    def _loop(self):
        interval = 1.0 / self.fps
        while self.running:
            t0 = time.time()
            try:
                frame_bytes = self.capture_frame()
                if self._callback:
                    self._callback(frame_bytes)
            except Exception:
                pass
            elapsed = time.time() - t0
            sleep_time = max(0, interval - elapsed)
            time.sleep(sleep_time)

    def set_quality(self, quality: int):
        """Ajusta qualidade JPEG (1-95)."""
        self.quality = max(1, min(95, quality))

    def set_fps(self, fps: int):
        """Ajusta FPS (1-60)."""
        self.fps = max(1, min(60, fps))


def decode_frame(data: bytes) -> Image.Image:
    """Decodifica frame JPEG para imagem PIL."""
    return Image.open(io.BytesIO(data))


def resize_frame(img: Image.Image, width: int, height: int) -> Image.Image:
    """Redimensiona frame para exibição."""
    return img.resize((width, height), Image.LANCZOS)
