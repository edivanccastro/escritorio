"""
EspiaDesk Audio Streaming Module
- Captura de áudio com PyAudio
- Streaming bidirecional (microfone + áudio do sistema)
- Compressão via zlib
"""
import threading
import zlib
import time
from typing import Callable


CHUNK_SIZE = 1024
SAMPLE_RATE = 44100
CHANNELS = 1
FORMAT_WIDTH = 2  # 16-bit PCM


class AudioCapture:
    """Captura áudio do microfone/sistema."""

    def __init__(self, callback: Callable[[bytes], None]):
        self.callback = callback
        self.running = False
        self._thread: threading.Thread | None = None
        self._pa = None
        self._stream = None

    def start(self):
        """Inicia captura de áudio."""
        try:
            import pyaudio
            self._pa = pyaudio.PyAudio()
            self._stream = self._pa.open(
                format=pyaudio.paInt16,
                channels=CHANNELS,
                rate=SAMPLE_RATE,
                input=True,
                frames_per_buffer=CHUNK_SIZE
            )
            self.running = True
            self._thread = threading.Thread(target=self._loop, daemon=True)
            self._thread.start()
        except Exception as e:
            print(f"[Audio] Erro ao iniciar captura: {e}")

    def _loop(self):
        while self.running:
            try:
                data = self._stream.read(CHUNK_SIZE, exception_on_overflow=False)
                compressed = zlib.compress(data, level=1)
                self.callback(compressed)
            except Exception:
                break

    def stop(self):
        """Para a captura de áudio."""
        self.running = False
        if self._stream:
            try:
                self._stream.stop_stream()
                self._stream.close()
            except Exception:
                pass
        if self._pa:
            try:
                self._pa.terminate()
            except Exception:
                pass


class AudioPlayer:
    """Reproduz áudio recebido."""

    def __init__(self):
        self._pa = None
        self._stream = None
        self._queue = []
        self._lock = threading.Lock()
        self._thread: threading.Thread | None = None
        self.running = False

    def start(self):
        """Inicia player de áudio."""
        try:
            import pyaudio
            self._pa = pyaudio.PyAudio()
            self._stream = self._pa.open(
                format=pyaudio.paInt16,
                channels=CHANNELS,
                rate=SAMPLE_RATE,
                output=True,
                frames_per_buffer=CHUNK_SIZE
            )
            self.running = True
            self._thread = threading.Thread(target=self._loop, daemon=True)
            self._thread.start()
        except Exception as e:
            print(f"[Audio] Erro ao iniciar player: {e}")

    def play(self, compressed_data: bytes):
        """Adiciona dados de áudio à fila de reprodução."""
        with self._lock:
            self._queue.append(compressed_data)
            if len(self._queue) > 50:
                self._queue.pop(0)

    def _loop(self):
        while self.running:
            data = None
            with self._lock:
                if self._queue:
                    data = self._queue.pop(0)
            if data:
                try:
                    pcm = zlib.decompress(data)
                    self._stream.write(pcm)
                except Exception:
                    pass
            else:
                time.sleep(0.01)

    def stop(self):
        """Para o player de áudio."""
        self.running = False
        if self._stream:
            try:
                self._stream.stop_stream()
                self._stream.close()
            except Exception:
                pass
        if self._pa:
            try:
                self._pa.terminate()
            except Exception:
                pass
