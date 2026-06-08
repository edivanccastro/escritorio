# EspiaDesk — Acesso Remoto Seguro

EspiaDesk é um aplicativo de acesso remoto desenvolvido em **C# (.NET 8 + WPF)**, parte da suíte Escritório CE.

> Este módulo é experimental. Não utilize em ambientes de produção ou críticos sem avaliação prévia.

## Tecnologia

- **Linguagem:** C# 12 / .NET 8
- **UI:** WPF (Windows Presentation Foundation)
- **Criptografia:** RSA 2048 (troca de chaves) + AES-256 (sessão)
- **Captura de tela:** GDI+ via `System.Drawing.Common`
- **Controle remoto:** Windows API (P/Invoke `SendInput`, `SetCursorPos`)
- **Protocolo:** binário customizado sobre TCP

## Funcionalidades

| Funcionalidade | Status |
|----------------|--------|
| Código de acesso alfanumérico (ex: `ED-123456`) | ✅ |
| Criptografia ponta a ponta (RSA + AES-256) | ✅ |
| Visualização de tela remota | ✅ |
| Controle de mouse e teclado remoto | ✅ |
| Proteção por senha de sessão | ✅ |
| Chat durante sessão remota | ✅ |
| Histórico de sessões | ✅ |

## Estrutura

```
src/EspiaDesk/
  App.xaml(.cs)          Inicialização do app
  MainWindow.xaml(.cs)   Tela principal (host + cliente)
  ViewerWindow.xaml(.cs) Visualizador de tela remota
  ChatWindow.xaml(.cs)   Chat de sessão
  Core/
    HostServer.cs        Servidor TCP (lado host)
    RemoteClient.cs      Cliente TCP (lado visualizador)
    ScreenCapture.cs     Captura GDI+
    InputController.cs   Injeção de input Win32
    SessionCrypto.cs     RSA + AES-256
    Protocol.cs          Protocolo binário
    Models.cs            Modelos de dados
  Assets/
    espiadisk.ico        Ícone do aplicativo
```

## Executar em desenvolvimento

```powershell
dotnet run --project .\src\EspiaDesk\EspiaDesk.csproj
```

## Segurança

- Troca de chaves RSA-OAEP (SHA-256) garante confidencialidade da chave de sessão
- Cada sessão usa uma chave AES-256 única gerada aleatoriamente
- IV aleatório prefixado a cada mensagem cifrada
- Senha de sessão com hash SHA-256 + salt fixo

## Licença

MIT — Copyright (c) 2026 Escritório. Consulte [LICENSE](../../LICENSE).

Dependências de terceiros: [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt).
