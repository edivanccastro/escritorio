"""
EspiaDesk Input Control Module
- Controle remoto de mouse e teclado
- Mapeamento de coordenadas relativas para absolutas
- Suporte a todos eventos de mouse e teclado
"""
import pyautogui
import threading
import time

pyautogui.FAILSAFE = False
pyautogui.PAUSE = 0


# Mapeamento de teclas especiais
SPECIAL_KEYS = {
    'ctrl': 'ctrl', 'alt': 'alt', 'shift': 'shift', 'win': 'win',
    'enter': 'enter', 'return': 'return', 'space': 'space',
    'backspace': 'backspace', 'delete': 'delete', 'insert': 'insert',
    'tab': 'tab', 'escape': 'esc', 'esc': 'esc',
    'up': 'up', 'down': 'down', 'left': 'left', 'right': 'right',
    'home': 'home', 'end': 'end', 'pageup': 'pageup', 'pagedown': 'pagedown',
    'f1': 'f1', 'f2': 'f2', 'f3': 'f3', 'f4': 'f4',
    'f5': 'f5', 'f6': 'f6', 'f7': 'f7', 'f8': 'f8',
    'f9': 'f9', 'f10': 'f10', 'f11': 'f11', 'f12': 'f12',
    'capslock': 'capslock', 'numlock': 'numlock', 'scrolllock': 'scrolllock',
    'printscreen': 'printscreen', 'pause': 'pause',
    'num0': 'num0', 'num1': 'num1', 'num2': 'num2', 'num3': 'num3',
    'num4': 'num4', 'num5': 'num5', 'num6': 'num6', 'num7': 'num7',
    'num8': 'num8', 'num9': 'num9',
}

BUTTON_MAP = {
    1: 'left', 2: 'middle', 3: 'right'
}


class InputController:
    """Controla mouse e teclado do host remoto."""

    def __init__(self, screen_width: int, screen_height: int):
        self.screen_width = screen_width
        self.screen_height = screen_height
        self._held_keys: set = set()
        self._lock = threading.Lock()

    def update_screen_size(self, width: int, height: int):
        """Atualiza resolução da tela remota."""
        self.screen_width = width
        self.screen_height = height

    def _map_coords(self, rel_x: float, rel_y: float,
                    remote_w: int, remote_h: int):
        """Converte coordenadas relativas (0-1) para absolutas da tela."""
        abs_x = int(rel_x * self.screen_width)
        abs_y = int(rel_y * self.screen_height)
        return abs_x, abs_y

    def mouse_move(self, rel_x: float, rel_y: float,
                   remote_w: int = 1920, remote_h: int = 1080):
        """Move o mouse para posição relativa."""
        x, y = self._map_coords(rel_x, rel_y, remote_w, remote_h)
        pyautogui.moveTo(x, y, _pause=False)

    def mouse_click(self, rel_x: float, rel_y: float,
                    button: int = 1, double: bool = False,
                    remote_w: int = 1920, remote_h: int = 1080):
        """Clica no mouse na posição relativa."""
        x, y = self._map_coords(rel_x, rel_y, remote_w, remote_h)
        btn = BUTTON_MAP.get(button, 'left')
        if double:
            pyautogui.doubleClick(x, y, button=btn, _pause=False)
        else:
            pyautogui.click(x, y, button=btn, _pause=False)

    def mouse_down(self, rel_x: float, rel_y: float,
                   button: int = 1,
                   remote_w: int = 1920, remote_h: int = 1080):
        """Pressiona botão do mouse."""
        x, y = self._map_coords(rel_x, rel_y, remote_w, remote_h)
        btn = BUTTON_MAP.get(button, 'left')
        pyautogui.mouseDown(x, y, button=btn, _pause=False)

    def mouse_up(self, rel_x: float, rel_y: float,
                 button: int = 1,
                 remote_w: int = 1920, remote_h: int = 1080):
        """Solta botão do mouse."""
        x, y = self._map_coords(rel_x, rel_y, remote_w, remote_h)
        btn = BUTTON_MAP.get(button, 'left')
        pyautogui.mouseUp(x, y, button=btn, _pause=False)

    def mouse_scroll(self, rel_x: float, rel_y: float, delta: int,
                     remote_w: int = 1920, remote_h: int = 1080):
        """Rola o scroll do mouse."""
        x, y = self._map_coords(rel_x, rel_y, remote_w, remote_h)
        pyautogui.scroll(delta, x=x, y=y, _pause=False)

    def key_down(self, key: str):
        """Pressiona uma tecla."""
        key = key.lower()
        mapped = SPECIAL_KEYS.get(key, key)
        with self._lock:
            if mapped not in self._held_keys:
                self._held_keys.add(mapped)
                pyautogui.keyDown(mapped, _pause=False)

    def key_up(self, key: str):
        """Solta uma tecla."""
        key = key.lower()
        mapped = SPECIAL_KEYS.get(key, key)
        with self._lock:
            self._held_keys.discard(mapped)
            pyautogui.keyUp(mapped, _pause=False)

    def key_press(self, key: str):
        """Pressiona e solta uma tecla."""
        key = key.lower()
        mapped = SPECIAL_KEYS.get(key, key)
        pyautogui.press(mapped, _pause=False)

    def type_text(self, text: str):
        """Digita um texto."""
        pyautogui.typewrite(text, interval=0.01, _pause=False)

    def hotkey(self, *keys):
        """Executa combinação de teclas."""
        mapped = [SPECIAL_KEYS.get(k.lower(), k.lower()) for k in keys]
        pyautogui.hotkey(*mapped, _pause=False)

    def release_all(self):
        """Solta todas as teclas pressionadas."""
        with self._lock:
            for key in list(self._held_keys):
                try:
                    pyautogui.keyUp(key, _pause=False)
                except Exception:
                    pass
            self._held_keys.clear()


