"""
EspiaDesk File Transfer Module
- Transferência bidirecional de arquivos
- Suporte a múltiplos arquivos e pastas
- Progresso em tempo real
- Verificação de integridade com hash MD5
"""
import os
import json
import hashlib
import threading
from typing import Callable
from pathlib import Path


CHUNK_SIZE = 65536  # 64 KB por chunk


def md5_file(path: str) -> str:
    """Calcula MD5 de um arquivo."""
    h = hashlib.md5()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(65536), b''):
            h.update(chunk)
    return h.hexdigest()


class FileSender:
    """Envia arquivos pelo socket."""

    def __init__(self, send_fn: Callable, progress_cb: Callable = None):
        self.send_fn = send_fn
        self.progress_cb = progress_cb
        self._active = True
        self._lock = threading.Lock()

    def send_file(self, file_path: str, dest_name: str = None):
        """
        Envia um arquivo.
        send_fn(msg_type, payload) — função de envio do socket.
        """
        from espiadisk.protocol import MsgType
        path = Path(file_path)
        if not path.exists():
            raise FileNotFoundError(f"Arquivo não encontrado: {file_path}")

        file_size = path.stat().st_size
        file_name = dest_name or path.name
        file_md5 = md5_file(str(path))

        meta = {
            'name': file_name,
            'size': file_size,
            'md5': file_md5,
            'chunks': (file_size // CHUNK_SIZE) + (1 if file_size % CHUNK_SIZE else 0)
        }
        self.send_fn(MsgType.FILE_START, json.dumps(meta).encode())

        sent = 0
        chunk_idx = 0
        with open(str(path), 'rb') as f:
            while self._active:
                chunk = f.read(CHUNK_SIZE)
                if not chunk:
                    break
                header = json.dumps({'idx': chunk_idx, 'size': len(chunk)}).encode()
                header_len = len(header).to_bytes(4, 'big')
                payload = header_len + header + chunk
                self.send_fn(MsgType.FILE_CHUNK, payload)
                sent += len(chunk)
                chunk_idx += 1
                if self.progress_cb:
                    self.progress_cb(file_name, sent, file_size)

        end_meta = {'name': file_name, 'md5': file_md5, 'total_chunks': chunk_idx}
        self.send_fn(MsgType.FILE_END, json.dumps(end_meta).encode())

    def send_files(self, file_paths: list):
        """Envia múltiplos arquivos sequencialmente."""
        for fp in file_paths:
            if not self._active:
                break
            self.send_file(fp)

    def cancel(self):
        self._active = False


class FileReceiver:
    """Recebe arquivos pelo socket."""

    def __init__(self, save_dir: str, progress_cb: Callable = None,
                 complete_cb: Callable = None):
        self.save_dir = save_dir
        self.progress_cb = progress_cb
        self.complete_cb = complete_cb
        self._current: dict | None = None
        self._file_handle = None
        self._received_size = 0
        self._chunks: dict = {}

    def on_file_start(self, payload: bytes):
        """Chamado quando recebe início de transferência."""
        meta = json.loads(payload.decode())
        os.makedirs(self.save_dir, exist_ok=True)
        self._current = meta
        self._received_size = 0
        self._chunks = {}
        safe_name = os.path.basename(meta['name'])
        dest_path = os.path.join(self.save_dir, safe_name)

        counter = 1
        base, ext = os.path.splitext(dest_path)
        while os.path.exists(dest_path):
            dest_path = f"{base}_{counter}{ext}"
            counter += 1

        self._current['dest_path'] = dest_path
        self._file_handle = open(dest_path, 'wb')
        if self.progress_cb:
            self.progress_cb(meta['name'], 0, meta['size'])

    def on_file_chunk(self, payload: bytes):
        """Chamado quando recebe chunk de arquivo."""
        if not self._current or not self._file_handle:
            return
        header_len = int.from_bytes(payload[:4], 'big')
        header = json.loads(payload[4:4 + header_len].decode())
        chunk_data = payload[4 + header_len:]
        self._file_handle.write(chunk_data)
        self._received_size += len(chunk_data)
        if self.progress_cb:
            self.progress_cb(
                self._current['name'],
                self._received_size,
                self._current['size']
            )

    def on_file_end(self, payload: bytes):
        """Chamado quando recebe fim de transferência."""
        if self._file_handle:
            self._file_handle.close()
            self._file_handle = None

        if not self._current:
            return

        end_meta = json.loads(payload.decode())
        dest_path = self._current.get('dest_path', '')
        expected_md5 = end_meta.get('md5', '')
        received_md5 = md5_file(dest_path) if os.path.exists(dest_path) else ''
        success = (received_md5 == expected_md5) if expected_md5 else True

        if self.complete_cb:
            self.complete_cb(
                self._current['name'],
                dest_path,
                success,
                self._current['size']
            )
        self._current = None
