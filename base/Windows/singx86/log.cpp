/////////////////////////////////////////////////////////////////////////////
//
//  log.cpp - Extension to find parse Singularity trace log.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
#include "singx86.h"

bool fCachedLogStateValid = false;

static ULONG64 c_txtBegin   = 0;
static ULONG64 c_txtLimit   = 0;
static ULONG64 c_txtHead    = 0;
static ULONG64 c_logBegin   = 0;
static ULONG64 c_logLimit   = 0;
static ULONG64 c_logHead    = 0;
static LONG    c_logHeadPos = 0;
static LONG    c_logCount   = 0;

static ULONG64 c_tscOffsets = 0;

static bool    difftsc              = true;
static ULONG64 tsc                  = 0;
static bool    filter_by_cpu        = false;
static bool    filter_by_process    = false;
static bool    filter_by_thread     = false;
static bool    filter_by_tag        = false;
static int     filter_by_class      = 0;
static int     filter_by_not_class  = 0;
static int     filter_by_method     = false;
static int     filter_by_not_method = false;
static bool    filter_by_severity   = false;
static LONG    filter_range_low     = 0;
static LONG    filter_range_high    = 0;

static int filter_cpu     = 0;
static int filter_process = 0;
static int filter_thread  = 0;
static int filter_tag     = 0;
static char filter_class[16][128];
static int filter_class_wild[16];
static bool filter_class_not[16];
static char filter_method[16][128];
static int filter_method_wild[16];
static bool filter_method_not[16];
static int filter_severity = 0;

static char szSymbol[512];
static char *pszSymbol;
static char *pszClass;
static char *pszMethod;

//////////////////////////////////////////////////////////////////////////////
//
static HRESULT Usage()
{
    ExtOut("Usage:\n"
           "    !log {options} {entry}\n"
           "    !log clear\n"
           "Options:\n"
           "    -a                : Print all matching entries.\n"
           "    -c class          : Filter by class (can use tail wild card like `cl*').\n"
           "    -nc class         : Filter out class (can use tail wild card).\n"
           "    -m method         : Filter by method (can use tail wild card).\n"
           "    -nc method        : Filter out method (can use tail wild card).\n"
           "    -p pid            : Filter by process ID.\n"
           "    -t tid            : Filter by thread ID.\n"
           "    -u cpuid          : Filter by cpu ID.\n"
           "    -x tag            : Filter by tag.\n"
           "    -s severity       : Filter by severity.\n"
           "    -r begin[,number] : Filter by range of entry ids.\n"
           "    -n [skip,]number  : Print at most `number' entries (default is 10).\n"
           "    -f                : Display full timespaces instead of indexes and diffs.\n"
           "    -q                : Quiet, not class and method.\n"
           "Examples:\n"
           "    !log -c timer* -d            : Print last entries from timer* class with diffs.\n"
           "    !log -a -c timer* -c irq*    : Print all timer or irq entries.\n"
           "    !log -n 15 -p 020            : Print last 15 entries for process 020.\n"
           "    !log -n 5,10                 : Print the first 10 of the last 15.\n"
           "    !log -c t* -nc ti*           : Print class entries starting with t, but not ti.\n"
           "    !log clear                   : Clear the log.\n"
           "Notes:\n"
           "    Log index numbers count backwards in time from 0.\n"
           "    0 is the most recent log entry, 1 the entry before that, etc.\n"
           "    The -c and -m filters are applied first, followed by -nc and -nm filters.\n"
           "    String and method name comparisons are case insensitive.\n"
          );
    return S_FALSE;
}

HRESULT DumpLogHead(bool detail)
{
    if (detail) {
        ExtOut("     Cycle Count: CPU   EIP    PID TID TAG S Class.Method             : Message\n");
    }
    else {
        ExtOut("     Cycle Count: CPU   EIP    PID TID TAG S: Message\n");
    }
    return S_OK;
}

