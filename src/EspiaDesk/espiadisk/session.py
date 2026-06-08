"""
EspiaDesk Session Management
- Gerenciamento de sessões ativas
- Registro de atividades
- Controle de permissões
"""
import time
import threading
from dataclasses import dataclass, field
from enum import IntEnum
from typing import Optional


class Permission(IntEnum):
    VIEW_ONLY = 0
    FULL_CONTROL = 1
    FILE_TRANSFER = 2
    CLIPBOARD = 3
    AUDIO = 4


@dataclass
class Session:
    session_id: str
    remote_address: str
    remote_name: str
    start_time: float = field(default_factory=time.time)
    end_time: Optional[float] = None
    permissions: set = field(default_factory=lambda: {
        Permission.VIEW_ONLY,
        Permission.FULL_CONTROL,
        Permission.FILE_TRANSFER,
        Permission.CLIPBOARD,
        Permission.AUDIO
    })
    bytes_sent: int = 0
    bytes_received: int = 0
    frames_sent: int = 0
    is_active: bool = True
    _events: list = field(default_factory=list)

    @property
    def duration(self) -> float:
        end = self.end_time or time.time()
        return end - self.start_time

    @property
    def duration_str(self) -> str:
        secs = int(self.duration)
        h = secs // 3600
        m = (secs % 3600) // 60
        s = secs % 60
        if h > 0:
            return f"{h:02d}:{m:02d}:{s:02d}"
        return f"{m:02d}:{s:02d}"

    @property
    def bandwidth_str(self) -> str:
        total = self.bytes_sent + self.bytes_received
        if total < 1024:
            return f"{total} B"
        elif total < 1024 * 1024:
            return f"{total / 1024:.1f} KB"
        else:
            return f"{total / (1024 * 1024):.1f} MB"

    def add_event(self, event_type: str, details: str = ""):
        self._events.append({
            'time': time.time(),
            'type': event_type,
            'details': details
        })

    def end(self):
        self.is_active = False
        self.end_time = time.time()
        self.add_event('session_end', f"Duração: {self.duration_str}")

    def has_permission(self, perm: Permission) -> bool:
        return perm in self.permissions

    def grant_permission(self, perm: Permission):
        self.permissions.add(perm)

    def revoke_permission(self, perm: Permission):
        self.permissions.discard(perm)


class SessionManager:
    """Gerencia todas as sessões."""

    def __init__(self):
        self._sessions: dict[str, Session] = {}
        self._lock = threading.Lock()
        self._history: list[Session] = []

    def create_session(self, session_id: str, remote_address: str,
                       remote_name: str) -> Session:
        session = Session(
            session_id=session_id,
            remote_address=remote_address,
            remote_name=remote_name
        )
        with self._lock:
            self._sessions[session_id] = session
        return session

    def get_session(self, session_id: str) -> Optional[Session]:
        return self._sessions.get(session_id)

    def end_session(self, session_id: str):
        with self._lock:
            session = self._sessions.pop(session_id, None)
            if session:
                session.end()
                self._history.append(session)

    def end_all(self):
        with self._lock:
            for sid in list(self._sessions.keys()):
                session = self._sessions.pop(sid)
                session.end()
                self._history.append(session)

    @property
    def active_sessions(self) -> list[Session]:
        return list(self._sessions.values())

    @property
    def session_history(self) -> list[Session]:
        return self._history.copy()

    @property
    def active_count(self) -> int:
        return len(self._sessions)
