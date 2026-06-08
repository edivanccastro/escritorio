"""
EspiaDesk UI Theme & Colors
"""

# Cores principais
DARK_BG      = "#1a1a2e"
PANEL_BG     = "#16213e"
CARD_BG      = "#0f3460"
ACCENT       = "#e94560"
ACCENT_HOVER = "#c73652"
SUCCESS      = "#00b894"
WARNING      = "#fdcb6e"
ERROR        = "#d63031"
TEXT_PRIMARY = "#ffffff"
TEXT_SECONDARY = "#b2bec3"
TEXT_MUTED   = "#636e72"
BORDER       = "#2d3561"
INPUT_BG     = "#0d1b2a"
BUTTON_BG    = "#e94560"
BUTTON_FG    = "#ffffff"
CONNECTED_COLOR = "#00b894"
DISCONNECTED_COLOR = "#d63031"

FONT_TITLE   = ("Segoe UI", 22, "bold")
FONT_HEADING = ("Segoe UI", 14, "bold")
FONT_NORMAL  = ("Segoe UI", 10)
FONT_SMALL   = ("Segoe UI", 9)
FONT_MONO    = ("Consolas", 11)
FONT_ID      = ("Consolas", 28, "bold")


def apply_theme(root):
    """Aplica o tema dark ao widget raiz."""
    root.configure(bg=DARK_BG)
    try:
        from tkinter import ttk
        style = ttk.Style(root)
        style.theme_use('clam')

        style.configure('.',
            background=DARK_BG,
            foreground=TEXT_PRIMARY,
            fieldbackground=INPUT_BG,
            borderwidth=0,
            relief='flat'
        )
        style.configure('TFrame', background=DARK_BG)
        style.configure('TLabel',
            background=DARK_BG,
            foreground=TEXT_PRIMARY,
            font=FONT_NORMAL
        )
        style.configure('TButton',
            background=BUTTON_BG,
            foreground=BUTTON_FG,
            font=FONT_NORMAL,
            padding=(12, 8),
            relief='flat',
            borderwidth=0
        )
        style.map('TButton',
            background=[('active', ACCENT_HOVER), ('pressed', ACCENT_HOVER)],
            foreground=[('active', TEXT_PRIMARY)]
        )
        style.configure('TEntry',
            fieldbackground=INPUT_BG,
            foreground=TEXT_PRIMARY,
            insertcolor=TEXT_PRIMARY,
            borderwidth=1,
            relief='solid',
            padding=(8, 6)
        )
        style.configure('TNotebook',
            background=PANEL_BG,
            borderwidth=0,
            tabmargins=[0, 0, 0, 0]
        )
        style.configure('TNotebook.Tab',
            background=DARK_BG,
            foreground=TEXT_SECONDARY,
            padding=[16, 8],
            font=FONT_NORMAL
        )
        style.map('TNotebook.Tab',
            background=[('selected', CARD_BG)],
            foreground=[('selected', TEXT_PRIMARY)]
        )
        style.configure('TScrollbar',
            background=PANEL_BG,
            troughcolor=DARK_BG,
            borderwidth=0,
            arrowcolor=TEXT_SECONDARY
        )
        style.configure('TProgressbar',
            background=ACCENT,
            troughcolor=INPUT_BG,
            borderwidth=0
        )
        style.configure('TScale',
            background=DARK_BG,
            troughcolor=INPUT_BG,
            sliderlength=16
        )
        style.configure('TCheckbutton',
            background=DARK_BG,
            foreground=TEXT_PRIMARY,
            font=FONT_NORMAL
        )
        style.map('TCheckbutton',
            background=[('active', DARK_BG)]
        )
        style.configure('Card.TFrame', background=CARD_BG)
        style.configure('Panel.TFrame', background=PANEL_BG)
        style.configure('Secondary.TButton',
            background=PANEL_BG,
            foreground=TEXT_PRIMARY
        )
        style.map('Secondary.TButton',
            background=[('active', CARD_BG)]
        )
        style.configure('Success.TButton',
            background=SUCCESS,
            foreground=TEXT_PRIMARY
        )
        style.configure('Danger.TButton',
            background=ERROR,
            foreground=TEXT_PRIMARY
        )
        style.map('Danger.TButton',
            background=[('active', '#c0392b')]
        )
    except Exception as e:
        print(f"[Theme] Aviso: {e}")