HRESULT FindBound(const char *symbol, ULONG64 *ptrval)
{
    HRESULT status = S_OK;
    ULONG64 address;

    EXT_CHECK(g_ExtSymbols->GetOffsetByName(symbol, &address));
    EXT_CHECK(g_ExtData->ReadPointersVirtual(1, address, ptrval));

    ExtVerb("Find(%s) = %p\n", symbol, *ptrval);
  Exit:
    return status;
}

HRESULT SetBound(const char *symbol, ULONG64 ptrval)
{
    HRESULT status = S_OK;
    ULONG64 address;

    EXT_CHECK(g_ExtSymbols->GetOffsetByName(symbol, &address));
    EXT_CHECK(g_ExtData->WritePointersVirtual(1, address, &ptrval));
  Exit:
    return status;
}

HRESULT FindBounds()
{
    HRESULT status = S_OK;

    if (!fCachedLogStateValid) {

        EXT_CHECK(FindBound("nt!Class_Microsoft_Singularity_Tracing::c_txtBegin",
                            &c_txtBegin));
        EXT_CHECK(FindBound("nt!Class_Microsoft_Singularity_Tracing::c_txtLimit",
                            &c_txtLimit));
        EXT_CHECK(FindBound("nt!Class_Microsoft_Singularity_Tracing::c_txtHead",
                            &c_txtHead));

        EXT_CHECK(FindBound("nt!Class_Microsoft_Singularity_Tracing::c_logBegin",
                            &c_logBegin));
        EXT_CHECK(FindBound("nt!Class_Microsoft_Singularity_Tracing::c_logLimit",
                            &c_logLimit));
        EXT_CHECK(FindBound("nt!Class_Microsoft_Singularity_Tracing::c_logHead",
                            &c_logHead));
        EXT_CHECK(FindBound("nt!Class_Microsoft_Singularity_Tracing::c_logHead",
                            &c_logHead));
        EXT_CHECK(FindBound("nt!Class_Microsoft_Singularity_Tracing::c_tscOffsets",
                            &c_tscOffsets));

        c_logHeadPos = (LONG)((c_logHead - c_logBegin) / LogEntryStruct.size);
        c_logCount = (LONG)((c_logLimit - c_logBegin) / LogEntryStruct.size);

        fCachedLogStateValid = true;
        status = S_OK;
    }

  Exit:
    return status;
}

static LONG64 GetTscOffset(int cpuId, PLONG64 pValue)
{
    HRESULT status = S_OK;

    ULONG64 address = c_tscOffsets + cpuId * sizeof(LONG64);
    EXT_CHECK(g_ExtData->ReadPointersVirtual(1, address, (PULONG64)pValue));
    // ExtOut("Cpu %d tscOffset %I64d\n", cpuId, *pValue);
  Exit:
    return status;
}

static ULONG64 IdToEntry(LONG id)
{
    LONG pos = (c_logHeadPos + (c_logCount - (id + 1))) % c_logCount;
    ULONG64 entry = c_logBegin + pos * LogEntryStruct.size;
    return entry;
}

static LONG EntryToId(ULONG64 entry)
{
    LONG pos = (ULONG)((entry - c_logBegin) / LogEntryStruct.size);
    return ((c_logCount - (pos + 1)) + c_logHeadPos) % c_logCount;
}

