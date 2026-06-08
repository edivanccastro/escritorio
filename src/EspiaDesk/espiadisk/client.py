"""
EspiaDesk Remote Client
- Conecta ao host remoto
- Exibe a tela remota
- Envia eventos de mouse e teclado
- Transferência de arquivos bidirecional
- Chat em tempo real
"""
import socket
import threading
import json
import time
from typing import Callable, Optional

from espiadisk.protocol import (
    MsgType, recv_message, send_message, send_json, parse_json
)
from espiadisk.crypto import SessionCrypto, hash_password
from espiadisk.audio import AudioCapture, AudioPlayer
from espiadisk.file_transfer import FileSender, FileReceiver
from espiadisk.input_ctrl import InputEventSerializer


DEFAULT_PORT = 7070


class RemoteClient:
    """Cliente de acesso remoto EspiaDesk."""

    def __init__(self,
                 on_frame: Callable[[bytes], None],
                 on_chat: Callable[[str, str], None] = None,
                 on_connected: Callable = None,
                 on_disconnected: Callable = None,
                 on_file_progress: Callable = None,
                 on_file_complete: Callable = None,
                 on_clipboard: Callable[[str], None] = None,
                 audio_enabled: bool = False,
                 download_dir: str = "."):

        self.on_frame = on_frame
        self.on_chat = on_chat or (lambda n, m: None)
        self.on_connected = on_connected or (lambda info: None)
        self.on_disconnected = on_disconnected or (lambda: None)
        self.on_file_progress = on_file_progress
        self.on_file_complete = on_file_complete
        self.on_clipboard = on_clipboard or (lambda t: None)
        self.audio_enabled = audio_enabled
        self.download_dir = download_dir

        self._sock: Optional[socket.socket] = None
        self._crypto = SessionCrypto()
        self._recv_thread: Optional[threading.Thread] = None
        self._heartbeat_thread: Optional[threading.Thread] = None

        self.running = False
        self.connected = False
        self.session_id: str = ""
        self.remote_width: int = 1920
        self.remote_height: int = 1080
        self.remote_host: str = ""
        self.local_name: str = socket.gethostname()

        self._file_sender: Optional[FileSender] = None
        self._file_receiver: Optional[FileReceiver] = None
        self._audio_player: Optional[AudioPlayer] = None
        self._audio_capture: Optional[AudioCapture] = None

    def connect(self, host: str, port: int = DEFAULT_PORT,
                password: str = "", name: str = "") -> dict:
        """
        Conecta ao host remoto.
        Retorna dict com info da sessão ou lança exceção.
        """
        self.remote_host = host
        if name:
            self.local_name = name

        self._sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._sock.settimeout(15)
        self._sock.connect((host, port))
        self._sock.settimeout(None)

        session_info = self._handshake(password)
        self._post_connect(session_info)
        return session_info

    def _handshake(self, password: str) -> dict:
        """Realiza handshake de autenticação."""
        # 1. Receber chave pública do servidor
        msg_type, server_pub_bytes = recv_message(self._sock)
        if msg_type != MsgType.AUTH_REQ:
            raise ValueError("Handshake inválido")

        # 2. Enviar chave pública própria
        my_pub = self._crypto.get_public_key_bytes()
        send_message(self._sock, MsgType.AUTH_REQ, my_pub)

        # 3. Receber chave de sessão encriptada
        msg_type, encrypted_session_key = recv_message(self._sock)
        if msg_type != MsgType.AUTH_RESP:
            raise ValueError("Handshake inválido: esperado AUTH_RESP")
        self._crypto.establish_as_client(encrypted_session_key)

        # 4. Enviar credenciais encriptadas
        creds = {'name': self.local_name, 'password': password}
        creds_encrypted = self._crypto.encrypt(json.dumps(creds).encode())
        send_message(self._sock, MsgType.AUTH_REQ, creds_encrypted)

        # 5. Receber resultado
        msg_type, payload = recv_message(self._sock)
        if msg_type != MsgType.AUTH_RESP:
            raise ValueError("Handshake inválido: esperado AUTH_RESP final")

        result = json.loads(self._crypto.decrypt(payload).decode())
        if not result.get('ok'):
            raise PermissionError(result.get('reason', 'Acesso negado'))

        self.session_id = result.get('session_id', '')
        self.remote_width = result.get('screen_width', 1920)
        self.remote_height = result.get('screen_height', 1080)
        return result

    def _post_connect(self, session_info: dict):
        """Configura componentes após conexão bem-sucedida."""
        self.connected = True
        self.running = True

        self._file_sender = FileSender(
            send_fn=self._send_raw,
            progress_cb=self.on_file_progress
        )
        self._file_receiver = FileReceiver(
            save_dir=self.download_dir,
            progress_cb=self.on_file_progress,
            complete_cb=self.on_file_complete
        )

        if self.audio_enabled:
            self._audio_player = AudioPlayer()
            self._audio_player.start()
            self._audio_capture = AudioCapture(self._send_audio)
            self._audio_capture.start()

        self._recv_thread = threading.Thread(
            target=self._receive_loop, daemon=True
        )
        self._recv_thread.start()

        self._heartbeat_thread = threading.Thread(
            target=self._heartbeat_loop, daemon=True
        )
        self._heartbeat_thread.start()

        self.on_connected(session_info)

    def _receive_loop(self):
        """Loop de recebimento de mensagens do servidor."""
        while self.running:
            try:
                msg_type, payload = recv_message(self._sock)

                if msg_type == MsgType.SCREEN_FRAME:
                    frame = self._crypto.decrypt(payload)
                    self.on_frame(frame)

                elif msg_type == MsgType.CHAT:
                    data = parse_json(self._crypto.decrypt(payload))
                    self.on_chat(data.get('name', 'Host'), data.get('msg', ''))

                elif msg_type == MsgType.CLIPBOARD:
                    data = parse_json(self._crypto.decrypt(payload))
                    self.on_clipboard(data.get('text', ''))

                elif msg_type == MsgType.FILE_START:
                    dec = self._crypto.decrypt(payload)
                    self._file_receiver.on_file_start(dec)

                elif msg_type == MsgType.FILE_CHUNK:
                    dec = self._crypto.decrypt(payload)
                    self._file_receiver.on_file_chunk(dec)

                elif msg_type == MsgType.FILE_END:
                    dec = self._crypto.decrypt(payload)
                    self._file_receiver.on_file_end(dec)

                elif msg_type == MsgType.AUDIO:
                    if self._audio_player:
                        self._audio_player.play(self._crypto.decrypt(payload))

                elif msg_type == MsgType.HEARTBEAT:
                    pass

                elif msg_type == MsgType.CONTROL:
                    data = parse_json(self._crypto.decrypt(payload))
                    if data.get('action') == 'disconnect':
                        break

            except Exception as e:
                if self.running:
                    print(f"[Client] Erro recebendo: {e}")
                break

        self.running = False
        self.connected = False
        self.on_disconnected()

    def _heartbeat_loop(self):
        """Mantém a conexão viva com heartbeats."""
        while self.running:
            time.sleep(10)
            try:
                self._send_control({'action': 'heartbeat'})
            except Exception:
                break

    def _send_raw(self, msg_type: int, payload: bytes):
        """Envia payload encriptado."""
        encrypted = self._crypto.encrypt(payload)
        send_message(self._sock, msg_type, encrypted)

    def _send_control(self, data: dict):
        self._send_raw(MsgType.CONTROL, json.dumps(data).encode())

    def _send_audio(self, audio_data: bytes):
        try:
            self._send_raw(MsgType.AUDIO, audio_data)
        except Exception:
            pass

    # Métodos públicos de controle

    def send_mouse_move(self, rel_x: float, rel_y: float):
        """Envia evento de movimento do mouse."""
        data = InputEventSerializer.mouse_move(
            rel_x, rel_y, self.remote_width, self.remote_height
        )
        self._send_raw(MsgType.MOUSE_MOVE, json.dumps(data).encode())

    def send_mouse_click(self, rel_x: float, rel_y: float,
                         button: int = 1, double: bool = False):
        """Envia evento de clique do mouse."""
        data = InputEventSerializer.mouse_click(
            rel_x, rel_y, button, double,
            self.remote_width, self.remote_height
        )
        self._send_raw(MsgType.MOUSE_CLICK, json.dumps(data).encode())

    def send_mouse_down(self, rel_x: float, rel_y: float, button: int = 1):
        data = InputEventSerializer.mouse_down(
            rel_x, rel_y, button, self.remote_width, self.remote_height
        )
        self._send_raw(MsgType.MOUSE_MOVE, json.dumps(data).encode())

    def send_mouse_scroll(self, rel_x: float, rel_y: float, delta: int):
        """Envia evento de scroll do mouse."""
        data = InputEventSerializer.mouse_scroll(
            rel_x, rel_y, delta, self.remote_width, self.remote_height
        )
        self._send_raw(MsgType.MOUSE_SCROLL, json.dumps(data).encode())

    def send_key_event(self, key: str, action: str = 'press'):
        """Envia evento de teclado."""
        data = InputEventSerializer.key_event(key, action)
        self._send_raw(MsgType.KEY_EVENT, json.dumps(data).encode())

    def send_chat(self, message: str):
        """Envia mensagem de chat."""
        data = {'name': self.local_name, 'msg': message}
        self._send_raw(MsgType.CHAT, json.dumps(data).encode())

    def send_clipboard(self, text: str):
        """Envia conteúdo da área de transferência."""
        data = {'text': text}
        self._send_raw(MsgType.CLIPBOARD, json.dumps(data).encode())

    def send_file(self, file_path: str):
        """Envia arquivo para o host."""
        def _send():
            try:
                self._file_sender.send_file(file_path)
            except Exception as e:
                print(f"[Client] Erro enviando arquivo: {e}")
        t = threading.Thread(target=_send, daemon=True)
        t.start()

    def send_files(self, file_paths: list):
        """Envia múltiplos arquivos."""
        def _send():
            try:
                self._file_sender.send_files(file_paths)
            except Exception as e:
                print(f"[Client] Erro enviando arquivos: {e}")
        t = threading.Thread(target=_send, daemon=True)
        t.start()

    def disconnect(self):
        """Encerra a conexão."""
        self.running = False
        try:
            self._send_control({'action': 'disconnect'})
        except Exception:
            pass
        if self._audio_capture:
            self._audio_capture.stop()
        if self._audio_player:
            self._audio_player.stop()
        try:
            self._sock.close()
        except Exception:
            pass
        self.connected = False
