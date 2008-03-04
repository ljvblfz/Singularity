// ----------------------------------------------------------------------------
//
// Program: RunAll
//
// Purpose: Run all processes simultaneously with optional fatal error
//          on any process failure or timeout.  This is intended to be
//          used by the automated build process to wrapper running
//          bootd and the kernel debugger side-by-side.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ----------------------------------------------------------------------------

#define _WIN32_WINNT 0x0500

#include <winlean.h>

#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

static BOOL g_bVerbose = FALSE;

#define tprintf if (g_bVerbose) printf

static BOOL WINAPI CtrlHandler(DWORD dwCtrlType)
{
    printf("Caught control event %I32u", dwCtrlType);
    switch (dwCtrlType) {
      case CTRL_C_EVENT:
      case CTRL_BREAK_EVENT:
        GenerateConsoleCtrlEvent(dwCtrlType, 0);
      default:
        break;
    }
    ExitProcess((UINT)-2);
}

static void
ErrorReport(char *function)
{
    VOID *message;
    DWORD error = GetLastError();

    FormatMessage(
                  FORMAT_MESSAGE_ALLOCATE_BUFFER |
                  FORMAT_MESSAGE_FROM_SYSTEM,
                  NULL,
                  error,
                  MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
                  (LPTSTR) &message,
                  0, NULL );

    printf("runall: %s failed with error %d: %s",
           function, error, message);

    LocalFree(message);
}

void usage()
{
    printf("Usage: runall [options] cmd [cmd1] ...\n\n");
    printf("Options:\n");
    printf("  /T:seconds    - Set timeout in seconds.");
    printf("Runs specified commands in parallel and wait for all to terminate successfully.  The program returns -1 if any process fails to launch, or exits with a non zero error-code, or the option timeout expires.");
}

static BOOL
ParseInt64(LPCSTR lpszValue, PINT64 pValue)
{
    PCHAR pszStop;

    errno = 0;
    *pValue = _strtoi64(lpszValue, &pszStop, 10);

    // Return no overflow and all characters parsed.
    return errno == 0 && *pszStop == '\0';
}

static int
RunAndWait(int                   nProcesses,
           LPPROCESS_INFORMATION lpProcessInfo,
           LPCSTR                lpszCommandLine[],
           time_t                endTime)
{
    HANDLE hProcesses[MAXIMUM_WAIT_OBJECTS];
    int    nProcessOrdinal[MAXIMUM_WAIT_OBJECTS];
    int    i;

    for (i = 0; i < nProcesses; i++) {
        if (ResumeThread(lpProcessInfo[i].hThread) == -1) {
            ErrorReport("ResumeThread");
            return -1;
        }
        hProcesses[i]      = lpProcessInfo[i].hProcess;
        nProcessOrdinal[i] = i;
    }

    while (nProcesses > 0) {
        DWORD  dwMilliseconds;
        DWORD  dwResult;
        time_t t;

        time(&t);
        if (t > endTime) {
            printf("runall: timeout\n");
            return -1;
        }

        t = (endTime - t) / 1000;
        dwMilliseconds = (DWORD)((t < INFINITE) ? t : INFINITE - 1);

        dwResult = WaitForMultipleObjects(nProcesses,
                                          hProcesses,
                                          FALSE,
                                          dwMilliseconds);
        if (dwResult == WAIT_TIMEOUT) {
        }
        else if (dwResult >= WAIT_ABANDONED_0 &&
                 dwResult <= WAIT_ABANDONED_0 + nProcesses) {
            printf("runall: wait abandoned (process %d)\n",
                   dwResult - WAIT_ABANDONED_0);
            return -1;
        }
        else {
            DWORD dwProcess = dwResult - WAIT_OBJECT_0;
            DWORD dwExitCode;

            hProcesses[dwProcess] = hProcesses[--nProcesses];
            nProcessOrdinal[dwProcess] = nProcessOrdinal[nProcesses];

            if (GetExitCodeProcess(hProcesses[dwProcess], &dwExitCode) == 0) {
                ErrorReport("GetExitCodeProcess");
                return -1;
            }

            printf("runall: Command \"%s\" => exited %d\n",
                   lpszCommandLine[nProcessOrdinal[dwProcess]],
                   dwExitCode);

            if (dwExitCode != 0) {
                return -1;
            }
        }
    }
    return 0;
}

static int
RunAll(int nProcesses, LPCSTR lpszCommandLine[], time_t endTime)
{
    PROCESS_INFORMATION pi[MAXIMUM_WAIT_OBJECTS];
    STARTUPINFO         si;

    int exitCode = -1;
    int i;

    for (i = 0; i < nProcesses; i++) {
        DWORD dwCreationFlags = CREATE_SUSPENDED | CREATE_NO_WINDOW;
        ZeroMemory(&si, sizeof(si));
        si.cb = sizeof(si);
        if (!CreateProcess(NULL, (LPSTR)lpszCommandLine[i], NULL, NULL, FALSE,
                           dwCreationFlags, NULL, NULL, &si, &pi[i])) {
            ErrorReport("CreateProcess");
            break;
        }
        tprintf("runall: Created PID %u: %s\n",
                pi[i].dwProcessId, lpszCommandLine[i]);
    }

    if (i == nProcesses) {
        exitCode = RunAndWait(nProcesses, pi, lpszCommandLine, endTime);
    }

    while (--i >= 0) {
        if (TerminateProcess(pi[i].hProcess, -1)) {
            tprintf("runall: Terminated PID %u: %s\n",
                    pi[i].dwProcessId, lpszCommandLine[i]);
        }
    }
    return exitCode;
}

int
main(int argc, LPCSTR argv[])
{
    BOOL   bLaunchFailure = FALSE;
    time_t endTime        = _I64_MAX;
    INT64  timeout        = 0;

    argc--;
    argv++;

    while (argc != 0 && (argv[0][0] == '/' || argv[0][0] == '-')) {
        switch (tolower(argv[0][1])) {
            case 't':
                if (argv[0][2] == ':' && ParseInt64(&argv[0][3], &timeout)) {
                    time(&endTime);
                    endTime += (time_t)timeout;
                }
                else {
                    printf("Bad timeout argument.");
                    return -1;
                }
                break;
            case 'v':
                g_bVerbose = TRUE;
            break;
            default:
                usage();
                return -1;
        }
        argc --;
        argv ++;
    }

    if (argc == 0) {
        usage();
        return -1;
    } else if (argc > MAXIMUM_WAIT_OBJECTS) {
        printf("Supported process limit exceeded.");
        return -1;
    }

    SetConsoleCtrlHandler(CtrlHandler, TRUE);

    return RunAll(argc, argv, endTime);
}