HRESULT FilterLogEntry(LONG id, LogEntry& log, bool detail)
{
    pszSymbol = NULL;
    pszClass = NULL;
    pszMethod = NULL;

    HRESULT status = S_OK;

    if ((ULONG64)log._text < c_txtBegin || (ULONG64)log._text > c_txtLimit) {
        return S_FALSE;
    }
    if (id < filter_range_low || id >= filter_range_high) {
        return S_FALSE;
    }
    if (filter_by_cpu && log._cpuId != filter_cpu) {
        return S_FALSE;
    }
    if (filter_by_process && log._processId != filter_process) {
        return S_FALSE;
    }
    if (filter_by_thread && log._threadId != filter_thread) {
        return S_FALSE;
    }
    if (filter_by_tag && log._tag != filter_tag) {
        return S_FALSE;
    }
    if (filter_by_severity && log._severity < filter_process) {
        return S_FALSE;
    }

    if (filter_by_class > 0 || filter_by_method > 0 || detail) {
        ULONG64 displacement = 0;

        status = g_ExtSymbols->GetNameByOffset((ULONG64)log._eip,
                                               szSymbol,
                                               arrayof(szSymbol),
                                               NULL,
                                               &displacement);
        if (status == S_OK) {
            CHAR *p;
            pszSymbol = szSymbol;

            if ((p = strrchr(szSymbol, '!')) != NULL) {
                pszSymbol = p + 1;
            }
            if ((p = strchr(szSymbol, ':')) != NULL && p[1] == ':') {
                pszClass = pszSymbol;
                pszMethod = p + 2;
                *p = '\0';

                if ((p = strrchr(pszClass, '_')) != NULL) {
                    pszClass = p + 1;
                }
                if ((p = strrchr(pszMethod, '_')) != NULL) {
                    pszMethod = p + 1;
                }
            }
        }

        PCHAR pszC = pszClass != NULL ? pszClass : (pszSymbol != NULL ? pszSymbol : "::");
        PCHAR pszM = pszMethod ? pszMethod : "::";

        // Do inclusive filters first.
        if (filter_by_class > filter_by_not_class) {
            bool found = false;
            for (int i = 0; i < filter_by_class; i++) {
                if (filter_class_not[i]) {
                    continue;
                }
                if (filter_class_wild[i] > 0) {
                    if (_strnicmp(filter_class[i], pszC, filter_class_wild[i]) == 0) {
                        found = true;
                        break;
                    }
                }
                else {
                    if (_strcmpi(filter_class[i], pszC) == 0) {
                        found = true;
                        break;
                    }
                }
            }
            if (!found) {
                return S_FALSE;
            }
        }
        if (filter_by_method > filter_by_not_method) {
            if (pszMethod == NULL) {
                return S_FALSE;
            }
            bool found = false;
            for (int i = 0; i < filter_by_method; i++) {
                if (filter_method_not[i]) {
                    continue;
                }
                if (filter_method_wild[i] > 0) {
                    if (_strnicmp(filter_method[i], pszM, filter_method_wild[i]) == 0) {
                        found = true;
                        break;
                    }
                }
                else {
                    if (_strcmpi(filter_method[i], pszM) == 0) {
                        found = true;
                        break;
                    }
                }
            }
            if (!found) {
                return S_FALSE;
            }
        }

        // Then do exclusive filters.
        if (filter_by_not_class > 0) {
            for (int i = 0; i < filter_by_class; i++) {
                if (!filter_class_not[i]) {
                    continue;
                }
                if (filter_class_wild[i] > 0) {
                    if (_strnicmp(filter_class[i], pszC, filter_class_wild[i]) == 0) {
                        return S_FALSE;
                    }
                }
                else {
                    if (_strcmpi(filter_class[i], pszC) == 0) {
                        return S_FALSE;
                    }
                }
            }
        }
        if (filter_by_not_method > 0) {
            for (int i = 0; i < filter_by_method; i++) {
                if (!filter_method_not[i]) {
                    continue;
                }
                if (filter_method_wild[i] > 0) {
                    if (_strnicmp(filter_method[i], pszM, filter_method_wild[i]) == 0) {
                        return S_FALSE;
                    }
                }
                else {
                    if (_strcmpi(filter_method[i], pszM) == 0) {
                        return S_FALSE;
                    }
                }
            }
        }
    }
    return S_OK;
}

static PCHAR Skip(PCHAR psz)
{
    while (*psz) {
        psz++;
    }
    return psz + 1;
}


