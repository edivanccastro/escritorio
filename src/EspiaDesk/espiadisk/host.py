"""
EspiaDesk Host Server
- Aguarda conexões de clientes
- Gerencia autenticação e criptografia
- Transmite tela e recebe inputs
- Gerencia múltiplas sessões
"""
import socket
import threading
import json
import time
import hashlib
from typing import Callable, Optional

from espiadisk.protocol import (
    MsgType, recv_message, send_message, send_json, parse_json
)
from espiadisk.crypto import SessionCrypto, hash_password, verify_password
from espiadisk.screen import ScreenCapture
from espiadisk.input_ctrl import InputController
from espiadisk.audio import AudioCapture, AudioPlayer
from espiadisk.file_transfer import FileReceiver
from espiadisk.session import SessionManager, Session


DEFAULT_PORT = 7070


class ClientHandler(threading.Thread):
    """Gerencia uma conexão de cliente no host."""

    def __init__(self, conn: socket.socket, addr: tuple,
                 password_hash: str,
                 accept_cb: Callable,
                 deny_cb: Callable,
                 screen: ScreenCapture,
                 session_manager: SessionManager,
                 on_disconnect: Callable,
                 audio_enabled: bool = False,
                 download_dir: str = "."):
        super().__init__(daemon=True)
        self.conn = conn
        self.addr = addr
        self.password_hash = password_hash
        self.accept_cb = accept_cb
        self.deny_cb = deny_cb
        self.screen = screen
        self.session_manager = session_manager
        self.on_disconnect = on_disconnect
        self.audio_enabled = audio_enabled
        self.download_dir = download_dir

        self.crypto = SessionCrypto()
        self.session: Optional[Session] = None
        self.running = False
        self._input: Optional[InputController] = None
        self._file_receiver: Optional[FileReceiver] = None
        self._audio_player: Optional[AudioPlayer] = None
        self._audio_capture: Optional[AudioCapture] = None

    def run(self):
        try:
            self._handshake()
        except Exception as e:
            print(f"[Host] Erro no handshake com {self.addr}: {e}")
            self._cleanup()
            return

        if not self.running:
            return

        try:
            self._receive_loop()
        except Exception as e:
            print(f"[Host] Erro na sessão com {self.addr}: {e}")
        finally:
            self._cleanup()

    def _handshake(self):
        """Realiza handshake de autenticação e criptografia."""
        # 1. Troca de chaves RSA
        my_pub = self.crypto.get_public_key_bytes()
        send_message(self.conn, MsgType.AUTH_REQ, my_pub)

        msg_type, payload = recv_message(self.conn)
        if msg_type != MsgType.AUTH_REQ:
            raise ValueError("Esperado AUTH_REQ do cliente")

        client_pub_bytes = payload
        encrypted_session_key = self.crypto.establish_as_server(client_pub_bytes)
        send_message(self.conn, MsgType.AUTH_RESP, encrypted_session_key)

        # 2. Receber credenciais do cliente
        msg_type, payload = recv_message(self.conn)
        if msg_type != MsgType.AUTH_REQ:
            raise ValueError("Esperado AUTH_REQ com credenciais")

        creds_encrypted = payload
        creds_bytes = self.crypto.decrypt(creds_encrypted)
        creds = json.loads(creds_bytes.decode())

        client_name = creds.get('name', 'Desconhecido')
        client_password = creds.get('password', '')

        # 3. Verificar senha
        if self.password_hash and not verify_password(client_password, self.password_hash):
            send_json(self.conn, MsgType.AUTH_RESP,
                      {'ok': False, 'reason': 'Senha incorreta'})
            return

        # 4. Pedir confirmação ao usuário (accept_cb)
        accepted = self.accept_cb(client_name, self.addr[0])
        if not accepted:
            send_json(self.conn, MsgType.AUTH_RESP,
                      {'ok': False, 'reason': 'Conexão recusada pelo host'})
            return

        # 5. Criar sessão
        import uuid
        session_id = str(uuid.uuid4())[:8].upper()
        self.session = self.session_manager.create_session(
            session_id, self.addr[0], client_name
        )

        # Enviar info da tela
        screen_info = self.screen.get_screen_info()
        auth_ok = {
            'ok': True,
            'session_id': session_id,
            'screen_width': screen_info['width'],
            'screen_height': screen_info['height'],
            'host_name': socket.gethostname()
        }
        auth_payload = self.crypto.encrypt(json.dumps(auth_ok).encode())
        send_message(self.conn, MsgType.AUTH_RESP, auth_payload)

        # Inicializar componentes
        self._input = InputController(screen_info['width'], screen_info['height'])
        self._file_receiver = FileReceiver(
            save_dir=self.download_dir,
            progress_cb=self._on_file_progress,
            complete_cb=self._on_file_complete
        )

        if self.audio_enabled:
            self._audio_player = AudioPlayer()
            self._audio_player.start()
            self._audio_capture = AudioCapture(self._send_audio)
            self._audio_capture.start()

        self.running = True
        print(f"[Host] Sessão {session_id} iniciada com {client_name} ({self.addr[0]})")

        # Iniciar envio de tela
        self._start_screen_stream()

    def _start_screen_stream(self):
        """Inicia thread de envio de frames da tela."""
        def stream():
            while self.running:
                try:
                    frame = self.screen.capture_frame()
                    encrypted = self.crypto.encrypt(frame)
                    send_message(self.conn, MsgType.SCREEN_FRAME, encrypted)
                    if self.session:
                        self.session.bytes_sent += len(encrypted)
                        self.session.frames_sent += 1
                    time.sleep(1 / self.screen.fps)
                except Exception:
                    break

        t = threading.Thread(target=stream, daemon=True)
        t.start()

    def _receive_loop(self):
        """Loop principal de recebimento de mensagens do cliente."""
        while self.running:
            try:
                msg_type, payload = recv_message(self.conn)

                if self.session:
                    self.session.bytes_received += len(payload)

                if msg_type == MsgType.MOUSE_MOVE:
                    data = parse_json(self.crypto.decrypt(payload))
                    self._input.mouse_move(data['x'], data['y'],
                                           data.get('rw', 1920),
                                           data.get('rh', 1080))

                elif msg_type == MsgType.MOUSE_CLICK:
                    data = parse_json(self.crypto.decrypt(payload))
                    self._input.mouse_click(
                        data['x'], data['y'],
                        data.get('btn', 1),
                        data.get('dbl', False),
                        data.get('rw', 1920),
                        data.get('rh', 1080)
                    )

                elif msg_type == MsgType.MOUSE_SCROLL:
                    data = parse_json(self.crypto.decrypt(payload))
                    self._input.mouse_scroll(
                        data['x'], data['y'],
                        data.get('delta', 3),
                        data.get('rw', 1920),
                        data.get('rh', 1080)
                    )

                elif msg_type == MsgType.KEY_EVENT:
                    data = parse_json(self.crypto.decrypt(payload))
                    action = data.get('action', 'press')
                    key = data.get('key', '')
                    if action == 'down':
                        self._input.key_down(key)
                    elif action == 'up':
                        self._input.key_up(key)
                    else:
                        self._input.key_press(key)

                elif msg_type == MsgType.CLIPBOARD:
                    data = parse_json(self.crypto.decrypt(payload))
                    self._set_clipboard(data.get('text', ''))

                elif msg_type == MsgType.CHAT:
                    data = parse_json(self.crypto.decrypt(payload))
                    print(f"[Chat] {data.get('name')}: {data.get('msg')}")

                elif msg_type == MsgType.FILE_START:
                    dec = self.crypto.decrypt(payload)
                    self._file_receiver.on_file_start(dec)

                elif msg_type == MsgType.FILE_CHUNK:
                    dec = self.crypto.decrypt(payload)
                    self._file_receiver.on_file_chunk(dec)

                elif msg_type == MsgType.FILE_END:
                    dec = self.crypto.decrypt(payload)
                    self._file_receiver.on_file_end(dec)

                elif msg_type == MsgType.AUDIO:
                    if self._audio_player:
                        self._audio_player.play(self.crypto.decrypt(payload))

                elif msg_type == MsgType.CONTROL:
                    data = parse_json(self.crypto.decrypt(payload))
                    if data.get('action') == 'disconnect':
                        break
                    elif data.get('action') == 'heartbeat':
                        send_json(self.conn, MsgType.HEARTBEAT, {'ok': True})

            except Exception as e:
                if self.running:
                    print(f"[Host] Erro recebendo: {e}")
                break

        self.running = False

    def _send_audio(self, audio_data: bytes):
        """Envia áudio comprimido ao cliente."""
        try:
            encrypted = self.crypto.encrypt(audio_data)
            send_message(self.conn, MsgType.AUDIO, encrypted)
        except Exception:
            pass

    def _set_clipboard(self, text: str):
        """Define conteúdo da área de transferência."""
        try:
            import tkinter as tk
            root = tk.Tk()
            root.withdraw()
            root.clipboard_clear()
            root.clipboard_append(text)
            root.after(100, root.destroy)
            root.mainloop()
        except Exception:
            pass

    def _on_file_progress(self, name, received, total):
        pct = int(received / total * 100) if total else 0
        print(f"[Host] Recebendo {name}: {pct}%")

    def _on_file_complete(self, name, dest_path, success, size):
        status = "OK" if success else "FALHOU (MD5 inválido)"
        print(f"[Host] Arquivo recebido: {name} -> {dest_path} [{status}]")

    def _cleanup(self):
        """Encerra recursos da sessão."""
        self.running = False
        if self._input:
            self._input.release_all()
        if self._audio_capture:
            self._audio_capture.stop()
        if self._audio_player:
            self._audio_player.stop()
        if self.session:
            self.session_manager.end_session(self.session.session_id)
        try:
            self.conn.close()
        except Exception:
            pass
        self.on_disconnect(self)

    def disconnect(self):
        """Desconecta o cliente."""
        self.running = False
        try:
            send_json(self.conn, MsgType.CONTROL, {'action': 'disconnect'})
        except Exception:
            pass
        self.conn.close()

    def send_chat(self, message: str, sender: str = "Host"):
        """Envia mensagem de chat ao cliente."""
        try:
            payload = json.dumps({'name': sender, 'msg': message}).encode()
            encrypted = self.crypto.encrypt(payload)
            send_message(self.conn, MsgType.CHAT, encrypted)
        except Exception:
            pass

    def send_clipboard(self, text: str):
        """Envia conteúdo da área de transferência."""
        try:
            payload = json.dumps({'text': text}).encode()
            encrypted = self.crypto.encrypt(payload)
            send_message(self.conn, MsgType.CLIPBOARD, encrypted)
        except Exception:
            pass