class InputEventSerializer:
    """Serializa/deserializa eventos de input para protocolo."""

    @staticmethod
    def mouse_move(rel_x: float, rel_y: float,
                   remote_w: int, remote_h: int) -> dict:
        return {
            'type': 'mouse_move',
            'x': round(rel_x, 5),
            'y': round(rel_y, 5),
            'rw': remote_w,
            'rh': remote_h
        }

    @staticmethod
    def mouse_click(rel_x: float, rel_y: float, button: int,
                    double: bool, remote_w: int, remote_h: int) -> dict:
        return {
            'type': 'mouse_click',
            'x': round(rel_x, 5),
            'y': round(rel_y, 5),
            'btn': button,
            'dbl': double,
            'rw': remote_w,
            'rh': remote_h
        }

    @staticmethod
    def mouse_down(rel_x: float, rel_y: float, button: int,
                   remote_w: int, remote_h: int) -> dict:
        return {
            'type': 'mouse_down',
            'x': round(rel_x, 5),
            'y': round(rel_y, 5),
            'btn': button,
            'rw': remote_w,
            'rh': remote_h
        }

    @staticmethod
    def mouse_up(rel_x: float, rel_y: float, button: int,
                 remote_w: int, remote_h: int) -> dict:
        return {
            'type': 'mouse_up',
            'x': round(rel_x, 5),
            'y': round(rel_y, 5),
            'btn': button,
            'rw': remote_w,
            'rh': remote_h
        }

    @staticmethod
    def mouse_scroll(rel_x: float, rel_y: float, delta: int,
                     remote_w: int, remote_h: int) -> dict:
        return {
            'type': 'mouse_scroll',
            'x': round(rel_x, 5),
            'y': round(rel_y, 5),
            'delta': delta,
            'rw': remote_w,
            'rh': remote_h
        }

    @staticmethod
    def key_event(key: str, action: str) -> dict:
        return {'type': 'key_event', 'key': key, 'action': action}

    @staticmethod
    def type_text(text: str) -> dict:
        return {'type': 'type_text', 'text': text}
