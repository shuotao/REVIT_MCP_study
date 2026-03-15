@echo off
rem ============================================
rem run_mcp_server.bat - MCP Server ?Ÿå??³æœ¬
rem ============================================

echo ==============================
echo MCP Server Startup
echo ==============================
echo.

rem Verify Node.js is installed
echo Verifying Node.js...
where node >nul 2>&1
if errorlevel 1 (
    echo ERROR: Node.js is not installed or not in PATH.
    echo Please install Node.js from https://nodejs.org/
    pause
    exit /b 1
)
node -v
npm -v
echo.

rem Install dependencies
echo ==============================
echo Step 1/3: npm install
echo ==============================
npm install
if errorlevel 1 (
    echo ERROR: npm install failed
    pause
    exit /b 1
)
echo.

rem Build TypeScript
echo ==============================
echo Step 2/3: npm run build
echo ==============================
npm run build
if errorlevel 1 (
    echo ERROR: npm run build failed
    pause
    exit /b 1
)
echo.

rem Start MCP Server
echo ==============================
echo Step 3/3: npm start
echo ==============================
echo MCP Server will run on ws://localhost:11111
echo Press Ctrl+C to stop
echo.
npm start

