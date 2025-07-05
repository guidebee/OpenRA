@echo off
rem templatereader.cmd
rem Export a single tile template from a tileset

if "%1"=="" goto usage
if "%2"=="" goto usage

cd %~dp0
dotnet run --project OpenRA.TemplateReader -- --tileset %1 --template-id %2 %3 %4 %5 %6 %7 %8 %9
goto end

:usage
echo.
echo Usage: templatereader ^<tileset^> ^<template-id^> [options]
echo.
echo Examples:
echo   templatereader desert 401
echo   templatereader temperat 115 --output c:\temp\templates
echo   templatereader snow 25 --game-path c:\games\openra
echo.

:end