HRESULT DumpLogEntry(LONG id, LogEntry& log, bool detail)
{
    HRESULT status = S_OK;

    CHAR msg[512];
    BYTE text[256];
    ULONG ret;
    EXT_CHECK(g_ExtData->ReadVirtual((ULONG64)log._text, text, sizeof(text), &ret));
    if (text[0] != (BYTE)log._cycleCount) {
        sprintf(msg, "<%p: message not available>", log._text);
    }
    else {
        CHAR *  pmsg = msg;
        CHAR *  ptxt = (CHAR *)(text + 1);
        CHAR *  sargs[6];
        CHAR *  pstr = Skip(ptxt);

        for (int i = 0 ; i < arrayof(sargs); i++) {
            if ((log._strings & ((ULONG64)1 << i)) != 0) {
                sargs[i] = pstr;
                pstr = Skip(pstr);
            }
            else {
                sargs[i] = NULL;
            }
        }

        while (*ptxt != '\0') {
            if (*ptxt == '{') {
                char * pbeg = ptxt;
                bool bad = false;
                ptxt++;

                int ndx = 0;
                int aln = 0;
                int wid = 0;
                char fmt = 'd';

                if (*ptxt == '{') {
                    *pmsg++ = *ptxt++;
                }
                else if (*ptxt >= '0' && *ptxt <= '9') {

                    // {index,alignment:type width}
                    // Get Index
                    while (*ptxt >= '0' && *ptxt <= '9') {
                        ndx = ndx * 10 + (*ptxt++ - '0');
                    }

                    // Get Alignment
                    if (*ptxt == ',') {
                        ptxt++;
                        while (*ptxt >= '0' && *ptxt <= '9') {
                            aln = aln * 10 + (*ptxt++ - '0');
                        }
                    }

                    // Get FormatString
                    if (*ptxt == ':') {
                        ptxt++;
                        if (*ptxt >= 'a' && *ptxt <= 'z') {
                            fmt = *ptxt++;
                        }
                        else if (*ptxt >= 'A' && *ptxt <= 'Z') {
                            fmt = *ptxt++ - 'A' + 'a';
                        }
                        while (*ptxt >= '0' && *ptxt <= '9') {
                            wid = wid * 10 + (*ptxt++ - '0');
                        }
                    }

                    // Get closing brace.
                    if (*ptxt == '}') {
                        ptxt++;
                    }
                    else {
                        bad = true;
                        ExtErr("Missing closing brace: `%s'\n", ptxt);
                    }

                    if (ndx >= arrayof(sargs)) {
                        bad = true;
                        ExtErr("Aln too large: %d\n", ndx);
                    }

                    if (sargs[ndx] != NULL) {
                        if (wid > 0) {
                            if (aln < wid) {
                                aln = wid;
                            }
                            pmsg += sprintf(pmsg, "%*.*s", aln, wid, sargs[ndx]);
                        }
                        else {
                            pmsg += sprintf(pmsg, "%*s", aln, sargs[ndx]);
                        }
                    }
                    else {
                        if (aln < wid) {
                            aln = wid;
                        }
                        if (fmt == 'x') {
                            if (wid > 0) {
                                pmsg += sprintf(pmsg, "%0*x", aln, log._args[ndx]);
                            }
                            else {
                                pmsg += sprintf(pmsg, "%*x", aln, log._args[ndx]);
                            }
                        }
                        else {
                            pmsg += sprintf(pmsg, "%*d", aln, log._args[ndx]);
                        }
                    }

                    // If the format was bad, then copy it.
                    if (bad) {
                        ExtErr("Format error: `%*s'\n", ptxt - pbeg, pbeg);
                        while (pbeg < ptxt) {
                            *pmsg++ = *pbeg++;
                        }
                    }
                }
            }
            else if (*ptxt == '}') {
                ptxt++;
                *pmsg++ = *ptxt++;
            }
            else if (*ptxt == '\n') {
                ptxt++;
            }
            else {
                *pmsg++ = *ptxt++;
            }
        }
        *pmsg++ = '\0';
    }

    LONG64 tscOffset = 0;
    if (GetTscOffset((int)log._cpuId, &tscOffset) != S_OK) {
        tscOffset = 0;
    }

    ULONG64 cycleCount = log._cycleCount - tscOffset;
    if (difftsc) {
        if (tsc == 0) {
            ExtOut("%16I64x: ", cycleCount);
        }
        else {
            ExtOut("%5d: %9I64x+ ", id, cycleCount - tsc);
        }
    }
    else {
        ExtOut("%16I64x: ", cycleCount);
    }
    tsc = cycleCount;

    if (detail) {
        CHAR name[512] = "";

        if (pszClass != NULL && pszMethod != NULL) {
            sprintf(name, "%s.%s", pszClass, pszMethod);
        }
        else if (pszSymbol != NULL) {
            strcat(name, pszSymbol);
        }

        ExtOut("%03x %p %03x %03x %03x %x %-25.25s: %s\n",
               (ULONG)log._cpuId,
               log._eip,
               (ULONG)log._processId,
               (ULONG)log._threadId,
               (ULONG)log._tag,
               (ULONG)log._severity,
               name,
               msg);
    }
    else {
        ExtOut("%03x %p %03x %03x %03x %x: %s\n",
               (ULONG)log._cpuId,
               log._eip,
               (ULONG)log._processId,
               (ULONG)log._threadId,
               (ULONG)log._tag,
               (ULONG)log._severity,
               msg);
    }

    status = S_OK;
  Exit:
    return status;
}

