@echo off
echo =========================================
echo   EspiaDesk - Instalador de Dependencias
echo =========================================
echo.

python --version >nul 2>&1
if errorlevel 1 (
    echo [ERRO] Python nao encontrado! Instale o Python 3.10+ primeiro.
    pause
    exit /b 1
)

echo [1/2] Instalando dependencias...
pip install -r requirements.txt

echo.
echo [2/2] Dependencias instaladas com sucesso!
echo.
echo Iniciando EspiaDesk...
timeout /t 2 >nul
python main.py

pause
