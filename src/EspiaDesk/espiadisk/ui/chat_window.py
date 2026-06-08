"""
EspiaDesk Chat Window
- Interface de chat em tempo real
- Histórico de mensagens
- Suporte a emojis
"""
import tkinter as tk
from tkinter import ttk
import time
from espiadisk.ui import theme


class ChatWindow(tk.Toplevel):
    """Janela de chat flutuante."""

    def __init__(self, parent, send_fn, local_name: str = "Você"):
        super().__init__(parent)
        self.send_fn = send_fn
        self.local_name = local_name
        self._messages = []

        self.title("💬 EspiaDesk - Chat")
        self.geometry("360x520")
        self.resizable(True, True)
        self.configure(bg=theme.DARK_BG)

        self._build_ui()
        self.protocol("WM_DELETE_WINDOW", self.withdraw)

    def _build_ui(self):
        # Header
        hdr = tk.Frame(self, bg=theme.CARD_BG, pady=10)
        hdr.pack(fill=tk.X)
        tk.Label(hdr, text="💬  Chat da Sessão",
                 bg=theme.CARD_BG, fg=theme.TEXT_PRIMARY,
                 font=theme.FONT_HEADING).pack(padx=16)

        # Área de mensagens
        msg_frame = tk.Frame(self, bg=theme.DARK_BG)
        msg_frame.pack(fill=tk.BOTH, expand=True, padx=8, pady=8)

        scrollbar = ttk.Scrollbar(msg_frame)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)

        self._text = tk.Text(
            msg_frame,
            yscrollcommand=scrollbar.set,
            bg=theme.INPUT_BG,
            fg=theme.TEXT_PRIMARY,
            font=theme.FONT_NORMAL,
            wrap=tk.WORD,
            state=tk.DISABLED,
            relief='flat',
            borderwidth=0,
            padx=8, pady=8,
            cursor="arrow"
        )
        self._text.pack(fill=tk.BOTH, expand=True)
        scrollbar.config(command=self._text.yview)

        self._text.tag_configure('time', foreground=theme.TEXT_MUTED,
                                 font=theme.FONT_SMALL)
        self._text.tag_configure('local', foreground=theme.ACCENT,
                                 font=("Segoe UI", 10, "bold"))
        self._text.tag_configure('remote', foreground=theme.SUCCESS,
                                 font=("Segoe UI", 10, "bold"))
        self._text.tag_configure('system', foreground=theme.WARNING,
                                 font=("Segoe UI", 9, "italic"),
                                 justify='center')
        self._text.tag_configure('msg', foreground=theme.TEXT_PRIMARY,
                                 font=theme.FONT_NORMAL)

        # Input
        input_frame = tk.Frame(self, bg=theme.PANEL_BG, pady=8)
        input_frame.pack(fill=tk.X, side=tk.BOTTOM)

        inner = tk.Frame(input_frame, bg=theme.PANEL_BG)
        inner.pack(fill=tk.X, padx=8)

        self._entry = tk.Entry(
            inner,
            bg=theme.INPUT_BG,
            fg=theme.TEXT_PRIMARY,
            insertbackground=theme.TEXT_PRIMARY,
            font=theme.FONT_NORMAL,
            relief='flat',
            bd=8
        )
        self._entry.pack(side=tk.LEFT, fill=tk.X, expand=True)
        self._entry.bind('<Return>', lambda e: self._send())
        self._entry.bind('<Shift-Return>', lambda e: None)

        send_btn = tk.Button(
            inner, text="➤",
            bg=theme.ACCENT, fg=theme.TEXT_PRIMARY,
            font=("Segoe UI", 12, "bold"),
            relief='flat', bd=0,
            padx=12,
            command=self._send,
            cursor="hand2",
            activebackground=theme.ACCENT_HOVER,
            activeforeground=theme.TEXT_PRIMARY
        )
        send_btn.pack(side=tk.RIGHT, padx=(4, 0))

    def _send(self):
        msg = self._entry.get().strip()
        if not msg:
            return
        self._entry.delete(0, tk.END)
        self.send_fn(msg)
        self.add_message(self.local_name, msg, is_local=True)

    def add_message(self, sender: str, message: str, is_local: bool = False):
        """Adiciona mensagem ao chat."""
        self._text.config(state=tk.NORMAL)
        ts = time.strftime("%H:%M")

        self._text.insert(tk.END, f"\n")
        tag = 'local' if is_local else 'remote'
        self._text.insert(tk.END, f"  {sender}", tag)
        self._text.insert(tk.END, f"  {ts}\n", 'time')
        self._text.insert(tk.END, f"  {message}\n", 'msg')

        self._text.config(state=tk.DISABLED)
        self._text.see(tk.END)

    def add_system_message(self, message: str):
        """Adiciona mensagem do sistema."""
        self._text.config(state=tk.NORMAL)
        self._text.insert(tk.END, f"\n  ─── {message} ───\n", 'system')
        self._text.config(state=tk.DISABLED)
        self._text.see(tk.END)

    def show(self):
        self.deiconify()
        self.lift()
        self.focus_force()
