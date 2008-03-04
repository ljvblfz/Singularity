//////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:       mkcore.cpp
//
//  Contents:   Creates a core file from one or more PE image by physically
//              aligning and allocating all of the sections.
//
//  Owner:
//
//  History:    2003/10/10             Created from mkflat.
//
#define UNICODE
#define _UNICODE

#include <stdlib.h>
#include <stdio.h>
#include <stddef.h>
#include <winlean.h>
#include <assert.h>

//////////////////////////////////////////////////////////////////////////////
//
static BOOL s_fVerbose = FALSE;

typedef signed int INT;
typedef signed char INT8, *LPINT8;
typedef signed short INT16, *LPINT16;
typedef unsigned int UINT;
typedef unsigned char UINT8, *LPUINT8;
typedef unsigned short UINT16, *LPUINT16;

#include "minidump.h"

#define arrayof(a)      (sizeof(a)/sizeof(a[0]))

//////////////////////////////////////////////////////////////////////////////
//
static inline UINT Max(UINT a, UINT b)
{
    return a > b ? a : b;
}

static inline UINT Min(UINT a, UINT b)
{
    return a < b ? a : b;
}

static inline UINT Align(UINT nValue, UINT nPowerOf2)
{
    return (nValue + nPowerOf2 - 1) & ~(nPowerOf2 - 1);
}

DWORD RvaToFileOffset(DWORD nRva, UINT nSecs, PIMAGE_SECTION_HEADER pSecs)
{
    DWORD n;
    for (n = 0; n < nSecs; n++) {
        DWORD vaStart = pSecs[n].VirtualAddress;
        DWORD vaEnd = vaStart + pSecs[n].Misc.VirtualSize;

        if (nRva >= vaStart && nRva < vaEnd) {
            return pSecs[n].PointerToRawData + nRva - pSecs[n].VirtualAddress;
        }
    }
    return 0;
}

BOOL error(PCSTR pwzError)
{
    fprintf(stderr, "%s\n", pwzError);
    fclose(stderr);
    return FALSE;
}

void Dump(PBYTE pbData, ULONG cbData)
{
    for (ULONG n = 0; n < cbData; n += 16) {
        printf("        ");
        for (ULONG o = n; o < n + 16; o++) {
            if (o >= cbData) {
                printf("  ");
            }
            else {
                printf("%02x", pbData[o]);
            }
            if (o % 4 == 3) {
                printf(" ");
            }
        }
        printf(" ");
        for (ULONG o = n; o < n + 16; o++) {
            if (o >= cbData) {
                printf("  ");
            }
            else {
                if (pbData[o] >= ' ' && pbData[o] < 127) {
                    printf("%c", pbData[o]);
                }
                else {
                    printf(".");
                }
            }
        }
        printf("\n");
    }
}

//////////////////////////////////////////////////////////////////// CFileMap.
//
class CFileMap
{
  public:
    CFileMap()
    {
        m_pbData = NULL;
        m_cbData = 0;
    }

    ~CFileMap()
    {
        Close();
    }

  public:
    BOOL    Load(PCWSTR pwzFile);
    PBYTE   Seek(UINT32 cbPos);
    UINT32  Size();
    VOID    Close();

  protected:
    PBYTE   m_pbData;
    UINT32  m_cbData;
};

VOID CFileMap::Close()
{
    if (m_pbData) {
        UnmapViewOfFile(m_pbData);
        m_pbData = NULL;
    }
    m_cbData = 0;
}

UINT32 CFileMap::Size()
{
    return m_cbData;
}

PBYTE CFileMap::Seek(UINT32 cbPos)
{
    if (m_pbData && cbPos <= m_cbData) {
        return m_pbData + cbPos;
    }
    return NULL;
}

BOOL CFileMap::Load(PCWSTR pwzFile)
{
    Close();

    HANDLE hFile = CreateFile(pwzFile,
                              GENERIC_READ,
                              FILE_SHARE_READ,
                              NULL,
                              OPEN_EXISTING,
                              0,
                              NULL);
    if (hFile == INVALID_HANDLE_VALUE) {
        return FALSE;
    }

    ULONG cbInFileData = GetFileSize(hFile, NULL);
    if (cbInFileData == 0xffffffff) {
        CloseHandle(hFile);
        return FALSE;
    }

    HANDLE hInFileMap = CreateFileMapping(hFile, NULL, PAGE_WRITECOPY, 0, 0, NULL);
    CloseHandle(hFile);
    if (hInFileMap == NULL) {
        return FALSE;
    }

    m_pbData = (PBYTE)MapViewOfFile(hInFileMap, FILE_MAP_COPY, 0, 0, 0);
    CloseHandle(hInFileMap);
    if (m_pbData == NULL) {
        return FALSE;
    }
    m_cbData = cbInFileData;
    return TRUE;
}

//////////////////////////////////////////////////////////////////////////////
//
class CArrayInternal
{
  public:
    CArrayInternal(UINT cbEach)
    {
        m_pbData = new BYTE [cbEach * 32];
        m_cbData = cbEach * 32;
        assert(m_pbData);

        m_cbEach = cbEach;
        m_cbUsed = 0;
    }

    ~CArrayInternal()
    {
        if (m_pbData != NULL) {
            delete[] m_pbData;
            m_pbData = NULL;
        }
    }

    PVOID   Seek(UINT nEntry)
    {
        return m_pbData + m_cbEach * nEntry;
    }

    PVOID   Add()
    {
        if (m_cbUsed + m_cbEach >= m_cbData) {
            UINT cbData = m_cbData + m_cbEach * 32;
            PBYTE pbData = new BYTE [cbData];
            assert(pbData);

            CopyMemory(pbData, m_pbData, m_cbUsed);
            delete[] m_pbData;

            m_pbData = pbData;
            m_cbData = cbData;
        }
        PBYTE pb = m_pbData + m_cbUsed;
        m_cbUsed += m_cbEach;
        ZeroMemory(pb, m_cbEach);

        return pb;
    }

