@echo off
rem Starts the KspBridge MCP server for Claude Code (stdio).
rem A new build published to .mcp-bin-next is swapped into .mcp-bin here, while no server is running.
rem Nothing may be written to stdout except by the server (stdout is the MCP channel).
setlocal
set "ROOT=%~dp0.."
if exist "%ROOT%\.mcp-bin-next\KspBridge.App.exe" (
  if exist "%ROOT%\.mcp-bin-old" rmdir /s /q "%ROOT%\.mcp-bin-old" >nul 2>&1
  if exist "%ROOT%\.mcp-bin" move "%ROOT%\.mcp-bin" "%ROOT%\.mcp-bin-old" >nul 2>&1
  rem Only swap when the old folder is really gone (a running server keeps it locked).
  if not exist "%ROOT%\.mcp-bin" move "%ROOT%\.mcp-bin-next" "%ROOT%\.mcp-bin" >nul 2>&1
)
"%ROOT%\.mcp-bin\KspBridge.App.exe" %*
exit /b %ERRORLEVEL%
