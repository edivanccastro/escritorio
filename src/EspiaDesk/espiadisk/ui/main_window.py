"""
EspiaDesk Main Window
- Tela principal com ID da máquina
- Painel de conexão remota
- Gerenciamento de sessões ativas
- Configurações do sistema
- Histórico de conexões
"""
import tkinter as tk
from tkinter import ttk, messagebox, simpledialog
import threading
import socket
import time
import os
import json

from espiadisk.ui import theme
from espiadisk.crypto import generate_session_id
from espiadisk.host import HostServer
from espiadisk.client import RemoteClient


CONFIG_FILE = os.path.join(os.path.expanduser("~"), ".espiadisk_config.json")


class MainWindow:
    """Janela principal do EspiaDesk."""

    def __init__(self):
        self.root = tk.Tk()
        self.root.title("EspiaDesk — Acesso Remoto")
        self.root.geometry("860x600")
        self.root.minsize(760, 540)
        self.root.resizable(True, True)

        theme.apply_theme(self.root)

        self._config = self._load_config()
        self._my_id = generate_session_id()
        self._host_server: HostServer | None = None
        self._host_running = False
        self._active_viewers = []
        self._history = []
        self._pending_request = None

        self._build_ui()
        self._start_host()
        self._update_status()

        self.root.protocol("WM_DELETE_WINDOW", self._on_exit)

    def _load_config(self) -> dict:
        try:
            with open(CONFIG_FILE, 'r') as f:
                return json.load(f)
        except Exception:
            return {
                'password': '',
                'quality': 60,
                'fps': 20,
                'audio': False,
                'your_name': socket.gethostname(),
                'port': 7070,
                'download_dir': os.path.expanduser("~/Downloads"),
                'history': []
            }

    def _save_config(self):
        try:
            self._config['history'] = self._history[-20:]
            with open(CONFIG_FILE, 'w') as f:
                json.dump(self._config, f, indent=2)
        except Exception:
            pass

    def _build_ui(self):
        # Header
        self._build_header()

        # Main content
        content = tk.Frame(self.root, bg=theme.DARK_BG)
        content.pack(fill=tk.BOTH, expand=True, padx=0, pady=0)

        # Notebook com abas
        self._nb = ttk.Notebook(content)
        self._nb.pack(fill=tk.BOTH, expand=True, padx=12, pady=12)

        # Aba: Acesso Remoto
        tab_main = ttk.Frame(self._nb)
        self._nb.add(tab_main, text="  🖥️  Acesso Remoto  ")
        self._build_main_tab(tab_main)

        # Aba: Sessões Ativas
        tab_sessions = ttk.Frame(self._nb)
        self._nb.add(tab_sessions, text="  📡  Sessões  ")
        self._build_sessions_tab(tab_sessions)

        # Aba: Histórico
        tab_history = ttk.Frame(self._nb)
        self._nb.add(tab_history, text="  📜  Histórico  ")
        self._build_history_tab(tab_history)

        # Aba: Configurações
        tab_settings = ttk.Frame(self._nb)
        self._nb.add(tab_settings, text="  ⚙️  Configurações  ")
        self._build_settings_tab(tab_settings)

    def _build_header(self):
        hdr = tk.Frame(self.root, bg=theme.PANEL_BG, height=56)
        hdr.pack(fill=tk.X)
        hdr.pack_propagate(False)

        # Logo
        logo_frame = tk.Frame(hdr, bg=theme.PANEL_BG)
        logo_frame.pack(side=tk.LEFT, padx=16)

        tk.Label(
            logo_frame, text="👁",
            bg=theme.PANEL_BG, fg=theme.ACCENT,
            font=("Segoe UI", 20)
        ).pack(side=tk.LEFT)
        tk.Label(
            logo_frame, text=" EspiaDesk",
            bg=theme.PANEL_BG, fg=theme.TEXT_PRIMARY,
            font=("Segoe UI", 16, "bold")
        ).pack(side=tk.LEFT)
        tk.Label(
            logo_frame, text=" v1.0",
            bg=theme.PANEL_BG, fg=theme.TEXT_MUTED,
            font=theme.FONT_SMALL
        ).pack(side=tk.LEFT, pady=(8, 0))

        # Status do host
        right_frame = tk.Frame(hdr, bg=theme.PANEL_BG)
        right_frame.pack(side=tk.RIGHT, padx=16)

        self._header_status = tk.Label(
            right_frame, text="● Pronto",
            bg=theme.PANEL_BG, fg=theme.SUCCESS,
            font=theme.FONT_SMALL
        )
        self._header_status.pack()

        self._header_ip = tk.Label(
            right_frame, text="IP: ...",
            bg=theme.PANEL_BG, fg=theme.TEXT_MUTED,
            font=theme.FONT_SMALL
        )
        self._header_ip.pack()

    def _build_main_tab(self, parent):
        outer = tk.Frame(parent, bg=theme.DARK_BG)
        outer.pack(fill=tk.BOTH, expand=True)

        left = tk.Frame(outer, bg=theme.DARK_BG)
        left.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(0, 6))

        right = tk.Frame(outer, bg=theme.DARK_BG)
        right.pack(side=tk.RIGHT, fill=tk.BOTH, expand=True, padx=(6, 0))

        self._build_my_id_card(left)
        self._build_connect_card(right)

    def _build_my_id_card(self, parent):
        card = tk.Frame(parent, bg=theme.CARD_BG, bd=0)
        card.pack(fill=tk.BOTH, expand=True, pady=(0, 6))

        tk.Label(card, text="Seu ID de Acesso",
                 bg=theme.CARD_BG, fg=theme.TEXT_SECONDARY,
                 font=theme.FONT_HEADING).pack(pady=(20, 4))
        tk.Label(card, text="Compartilhe este ID para permitir acesso",
                 bg=theme.CARD_BG, fg=theme.TEXT_MUTED,
                 font=theme.FONT_SMALL).pack()

        id_frame = tk.Frame(card, bg=theme.INPUT_BG, pady=12, padx=20)
        id_frame.pack(padx=20, pady=12, fill=tk.X)

        formatted_id = f"{self._my_id[:3]} {self._my_id[3:6]} {self._my_id[6:]}"
        self._id_label = tk.Label(
            id_frame, text=formatted_id,
            bg=theme.INPUT_BG, fg=theme.ACCENT,
            font=theme.FONT_ID,
            cursor="hand2"
        )
        self._id_label.pack()
        self._id_label.bind('<Button-1>', lambda e: self._copy_id())

        tk.Label(id_frame, text="Clique para copiar",
                 bg=theme.INPUT_BG, fg=theme.TEXT_MUTED,
                 font=theme.FONT_SMALL).pack()

        # Senha de acesso
        pwd_frame = tk.Frame(card, bg=theme.CARD_BG)
        pwd_frame.pack(fill=tk.X, padx=20, pady=(0, 8))

        tk.Label(pwd_frame, text="🔒 Senha:",
                 bg=theme.CARD_BG, fg=theme.TEXT_SECONDARY,
                 font=theme.FONT_NORMAL).pack(side=tk.LEFT)

        self._pwd_var = tk.StringVar(value=self._config.get('password', ''))
        pwd_entry = tk.Entry(
            pwd_frame,
            textvariable=self._pwd_var,
            show='●',
            bg=theme.INPUT_BG, fg=theme.TEXT_PRIMARY,
            insertbackground=theme.TEXT_PRIMARY,
            font=theme.FONT_MONO,
            relief='flat', bd=6, width=14
        )
        pwd_entry.pack(side=tk.LEFT, padx=8)

        tk.Button(
            pwd_frame, text="Aplicar",
            bg=theme.ACCENT, fg=theme.TEXT_PRIMARY,
            font=theme.FONT_SMALL, relief='flat', bd=0, padx=8, pady=3,
            command=self._apply_password,
            cursor="hand2",
            activebackground=theme.ACCENT_HOVER
        ).pack(side=tk.LEFT)

        # Status do servidor
        srv_frame = tk.Frame(card, bg=theme.CARD_BG)
        srv_frame.pack(fill=tk.X, padx=20, pady=(0, 16))

        self._srv_status = tk.Label(
            srv_frame, text="● Servidor: Iniciando...",
            bg=theme.CARD_BG, fg=theme.WARNING,
            font=theme.FONT_SMALL
        )
        self._srv_status.pack(side=tk.LEFT)

        self._sessions_count = tk.Label(
            srv_frame, text="0 sessões",
            bg=theme.CARD_BG, fg=theme.TEXT_MUTED,
            font=theme.FONT_SMALL
        )
        self._sessions_count.pack(side=tk.RIGHT)

        # Botões
        btn_frame = tk.Frame(card, bg=theme.CARD_BG)
        btn_frame.pack(fill=tk.X, padx=20, pady=(0, 20))

        tk.Button(
            btn_frame, text="🔄 Novo ID",
            bg=theme.PANEL_BG, fg=theme.TEXT_PRIMARY,
            font=theme.FONT_SMALL, relief='flat', bd=0, padx=10, pady=6,
            command=self._regenerate_id,
            cursor="hand2",
            activebackground=theme.CARD_BG
        ).pack(side=tk.LEFT, padx=(0, 4))

        self._srv_btn = tk.Button(
            btn_frame, text="⏸ Pausar Servidor",
            bg=theme.PANEL_BG, fg=theme.TEXT_PRIMARY,
            font=theme.FONT_SMALL, relief='flat', bd=0, padx=10, pady=6,
            command=self._toggle_server,
            cursor="hand2",
            activebackground=theme.CARD_BG
        )
        self._srv_btn.pack(side=tk.LEFT)

    def _build_connect_card(self, parent):
        card = tk.Frame(parent, bg=theme.CARD_BG)
        card.pack(fill=tk.BOTH, expand=True, pady=(0, 6))

        tk.Label(card, text="Conectar a Computador Remoto",
                 bg=theme.CARD_BG, fg=theme.TEXT_SECONDARY,
                 font=theme.FONT_HEADING).pack(pady=(20, 4))
        tk.Label(card, text="Digite o ID ou endereço IP do host",
                 bg=theme.CARD_BG, fg=theme.TEXT_MUTED,
                 font=theme.FONT_SMALL).pack()

        # Campo de entrada
        entry_frame = tk.Frame(card, bg=theme.CARD_BG)
        entry_frame.pack(fill=tk.X, padx=24, pady=16)

        tk.Label(entry_frame, text="ID / IP:",
                 bg=theme.CARD_BG, fg=theme.TEXT_SECONDARY,
                 font=theme.FONT_NORMAL).pack(anchor=tk.W)

        self._remote_id_var = tk.StringVar()
        id_entry = tk.Entry(
            entry_frame,
            textvariable=self._remote_id_var,
            bg=theme.INPUT_BG, fg=theme.TEXT_PRIMARY,
            insertbackground=theme.TEXT_PRIMARY,
            font=("Segoe UI", 14),
            relief='flat', bd=8,
            width=22
        )
        id_entry.pack(fill=tk.X, pady=(4, 0))
        id_entry.bind('<Return>', lambda e: self._connect())

        # Senha remota
        tk.Label(entry_frame, text="Senha (opcional):",
                 bg=theme.CARD_BG, fg=theme.TEXT_SECONDARY,
                 font=theme.FONT_NORMAL).pack(anchor=tk.W, pady=(12, 0))

        self._remote_pwd_var = tk.StringVar()
        tk.Entry(
            entry_frame,
            textvariable=self._remote_pwd_var,
            show='●',
            bg=theme.INPUT_BG, fg=theme.TEXT_PRIMARY,
            insertbackground=theme.TEXT_PRIMARY,
            font=theme.FONT_MONO,
            relief='flat', bd=8,
            width=22
        ).pack(fill=tk.X, pady=(4, 0))

        # Opções
        opts_frame = tk.Frame(card, bg=theme.CARD_BG)
        opts_frame.pack(fill=tk.X, padx=24)

        self._view_only_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(
            opts_frame, text="Somente visualização",
            variable=self._view_only_var
        ).pack(anchor=tk.W)

        self._audio_var = tk.BooleanVar(value=self._config.get('audio', False))
        ttk.Checkbutton(
            opts_frame, text="Transmitir áudio",
            variable=self._audio_var
        ).pack(anchor=tk.W)

        # Botão conectar
        self._connect_btn = tk.Button(
            card, text="▶  CONECTAR",
            bg=theme.ACCENT, fg=theme.TEXT_PRIMARY,
            font=("Segoe UI", 13, "bold"),
            relief='flat', bd=0, pady=12,
            command=self._connect,
            cursor="hand2",
            activebackground=theme.ACCENT_HOVER
        )
        self._connect_btn.pack(fill=tk.X, padx=24, pady=16)

        # Histórico rápido
        tk.Label(card, text="Recentes:",
                 bg=theme.CARD_BG, fg=theme.TEXT_MUTED,
                 font=theme.FONT_SMALL).pack(padx=24, anchor=tk.W)

        self._recent_frame = tk.Frame(card, bg=theme.CARD_BG)
        self._recent_frame.pack(fill=tk.X, padx=24, pady=(4, 16))
        self._refresh_recent()

    def _build_sessions_tab(self, parent):
        frame = tk.Frame(parent, bg=theme.DARK_BG)
        frame.pack(fill=tk.BOTH, expand=True)

        tk.Label(frame, text="Sessões Ativas",
                 bg=theme.DARK_BG, fg=theme.TEXT_PRIMARY,
                 font=theme.FONT_HEADING).pack(pady=(12, 4))

        # Lista de sessões
        cols = ('ID', 'Usuário', 'IP', 'Duração', 'Dados', 'Ação')
        self._sessions_tree = ttk.Treeview(
            frame, columns=cols, show='headings', height=8
        )
        for col in cols:
            self._sessions_tree.heading(col, text=col)
            self._sessions_tree.column(col, width=100)
        self._sessions_tree.column('ID', width=80)
        self._sessions_tree.column('Usuário', width=140)
        self._sessions_tree.column('IP', width=120)
        self._sessions_tree.column('Ação', width=80)
        self._sessions_tree.pack(fill=tk.BOTH, expand=True, padx=12, pady=8)

        btn_row = tk.Frame(frame, bg=theme.DARK_BG)
        btn_row.pack(fill=tk.X, padx=12, pady=4)

        tk.Button(
            btn_row, text="🔄 Atualizar",
            bg=theme.PANEL_BG, fg=theme.TEXT_PRIMARY,
            font=theme.FONT_SMALL, relief='flat', bd=0, padx=10, pady=6,
            command=self._refresh_sessions,
            cursor="hand2"
        ).pack(side=tk.LEFT, padx=2)

        tk.Button(
            btn_row, text="✕ Desconectar Selecionado",
            bg=theme.ERROR, fg=theme.TEXT_PRIMARY,
            font=theme.FONT_SMALL, relief='flat', bd=0, padx=10, pady=6,
            command=self._disconnect_selected,
            cursor="hand2"
        ).pack(side=tk.LEFT, padx=2)

        self._auto_refresh_sessions()

    def _build_history_tab(self, parent):
        frame = tk.Frame(parent, bg=theme.DARK_BG)
        frame.pack(fill=tk.BOTH, expand=True)

        tk.Label(frame, text="Histórico de Conexões",
                 bg=theme.DARK_BG, fg=theme.TEXT_PRIMARY,
                 font=theme.FONT_HEADING).pack(pady=(12, 4))

        cols = ('Data', 'Host', 'Duração', 'Dados')
        self._history_tree = ttk.Treeview(
            frame, columns=cols, show='headings', height=12
        )
        for col in cols:
            self._history_tree.heading(col, text=col)
            self._history_tree.column(col, width=160)

        scrollbar = ttk.Scrollbar(frame, orient=tk.VERTICAL,
                                  command=self._history_tree.yview)
        self._history_tree.configure(yscroll=scrollbar.set)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y, padx=(0, 12))
        self._history_tree.pack(fill=tk.BOTH, expand=True, padx=(12, 0), pady=8)

        btn_row = tk.Frame(frame, bg=theme.DARK_BG)
        btn_row.pack(fill=tk.X, padx=12)

        tk.Button(
            btn_row, text="🗑 Limpar Histórico",
            bg=theme.PANEL_BG, fg=theme.TEXT_PRIMARY,
            font=theme.FONT_SMALL, relief='flat', bd=0, padx=10, pady=6,
            command=self._clear_history,
            cursor="hand2"
        ).pack(side=tk.LEFT)

        tk.Button(
            btn_row, text="▶ Reconectar",
            bg=theme.ACCENT, fg=theme.TEXT_PRIMARY,
            font=theme.FONT_SMALL, relief='flat', bd=0, padx=10, pady=6,
            command=self._reconnect_history,
            cursor="hand2"
        ).pack(side=tk.LEFT, padx=4)

        self._refresh_history()

    def _build_settings_tab(self, parent):
        frame = tk.Frame(parent, bg=theme.DARK_BG)
        frame.pack(fill=tk.BOTH, expand=True)

        canvas = tk.Canvas(frame, bg=theme.DARK_BG, highlightthickness=0)
        scrollbar = ttk.Scrollbar(frame, orient=tk.VERTICAL, command=canvas.yview)
        scroll_frame = tk.Frame(canvas, bg=theme.DARK_BG)
        scroll_frame.bind('<Configure>',
                          lambda e: canvas.configure(scrollregion=canvas.bbox("all")))
        canvas.create_window((0, 0), window=scroll_frame, anchor=tk.NW)
        canvas.configure(yscrollcommand=scrollbar.set)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)
        canvas.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

        def section(title):
            f = tk.Frame(scroll_frame, bg=theme.CARD_BG)
            f.pack(fill=tk.X, padx=12, pady=6)
            tk.Label(f, text=title,
                     bg=theme.CARD_BG, fg=theme.ACCENT,
                     font=theme.FONT_HEADING).pack(anchor=tk.W, padx=12, pady=(10, 4))
            return f

        def row(parent, label, widget_factory):
            r = tk.Frame(parent, bg=theme.CARD_BG)
            r.pack(fill=tk.X, padx=12, pady=4)
            tk.Label(r, text=label, width=24, anchor=tk.W,
                     bg=theme.CARD_BG, fg=theme.TEXT_SECONDARY,
                     font=theme.FONT_NORMAL).pack(side=tk.LEFT)
            widget_factory(r).pack(side=tk.LEFT, fill=tk.X, expand=True)
            return r

        # Seção: Identidade
        sec1 = section("👤 Identidade")
        self._name_var = tk.StringVar(value=self._config.get('your_name', ''))
        row(sec1, "Nome exibido:", lambda p: tk.Entry(
            p, textvariable=self._name_var,
            bg=theme.INPUT_BG, fg=theme.TEXT_PRIMARY,
            insertbackground=theme.TEXT_PRIMARY,
            font=theme.FONT_NORMAL, relief='flat', bd=6
        ))

        # Seção: Servidor
        sec2 = section("🌐 Servidor")
        self._port_var = tk.IntVar(value=self._config.get('port', 7070))
        row(sec2, "Porta TCP:", lambda p: tk.Entry(
            p, textvariable=self._port_var,
            bg=theme.INPUT_BG, fg=theme.TEXT_PRIMARY,
            insertbackground=theme.TEXT_PRIMARY,
            font=theme.FONT_NORMAL, relief='flat', bd=6, width=8
        ))

        # Seção: Vídeo
        sec3 = section("🎥 Vídeo & Performance")
        self._quality_set_var = tk.IntVar(value=self._config.get('quality', 60))
        row(sec3, f"Qualidade JPEG (%):",
            lambda p: ttk.Scale(p, from_=20, to=95,
                                variable=self._quality_set_var, orient=tk.HORIZONTAL))

        self._fps_var = tk.IntVar(value=self._config.get('fps', 20))
        row(sec3, "FPS máximo:",
            lambda p: ttk.Scale(p, from_=5, to=60,
                                variable=self._fps_var, orient=tk.HORIZONTAL))

        # Seção: Pastas
        sec4 = section("📁 Pastas")
        self._dl_dir_var = tk.StringVar(
            value=self._config.get('download_dir', os.path.expanduser("~/Downloads"))
        )
        row(sec4, "Pasta de downloads:", lambda p: tk.Entry(
            p, textvariable=self._dl_dir_var,
            bg=theme.INPUT_BG, fg=theme.TEXT_PRIMARY,
            insertbackground=theme.TEXT_PRIMARY,
            font=theme.FONT_NORMAL, relief='flat', bd=6
        ))

        # Botão salvar
        tk.Button(
            scroll_frame, text="💾 Salvar Configurações",
            bg=theme.ACCENT, fg=theme.TEXT_PRIMARY,
            font=theme.FONT_NORMAL, relief='flat', bd=0, padx=20, pady=10,
            command=self._save_settings,
            cursor="hand2"
        ).pack(pady=16)

    def _start_host(self):
        """Inicia o servidor host."""
        port = self._config.get('port', 7070)
        password = self._config.get('password', '')
        quality = self._config.get('quality', 60)
        fps = self._config.get('fps', 20)

        self._host_server = HostServer(
            port=port,
            password=password,
            quality=quality,
            fps=fps,
            download_dir=self._config.get('download_dir', '.')
        )
        self._host_server.on_accept_request = self._on_accept_request
        self._host_server.on_client_connected = self._on_client_connected
        self._host_server.on_client_disconnected = self._on_client_disconnected

        try:
            self._host_server.start()
            self._host_running = True
            ip = self._host_server.get_local_ip()
            self.root.after(0, lambda: self._header_ip.config(text=f"IP: {ip}"))
            self.root.after(0, lambda: self._srv_status.config(
                text=f"● Servidor ativo — porta {port}", fg=theme.SUCCESS))
        except Exception as e:
            self.root.after(0, lambda: self._srv_status.config(
                text=f"✗ Erro: {e}", fg=theme.ERROR))

    def _on_accept_request(self, client_name: str, client_ip: str) -> bool:
        """Exibe diálogo de aceite de conexão (thread-safe via root.after)."""
        result = [None]
        event = threading.Event()

        def show_dialog():
            result[0] = messagebox.askyesno(
                "Solicitação de Acesso — EspiaDesk",
                f"'{client_name}' ({client_ip}) quer se conectar.\n\n"
                f"Deseja permitir o acesso?",
                icon='question'
            )
            event.set()

        self.root.after(0, show_dialog)
        event.wait(timeout=30)
        return bool(result[0])

    def _on_client_connected(self, handler):
        self.root.after(0, self._refresh_sessions)

    def _on_client_disconnected(self, handler):
        self.root.after(0, self._refresh_sessions)

    def _connect(self):
        """Inicia conexão com host remoto."""
        target = self._remote_id_var.get().strip().replace(' ', '')
        if not target:
            messagebox.showwarning("Atenção", "Digite o ID ou IP do host remoto.")
            return

        # ID de 9 dígitos ou IP
        host = target
        port = self._config.get('port', 7070)

        password = self._remote_pwd_var.get()
        audio = self._audio_var.get()
        dl_dir = self._config.get('download_dir', '.')

        self._connect_btn.config(
            text="⏳ Conectando...", state=tk.DISABLED, bg=theme.TEXT_MUTED
        )

        def do_connect():
            try:
                viewer_ref = [None]

                def on_frame(frame_bytes):
                    if viewer_ref[0]:
                        viewer_ref[0].on_frame_received(frame_bytes)

                def on_chat(sender, msg):
                    if viewer_ref[0]:
                        self.root.after(0, lambda: viewer_ref[0].on_chat_received(sender, msg))

                def on_connected(info):
                    self.root.after(0, lambda: self._open_viewer(client, info, viewer_ref))
                    entry = {
                        'host': host,
                        'port': port,
                        'date': time.strftime('%d/%m/%Y %H:%M'),
                        'name': info.get('host_name', host)
                    }
                    self._history.append(entry)
                    self._save_config()
                    self.root.after(100, self._refresh_history)

                def on_disconnected():
                    self.root.after(0, lambda: self._connect_btn.config(
                        text="▶  CONECTAR", state=tk.NORMAL, bg=theme.ACCENT
                    ))

                def on_clipboard(text):
                    if viewer_ref[0]:
                        self.root.after(0, lambda: viewer_ref[0].on_clipboard_received(text))

                client = RemoteClient(
                    on_frame=on_frame,
                    on_chat=on_chat,
                    on_connected=on_connected,
                    on_disconnected=on_disconnected,
                    on_clipboard=on_clipboard,
                    audio_enabled=audio,
                    download_dir=dl_dir
                )
                client.local_name = self._config.get('your_name', socket.gethostname())
                client.connect(host, port, password)

            except Exception as e:
                self.root.after(0, lambda: messagebox.showerror(
                    "Erro de Conexão",
                    f"Não foi possível conectar:\n{e}"
                ))
                self.root.after(0, lambda: self._connect_btn.config(
                    text="▶  CONECTAR", state=tk.NORMAL, bg=theme.ACCENT
                ))

        threading.Thread(target=do_connect, daemon=True).start()

    def _open_viewer(self, client, session_info: dict, viewer_ref: list):
        """Abre janela de visualização remota."""
        from espiadisk.ui.viewer_window import ViewerWindow
        viewer = ViewerWindow(self.root, client, session_info)
        viewer_ref[0] = viewer
        self._active_viewers.append(viewer)
        self._connect_btn.config(
            text="▶  CONECTAR", state=tk.NORMAL, bg=theme.ACCENT
        )

    def _copy_id(self):
        """Copia ID para área de transferência."""
        self.root.clipboard_clear()
        self.root.clipboard_append(self._my_id)
        self._id_label.config(fg=theme.SUCCESS, text="ID Copiado! ✓")
        self.root.after(1500, lambda: self._id_label.config(
            fg=theme.ACCENT,
            text=f"{self._my_id[:3]} {self._my_id[3:6]} {self._my_id[6:]}"
        ))

    def _regenerate_id(self):
        """Gera novo ID de sessão."""
        self._my_id = generate_session_id()
        formatted = f"{self._my_id[:3]} {self._my_id[3:6]} {self._my_id[6:]}"
        self._id_label.config(text=formatted)

    def _apply_password(self):
        """Aplica nova senha ao servidor."""
        pwd = self._pwd_var.get()
        if self._host_server:
            self._host_server.set_password(pwd)
        self._config['password'] = pwd
        self._save_config()
        messagebox.showinfo("Senha atualizada", "Senha aplicada com sucesso!")

    def _toggle_server(self):
        """Liga/desliga o servidor host."""
        if self._host_running:
            self._host_server.stop()
            self._host_running = False
            self._srv_btn.config(text="▶ Iniciar Servidor")
            self._srv_status.config(text="○ Servidor pausado", fg=theme.WARNING)
        else:
            self._start_host()
            self._srv_btn.config(text="⏸ Pausar Servidor")

    def _refresh_sessions(self):
        """Atualiza lista de sessões ativas."""
        if not hasattr(self, '_sessions_tree'):
            return
        for row in self._sessions_tree.get_children():
            self._sessions_tree.delete(row)

        if self._host_server:
            for s in self._host_server.active_sessions:
                self._sessions_tree.insert('', tk.END, values=(
                    s.session_id,
                    s.remote_name,
                    s.remote_address,
                    s.duration_str,
                    s.bandwidth_str,
                    '✕ Kick'
                ))
        count = self._host_server.active_count if self._host_server else 0
        self._sessions_count.config(text=f"{count} sessão(ões)")

    def _auto_refresh_sessions(self):
        """Atualiza sessões automaticamente."""
        self._refresh_sessions()
        self.root.after(3000, self._auto_refresh_sessions)

    def _disconnect_selected(self):
        """Desconecta sessão selecionada."""
        sel = self._sessions_tree.selection()
        if not sel:
            return
        item = self._sessions_tree.item(sel[0])
        session_id = item['values'][0]
        if messagebox.askyesno("Desconectar", f"Desconectar sessão {session_id}?"):
            if self._host_server:
                for client in self._host_server._clients:
                    if client.session and client.session.session_id == session_id:
                        client.disconnect()
                        break
            self._refresh_sessions()

    def _refresh_history(self):
        """Atualiza lista de histórico."""
        if not hasattr(self, '_history_tree'):
            return
        for row in self._history_tree.get_children():
            self._history_tree.delete(row)
        for h in reversed(self._history):
            self._history_tree.insert('', tk.END, values=(
                h.get('date', ''),
                h.get('name', h.get('host', '')),
                h.get('duration', '--'),
                h.get('data', '--')
            ))

    def _refresh_recent(self):
        """Atualiza histórico rápido na aba de conexão."""
        for w in self._recent_frame.winfo_children():
            w.destroy()
        recents = self._history[-5:] if self._history else []
        if not recents:
            tk.Label(self._recent_frame, text="Sem conexões recentes",
                     bg=theme.CARD_BG, fg=theme.TEXT_MUTED,
                     font=theme.FONT_SMALL).pack(anchor=tk.W)
        for h in reversed(recents):
            name = h.get('name', h.get('host', ''))
            tk.Button(
                self._recent_frame, text=f"▶ {name}",
                bg=theme.CARD_BG, fg=theme.TEXT_SECONDARY,
                font=theme.FONT_SMALL, relief='flat', bd=0,
                cursor="hand2",
                command=lambda host=h.get('host', ''): (
                    self._remote_id_var.set(host),
                    self._connect()
                )
            ).pack(anchor=tk.W, pady=1)

    def _clear_history(self):
        if messagebox.askyesno("Limpar", "Limpar todo o histórico?"):
            self._history.clear()
            self._save_config()
            self._refresh_history()

    def _reconnect_history(self):
        sel = self._history_tree.selection()
        if not sel:
            return
        item = self._history_tree.item(sel[0])
        host = item['values'][1]
        self._remote_id_var.set(host)
        self._nb.select(0)
        self._connect()

    def _save_settings(self):
        """Salva configurações."""
        self._config['your_name'] = self._name_var.get()
        self._config['port'] = int(self._port_var.get())
        self._config['quality'] = int(self._quality_set_var.get())
        self._config['fps'] = int(self._fps_var.get())
        self._config['download_dir'] = self._dl_dir_var.get()
        self._save_config()

        if self._host_server:
            self._host_server.set_quality(self._config['quality'])
            self._host_server.set_fps(self._config['fps'])

        messagebox.showinfo("Configurações", "Configurações salvas com sucesso!")

    def _update_status(self):
        """Atualiza status geral periodicamente."""
        count = self._host_server.active_count if self._host_server else 0
        self._sessions_count.config(text=f"{count} sessão(ões)")
        if count > 0:
            self._header_status.config(
                text=f"● {count} sessão(ões) ativa(s)", fg=theme.ACCENT
            )
        else:
            self._header_status.config(text="● Pronto", fg=theme.SUCCESS)
        self.root.after(2000, self._update_status)

    def _on_exit(self):
        """Encerra o aplicativo."""
        if messagebox.askyesno("Sair", "Deseja fechar o EspiaDesk?"):
            self._save_config()
            if self._host_server:
                self._host_server.stop()
            self.root.destroy()

    def run(self):
        """Inicia o loop principal da interface."""
        self.root.mainloop()