    UINT    Count()
    {
        return m_cbUsed / m_cbEach;
    }

    UINT    Size()
    {
        return m_cbUsed;
    }

  protected:
    PBYTE   m_pbData;
    UINT    m_cbData;
    UINT    m_cbEach;
    UINT    m_cbUsed;
};

template<class T> class CArray : public CArrayInternal
{
  public:
    CArray()
        : CArrayInternal(sizeof (T)) {}

    T* Seek(UINT nEntry)
    { return (T*)CArrayInternal::Seek(nEntry); }

    T* Add()
    { return (T*)CArrayInternal::Add(); }
};

//////////////////////////////////////////////////////////////////// CFileOut.
//
class CFileOut
{
  public:
    CFileOut();
    ~CFileOut();

  public:
    BOOL    Create(PCWSTR pwzFile);

    BOOL    Seek(UINT32 cbPos);
    BOOL    Write(PBYTE pbData, UINT32 cbData);
    BOOL    Read(PBYTE pbData, UINT32 cbData);
    BOOL    Zero(UINT32 cbData);
    BOOL    Delete();
    UINT32  Size();

    UINT32  Checksum();

    VOID    Close();

  protected:
    HANDLE  m_hFile;
    WCHAR   m_wzFile[MAX_PATH];
    UINT32  m_cbPos;
};

CFileOut::CFileOut()
{
    m_hFile = INVALID_HANDLE_VALUE;
    m_wzFile[0] = '\0';
    m_cbPos = 0;
}

CFileOut::~CFileOut()
{
    Close();
}

VOID CFileOut::Close()
{
    if (m_hFile != INVALID_HANDLE_VALUE) {
        CloseHandle(m_hFile);
        m_hFile = INVALID_HANDLE_VALUE;
    }
}

BOOL CFileOut::Seek(UINT32 cbPos)
{
    if (m_hFile == INVALID_HANDLE_VALUE) {
        return FALSE;
    }
    if (SetFilePointer(m_hFile, cbPos, NULL, FILE_BEGIN) != cbPos) {
        return FALSE;
    }
    m_cbPos = cbPos;
    return TRUE;
}

BOOL CFileOut::Write(PBYTE pbData, UINT32 cbData)
{
    if (m_hFile == INVALID_HANDLE_VALUE) {
        return FALSE;
    }

    DWORD dwWrote = 0;
    if (!WriteFile(m_hFile, pbData, cbData, &dwWrote, NULL) || dwWrote != cbData) {
        return FALSE;
    }
    m_cbPos += cbData;
    return TRUE;
}

BOOL CFileOut::Zero(UINT32 cbData)
{
    if (m_hFile == INVALID_HANDLE_VALUE) {
        return FALSE;
    }

    if (cbData == 0) {
        return TRUE;
    }

    UINT zero_size = cbData < 65536 ? cbData : 65536;
    PBYTE buf = new BYTE [zero_size];
    assert(buf);

    if (!buf) {
        return FALSE;
    }

    ZeroMemory(buf, zero_size);

    for (; cbData > 0; cbData -= zero_size) {
        if (zero_size > cbData) {
            zero_size = cbData;
        }
        if (!Write(buf, zero_size)) {
            delete[] buf;
            return FALSE;
        }
    }
    delete[] buf;
    buf = NULL;

    return TRUE;
}

BOOL CFileOut::Delete()
{
    if (m_hFile != INVALID_HANDLE_VALUE) {
        Close();
        return DeleteFile(m_wzFile);
    }
    return FALSE;
}

UINT32 CFileOut::Size()
{
    return SetFilePointer(m_hFile, 0, NULL, FILE_END);
}

BOOL CFileOut::Read(PBYTE pbData, UINT32 cbData)
{
    if (m_hFile == INVALID_HANDLE_VALUE) {
        return FALSE;
    }

    DWORD dwRead = 0;
    if (!ReadFile(m_hFile, pbData, cbData, &dwRead, NULL) || dwRead != cbData) {
        return FALSE;
    }
    m_cbPos += cbData;
    return TRUE;
}