HRESULT ClearLog()
{
    HRESULT status = S_OK;

    ExtOut("Clearing %p..%p\n", c_logBegin, c_logLimit);
    LogEntryStruct.Clear();
    for (ULONG64 entry = c_logBegin; entry < c_logLimit; entry += LogEntryStruct.size) {
        if ((entry & 0xff) == 0) {
            ExtOut(" %p\r", entry);
        }
        EXT_CHECK(LogEntryStruct.Flush(entry));
    }
    ExtOut("\n");
    EXT_CHECK(SetBound("nt!Class_Microsoft_Singularity_Tracing::c_txtHead", c_txtBegin));
    EXT_CHECK(SetBound("nt!Class_Microsoft_Singularity_Tracing::c_logHead", c_logBegin));

  Exit:
    return status;
}

static ULONG64 GetValue(PCSTR& args, bool fHex)
{
    ULONG base = fHex ? 16 : 10;
    if (*args == '0') {
        fHex = true;
        base = 16;
    }
    ULONG64 value = 0;

    while (*args && *args != ' ' && *args != '\t') {
        if (*args >= '0' && *args <= '9') {
            value = value * base + (*args++ - '0');
        }
        else if (*args >= 'A' && *args <= 'F' && fHex) {
            value = value * base + (*args++ - 'A') + 10;
        }
        else if (*args >= 'a' && *args <= 'f' && fHex) {
            value = value * base + (*args++ - 'a') + 10;
        }
        else {
            break;
        }
    }
    return value;
}

