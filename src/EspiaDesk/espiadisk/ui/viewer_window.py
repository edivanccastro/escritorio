"""
EspiaDesk Remote Viewer Window
- Exibe a tela do host remoto
- Captura e envia eventos de mouse/teclado
- Toolbar com controles da sessão
- Escalamento automático da tela
- Suporte a tela cheia
"""
import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import threading
import time
from PIL import Image, ImageTk
import io

from espiadisk.ui import theme
from espiadisk.ui.chat_window import ChatWindow


class ViewerWindow(tk.Toplevel):
    """Janela principal de visualização remota."""

    def __init__(self, parent, client, session_info: dict):
        super().__init__(parent)
        self.client = client
        self.session_info = session_info
        self.remote_name = session_info.get('host_name', 'Remoto')
        self.session_id = session_info.get('session_id', '')

        self.title(f"EspiaDesk — {self.remote_name} [{self.session_id}]")
        self.geometry("1024x700")
        self.minsize(640, 480)
        self.configure(bg=theme.DARK_BG)

        self._fullscreen = False
        self._last_frame: bytes | None = None
        self._tk_image: ImageTk.PhotoImage | None = None
        self._canvas_w = 0
        self._canvas_h = 0
        self._mouse_dragging = False
        self._drag_button = 1
        self._audio_enabled = False
        self._chat_window: ChatWindow | None = None
        self._stats_fps = 0
        self._stats_latency = 0
        self._frames_count = 0
        self._last_fps_time = time.time()
        self._quality_var = tk.IntVar(value=60)
        self._ctrl_pressed = False
        self._alt_pressed = False

        self._build_ui()
        self._bind_events()

        self.protocol("WM_DELETE_WINDOW", self._on_close)
        self._start_stats_timer()

    def _build_ui(self):
        # Toolbar principal
        self._toolbar = tk.Frame(self, bg=theme.PANEL_BG, height=48)
        self._toolbar.pack(fill=tk.X, side=tk.TOP)
        self._toolbar.pack_propagate(False)

        self._build_toolbar()

        # Canvas da tela remota
        canvas_frame = tk.Frame(self, bg="#000000")
        canvas_frame.pack(fill=tk.BOTH, expand=True)

        self._canvas = tk.Canvas(
            canvas_frame,
            bg="#000000",
            cursor="crosshair",
            highlightthickness=0
        )
        self._canvas.pack(fill=tk.BOTH, expand=True)

        # Status bar
        self._statusbar = tk.Frame(self, bg=theme.PANEL_BG, height=28)
        self._statusbar.pack(fill=tk.X, side=tk.BOTTOM)
        self._statusbar.pack_propagate(False)
        self._build_statusbar()

    def _build_toolbar(self):
        tb = self._toolbar

        # Logo / título
        tk.Label(tb, text="👁 EspiaDesk",
                 bg=theme.PANEL_BG, fg=theme.ACCENT,
                 font=("Segoe UI", 11, "bold")).pack(side=tk.LEFT, padx=12)

        tk.Frame(tb, bg=theme.BORDER, width=1).pack(side=tk.LEFT, fill=tk.Y, pady=8)

        # Botões de ação
        btns = [
            ("📁 Arquivos", self._open_file_dialog, theme.PANEL_BG),
            ("💬 Chat", self._toggle_chat, theme.PANEL_BG),
            ("📋 Clipboard", self._sync_clipboard, theme.PANEL_BG),
            ("🎙 Áudio", self._toggle_audio, theme.PANEL_BG),
            ("⛶ Tela Cheia", self._toggle_fullscreen, theme.PANEL_BG),
            ("📸 Screenshot", self._take_screenshot, theme.PANEL_BG),
        ]

        self._toolbar_buttons = {}
        for label, cmd, bg in btns:
            btn = tk.Button(
                tb, text=label,
                bg=bg, fg=theme.TEXT_PRIMARY,
                font=theme.FONT_SMALL,
                relief='flat', bd=0, padx=10, pady=4,
                command=cmd, cursor="hand2",
                activebackground=theme.CARD_BG,
                activeforeground=theme.TEXT_PRIMARY
            )
            btn.pack(side=tk.LEFT, padx=2, pady=6)
            self._toolbar_buttons[label] = btn

        # Separador
        tk.Frame(tb, bg=theme.BORDER, width=1).pack(side=tk.LEFT, fill=tk.Y, pady=8)

        # Controle de qualidade
        tk.Label(tb, text="Qualidade:",
                 bg=theme.PANEL_BG, fg=theme.TEXT_SECONDARY,
                 font=theme.FONT_SMALL).pack(side=tk.LEFT, padx=(8, 2))

        quality_scale = ttk.Scale(
            tb, from_=20, to=95,
            variable=self._quality_var,
            orient=tk.HORIZONTAL, length=80,
            command=self._on_quality_change
        )
        quality_scale.pack(side=tk.LEFT, pady=6)

        self._quality_label = tk.Label(
            tb, text="60%",
            bg=theme.PANEL_BG, fg=theme.TEXT_SECONDARY,
            font=theme.FONT_SMALL
        )
        self._quality_label.pack(side=tk.LEFT, padx=4)

        # Desconectar
        tk.Button(
            tb, text="✕ Desconectar",
            bg=theme.ERROR, fg=theme.TEXT_PRIMARY,
            font=theme.FONT_SMALL,
            relief='flat', bd=0, padx=10, pady=4,
            command=self._on_close, cursor="hand2",
            activebackground="#c0392b",
            activeforeground=theme.TEXT_PRIMARY
        ).pack(side=tk.RIGHT, padx=8, pady=6)

    def _build_statusbar(self):
        sb = self._statusbar

        self._status_conn = tk.Label(
            sb, text="● Conectado",
            bg=theme.PANEL_BG, fg=theme.SUCCESS,
            font=theme.FONT_SMALL
        )
        self._status_conn.pack(side=tk.LEFT, padx=12)

        tk.Label(sb, text=f"Sessão: {self.session_id}",
                 bg=theme.PANEL_BG, fg=theme.TEXT_MUTED,
                 font=theme.FONT_SMALL).pack(side=tk.LEFT, padx=8)

        self._status_fps = tk.Label(
            sb, text="FPS: --",
            bg=theme.PANEL_BG, fg=theme.TEXT_MUTED,
            font=theme.FONT_SMALL
        )
        self._status_fps.pack(side=tk.RIGHT, padx=8)

        self._status_res = tk.Label(
            sb, text=f"{self.client.remote_width}×{self.client.remote_height}",
            bg=theme.PANEL_BG, fg=theme.TEXT_MUTED,
            font=theme.FONT_SMALL
        )
        self._status_res.pack(side=tk.RIGHT, padx=8)

        self._status_bytes = tk.Label(
            sb, text="0 KB recebidos",
            bg=theme.PANEL_BG, fg=theme.TEXT_MUTED,
            font=theme.FONT_SMALL
        )
        self._status_bytes.pack(side=tk.RIGHT, padx=8)

    def _bind_events(self):
        """Vincula eventos de mouse e teclado."""
        c = self._canvas
        c.bind('<Motion>', self._on_mouse_move)
        c.bind('<Button-1>', lambda e: self._on_mouse_btn(e, 1, False))
        c.bind('<Button-2>', lambda e: self._on_mouse_btn(e, 2, False))
        c.bind('<Button-3>', lambda e: self._on_mouse_btn(e, 3, False))
        c.bind('<Double-Button-1>', lambda e: self._on_mouse_btn(e, 1, True))
        c.bind('<ButtonPress-1>', lambda e: self._on_mouse_press(e, 1))
        c.bind('<ButtonRelease-1>', lambda e: self._on_mouse_release(e, 1))
        c.bind('<ButtonPress-3>', lambda e: self._on_mouse_press(e, 3))
        c.bind('<ButtonRelease-3>', lambda e: self._on_mouse_release(e, 3))
        c.bind('<MouseWheel>', self._on_scroll)
        c.bind('<Button-4>', lambda e: self._on_scroll_linux(e, 3))
        c.bind('<Button-5>', lambda e: self._on_scroll_linux(e, -3))

        self.bind('<KeyPress>', self._on_key_press)
        self.bind('<KeyRelease>', self._on_key_release)
        self.bind('<Configure>', self._on_resize)
        self.bind('<F11>', lambda e: self._toggle_fullscreen())
        self.bind('<Escape>', lambda e: self._exit_fullscreen())

        c.focus_set()

    def _get_relative_coords(self, event) -> tuple[float, float]:
        """Converte coordenadas do canvas para relativas (0-1)."""
        cw = self._canvas.winfo_width()
        ch = self._canvas.winfo_height()
        if cw <= 0 or ch <= 0:
            return 0.5, 0.5
        rx = max(0, min(1, event.x / cw))
        ry = max(0, min(1, event.y / ch))
        return rx, ry

    def _on_mouse_move(self, event):
        if not self.client.connected:
            return
        rx, ry = self._get_relative_coords(event)
        self.client.send_mouse_move(rx, ry)

    def _on_mouse_btn(self, event, button: int, double: bool):
        if not self.client.connected:
            return
        rx, ry = self._get_relative_coords(event)
        self.client.send_mouse_click(rx, ry, button, double)

    def _on_mouse_press(self, event, button: int):
        self._mouse_dragging = True
        self._drag_button = button

    def _on_mouse_release(self, event, button: int):
        self._mouse_dragging = False

    def _on_scroll(self, event):
        if not self.client.connected:
            return
        rx, ry = self._get_relative_coords(event)
        delta = event.delta // 120 if event.delta else 1
        self.client.send_mouse_scroll(rx, ry, delta)

    def _on_scroll_linux(self, event, delta: int):
        if not self.client.connected:
            return
        rx, ry = self._get_relative_coords(event)
        self.client.send_mouse_scroll(rx, ry, delta)

    def _on_key_press(self, event):
        if not self.client.connected:
            return
        key = self._map_key(event)
        if key:
            self.client.send_key_event(key, 'down')

    def _on_key_release(self, event):
        if not self.client.connected:
            return
        key = self._map_key(event)
        if key:
            self.client.send_key_event(key, 'up')

    def _map_key(self, event) -> str:
        keysym = event.keysym.lower()
        # Teclas especiais
        special_map = {
            'control_l': 'ctrl', 'control_r': 'ctrl',
            'alt_l': 'alt', 'alt_r': 'alt',
            'shift_l': 'shift', 'shift_r': 'shift',
            'super_l': 'win', 'super_r': 'win',
            'return': 'enter', 'backspace': 'backspace',
            'delete': 'delete', 'insert': 'insert',
            'tab': 'tab', 'escape': 'esc',
            'up': 'up', 'down': 'down',
            'left': 'left', 'right': 'right',
            'home': 'home', 'end': 'end',
            'prior': 'pageup', 'next': 'pagedown',
            'space': 'space', 'caps_lock': 'capslock',
            'f1': 'f1', 'f2': 'f2', 'f3': 'f3', 'f4': 'f4',
            'f5': 'f5', 'f6': 'f6', 'f7': 'f7', 'f8': 'f8',
            'f9': 'f9', 'f10': 'f10', 'f11': 'f11', 'f12': 'f12',
        }
        if keysym in special_map:
            return special_map[keysym]
        if event.char and event.char.isprintable() and len(event.char) == 1:
            return event.char
        return keysym if len(keysym) == 1 else ''

    def _on_resize(self, event):
        if self._last_frame:
            self._render_frame(self._last_frame)

    def on_frame_received(self, frame_bytes: bytes):
        """Chamado quando um novo frame é recebido."""
        self._last_frame = frame_bytes
        self._frames_count += 1
        self.after(0, lambda: self._render_frame(frame_bytes))

    def _render_frame(self, frame_bytes: bytes):
        """Renderiza frame no canvas."""
        try:
            img = Image.open(io.BytesIO(frame_bytes))
            cw = self._canvas.winfo_width()
            ch = self._canvas.winfo_height()
            if cw <= 1 or ch <= 1:
                return

            # Manter proporção
            img_w, img_h = img.size
            scale = min(cw / img_w, ch / img_h)
            new_w = int(img_w * scale)
            new_h = int(img_h * scale)

            img = img.resize((new_w, new_h), Image.BILINEAR)
            self._tk_image = ImageTk.PhotoImage(img)

            x = cw // 2
            y = ch // 2
            self._canvas.delete("all")
            self._canvas.create_image(x, y, anchor=tk.CENTER, image=self._tk_image)
        except Exception:
            pass

    def _start_stats_timer(self):
        """Atualiza estatísticas periodicamente."""
        def update():
            now = time.time()
            elapsed = now - self._last_fps_time
            if elapsed >= 1.0:
                fps = self._frames_count / elapsed
                self._frames_count = 0
                self._last_fps_time = now
                self._status_fps.config(text=f"FPS: {fps:.0f}")
            self.after(1000, update)
        self.after(1000, update)

    def _toggle_chat(self):
        """Abre/fecha janela de chat."""
        if self._chat_window is None:
            self._chat_window = ChatWindow(
                self, self.client.send_chat, self.client.local_name
            )
        self._chat_window.show()

    def on_chat_received(self, sender: str, message: str):
        """Chamado quando chat é recebido."""
        if self._chat_window is None:
            self._chat_window = ChatWindow(
                self, self.client.send_chat, self.client.local_name
            )
        self._chat_window.add_message(sender, message, is_local=False)
        self._chat_window.show()

    def _open_file_dialog(self):
        """Abre diálogo para enviar arquivo."""
        files = filedialog.askopenfilenames(
            title="Selecionar arquivos para enviar",
            parent=self
        )
        if files:
            self.client.send_files(list(files))
            messagebox.showinfo(
                "Transferência iniciada",
                f"{len(files)} arquivo(s) sendo enviado(s)...",
                parent=self
            )

    def _sync_clipboard(self):
        """Sincroniza área de transferência."""
        try:
            text = self.clipboard_get()
            if text:
                self.client.send_clipboard(text)
        except Exception:
            pass

    def on_clipboard_received(self, text: str):
        """Recebe clipboard do host."""
        try:
            self.clipboard_clear()
            self.clipboard_append(text)
        except Exception:
            pass

    def _toggle_audio(self):
        """Liga/desliga áudio."""
        self._audio_enabled = not self._audio_enabled
        btn = self._toolbar_buttons.get("🎙 Áudio")
        if btn:
            if self._audio_enabled:
                btn.config(bg=theme.SUCCESS, text="🎙 Áudio ON")
            else:
                btn.config(bg=theme.PANEL_BG, text="🎙 Áudio")

    def _toggle_fullscreen(self):
        """Alterna tela cheia."""
        self._fullscreen = not self._fullscreen
        self.attributes('-fullscreen', self._fullscreen)
        if self._fullscreen:
            self._toolbar.pack_forget()
            self._statusbar.pack_forget()
            self.bind('<Escape>', lambda e: self._exit_fullscreen())
        else:
            self._toolbar.pack(fill=tk.X, side=tk.TOP, before=self._canvas.master)
            self._statusbar.pack(fill=tk.X, side=tk.BOTTOM)

    def _exit_fullscreen(self):
        if self._fullscreen:
            self._toggle_fullscreen()

    def _take_screenshot(self):
        """Salva screenshot da tela remota."""
        if not self._last_frame:
            return
        path = filedialog.asksaveasfilename(
            title="Salvar screenshot",
            defaultextension=".jpg",
            filetypes=[("JPEG", "*.jpg"), ("PNG", "*.png")],
            parent=self
        )
        if path:
            img = Image.open(io.BytesIO(self._last_frame))
            img.save(path)
            messagebox.showinfo("Screenshot salvo", f"Salvo em:\n{path}", parent=self)

    def _on_quality_change(self, value):
        q = int(float(value))
        self._quality_label.config(text=f"{q}%")

    def _on_close(self):
        """Fecha a janela e desconecta."""
        if messagebox.askyesno(
            "Desconectar",
            "Deseja encerrar a sessão remota?",
            parent=self
        ):
            self.client.disconnect()
            self.destroy()