BOOL CFileOut::Create(PCWSTR pwzFile)
{
    Close();
    m_wzFile[0] = '\0';
    m_hFile = CreateFile(pwzFile,
                         GENERIC_READ | GENERIC_WRITE,
                         0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (m_hFile == INVALID_HANDLE_VALUE) {
        return FALSE;
    }
    wcscpy(m_wzFile, pwzFile);
    m_cbPos = 0;
    return TRUE;
}


UINT32 CFileOut::Checksum()
{
    if (m_hFile == INVALID_HANDLE_VALUE) {
        return FALSE;
    }

    PBYTE buf = new BYTE [65536];
    assert(buf);

    if (!buf) {
        return ~0u;
    }

    UINT32 size = Size();
    UINT32 sum = 0;

    if (!Seek(0)) {
        assert(!"Couldn't seek.");
        delete[] buf;
        return ~0u;
    }

    for (UINT32 pos = 0; pos < size;) {
        UINT32 read = size - pos < 65536 ? size - pos : 65536;

        if (!Read(buf, read)) {
            printf("Pos: %d/%d\n", pos, m_cbPos);
            printf("Len: %d\n", size);
            __asm int 3;
            assert(!"Couldn't read.");
            delete[] buf;
            return ~0u;
        }

        UINT32 *pBeg = (UINT32*)(buf);
        UINT32 *pEnd = (UINT32*)(buf + read);

        while (pBeg < pEnd) {
            sum += *pBeg++;
        }

        pos += read;
    }

    delete[] buf;
    buf = NULL;

    return sum;
}

//////////////////////////////////////////////////////////////////////////////
//
class CMiniDump
{
  public:
    struct MODULE
    {
        PUINT8  pbImageMap;
        UINT64  vaImageBase;
        UINT64  vaImageHead;
        UINT64  vaImageEntry;

        PCWSTR  pwzImage;
        UINT32  nRegions;

        PUINT8  pbCvData;
        UINT32  cbCvData;
    };

    struct BLOB
    {
        UINT64  vaBlob;
        PUINT8  pbBlob;
        UINT32  cbBlob;

        PCWSTR  pwzImage;
    };

  public:
    CMiniDump();
    ~CMiniDump();

    BOOL    Create(PCWSTR pwzFile);
    BOOL    Write();
    VOID    Close();
    BOOL    Delete();

    BOOL    Stack(UINT64 vaStack, UINT32 cbStack);
    BOOL    AddModule(PCWSTR pwzImage);
    BOOL    AddBlob(PCWSTR pwzImage, UINT64 vaBlob, UINT32 *pcbBlob);

  protected:
    BOOL    AddMemory(ULONG64 address,
                      PBYTE pbData, UINT32 cbData, UINT32 cbFull,
                      PMINIDUMP_MEMORY_DESCRIPTOR pOut);
    BOOL    AddString(PCWSTR pwzString, UINT32 *pRva);

  protected:
    ULONG   Allocate(UINT32 cbData);

    BOOL    WriteDebug(MODULE *pSelf,
                       PMINIDUMP_MODULE pModule);
    BOOL    WriteModule(MODULE *pSelf,
                        PMINIDUMP_MODULE pModule,
                        PMINIDUMP_MEMORY_DESCRIPTOR pRegions);
    BOOL    WriteBlob(BLOB *pBlob,
                      PMINIDUMP_MEMORY_DESCRIPTOR pRegions);

    BOOL    AddData(PBYTE pbData, UINT32 cbData, UINT32 cbFull,
                    MINIDUMP_LOCATION_DESCRIPTOR *pOut);
    PBYTE   AddCreate(UINT32 cbData, UINT32 *pRva);
    PBYTE   AddCreate(UINT32 cbData, MINIDUMP_LOCATION_DESCRIPTOR* pOut);

    BOOL    Write(PVOID pvData, MINIDUMP_LOCATION_DESCRIPTOR Out);

  protected:
    static int __cdecl CompareModules(const void *p1, const void *p2);
    static int __cdecl CompareBlobs(const void *p1, const void *p2);
    static int __cdecl CompareMinidumpModules(const void *p1, const void *p2);

  protected:
    CFileOut            m_Out;
    ULONG               m_cbAllocated;
    MINIDUMP_HEADER     m_Header;
    MINIDUMP_DIRECTORY  m_Directory[8];
    UINT64              m_vaImageBase;
    UINT64              m_vaImageHead;
    UINT64              m_vaImageEntry;
    UINT64              m_vaStack;
    UINT32              m_cbStack;
    CArray<MODULE>      m_Modules;
    CArray<BLOB>        m_Blobs;

    static UINT64       s_vaImageBase;

  public:
    static const UINT64 c_vaBlob = 0x7b00;
    static const UINT64 c_vaStack = 0x2fff00;
    static const UINT32 c_cbStack = 0x100;
};

UINT64 CMiniDump::s_vaImageBase = 0;

CMiniDump::CMiniDump()
{
    m_cbAllocated = sizeof(m_Header) + sizeof(m_Directory);

    ZeroMemory(&m_Header, sizeof(m_Header));
    ZeroMemory(&m_Directory, sizeof(m_Directory));

    m_Header.Signature = MINIDUMP_SIGNATURE;
    m_Header.Version = MINIDUMP_VERSION;
    m_Header.NumberOfStreams = arrayof(m_Directory);
    m_Header.StreamDirectoryRva = sizeof(MINIDUMP_HEADER);
    m_Header.CheckSum = 0;
    m_Header.TimeDateStamp = 0;
    m_Header.Flags = (MiniDumpNormal | MiniDumpWithDataSegs);

    m_vaImageBase = 0;
    m_vaImageHead = 0;
    m_vaImageEntry = 0;
    m_vaStack = c_vaStack;
    m_cbStack = c_cbStack;
}

CMiniDump::~CMiniDump()
{
    Close();
}

BOOL CMiniDump::Create(PCWSTR pwzFile)
{
    if (!m_Out.Create(pwzFile)) {
        return FALSE;
    }
    return TRUE;
}

VOID CMiniDump::Close()
{
    m_Out.Close();
}

BOOL CMiniDump::Stack(UINT64 vaStack, UINT32 cbStack)
{
    m_vaStack = vaStack;
    m_cbStack = cbStack;
    return TRUE;
}

int __cdecl CMiniDump::CompareModules(const void *p1, const void *p2)
{
    const MODULE *m1 = (const MODULE *)p1;
    const MODULE *m2 = (const MODULE *)p2;

    return (m1->vaImageBase < m2->vaImageBase) ? -1 : 1;
}

int __cdecl CMiniDump::CompareBlobs(const void *p1, const void *p2)
{
    const BLOB *m1 = (const BLOB *)p1;
    const BLOB *m2 = (const BLOB *)p2;

    return (m1->vaBlob < m2->vaBlob) ? -1 : 1;
}

int __cdecl CMiniDump::CompareMinidumpModules(const void *p1, const void *p2)
{
    const MINIDUMP_MODULE *m1 = (const MINIDUMP_MODULE *)p1;
    const MINIDUMP_MODULE *m2 = (const MINIDUMP_MODULE *)p2;

    UINT64 vaImageBase1 = m1->BaseOfImage == s_vaImageBase ? 0 : m1->BaseOfImage;
    UINT64 vaImageBase2 = m2->BaseOfImage == s_vaImageBase ? 0 : m2->BaseOfImage;

    return (vaImageBase1 < vaImageBase2) ? -1 : 1;
}

BOOL CMiniDump::Write()
{
    s_vaImageBase = m_vaImageBase;
    qsort(m_Modules.Seek(0), m_Modules.Count(), sizeof(MODULE), CompareModules);
    qsort(m_Blobs.Seek(0), m_Blobs.Count(), sizeof(BLOB), CompareBlobs);

    UINT32 nRegions = 0;

    nRegions += 1; // one for the thread stack.

    for (UINT m = 0; m < m_Modules.Count(); m++) {
        nRegions += m_Modules.Seek(m)->nRegions;
    }
    for (UINT m = 0; m < m_Blobs.Count(); m++) {
        nRegions++;
    }

    //
    m_Directory[0].StreamType = ThreadListStream;
    m_Directory[1].StreamType = ModuleListStream;
    m_Directory[2].StreamType = MemoryListStream;
    m_Directory[3].StreamType = SystemInfoStream;
    m_Directory[4].StreamType = 0xcafeface;

    // First we must allocate all of the dump before the memory regions...
    PMINIDUMP_THREAD_LIST pThreads = (PMINIDUMP_THREAD_LIST)AddCreate
        (sizeof(MINIDUMP_THREAD_LIST) + sizeof(MINIDUMP_THREAD),
         &m_Directory[0].Location);

    PMINIDUMP_MODULE_LIST pModules = (PMINIDUMP_MODULE_LIST)AddCreate
        (sizeof(MINIDUMP_MODULE_LIST) + m_Modules.Count() * sizeof(MINIDUMP_MODULE),
         &m_Directory[1].Location);

    PMINIDUMP_MEMORY_LIST pRegions = (PMINIDUMP_MEMORY_LIST)AddCreate
        (sizeof(MINIDUMP_MEMORY_LIST) + nRegions * sizeof(MINIDUMP_MEMORY_DESCRIPTOR),
         &m_Directory[2].Location);

    PMINIDUMP_SYSTEM_INFO pSystems = (PMINIDUMP_SYSTEM_INFO)AddCreate
        (sizeof(MINIDUMP_SYSTEM_INFO),
         &m_Directory[3].Location);

    pThreads->NumberOfThreads = 1;
    PCONTEXT pContext = (PCONTEXT)AddCreate(sizeof(CONTEXT),
                                            &pThreads->Threads[0].ThreadContext);

    for (UINT m = 0; m < m_Modules.Count(); m++) {
        MODULE *pSelf = m_Modules.Seek(m);

        AddString(pSelf->pwzImage, &pModules->Modules[m].ModuleNameRva);
        WriteDebug(pSelf, &pModules->Modules[m]);
    }

    // Now, we can write the memory regions...


    PMINIDUMP_MEMORY_DESCRIPTOR pRegion = pRegions->MemoryRanges;
    UINT module = 0;
    UINT blob = 0;
    UINT stack = 0;

    while (stack < 1 || module < m_Modules.Count() || blob < m_Blobs.Count()) {
        UINT64 vaModule = (module < m_Modules.Count())
            ? m_Modules.Seek(module)->vaImageBase : 0xffffffffffffffff;
        UINT64 vaBlob = (blob < m_Blobs.Count())
            ? m_Blobs.Seek(blob)->vaBlob : 0xffffffffffffffff;
        UINT64 vaStack = (stack < 1)
            ? m_vaStack : 0xffffffffffffffff;

        if (vaModule <= vaBlob && vaModule <= vaStack) {
            // Module next.

            WriteModule(m_Modules.Seek(module), &pModules->Modules[module], pRegion);
            pRegion += m_Modules.Seek(module)->nRegions;
            module++;
        }
        else if (vaBlob < vaModule && vaBlob < vaStack) {
            // Blob next.
            WriteBlob(m_Blobs.Seek(blob), pRegion);
            pRegion++;
            blob++;
        }
        else if (vaStack < vaModule && vaStack < vaBlob) {
            // Stack next.

            AddMemory(m_vaStack, NULL, 0, 0x100, pRegion);
            pThreads->Threads[0].Stack = *pRegion;

            if (s_fVerbose) {
                printf("  add %08I64x..%08I64x %-8.8s\n",
                       m_vaStack, m_vaStack + 0x100, "stack");
            }

            stack++;
            pRegion++;
        }

    }

    pThreads->Threads[0].ThreadId = 1001;
    pThreads->Threads[0].SuspendCount = 0;
    pThreads->Threads[0].PriorityClass = 0;
    pThreads->Threads[0].Priority = 0;
    pThreads->Threads[0].Teb = 0;
    Write(pThreads, m_Directory[0].Location);

    pModules->NumberOfModules = m_Modules.Count();
    s_vaImageBase = m_vaImageBase;
    qsort(pModules->Modules, pModules->NumberOfModules,
          sizeof(MINIDUMP_MODULE), CompareMinidumpModules);
    Write(pModules, m_Directory[1].Location);

    pRegions->NumberOfMemoryRanges = nRegions;
    Write(pRegions, m_Directory[2].Location);

    pSystems->NumberOfProcessors = 1;
    // TODO64 add command-line machine flag
    if (false) {
        pSystems->ProcessorArchitecture = PROCESSOR_ARCHITECTURE_AMD64;
    }
    else {
        pSystems->ProcessorArchitecture = 0;
    }

    pSystems->ProcessorLevel = 15;
    pSystems->ProcessorRevision = 519;
    pSystems->MajorVersion = 1;
    pSystems->MinorVersion = 1;
    pSystems->BuildNumber = 0xdead;
    pSystems->PlatformId = 7;
    Write(pSystems, m_Directory[3].Location);

    pContext->Esp = (UINT32)m_vaStack;
    pContext->Ebx = (UINT32)m_vaImageBase;
    pContext->Eax = (UINT32)m_vaImageHead;
    pContext->Eip = (UINT32)m_vaImageEntry;
    if (s_fVerbose) {
        printf("  eip: %x, ebx: %x, eax: %x, esp: %x\n",
               pContext->Eip, pContext->Ebx, pContext->Eax, pContext->Esp);
    }

    Write(pContext, pThreads->Threads[0].ThreadContext);

    // Finally, the trailer must follow all of the memory regions.
    PUINT32 pTrailer = (PUINT32)AddCreate(4 * sizeof(UINT32), &m_Directory[4].Location);
    pTrailer[0] = 0xfeedbeef;
    pTrailer[1] = 0xcafeface;
    pTrailer[2] = 0x00000000;
    pTrailer[3] = 0x00000000;
    Write(pTrailer, m_Directory[4].Location);

    m_Out.Seek(0);
    m_Out.Write((PBYTE)&m_Header, sizeof(m_Header));
    m_Out.Write((PBYTE)&m_Directory, sizeof(m_Directory));

    UINT32 sum = m_Out.Checksum();

    pTrailer[3] = -(INT32)sum;
    Write(pTrailer, m_Directory[4].Location);

    return TRUE;
}

BOOL CMiniDump::Delete()
{
    return m_Out.Delete();
}

ULONG CMiniDump::Allocate(UINT32 cbData)
{
    ULONG cbWhere = m_cbAllocated;
    m_cbAllocated += (cbData + 7) & ~7;                 // Align to UINT64.

    return cbWhere;
}

BOOL CMiniDump::Write(PVOID pvData, MINIDUMP_LOCATION_DESCRIPTOR Out)
{
    m_Out.Seek(Out.Rva);
    return m_Out.Write((PBYTE)pvData, Out.DataSize);
}

BOOL CMiniDump::AddData(PBYTE pbData, UINT32 cbData, UINT32 cbFull,
                        MINIDUMP_LOCATION_DESCRIPTOR* pOut)
{
    ULONG cbWhere = Allocate(cbData);
    ULONG cbZero = cbFull - cbData;

    if (!m_Out.Seek(cbWhere)) {
        return FALSE;
    }

    if (cbData > 0 && !m_Out.Write(pbData, cbData)) {
        return FALSE;
    }

    if (!m_Out.Zero(cbZero)) {
        return FALSE;

    }

    if (pOut) {
        pOut->DataSize = cbFull;
        pOut->Rva = cbWhere;
    }
    return TRUE;
}

PBYTE CMiniDump::AddCreate(UINT32 cbData, UINT32 * pRva)
{
    PBYTE pbData = new BYTE [cbData];
    assert(pbData);
    ZeroMemory(pbData, cbData);

    *pRva = Allocate(cbData);
    return pbData;
}

PBYTE CMiniDump::AddCreate(UINT32 cbData,
                           MINIDUMP_LOCATION_DESCRIPTOR* pOut)
{
    pOut->DataSize = cbData;
    return AddCreate(cbData, &pOut->Rva);
}

BOOL CMiniDump::AddString(PCWSTR pwzString, UINT32 *pRva)
{
    ULONG cbData = sizeof(MINIDUMP_STRING) + wcslen(pwzString) * 2 + 2;
    PMINIDUMP_STRING pString = (PMINIDUMP_STRING)AddCreate(cbData, pRva);

    pString->Length = wcslen(pwzString) * 2;
    wcscpy(pString->Buffer, pwzString);

    m_Out.Seek(*pRva);
    m_Out.Write((PBYTE)pString, cbData);

    return TRUE;
}

BOOL CMiniDump::AddMemory(ULONG64 address,
                          PBYTE pbData, UINT32 cbData, UINT32 cbFull,
                          PMINIDUMP_MEMORY_DESCRIPTOR pOut)
{
    if (AddData(pbData, cbData, cbFull, &pOut->Memory)) {
        pOut->StartOfMemoryRange = address;
        return TRUE;
    }
    return FALSE;
}

BOOL CMiniDump::AddModule(PCWSTR pwzImage)
{
    bool isPe64 = false;
    CFileMap *pfImage = new CFileMap();
    assert(pfImage);

    if (!pfImage->Load(pwzImage)) {
        fprintf(stderr, "Could not open image file: %ls\n", pwzImage);

      abort:
        delete pfImage;
        return FALSE;
    }

    MODULE * pModule = m_Modules.Add();
    PIMAGE_DOS_HEADER pdos;
    PIMAGE_NT_HEADERS ppe;
    PIMAGE_NT_HEADERS64 ppe64;

    // Read in the PE image header
    //
    pdos = (PIMAGE_DOS_HEADER)pfImage->Seek(0);
    if (pdos == NULL || pdos->e_magic != IMAGE_DOS_SIGNATURE) {
        error("Image doesn't have MZ signature.");
        goto abort;
    }

    ppe = (PIMAGE_NT_HEADERS)pfImage->Seek(pdos->e_lfanew);
    if (ppe == NULL || ppe->Signature != IMAGE_NT_SIGNATURE) {
        error("Image doesn't have PE signature.");
        goto abort;
    }

    if (ppe->FileHeader.Machine == IMAGE_FILE_MACHINE_AMD64) {
        fprintf(stderr, "mkcore: 64-bit PE files!\n");
        isPe64 = true;
        ppe64 = (PIMAGE_NT_HEADERS64) ppe;
    }

    pModule->nRegions = 1+ ppe->FileHeader.NumberOfSections;

    // this is not right we could be truncating addresses here
    DWORD debugAddr;
    DWORD debugSize;
    if (isPe64) {
         debugAddr = ppe64->OptionalHeader
            .DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG].VirtualAddress;
         debugSize = ppe64->OptionalHeader
            .DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG].Size;
    }
    else {
         debugAddr = ppe->OptionalHeader
            .DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG].VirtualAddress;
         debugSize = ppe->OptionalHeader
            .DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG].Size;
    }
        DWORD debugPos = (debugAddr && debugSize)
        ? RvaToFileOffset(debugAddr, ppe->FileHeader.NumberOfSections,
                          IMAGE_FIRST_SECTION(ppe))
        : 0;

    ////////////////////////////////////////////////// Adjust Debug Directory.
    //
    if (debugPos) {
        PIMAGE_DEBUG_DIRECTORY pDir = (PIMAGE_DEBUG_DIRECTORY)pfImage->Seek(debugPos);
        assert(pDir);

        DWORD nEntries = debugSize / sizeof(*pDir);
        for (DWORD n = 0; n < nEntries; n++) {
            PBYTE pbData = pfImage->Seek(pDir[n].PointerToRawData);

            if ((pbData[0] == 'R' && pbData[1] == 'S') ||
                (pbData[0] == 'N' && pbData[1] == 'B')) {

                pModule->pbCvData = pbData;
                pModule->cbCvData = pDir[n].SizeOfData;
                break;
            }
        }
        if (nEntries > 1) {
            printf("   Dropped %d DBG entries.\n", nEntries - 1);
        }
    }

    pModule->pbImageMap = pfImage->Seek(0);
    pModule->pwzImage = pwzImage;
    if (isPe64) {
        pModule->vaImageBase = ppe64->OptionalHeader.ImageBase;
        pModule->vaImageHead = ppe64->OptionalHeader.ImageBase + pdos->e_lfanew;
        pModule->vaImageEntry = ppe64->OptionalHeader.ImageBase
            + ppe64->OptionalHeader.AddressOfEntryPoint;
    }
    else {
        pModule->vaImageBase = ppe->OptionalHeader.ImageBase;
        pModule->vaImageHead = ppe->OptionalHeader.ImageBase + pdos->e_lfanew;
        pModule->vaImageEntry = ppe->OptionalHeader.ImageBase
            + ppe->OptionalHeader.AddressOfEntryPoint;
    }
    // Copy the image name into the image...
    PCWSTR pwzName = pwzImage;
    PCWSTR pwz;
    if ((pwz = wcsrchr(pwzName, ':')) != NULL) {
        pwzName = pwz + 1;
    }
    if ((pwz = wcsrchr(pwzName, '\\')) != NULL) {
        pwzName = pwz + 1;
    }
    ZeroMemory(pfImage->Seek(64), 64);
    wcscpy((PWCHAR)pfImage->Seek(68), pwzName);
    _wcslwr((PWCHAR)pfImage->Seek(68));

    if (m_vaImageBase == 0) {
        m_vaImageBase = pModule->vaImageBase;
        m_vaImageHead = pModule->vaImageHead;
        m_vaImageEntry = pModule->vaImageEntry;
    }
    return TRUE;
}

