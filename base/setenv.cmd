@echo off

@rem @setlocal enableextensions

rem This needs to be defined before invoking goto usage.
set _EXIT_CMD=exit /b

if .==.%1 goto good
if ".%1"=="./?" goto usage

:good
rem Save path when first run.
if not defined SAVED_PATH (
    set "SAVED_PATH=%PATH%"
)

rem ###### Remember the current directory ######
set SINGULARITY_ROOT=%~ds0%~ps0%
set CLEAN_SINGULARITY_ROOT=%~d0%~p0%
if .%SINGULARITY_ROOT:~-1%==.\ (
   set "SINGULARITY_ROOT=%SINGULARITY_ROOT:~0,-1%"
)

rem Clear no defaults variable.  Needs to be reset after missing default
rem causes an error.
set NO_SINGULARITY_DEFAULTS=

:parse

if /I .%1==./release (
  set BUILDTYPE=Release
  shift /1
  goto parse
)

if /I .%1==./debug (
  set BUILDTYPE=Debug
  shift /1
  goto parse
)

if /I .%1==./terminate (
  set _EXIT_CMD=exit
  shift /1
  goto parse
)

if /I .%1==./prototype (
  set BUILDTYPE=Prototype
  shift /1
  goto parse
)

if /I .%1==./apic (
  set PLATFORM=ApicPC
  shift /1
  goto parse
)

if /I .%1==./legacy (
  set PLATFORM=LegacyPC
  shift /1
  goto parse
)

if /I .%1==./mp (
  set PLATFORM=ApicMP
  shift /1
  goto parse
)

if /I .%1==./enlightened (
  set PLATFORM=EnlightenedPC
  shift /1
  goto parse
)

if /I .%1==./kms (
  set COLLECTOR_KERNEL=MarkSweep
  shift /1
  goto parse
)

if /I .%1==./kcc (
  set COLLECTOR_KERNEL=Concurrent
  shift /1
  goto parse
)

if /I .%1==./kss (
  set COLLECTOR_KERNEL=Semispace
  shift /1
  goto parse
)

if /I .%1==./noi (
  set SINGULARITY_INTERNAL=No
  shift /1
  goto parse
)

if /I .%1==./pms (
  set COLLECTOR_APP=MarkSweep
  shift /1
  goto parse
)

if /I .%1==./pcc (
  set COLLECTOR_APP=Concurrent
  shift /1
  goto parse
)

if /I .%1==./pss (
  set COLLECTOR_APP=Semispace
  shift /1
  goto parse
)

if /I .%1==./paging (
  set PAGING=On
  shift /1
  goto parse
)

if /I .%1==./nopaging (
  set PAGING=Off
  shift /1
  goto parse
)

if /I .%1==./abishim (
  set GENERATE_ABI_SHIM=On
  shift /1
  goto parse
)

if /I .%1==./noabishim (
  set GENERATE_ABI_SHIM=Off
  shift /1
  goto parse
)

if /I .%1==./clean (
  set SINGULARITY_ROOT=
  set SINGULARITY_INTERNAL=
  set BUILDTYPE=
  set PLATFORM=
  set COLLECTOR_KERNEL=
  set COLLECTOR_APP=
  set CONFIGURATION=
  set "PATH=%SAVED_PATH%"
  set SAVED_PATH=
  set PAGING=
  set GENERATE_ABI_SHIM=
  echo.Environment cleaned.
  %_EXIT_CMD% 0
)

if /I .%1==./nodefaults (
  set GENERATE_ABI_SHIM=Off
  set NO_SINGULARITY_DEFAULTS=Yes
  shift /1
  goto parse
)

if /I .%1==./notitle (
  set NO_SINGULARITY_WINDOW_TITLE=Yes
  shift /1
  goto parse
)

setlocal
set ARG=.%1
if %ARG:~0,2%==./ (
  echo.Unrecognized option "%1"
  %_EXIT_CMD% 2
)
endlocal

:finished

