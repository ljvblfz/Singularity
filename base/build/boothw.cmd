@echo off

setlocal enableextensions
setlocal enabledelayedexpansion
setlocal

if not exist "%SINGULARITY_ROOT%\buildcfg.cmd" (
    echo Build configuration settings file not found.  Has a distribution been built?
    exit /b 1
)

call "%SINGULARITY_ROOT%\buildcfg.cmd" >nul

set MacAddress=00-00-00-00-00-00-00
set MacAddress=00-08-02-01-b8-a1
set MacAddress=00-0c-76-4e-ee-37

echo Boot serving host with MAC address: !MacAddress!

start bootd.exe /dhcp /b:SINGLDR /m:!MacAddress! /tftp /e %_BOOTD_TFTP_DIR% %*
