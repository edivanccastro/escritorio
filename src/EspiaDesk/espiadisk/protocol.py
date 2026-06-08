"""
EspiaDesk Network Protocol
Binary protocol: [1B type][4B length][payload]
"""
import struct
import json
from enum import IntEnum


class MsgType(IntEnum):
    SCREEN_FRAME  = 0x01
    MOUSE_MOVE    = 0x02
    MOUSE_CLICK   = 0x03
    MOUSE_SCROLL  = 0x04
    KEY_EVENT     = 0x05
    CHAT          = 0x06
    FILE_START    = 0x07
    FILE_CHUNK    = 0x08
    FILE_END      = 0x09
    AUDIO         = 0x0A
    CLIPBOARD     = 0x0B
    AUTH_REQ      = 0x0C
    AUTH_RESP     = 0x0D
    CONTROL       = 0x0E
    CURSOR_SHAPE  = 0x0F
    SCREEN_INFO   = 0x10
    HEARTBEAT     = 0x11
    FILE_REQUEST  = 0x12
    PERMISSION    = 0x13


HEADER_SIZE = 5  # 1 byte type + 4 bytes length


def pack_message(msg_type: int, payload: bytes) -> bytes:
    """Empacota mensagem com header."""
    header = struct.pack('!BI', int(msg_type), len(payload))
    return header + payload


def pack_json(msg_type: int, data: dict) -> bytes:
    """Empacota dicionário JSON como mensagem."""
    payload = json.dumps(data, ensure_ascii=False).encode('utf-8')
    return pack_message(msg_type, payload)


def unpack_header(data: bytes):
    """Desempacota header. Retorna (msg_type, payload_length)."""
    if len(data) < HEADER_SIZE:
        raise ValueError("Header incompleto")
    msg_type, length = struct.unpack('!BI', data[:HEADER_SIZE])
    return msg_type, length


def recv_exact(sock, n: int) -> bytes:
    """Recebe exatamente n bytes do socket."""
    data = b''
    while len(data) < n:
        chunk = sock.recv(n - len(data))
        if not chunk:
            raise ConnectionError("Conexão encerrada pelo host remoto")
        data += chunk
    return data


def recv_message(sock):
    """
    Recebe uma mensagem completa do socket.
    Retorna (msg_type, payload_bytes) ou lança exceção.
    """
    header = recv_exact(sock, HEADER_SIZE)
    msg_type, length = unpack_header(header)
    payload = recv_exact(sock, length) if length > 0 else b''
    return msg_type, payload


def send_message(sock, msg_type: int, payload: bytes):
    """Envia mensagem pelo socket com tratamento de bloqueio."""
    data = pack_message(msg_type, payload)
    total_sent = 0
    while total_sent < len(data):
        sent = sock.send(data[total_sent:])
        if sent == 0:
            raise ConnectionError("Conexão perdida ao enviar")
        total_sent += sent


def send_json(sock, msg_type: int, data: dict):
    """Envia dicionário JSON pelo socket."""
    payload = json.dumps(data, ensure_ascii=False).encode('utf-8')
    send_message(sock, msg_type, payload)


def parse_json(payload: bytes) -> dict:
    """Converte payload bytes em dicionário."""
    return json.loads(payload.decode('utf-8'))