if .%BUILDTYPE%==.Release (
  rem
) else if .%BUILDTYPE%==.Debug (
  rem
) else if .%BUILDTYPE%==.Prototype (
  rem
) else (
  if .%NO_SINGULARITY_DEFAULTS%==.Yes (
    echo.Missing or invalid BUILDTYPE value: "%BUILDTYPE%"
    %_EXIT_CMD% 1
  )
  set BUILDTYPE=Prototype
)

if .%PLATFORM%==.ApicPC (
  rem
) else if .%PLATFORM%==.ApicMP (
  rem
) else if .%PLATFORM%==.LegacyPC (
  rem
) else if .%PLATFORM%==.EnlightenedPC (
  rem
) else (
  if .%NO_SINGULARITY_DEFAULTS%==.Yes (
    echo.Missing or invalid PLATFORM value: "%PLATFORM%"
    %_EXIT_CMD% 1
  )
  set PLATFORM=LegacyPC
)

if .%COLLECTOR_KERNEL%==.MarkSweep (
  rem
) else if .%COLLECTOR_KERNEL%==.Concurrent (
  rem
) else if .%COLLECTOR_KERNEL%==.Semispace (
  rem
) else (
  if .%NO_SINGULARITY_DEFAULTS%==.Yes (
    echo.Missing or invalid COLLECTOR_KERNEL value: "%COLLECTOR_KERNEL"
    %_EXIT_CMD% 1
  )
  set COLLECTOR_KERNEL=MarkSweep
)

if .%COLLECTOR_APP%==.MarkSweep (
  rem
) else if .%COLLECTOR_APP%==.Concurrent (
  rem
) else if .%COLLECTOR_APP%==.Semispace (
  rem
) else (
  if .%NO_SINGULARITY_DEFAULTS%==.Yes (
    echo.Missing or invalid COLLECTOR_APP value: "%COLLECTOR_APP%"
    %_EXIT_CMD% 1
  )
  set COLLECTOR_APP=MarkSweep
)

if .%PAGING%==.On (
  set PAGING_FLAG=.Paging
) else if .%PAGING%==.Off (
  set PAGING_FLAG=
) else (
  if .%NO_SINGULARITY_DEFAULTS%==.Yes (
    echo.Missing or invalid PAGING value: "%PAGING%"
    %_EXIT_CMD% 1
  )
  set PAGING=Off
  set PAGING_FLAG=
)

if .%GENERATE_ABI_SHIM%==.On (
  rem
) else if .%GENERATE_ABI_SHIM%==.Off (
  rem
) else (
  if .%NO_SINGULARITY_DEFAULTS%==.Yes (
    echo.Missing or invalid GENERATE_ABI_SHIM value: "%GENERATE_ABI_SHIM%"
    %_EXIT_CMD% 1
  )
  set GENERATE_ABI_SHIM=Off
)

goto :finale

:usage
echo.Usage:
echo.    setenv.cmd [options]
echo.
echo.Summary:
echo.    Configure environment variables for building Singularity.
echo.
echo.Options:
echo.    /prototype     Prototype build (no optimization w/ debug asserts).[default]
echo.    /debug         Debug build (full optimization w/ debug asserts).
echo.    /release       Release build (full optimization w/o debug asserts).
echo.
echo.    /legacy        Legacy PC (Virtual PC).                            [default]
echo.    /apic          Single-core nForce4 PC.
echo.    /mp            Multi-core nForce4 PC.
echo.    /enlightened   Only executes on Viridian hypervisor.
echo.
echo.    /kms           Kernel Mark Sweep Collector.                       [default]
echo.    /kcc           Kernel Concurrent Collector.
echo.    /kss           Kernel Semispace Collector.
echo.
echo.    /pms           Process Mark Sweep Collector.                      [default]
echo.    /pcc           Process Concurrent Collector.
echo.    /pss           Process Semispace Collector.
echo.
echo.    /nopaging      Page translation off.                              [default]
echo.    /paging        Page translation on.
echo.
echo.    /clean         Remove Singularity build variables from environment.
echo.
echo.    /nodefaults    Do not use defaults, underspecification is an error.
echo.    /noi           Force no internal tools directory (otherwise autodetected).
echo.    /notitle       Do not change the window title.
echo.

