"""
EspiaDesk Encryption Module
- RSA 2048 para troca de chaves
- AES-256 (Fernet) para criptografia de sessão
- Hash SHA-256 para autenticação de senha
"""
import os
import hashlib
import base64
from cryptography.hazmat.primitives.asymmetric import rsa, padding
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.fernet import Fernet
from cryptography.hazmat.backends import default_backend


def generate_rsa_keypair():
    """Gera par de chaves RSA 2048 bits."""
    private_key = rsa.generate_private_key(
        public_exponent=65537,
        key_size=2048,
        backend=default_backend()
    )
    return private_key, private_key.public_key()


def serialize_public_key(public_key) -> bytes:
    """Serializa chave pública para bytes (PEM)."""
    return public_key.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo
    )


def deserialize_public_key(data: bytes):
    """Desserializa chave pública de bytes PEM."""
    return serialization.load_pem_public_key(data, backend=default_backend())


def rsa_encrypt(public_key, data: bytes) -> bytes:
    """Encripta dados com chave pública RSA."""
    return public_key.encrypt(
        data,
        padding.OAEP(
            mgf=padding.MGF1(algorithm=hashes.SHA256()),
            algorithm=hashes.SHA256(),
            label=None
        )
    )


def rsa_decrypt(private_key, data: bytes) -> bytes:
    """Decripta dados com chave privada RSA."""
    return private_key.decrypt(
        data,
        padding.OAEP(
            mgf=padding.MGF1(algorithm=hashes.SHA256()),
            algorithm=hashes.SHA256(),
            label=None
        )
    )


def generate_session_key() -> bytes:
    """Gera chave de sessão AES (Fernet key = 32 bytes AES-128-CBC + HMAC-SHA256)."""
    return Fernet.generate_key()


def create_cipher(key: bytes) -> Fernet:
    """Cria objeto Fernet com a chave de sessão."""
    return Fernet(key)


def encrypt_payload(cipher: Fernet, data: bytes) -> bytes:
    """Encripta payload com cifra de sessão."""
    return cipher.encrypt(data)


def decrypt_payload(cipher: Fernet, data: bytes) -> bytes:
    """Decripta payload com cifra de sessão."""
    return cipher.decrypt(data)


def hash_password(password: str) -> str:
    """Hash SHA-256 da senha com salt."""
    salt = "EspiaDesk_Salt_2024"
    return hashlib.sha256(f"{salt}{password}".encode()).hexdigest()


def verify_password(password: str, hashed: str) -> bool:
    """Verifica senha contra hash."""
    return hash_password(password) == hashed


def generate_session_id() -> str:
    """Gera ID de sessão de 9 dígitos."""
    raw = int.from_bytes(os.urandom(4), 'big') % 1_000_000_000
    return f"{raw:09d}"


class SessionCrypto:
    """Gerencia criptografia completa de uma sessão."""

    def __init__(self):
        self.private_key, self.public_key = generate_rsa_keypair()
        self.session_key: bytes | None = None
        self.cipher: Fernet | None = None

    def get_public_key_bytes(self) -> bytes:
        return serialize_public_key(self.public_key)

    def establish_as_server(self, client_public_key_bytes: bytes) -> bytes:
        """
        Servidor: recebe chave pública do cliente,
        gera chave de sessão, encripta com a chave pública do cliente.
        Retorna chave de sessão encriptada para enviar ao cliente.
        """
        self.session_key = generate_session_key()
        self.cipher = create_cipher(self.session_key)
        client_pub = deserialize_public_key(client_public_key_bytes)
        return rsa_encrypt(client_pub, self.session_key)

    def establish_as_client(self, encrypted_session_key: bytes):
        """
        Cliente: decripta chave de sessão com chave privada própria.
        """
        self.session_key = rsa_decrypt(self.private_key, encrypted_session_key)
        self.cipher = create_cipher(self.session_key)

    def encrypt(self, data: bytes) -> bytes:
        if not self.cipher:
            return data
        return encrypt_payload(self.cipher, data)

    def decrypt(self, data: bytes) -> bytes:
        if not self.cipher:
            return data
        return decrypt_payload(self.cipher, data)

    @property
    def ready(self) -> bool:
        return self.cipher is not None
