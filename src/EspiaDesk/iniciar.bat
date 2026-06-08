@echo off
echo Iniciando EspiaDesk...
python main.py
if errorlevel 1 (
    echo [ERRO] Falha ao iniciar. Execute instalar.bat primeiro.
    pause
)
