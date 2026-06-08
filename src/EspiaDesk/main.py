"""
EspiaDesk — Software de Acesso Remoto
Criptografia E2E (RSA 2048 + AES-256) e interface moderna.

Uso:
    python main.py              # Interface gráfica completa
    python main.py --host       # Modo servidor apenas (sem UI)
    python main.py --client IP  # Modo cliente (sem UI)
"""
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def main():
    args = sys.argv[1:]

    if '--host' in args:
        _run_cli_host()
    elif '--client' in args:
        idx = args.index('--client')
        host = args[idx + 1] if idx + 1 < len(args) else '127.0.0.1'
        _run_cli_client(host)
    else:
        _run_gui()


def _run_gui():
    """Inicia a interface gráfica completa."""
    try:
        from espiadisk.ui.main_window import MainWindow
        app = MainWindow()
        app.run()
    except ImportError as e:
        print(f"[ERRO] Dependência faltando: {e}")
        print("Execute: pip install -r requirements.txt")
        sys.exit(1)


def _run_cli_host(port: int = 7070, password: str = ""):
    """Executa no modo servidor de linha de comando."""
    from espiadisk.host import HostServer
    print("=" * 50)
    print("  EspiaDesk — Modo Servidor")
    print("=" * 50)

    server = HostServer(port=port, password=password)
    server.on_accept_request = lambda name, ip: (
        print(f"[+] Conexão de {name} ({ip}) — ACEITA automaticamente"),
        True
    )[1]

    server.start()
    ip = server.get_local_ip()
    print(f"[*] Servidor rodando em {ip}:{port}")
    print("[*] Pressione Ctrl+C para parar\n")

    try:
        import time
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        server.stop()
        print("\n[*] Servidor parado.")


def _run_cli_client(host: str, port: int = 7070):
    """Executa no modo cliente de linha de comando."""
    print("=" * 50)
    print(f"  EspiaDesk — Conectando a {host}:{port}")
    print("=" * 50)

    import time

    def on_frame(data):
        print(f"[Frame] {len(data)} bytes recebidos")

    from espiadisk.client import RemoteClient
    client = RemoteClient(on_frame=on_frame)

    try:
        info = client.connect(host, port)
        print(f"[+] Conectado! Sessão: {info.get('session_id')}")
        print("[*] Pressione Ctrl+C para desconectar")
        while client.connected:
            time.sleep(1)
    except Exception as e:
        print(f"[-] Erro: {e}")
    finally:
        client.disconnect()


if __name__ == '__main__':
    main()