BOOL CMiniDump::AddBlob(PCWSTR pwzImage, UINT64 vaBlob, UINT32 *pcbBlob)
{
    CFileMap *pfImage = new CFileMap();
    assert(pfImage);

    if (!pfImage->Load(pwzImage)) {
        fprintf(stderr, "Could not open blob file: %ls\n", pwzImage);

        delete pfImage;
        return FALSE;
    }

    BLOB * pBlob = m_Blobs.Add();

    pBlob->pwzImage = pwzImage;
    pBlob->pbBlob = pfImage->Seek(0);
    pBlob->cbBlob = pfImage->Size();
    pBlob->vaBlob = vaBlob;
    *pcbBlob = pBlob->cbBlob;

    return TRUE;
}

BOOL CMiniDump::WriteDebug(MODULE *pSelf,
                           PMINIDUMP_MODULE pModule)
{
    AddString(pSelf->pwzImage, &pModule->ModuleNameRva);

    if (pSelf->cbCvData > 0) {
        AddData(pSelf->pbCvData, pSelf->cbCvData, pSelf->cbCvData, &pModule->CvRecord);
    }
    return TRUE;
}

BOOL CMiniDump::WriteModule(MODULE *pSelf,
                            PMINIDUMP_MODULE pModule,
                            PMINIDUMP_MEMORY_DESCRIPTOR pRegions)
{
    UINT32 cbHeader;
    UINT64 vaImageBase;
    PIMAGE_DOS_HEADER       pdos;
    PIMAGE_NT_HEADERS       ppe;
    PIMAGE_SECTION_HEADER   psec;
    PIMAGE_SECTION_HEADER   pfsec;
    PIMAGE_SECTION_HEADER   plsec;
    bool isPe64;
    PIMAGE_NT_HEADERS64 ppe64;

    // Read in the PE image header
    //
    pdos = (PIMAGE_DOS_HEADER)(pSelf->pbImageMap);
    ppe = (PIMAGE_NT_HEADERS)(pSelf->pbImageMap + pdos->e_lfanew);

    if (ppe->FileHeader.Machine == IMAGE_FILE_MACHINE_AMD64) {
        fprintf(stderr, "WriteModule: 64-bit PE files!\n");
        isPe64 = true;
        ppe64 = (PIMAGE_NT_HEADERS64) ppe;
    }

    pfsec = IMAGE_FIRST_SECTION(ppe);
    plsec = pfsec + ppe->FileHeader.NumberOfSections;

    if (isPe64) {
        cbHeader = ppe64->OptionalHeader.SizeOfHeaders;
        vaImageBase = ppe64->OptionalHeader.ImageBase;
    }
    else {
        cbHeader = ppe->OptionalHeader.SizeOfHeaders;
        vaImageBase = ppe->OptionalHeader.ImageBase;
    }
    AddMemory(vaImageBase, pSelf->pbImageMap, cbHeader, cbHeader, pRegions++);
    if (s_fVerbose) {
        printf("  add %08I64x..%08I64x %-8.8s %ls\n",
               vaImageBase,
               vaImageBase + cbHeader,
               "pe",
               pSelf->pwzImage);
    }


    for (psec = pfsec; psec < plsec; psec++) {
        DWORD cbData = Min(psec->SizeOfRawData, psec->Misc.VirtualSize);
        DWORD cbFull = Max(psec->SizeOfRawData, psec->Misc.VirtualSize);
        PBYTE pbData = (pSelf->pbImageMap + psec->PointerToRawData);

        if (s_fVerbose) {
            printf("  add %08I64x..%08I64x %-8.8s %ls\n",
                   vaImageBase + psec->VirtualAddress,
                   vaImageBase + psec->VirtualAddress + cbFull,
                   psec->Name,
                   pSelf->pwzImage);
        }

        AddMemory(vaImageBase + psec->VirtualAddress, pbData, cbData, cbFull,
                  pRegions++);
    }

    pModule->BaseOfImage = ppe->OptionalHeader.ImageBase;
    pModule->SizeOfImage = ppe->OptionalHeader.SizeOfImage;
    pModule->CheckSum = ppe->OptionalHeader.CheckSum;
    pModule->TimeDateStamp = ppe->FileHeader.TimeDateStamp;

    return TRUE;
}

