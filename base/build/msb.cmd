@echo off
rem This script is a wrapper around invoking MSBuild.  It causes MSBuild to record
rem a log file under %SINGULARITY_OBJROOT%.obj\MSBuildLogs.

if /i "%ScriptDebug%" == "Yes" (
   @echo on
)

setlocal ENABLEDELAYEDEXPANSION

@rem Some of the old 16-bit tools do not deal well with long paths.
@rem Trim our path while building to the bare essentials.  The
@rem setlocal above will prevent this from affecting the shell that
@rem invoked msb.cmd.
path %SystemRoot%;%SystemRoot%\System32;%SystemRoot%\System32\Wbem;%SINGULARITY_ROOT%\Build

set _msbuild=%SystemRoot%\Microsoft.NET\Framework\v2.0.50727\msbuild.exe

if not exist "%_msbuild%" (
    echo %_msbuild% is not found.
    echo Please install the .Net Framework 2.0.
    goto :eof
)

rem -XXX- This assumes US locale date format.
rem                  01234567890123
rem %DATE% gives us "Fri 03/30/2007"
rem %TIME% gives us "15:49:15.24"
set _date=%DATE%
set _time=%TIME%

rem We want to build a log file name, based on the current date and time.
rem We can't use %DATE% and %TIME% directly, because they contain characters
rem that we don't want in filenames, and because the sort order of those strings
rem does not match time order.  So we swap things around a bit.  We intentionally
rem leave out the usual time/date separators, because we're using YYYYMMDD format,
rem not the usual US format.

set _logfile=msbuild-%COMPUTERNAME%-%_date:~10,4%%_date:~4,2%%_date:~7,2%-%_time:~0,2%%_time:~3,2%%_time:~6,2%.log
set _logfile=%_logfile: =0%
set _logfile=%_logfile::=_%
set _logfile=%_logfile:/=_%
set _logfile=%_logfile:\=_%

if not "!SINGULARITY_ROOT!" == "" (
    if "!SINGULARITY_OBJROOT!" == "" set SINGULARITY_OBJROOT=!SINGULARITY_ROOT!.obj
)

if not "!SINGULARITY_OBJROOT!" == "" set _logdir=!SINGULARITY_OBJROOT!\MSBuildLogs

if not "!_logdir" == "" (
    if not exist "!_logdir!" (
        echo creating log dir - !_logdir!
        mkdir "!_logdir!"
    )
)

if not "!_logdir!" == "" set _logfile=%_logdir%\%_logfile%

set _logargs=/logger:FileLogger,Microsoft.Build.Engine;LogFile="!_logfile!";Verbosity=Normal;PerformanceSummary
rem

rem echo %_msbuild% /nologo /v:m %* !_logargs!

rem Do not put any statements between the invocation
rem of msbuild and the capture of its exit code.
%_msbuild% /nologo /v:m %* !_logargs!
set exitCode=%ErrorLevel%

(
echo.
echo SINGULARITY_ROOT:     %SINGULARITY_ROOT%
echo Current directory:    %CD%
echo MSBuild args:         %*
echo Invoking user:        %USERNAME%
echo Computer:             %COMPUTERNAME%
echo Date and time:        %_date% %_time%
echo MSBuild Result:       ERRORLEVEL = %ERRORLEVEL%
echo.
echo Configuration:        %Configuration%
echo Platform:             %Platform%
echo Paging:               %PAGING%
echo App collector:        %COLLECTOR_APP%
echo Kernel collector:     %COLLECTOR_KERNEL%
) >> "%_logfile%"

echo Log file: %_logfile%

if %ExitCode% == 0 (
    echo Build Succeeded.
) else (
    echo Build Failed.
)

rem Automated build scripts depend on the exit code
rem in order to detect successful or error hampered execution of MSBuild.
exit /b %exitCode%
