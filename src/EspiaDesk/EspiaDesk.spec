# -*- mode: python ; coding: utf-8 -*-
# PyInstaller spec para EspiaDesk — gera EspiaDesk.exe standalone
# Uso: pyinstaller EspiaDesk.spec

import sys
from PyInstaller.utils.hooks import collect_submodules, collect_data_files

block_cipher = None

# Coleta todos os submódulos do pacote espiadisk
hiddenimports = (
    collect_submodules('espiadisk') +
    collect_submodules('PIL') +
    collect_submodules('cryptography') +
    [
        'tkinter',
        'tkinter.ttk',
        'tkinter.filedialog',
        'tkinter.messagebox',
        'tkinter.simpledialog',
        'PIL._tkinter_finder',
        'pyautogui',
        'numpy',
        'zlib',
        'threading',
        'socket',
        'json',
        'hashlib',
        'struct',
        'io',
        'uuid',
        'time',
        'os',
        'pathlib',
    ]
)

datas = []

a = Analysis(
    ['main.py'],
    pathex=['.'],
    binaries=[],
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        'matplotlib', 'scipy', 'pandas', 'IPython',
        'jupyter', 'notebook', 'PyQt5', 'PyQt6',
        'wx', 'gi', 'gtk',
    ],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name='EspiaDesk',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,          # Sem janela de console (windowed)
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    # icon='assets\icon.ico',  # Descomente se tiver ícone .ico
)