%_EXIT_CMD% 1

:BinaryIsInPath
    if "%~$PATH:1" == "" (
        exit /b 1
    ) else (
        exit /b 0
    )

:finale

if not defined SINGULARITY_INTERNAL (
    if exist "%SINGULARITY_ROOT%\build\internal%SINGULARITY_NOINTERNAL%" (
        set SINGULARITY_INTERNAL=Yes
    ) else (
        set SINGULARITY_INTERNAL=No
    )
)

if %SINGULARITY_INTERNAL%==Yes (
    set "SINGULARITY_PATH=%SINGULARITY_ROOT%;%SINGULARITY_ROOT%\build;%SINGULARITY_ROOT%\build\internal"
) else (
    set "SINGULARITY_PATH=%SINGULARITY_ROOT%;%SINGULARITY_ROOT%\build"
)

call :BinaryIsInPath windbg.exe
if ErrorLevel 1 (
    if exist "c:\debuggers\windbg.exe" (
        @rem The standard MS installed location
        set _DEBUGGER_PATH=c:\debuggers
    ) else if exist "%ProgramFiles%\Debugging Tools for Windows\windbg.exe" (
        @rem The likely user installed location
        set _DEBUGGER_PATH=%ProgramFiles%\Debugging Tools for Windows
    ) else (
        echo Warning - Debugging Tools for Windows does not appear to be installed.
        echo Visit http://www.microsoft.com/whdc/devtools/debugging/default.mspx.
        echo.
        @rem Or update this script to know about where it is installed.
    )
)

if defined _DEBUGGER_PATH (
    set "PATH=%SINGULARITY_PATH%;%SAVED_PATH%;%_DEBUGGER_PATH%"
) else (
    set "PATH=%SINGULARITY_PATH%;%SAVED_PATH%"
)

set _DEBUGGER_PATH=
set INCLUDE=
set LIB=
rem set "INCLUDE=%SINGULARITY_ROOT%\Windows\inc"
rem set "LIB=%SINGULARITY_ROOT%\Windows\lib"

if not defined NO_SINGULARITY_WINDOW_TITLE (
  if .%PAGING%==.On (
    title %BUILDTYPE% %PLATFORM% Paging [%SINGULARITY_ROOT%]
  ) else if .%COLLECTOR_APP%==.Semispace (
    title %BUILDTYPE% %PLATFORM% %COLLECTOR_APP% [%SINGULARITY_ROOT%]
  ) else (
    title %BUILDTYPE% %PLATFORM% [%SINGULARITY_ROOT%]
  )
)

set NO_SINGULARITY_WINDOW_TITLE=
set NO_SINGULARITY_DEFAULTS=

@rem In the transition to MSBuild the former BUILDTYPE variable became
@rem CONFIGURATION in the MSBuild scripts...
set Configuration=%BuildType%

echo ** Singularity Build Environment:
echo **   Base Directory:     %SINGULARITY_ROOT%
echo **   Build Type:         %BUILDTYPE%
echo **   Target Platform:    %PLATFORM%
echo **   Kernel Collector:   %COLLECTOR_KERNEL%
echo **   Process Collector:  %COLLECTOR_APP%
echo **   Page Translation:   %PAGING%
echo **   Generate ABI Shim:  %GENERATE_ABI_SHIM%
cd /d "%CLEAN_SINGULARITY_ROOT%"

@rem
@rem *IMPORTANT* This script is used by the
@rem automated build system and users within command-line sessions.
@rem The final invocation of user supplied command happens at the end
@rem of this script because C#\'s Process class code only gets the exit
@rem code this way or via exit, exit /b does not work.  Exit without
@rem arguments terminates the interpreter.  Exit /b terminates the current
@rem batch script.  The former is not acceptable for general purpose
@rem and the latter stops the automated build system from detecting
@rem errors.
@rem
@rem Using 'shift' on command line arguments
@rem does not affect $* expand arguments thy self.
@rem
if not "%1" == "" call %1 %2 %3 %4 %5 %6 %7 %8 %9