EXT_DECL(log) // Defines: PDEBUG_CLIENT Client, PCSTR args
{
    EXT_ENTER();    // Defines: HRESULT status = S_OK;
    bool detail = true;
    bool doall = false;

    status = FindBounds();

#if TEST_ENTRY
    if (fCachedLogStateValid) {
        for (LONG id = 0; id < 10; id++) {
            ULONG64 entry = IdToEntry(id);
            ULONG bid = EntryToId(entry);
            ExtOut("%4d: %p :%4d\n", id, entry, bid);
        }
        for (LONG id = c_logCount - 10; id < c_logCount; id++) {
            ULONG64 entry = IdToEntry(id);
            ULONG bid = EntryToId(entry);
            ExtOut("%4d: %p :%4d\n", id, entry, bid);
        }
        goto Exit;
    }
#endif

    difftsc = true;

    filter_range_low  = 0;
    filter_range_high = c_logCount;

    filter_by_cpu        = false;
    filter_by_process    = false;
    filter_by_thread     = false;
    filter_by_tag        = false;
    filter_by_class      = 0;
    filter_by_not_class  = 0;
    filter_by_method     = 0;
    filter_by_not_method = 0;
    filter_by_severity   = false;

    filter_cpu      = 0;
    filter_process  = 0;
    filter_thread   = 0;
    filter_tag      = 0;
    filter_severity = 0;

    int limit = 10;
    int skip = 0;
    char *p;

    tsc = 0;
    if (_strcmpi(args, "clear") == 0) {
        return ClearLog();
    }

    while (*args != '\0') {
        // skip whitespace
        while (*args == ' ' || *args == '\t') {
            args++;
        }

        // process argument
        if (*args == '-' || *args == '/') {
            args++;
            switch (*args++) {
              case 'q': // Quiet
              case 'Q':
                detail = false;
                break;

              case 'f': // Full timestamps, not diffs (not absolute value)
              case 'F':
                difftsc = false;
                break;

              case 'a': // All records
              case 'A':
                doall = true;
                limit = 100000;
                break;

              case 'n':
              case 'N':
                if (*args == 'c') {
                    // Not class
                    args++;
                    while (*args == ' ' || *args == '\t') {
                        args++;
                    }
                    filter_class[filter_by_class][0] = '\0';
                    filter_class_wild[filter_by_class] = 0;
                    filter_class_not[filter_by_class] = true;
                    p = filter_class[filter_by_class];
                    while (*args && *args != ' ' && *args != '\t') {
                        *p++ = *args++;
                    }
                    *p = '\0';
                    if ((p = strchr(filter_class[filter_by_class], '*')) != NULL) {
                        filter_class_wild[filter_by_class] = p - filter_class[filter_by_class];
                    }
                    ExtVerb("Filter out class: `%s'\n", filter_class[filter_by_class]);
                    filter_by_class++;
                    filter_by_not_class++;
                }
                else if (*args == 'm') {
                    // Not method
                    args++;
                    while (*args == ' ' || *args == '\t') {
                        args++;
                    }
                    filter_method[filter_by_method][0] = '\0';
                    filter_method_wild[filter_by_method] = 0;
                    filter_method_not[filter_by_method] = true;
                    p = filter_method[filter_by_method];
                    while (*args && *args != ' ' && *args != '\t') {
                        *p++ = *args++;
                    }
                    *p = '\0';
                    if ((p = strchr(filter_method[filter_by_method], '*')) != NULL) {
                        filter_method_wild[filter_by_method] = p - filter_method[filter_by_method];
                    }
                    ExtVerb("Filter not method: `%s'\n", filter_method[filter_by_method]);
                    filter_by_method++;
                    filter_by_not_method++;
                }
                else {
                    // Number of entries to display
                    while (*args == ' ' || *args == '\t') {
                        args++;
                    }
                    limit = (int)GetValue(args, false);
                    if (*args == ',') {
                        skip = limit;
                        args++;
                        limit = (int)GetValue(args, false);
                    }
                    ExtVerb("Limit to: %d (skip %d)\n", limit, skip);
                }
                break;

              case 'r': // filter by range
              case 'R':
                while (*args == ' ' || *args == '\t') {
                    args++;
                }
                filter_range_low = (LONG)GetValue(args, false);
                if (filter_range_low < 0) {
                    filter_range_low = 0;
                }
                if (filter_range_low >= c_logCount) {
                    filter_range_low = c_logCount - 1;
                }
                if (*args == ',') {
                    args++;
                    filter_range_high = filter_range_low + (LONG)GetValue(args, false);
                    if (filter_range_high > c_logCount) {
                        filter_range_high = c_logCount;
                    }
                }
                ExtVerb("Range %d..%d\n", filter_range_low, filter_range_high);
                break;

              case 'u': // filter by cpu
              case 'U':
                filter_by_cpu = true;
                while (*args == ' ' || *args == '\t') {
                    args++;
                }
                filter_cpu = (int)GetValue(args, true);
                ExtVerb("Filter by cpu: 0x%x\n", filter_cpu);
                break;

              case 'p': // filter by process
              case 'P':
                filter_by_process = true;
                while (*args == ' ' || *args == '\t') {
                    args++;
                }
                filter_process = (int)GetValue(args, true);
                ExtVerb("Filter by process: 0x%x\n", filter_process);
                break;

              case 't': // filter by thread
              case 'T':
                filter_by_thread = true;
                while (*args == ' ' || *args == '\t') {
                    args++;
                }
                filter_thread = (int)GetValue(args, true);
                ExtVerb("Filter by tid: 0x%x\n", filter_thread);
                break;

              case 'x': // filter by tag
              case 'X':
                filter_by_tag = true;
                while (*args == ' ' || *args == '\t') {
                    args++;
                }
                filter_tag = (int)GetValue(args, true);
                ExtVerb("Filter by tag: 0x%x\n", filter_tag);
                break;

              case 'c': // filter by class
              case 'C':
                while (*args == ' ' || *args == '\t') {
                    args++;
                }
                filter_class[filter_by_class][0] = '\0';
                filter_class_wild[filter_by_class] = 0;
                filter_class_not[filter_by_class] = false;
                p = filter_class[filter_by_class];
                while (*args && *args != ' ' && *args != '\t') {
                    *p++ = *args++;
                }
                *p = '\0';
                if ((p = strchr(filter_class[filter_by_class], '*')) != NULL) {
                    filter_class_wild[filter_by_class] = p - filter_class[filter_by_class];
                }
                ExtVerb("Filter by class: `%s'\n", filter_class[filter_by_class]);
                filter_by_class++;
                break;

              case 'm': // filter by method
              case 'M':
                while (*args == ' ' || *args == '\t') {
                    args++;
                }
                filter_method[filter_by_method][0] = '\0';
                filter_method_wild[filter_by_method] = 0;
                filter_method_not[filter_by_method] = false;
                p = filter_method[filter_by_method];
                while (*args && *args != ' ' && *args != '\t') {
                    *p++ = *args++;
                }
                *p = '\0';
                if ((p = strchr(filter_method[filter_by_method], '*')) != NULL) {
                    filter_method_wild[filter_by_method] = p - filter_method[filter_by_method];
                }
                ExtVerb("Filter by method: `%s'\n", filter_method[filter_by_method]);
                filter_by_method++;
                break;

              case 's': // filter by severity
              case 'S':
                filter_by_severity = true;
                while (*args == ' ' || *args == '\t') {
                    args++;
                }
                filter_severity = (int)GetValue(args, true);
                break;

              case '?': // Help
              case 'h':
              case 'H':
                status = Usage();
                goto Exit;
            }
            while (*args && *args != ' ') {
                args++;
            }
        }
        else {
            break;
        }
    }

    DumpLogHead(detail);

    ULONG64 entry = 0;
    LogEntry log;

    if (*args != '\0') {
        status = ExtEvalU64(&args, &entry);
        if (status == S_OK) {
            if (entry < c_logBegin || entry >= c_logLimit) {
                ExtErr("Entry value %p not supported.\n", entry);
                return S_FALSE;
            }
            EXT_CHECK(LogEntryStruct.Read(entry, &log));
            LONG id = EntryToId(entry);
            status = FilterLogEntry(id, log, detail);
            status = DumpLogEntry(id, log, detail);
            goto Exit;
        }
        else {
            ExtErr("Invalid argument %s.\n", args);
            goto Exit;
        }
    }

    ExtVerb("Range [%d..%d], Skip=%d, Limit=%d\n",
            filter_range_low, filter_range_high, skip, limit);

    // First, we search backward until we find the right number of records
    if (!doall) {
        int find = limit + skip;
        for (LONG id = filter_range_low; id < filter_range_high; id++) {
            entry = IdToEntry(id);

            EXT_CHECK(LogEntryStruct.Read(entry, &log));
            if (log._eip == 0 || log._cycleCount == 0) {
                continue;
            }

            status = FilterLogEntry(id, log, detail);
            if (status == S_OK) {
                find--;
                if (find < 0) {
                    filter_range_high = id;
                    break;
                }
            }
        }
    }
    ExtVerb("Begin: %p, Head: %p, Limit: %p, Base: %p [%d..%d]\n",
            c_logBegin, c_logHead, c_logLimit, entry, filter_range_low, filter_range_high);

    for (LONG id = filter_range_high - 1; id >= filter_range_low; id--) {
        entry = IdToEntry(id);

        EXT_CHECK(LogEntryStruct.Read(entry, &log));
        if (log._eip == 0 || log._cycleCount == 0) {
            continue;
        }

        status = FilterLogEntry(id, log, detail);
        if (status != S_OK) {
            continue;
        }

        status = DumpLogEntry(id, log, detail);
        limit--;
        if (limit <= 0) {
            break;
        }
    }

    EXT_LEAVE();    // Macro includes: return status;
}