BOOL CMiniDump::WriteBlob(BLOB *pBlob,
                          PMINIDUMP_MEMORY_DESCRIPTOR pRegions)
{
    if (s_fVerbose) {
        printf("  add %08I64x..%08I64x %-8.8s %ls\n",
               pBlob->vaBlob,
               pBlob->vaBlob + pBlob->cbBlob,
               "blob",
               pBlob->pwzImage);
    }
    return AddMemory(pBlob->vaBlob, pBlob->pbBlob, pBlob->cbBlob, pBlob->cbBlob, pRegions);
}

//
//////////////////////////////////////////////////////////////////////////////

BOOL DumpFile(PCWSTR file)
{
    CFileMap cfImage;

    if (!cfImage.Load(file)) {
        fprintf(stderr, "Could not open dump file: %ls\n", file);
        return FALSE;
    }

    PMINIDUMP_HEADER pHeader = (PMINIDUMP_HEADER)cfImage.Seek(0);

    printf("Header: [%08x..%08x]\n", 0, sizeof(MINIDUMP_HEADER));
    printf("  Signature: %c%c%c%c\n",
           ((char *)&pHeader->Signature)[0],
           ((char *)&pHeader->Signature)[1],
           ((char *)&pHeader->Signature)[2],
           ((char *)&pHeader->Signature)[3]);
    printf("  Version:   %08x\n", pHeader->Version);
    printf("  Streams:   %d\n", pHeader->NumberOfStreams);
    printf("  Dir RVA:   %08x\n", pHeader->StreamDirectoryRva);
    printf("  CheckSum:  %08x\n", pHeader->CheckSum);
    printf("  TimeDate:  %08x\n", pHeader->TimeDateStamp);
    printf("  Flags:     %016I64x\n", pHeader->Flags);

    PMINIDUMP_DIRECTORY pDir
        = (PMINIDUMP_DIRECTORY)cfImage.Seek(pHeader->StreamDirectoryRva);

    printf("Directory [%08x..%08x]\n", pHeader->StreamDirectoryRva,
           pHeader->StreamDirectoryRva
           + sizeof(MINIDUMP_DIRECTORY) * pHeader->NumberOfStreams);

    for (UINT i = 0; i < pHeader->NumberOfStreams; i++) {
        printf("%4d. Type: %8x, RVA: %8x, Size: %8x\n",
               i, pDir[i].StreamType,
               (UINT32)pDir[i].Location.Rva, pDir[i].Location.DataSize);
    }

    for (UINT i = 0; i < pHeader->NumberOfStreams; i++) {
        switch (pDir[i].StreamType) {
          case UnusedStream:
            break;
          case ThreadListStream:
              {
                  PMINIDUMP_THREAD_LIST pl =
                      (PMINIDUMP_THREAD_LIST)cfImage.Seek(pDir[i].Location.Rva);
                  printf("ThreadList:\n");
                  printf("  Thread:     %d\n", pl->NumberOfThreads);
                  for (UINT t = 0; t < pl->NumberOfThreads; t++) {
                      printf("  %8d: Stack=%08I64x [%08x..%08x] [%08x..%08x] %d\n",
                             pl->Threads[t].ThreadId,
                             pl->Threads[t].Stack.StartOfMemoryRange,
                             pl->Threads[t].Stack.Memory.Rva,
                             pl->Threads[t].Stack.Memory.Rva +
                             pl->Threads[t].Stack.Memory.DataSize,
                             pl->Threads[t].ThreadContext.Rva,
                             pl->Threads[t].ThreadContext.Rva +
                             pl->Threads[t].ThreadContext.DataSize,
                             pl->Threads[t].ThreadContext.DataSize);
                  }
              }
            break;

          case ModuleListStream:
              {
                  PMINIDUMP_MODULE_LIST pl =
                      (PMINIDUMP_MODULE_LIST)cfImage.Seek(pDir[i].Location.Rva);
                  printf("ModuleList: (%x)\n", pDir[i].Location.Rva);
                  printf("  Modules:    %d\n", pl->NumberOfModules);
                  UINT t = 0;
                  for (; t < pl->NumberOfModules && t < 10; t++) {
                      printf("  %08I64x Size=%08x NameRva=%08x chks=%08x date=%08x \n"
                             "           `%.60ls'(%d.%d)\n",
                             pl->Modules[t].BaseOfImage,
                             pl->Modules[t].SizeOfImage,
                             pl->Modules[t].ModuleNameRva,
                             pl->Modules[t].CheckSum,
                             pl->Modules[t].TimeDateStamp,
                             cfImage.Seek(pl->Modules[t].ModuleNameRva)+4,
                             *(PULONG)cfImage.Seek(pl->Modules[t].ModuleNameRva),
                             2* wcslen((PWCHAR)cfImage.Seek(pl->Modules[t].ModuleNameRva)+4)
                            );
#if 0
                      if (pl->Modules[t].CvRecord.Rva) {
                          printf("  CV   [%08x..%08x]\n",
                                 pl->Modules[t].CvRecord.Rva,
                                 pl->Modules[t].CvRecord.Rva +
                                 pl->Modules[t].CvRecord.DataSize);
                          Dump(cfImage.Seek(pl->Modules[t].CvRecord.Rva),
                               pl->Modules[t].CvRecord.DataSize);
                      }
                      if (pl->Modules[t].MiscRecord.Rva) {
                          printf("  Misc [%08x..%08x]\n",
                                 pl->Modules[t].MiscRecord.Rva,
                                 pl->Modules[t].MiscRecord.Rva +
                                 pl->Modules[t].MiscRecord.DataSize);
                          Dump(cfImage.Seek(pl->Modules[t].MiscRecord.Rva),
                               pl->Modules[t].MiscRecord.DataSize);
                      }
#endif
                  }
                  if (t < pl->NumberOfModules) {
                      printf("  ...\n");
                  }
              }
              break;
          case MemoryListStream:
              {
                  PMINIDUMP_MEMORY_LIST pl =
                      (PMINIDUMP_MEMORY_LIST)cfImage.Seek(pDir[i].Location.Rva);
                  printf("MemoryList:\n");
                  printf("  Ranges:     %d\n", pl->NumberOfMemoryRanges);
                  UINT t = 0;
                  for (; t < pl->NumberOfMemoryRanges /* && t < 10 */; t++) {
                      printf("  %08I64x..%08I64x [%08x..%08x]\n",
                             pl->MemoryRanges[t].StartOfMemoryRange,
                             pl->MemoryRanges[t].StartOfMemoryRange
                             + pl->MemoryRanges[t].Memory.DataSize,
                             pl->MemoryRanges[t].Memory.Rva,
                             pl->MemoryRanges[t].Memory.Rva
                             + pl->MemoryRanges[t].Memory.DataSize);
                  }
                  if (t < pl->NumberOfMemoryRanges) {
                      printf("  ...\n");
                  }

              }
              break;
          case SystemInfoStream:
              {
                  PMINIDUMP_SYSTEM_INFO pi =
                      (PMINIDUMP_SYSTEM_INFO)cfImage.Seek(pDir[i].Location.Rva);
                  printf("SystemInfo:\n");
                  printf("  Processors: %d\n", pi->NumberOfProcessors);
                  printf("  Processor:  %d.%d.%d\n",
                         pi->ProcessorArchitecture,
                         pi->ProcessorLevel,
                         pi->ProcessorRevision);
                  printf("  Version:    %d.%d.%d %d\n",
                         pi->MajorVersion,
                         pi->MinorVersion,
                         pi->BuildNumber,
                         pi->PlatformId);
                  printf("  VendorId:   %-12.12s\n", pi->Cpu.X86CpuInfo.VendorId);
                  printf("  Version:    %08x\n", pi->Cpu.X86CpuInfo.VersionInformation);
                  printf("  Feature:    %08x\n", pi->Cpu.X86CpuInfo.FeatureInformation);
                  printf("  AMDFeat:    %08x\n", pi->Cpu.X86CpuInfo.AMDExtendedCpuFeatures);
              }
              break;
        }
    }

    return TRUE;
}