class HostServer:
    """Servidor principal do EspiaDesk."""

    def __init__(self,
                 port: int = DEFAULT_PORT,
                 password: str = "",
                 quality: int = 60,
                 fps: int = 20,
                 audio_enabled: bool = False,
                 download_dir: str = "."):
        self.port = port
        self.password_hash = hash_password(password) if password else ""
        self.quality = quality
        self.fps = fps
        self.audio_enabled = audio_enabled
        self.download_dir = download_dir

        self.running = False
        self._server_sock: Optional[socket.socket] = None
        self._thread: Optional[threading.Thread] = None
        self._clients: list[ClientHandler] = []
        self._clients_lock = threading.Lock()
        self._session_manager = SessionManager()
        self._screen = ScreenCapture(quality=quality, fps=fps)

        # Callbacks configuráveis
        self.on_accept_request: Callable = lambda name, ip: True
        self.on_client_connected: Callable = lambda handler: None
        self.on_client_disconnected: Callable = lambda handler: None
        self.on_chat_received: Callable = lambda name, msg: None

    def start(self):
        """Inicia o servidor."""
        self._server_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._server_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._server_sock.bind(('0.0.0.0', self.port))
        self._server_sock.listen(5)
        self._server_sock.settimeout(1.0)
        self.running = True
        self._thread = threading.Thread(target=self._accept_loop, daemon=True)
        self._thread.start()
        print(f"[Host] Servidor iniciado na porta {self.port}")

    def _accept_loop(self):
        """Loop de aceite de conexões."""
        while self.running:
            try:
                conn, addr = self._server_sock.accept()
                print(f"[Host] Nova conexão de {addr[0]}:{addr[1]}")
                handler = ClientHandler(
                    conn=conn,
                    addr=addr,
                    password_hash=self.password_hash,
                    accept_cb=self.on_accept_request,
                    deny_cb=lambda n, ip: None,
                    screen=self._screen,
                    session_manager=self._session_manager,
                    on_disconnect=self._on_client_disconnect,
                    audio_enabled=self.audio_enabled,
                    download_dir=self.download_dir
                )
                with self._clients_lock:
                    self._clients.append(handler)
                handler.start()
                self.on_client_connected(handler)
            except socket.timeout:
                continue
            except Exception as e:
                if self.running:
                    print(f"[Host] Erro no accept: {e}")

    def _on_client_disconnect(self, handler: ClientHandler):
        with self._clients_lock:
            if handler in self._clients:
                self._clients.remove(handler)
        self.on_client_disconnected(handler)

    def stop(self):
        """Para o servidor."""
        self.running = False
        with self._clients_lock:
            for client in list(self._clients):
                client.disconnect()
        if self._server_sock:
            self._server_sock.close()
        self._session_manager.end_all()
        print("[Host] Servidor parado")

    def set_password(self, password: str):
        """Define/atualiza a senha de acesso."""
        from espiadisk.crypto import hash_password
        self.password_hash = hash_password(password) if password else ""

    def set_quality(self, quality: int):
        self._screen.set_quality(quality)

    def set_fps(self, fps: int):
        self._screen.set_fps(fps)

    def broadcast_chat(self, message: str, sender: str = "Host"):
        """Envia chat para todos os clientes."""
        with self._clients_lock:
            for client in self._clients:
                client.send_chat(message, sender)

    def broadcast_clipboard(self, text: str):
        """Envia clipboard para todos os clientes."""
        with self._clients_lock:
            for client in self._clients:
                client.send_clipboard(text)

    def disconnect_client(self, handler: ClientHandler):
        handler.disconnect()

    @property
    def active_sessions(self):
        return self._session_manager.active_sessions

    @property
    def active_count(self) -> int:
        return self._session_manager.active_count

    def get_local_ip(self) -> str:
        """Obtém IP local da máquina."""
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            s.connect(("8.8.8.8", 80))
            ip = s.getsockname()[0]
            s.close()
            return ip
        except Exception:
            return "127.0.0.1"