int __cdecl wmain(int argc, WCHAR **argv)
{
    BOOL fNeedHelp = FALSE;
    BOOL fGood = FALSE;
    PCWSTR pwzOutput = NULL;
    CMiniDump miniDump;
    UINT64 vaStack = CMiniDump::c_vaStack;
    UINT32 cbStack = CMiniDump::c_cbStack;
    UINT64 vaBlob = CMiniDump::c_vaBlob;
    UINT32 cbBlob = 0;

    for (int arg = 1; arg < argc && !fNeedHelp; arg++) {
        if (argv[arg][0] == '-' || argv[arg][0] == '/') {
            WCHAR *argn = argv[arg]+1;                   // Argument name
            WCHAR *argp = argn;                          // Argument parameter

            while (*argp && *argp != ':') {
                argp++;
            }
            if (*argp == ':')
                *argp++ = '\0';

            switch (argn[0]) {

              case 'a':                                 // Set next blob address
              case 'A':
                vaBlob = _wcstoi64(argp, NULL, 0);
                break;

              case 'b':
              case 'B':
                if (!miniDump.AddBlob(argp, vaBlob, &cbBlob)) {
                    break;
                }
                vaBlob += cbBlob;
                break;

              case 'c':                                 // Stack size
              case 'C':
                cbStack = (UINT32)_wcstoi64(argp, NULL, 0);
                break;

              case 's':                                 // Stack base
              case 'S':
                vaStack = _wcstoi64(argp, NULL, 0);
                break;

              case 'd':                                 // Dump
              case 'D':
                DumpFile(argp);
                break;

              case 'o':                                 // Output file.
              case 'O':
                pwzOutput = argp;
                if (!miniDump.Create(pwzOutput)) {
                    fprintf(stderr, "Could not open output file: %ls\n", pwzOutput);
                    break;
                }
                printf("%ls:\n", pwzOutput);
                break;

              case 'v':                                 // Verbose
              case 'V':
                s_fVerbose = TRUE;
                break;

              case '?':                                 // Help
                fNeedHelp = TRUE;
                break;

              default:
                printf("Unknown argument: %ls\n", argv[arg]);
                fNeedHelp = TRUE;
                break;
            }
        }
        else {
            if (pwzOutput == NULL) {
                fprintf(stderr, "Must specify output file before input files.\n");
                fNeedHelp = TRUE;
                break;
            }

            if (!miniDump.AddModule(argv[arg])) {
                break;
            }
            else {
                fGood = TRUE;
            }
        }
    }
    if (!fGood) {
        miniDump.Delete();
    }
    else {
        miniDump.Stack(vaStack, cbStack);
        miniDump.Write();
        miniDump.Close();
    }

    if (argc == 1) {
        fNeedHelp = TRUE;
    }

    if (fNeedHelp) {
        printf(
               "Usage:\n"
               "    mkcore [options] images...\n"
               "or:\n"
               "    mkcore /d:minidump\n"
               "Options:\n"
               "    /a:address    -- Set virtual address for subsequent blobs.\n"
               "    /b:file       -- Add a blob file to the minidump.\n"
               "    /d:minidump   -- Display the context of minidump file.\n"
               "    /o:out_file   -- Specify output file (defaults to image.exe).\n"
               "    /s:stackbase  -- Set the base address stack (defaults to 0x%I64x).\n"
               "    /c:stacksize  -- Set the size of zeroed stack data (defaults to 0x%x).\n"
               "    /v            -- Verbose.\n"
               "    /?            -- Display this help screen.\n"
               "Summary:\n"
               "    Creates a minidump file from the sections of one or more PEs.\n"
               "    The first PE file is marked for entry on startup.\n",
               (UINT64)CMiniDump::c_vaStack,
               (UINT32)CMiniDump::c_cbStack
              );
    }

    return 0;
}

//
///////////////////////////////////////////////////////////////// End of File.
