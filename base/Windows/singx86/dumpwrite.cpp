//----------------------------------------------------------------------------
//
// Dump file writing.
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
//----------------------------------------------------------------------------

#include "ntsdp.hpp"

#include <uminiprov.hpp>
#include <dbgver.h>
#include <bugcodes.h>

#define GENERIC_FORMATS \
    (DEBUG_FORMAT_WRITE_CAB | \
     DEBUG_FORMAT_CAB_SECONDARY_FILES | \
     DEBUG_FORMAT_NO_OVERWRITE)
#define UMINI_FORMATS \
    (DEBUG_FORMAT_USER_SMALL_FULL_MEMORY | \
     DEBUG_FORMAT_USER_SMALL_HANDLE_DATA | \
     DEBUG_FORMAT_USER_SMALL_UNLOADED_MODULES | \
     DEBUG_FORMAT_USER_SMALL_INDIRECT_MEMORY | \
     DEBUG_FORMAT_USER_SMALL_DATA_SEGMENTS | \
     DEBUG_FORMAT_USER_SMALL_FILTER_MEMORY | \
     DEBUG_FORMAT_USER_SMALL_FILTER_PATHS | \
     DEBUG_FORMAT_USER_SMALL_PROCESS_THREAD_DATA | \
     DEBUG_FORMAT_USER_SMALL_PRIVATE_READ_WRITE_MEMORY | \
     DEBUG_FORMAT_USER_SMALL_NO_OPTIONAL_DATA | \
     DEBUG_FORMAT_USER_SMALL_FULL_MEMORY_INFO | \
     DEBUG_FORMAT_USER_SMALL_THREAD_INFO | \
     DEBUG_FORMAT_USER_SMALL_CODE_SEGMENTS)

// Internal format flags for testing of microdumps.
#define UMINI_MICRO       0x00000001
#define UMINI_FORCE_DBG   0x00000002
#define UMINI_WRITE_KMINI 0x00000004

HRESULT
DumpTargetInfo::DumpWriteFile(HANDLE File, PVOID Buffer, ULONG Size)
{
    HRESULT Status = S_OK;
    ULONG Done;
    
    if (!WriteFile(File, Buffer, Size, &Done, NULL))
    {
        Status = WIN32_LAST_STATUS();
    }
    else if (Done != Size)
    {
        Status = HRESULT_FROM_WIN32(ERROR_DISK_FULL);
    }

    if (Status != S_OK)
    {
        ErrOut(_T("Error writing to dump file - %s\n    \"%s\"\n"),
               FormatStatusCode(Status),
               FormatStatusArgs(Status, NULL));
    }

    return Status;
}

//----------------------------------------------------------------------------
//
// UserFullDumpTargetInfo::Write.
//
//----------------------------------------------------------------------------

#define USER_DUMP_MEMORY_BUFFER 65536

struct CREATE_USER_DUMP_STATE
{
    ThreadInfo* Thread;
    ImageInfo* Image;
    ULONG64 MemHandle;
    HANDLE DumpFileHandle;
    MEMORY_BASIC_INFORMATION64 MemInfo;
    MEMORY_BASIC_INFORMATION32 MemInfo32;
    ULONG64 MemBufDone;
    ULONG64 TotalMemQueried;
    ULONG64 TotalMemData;
    CROSS_PLATFORM_CONTEXT TargetContext;
    DEBUG_EVENT Event;
    CRASH_THREAD CrashThread;
    ULONG64 MemBuf[USER_DUMP_MEMORY_BUFFER / sizeof(ULONG64)];
};

BOOL WINAPI
CreateUserDumpCallback(ULONG DataType,
                       PVOID* Data,
                       PULONG DataLength,
                       PVOID UserData)
{
    CREATE_USER_DUMP_STATE* State = (CREATE_USER_DUMP_STATE*)UserData;
    ThreadInfo* Thread;

    switch(DataType)
    {
    case DMP_DUMP_FILE_HANDLE:
        *Data = State->DumpFileHandle;
        *DataLength = sizeof(HANDLE);
        break;

    case DMP_DEBUG_EVENT:
        ADDR PcAddr;

        //
        // Fake up an exception event for the current thread.
        //

        ZeroMemory(&State->Event, sizeof(State->Event));

        g_Machine->GetPC(&PcAddr);

        State->Event.dwDebugEventCode = EXCEPTION_DEBUG_EVENT;
        State->Event.dwProcessId = g_Process->m_SystemId;
        if (g_LastEventType == DEBUG_EVENT_EXCEPTION)
        {
            // Use the exception record from the last exception.
            State->Event.dwThreadId = g_EventThread->m_SystemId;
            ExceptionRecord64To(&g_LastEventInfo.Exception.ExceptionRecord,
                                &State->Event.u.Exception.ExceptionRecord);
            State->Event.u.Exception.dwFirstChance =
                g_LastEventInfo.Exception.FirstChance;
        }
        else
        {
            // Fake a breakpoint exception.
            State->Event.dwThreadId = g_Thread->m_SystemId;
            State->Event.u.Exception.ExceptionRecord.ExceptionCode =
                STATUS_BREAKPOINT;
            State->Event.u.Exception.ExceptionRecord.ExceptionAddress =
                (PVOID)(ULONG_PTR)Flat(PcAddr);
            State->Event.u.Exception.dwFirstChance = TRUE;
        }

        *Data = &State->Event;
        *DataLength = sizeof(State->Event);
        break;

    case DMP_THREAD_STATE:
        ULONG64 Teb64;

        if (State->Thread == NULL)
        {
            Thread = g_Process->m_ThreadHead;
        }
        else
        {
            Thread = State->Thread->m_Next;
        }
        State->Thread = Thread;
        if (Thread == NULL)
        {
            return FALSE;
        }

        ZeroMemory(&State->CrashThread, sizeof(State->CrashThread));

        State->CrashThread.ThreadId = Thread->m_SystemId;
        State->CrashThread.SuspendCount = Thread->m_SuspendCount;
        Thread->GetBasicInfo(DEBUG_TBINFO_PRIORITY_CLASS |
                             DEBUG_TBINFO_PRIORITY);
        if (Thread->m_BasicInfo.Valid & DEBUG_TBINFO_PRIORITY_CLASS)
        {
            State->CrashThread.PriorityClass =
                Thread->m_BasicInfo.PriorityClass;
        }
        else
        {
            State->CrashThread.PriorityClass = NORMAL_PRIORITY_CLASS;
        }
        if (Thread->m_BasicInfo.Valid & DEBUG_TBINFO_PRIORITY)
        {
            State->CrashThread.Priority = Thread->m_BasicInfo.Priority;
        }
        else
        {
            State->CrashThread.Priority = THREAD_PRIORITY_NORMAL;
        }
        if (g_Target->GetThreadInfoDataOffset(Thread, NULL, &Teb64) != S_OK)
        {
            Teb64 = 0;
        }
        State->CrashThread.Teb = (ULONG_PTR)Teb64;

        *Data = &State->CrashThread;
        *DataLength = sizeof(State->CrashThread);
        break;

    case DMP_MEMORY_BASIC_INFORMATION:
        for (;;)
        {
            if (g_Target->QueryMemoryRegion(g_Process,
                                            &State->MemHandle,
                                            &State->MemInfo) != S_OK)
            {
                State->MemHandle = 0;
                State->MemInfo.RegionSize = 0;
                return FALSE;
            }

            State->MemHandle = State->MemInfo.BaseAddress +
                State->MemInfo.RegionSize;

            if (!((State->MemInfo.Protect & PAGE_GUARD) ||
                  (State->MemInfo.Protect & PAGE_NOACCESS) ||
                  (State->MemInfo.State & MEM_FREE) ||
                  (State->MemInfo.State & MEM_RESERVE)))
            {
                break;
            }
        }

        State->TotalMemQueried += State->MemInfo.RegionSize;

#ifdef _WIN64
        *Data = &State->MemInfo;
        *DataLength = sizeof(State->MemInfo);
#else
        State->MemInfo32.BaseAddress = (ULONG)State->MemInfo.BaseAddress;
        State->MemInfo32.AllocationBase = (ULONG)State->MemInfo.AllocationBase;
        State->MemInfo32.AllocationProtect = State->MemInfo.AllocationProtect;
        State->MemInfo32.RegionSize = (ULONG)State->MemInfo.RegionSize;
        State->MemInfo32.State = State->MemInfo.State;
        State->MemInfo32.Protect = State->MemInfo.Protect;
        State->MemInfo32.Type = State->MemInfo.Type;
        *Data = &State->MemInfo32;
        *DataLength = sizeof(State->MemInfo32);
#endif
        break;

    case DMP_THREAD_CONTEXT:
        if (State->Thread == NULL)
        {
            Thread = g_Process->m_ThreadHead;
        }
        else
        {
            Thread = State->Thread->m_Next;
        }
        State->Thread = Thread;
        if (Thread == NULL)
        {
            g_Target->ChangeRegContext(g_Thread);
            return FALSE;
        }

        g_Target->ChangeRegContext(Thread);
        if (g_Machine->GetContextState(MCTX_CONTEXT) != S_OK ||
            g_Machine->
            ConvertCanonContextToTarget(&g_Machine->m_Context,
                                        g_Target->m_TypeInfo.SizeTargetContext,
                                        &State->TargetContext) != S_OK)
        {
            ErrOut(_T("Unable to retrieve context for thread %d. ")
                   _T("Dump may be corrupt."), Thread->m_UserId);
            return FALSE;
        }

        *Data = &State->TargetContext;
        *DataLength = g_Target->m_TypeInfo.SizeTargetContext;
        break;

    case DMP_MODULE:
        ImageInfo* Image;
        PCRASH_MODULE Module;

        if (State->Image == NULL)
        {
            Image = g_Process->m_ImageHead;
        }
        else
        {
            Image = State->Image->m_Next;
        }

        // Skip over modules that are marked for unload
        // as they don't have valid memory and thus
        // will confuse the loaded module list validation
        // when the dump is loaded.
        for (;;)
        {
            State->Image = Image;
            if (!Image)
            {
                return FALSE;
            }

            if (!Image->m_Unloaded)
            {
                break;
            }
            
            Image = State->Image->m_Next;
        }

        Module = (PCRASH_MODULE)State->MemBuf;
        Module->BaseOfImage = (ULONG_PTR)Image->m_BaseOfImage;
        Module->SizeOfImage = Image->m_SizeOfImage;
        Module->ImageNameLength = _tcslen(Image->m_ImagePath) + 1;
#ifndef UNICODE
        CopyString(Module->ImageName, Image->m_ImagePath,
                   USER_DUMP_MEMORY_BUFFER - sizeof(*Module));
#else
        FillStringBufferTA(Image->m_ImagePath, 0,
                           Module->ImageName,
                           USER_DUMP_MEMORY_BUFFER - sizeof(*Module),
                           NULL);
#endif

        *Data = Module;
        *DataLength = sizeof(*Module) + Module->ImageNameLength;
        break;

    case DMP_MEMORY_DATA:
        ULONG64 Left;

        Left = State->MemInfo.RegionSize - State->MemBufDone;
        if (Left == 0)
        {
            State->MemBufDone = 0;

            for (;;)
            {
                if (g_Target->QueryMemoryRegion(g_Process,
                                                &State->MemHandle,
                                                &State->MemInfo) != S_OK)
                {
                    State->MemHandle = 0;
                    State->MemInfo.RegionSize = 0;

                    // Sanity check that we wrote out as much data
                    // as we stored in the MEMORY_BASIC phase.
                    if (State->TotalMemQueried != State->TotalMemData)
                    {
                        ErrOut(_T("Queried %s bytes of memory but wrote %s ")
                               _T("bytes of memory data.\n")
                               _T("Dump may be corrupt.\n"),
                               FormatDisp64(State->TotalMemQueried),
                               FormatDisp64(State->TotalMemData));
                    }

                    return FALSE;
                }

                State->MemHandle = State->MemInfo.BaseAddress +
                    State->MemInfo.RegionSize;

                if (!((State->MemInfo.Protect & PAGE_GUARD) ||
                      (State->MemInfo.Protect & PAGE_NOACCESS) ||
                      (State->MemInfo.State & MEM_FREE) ||
                      (State->MemInfo.State & MEM_RESERVE)))
                {
                    break;
                }
            }

            Left = State->MemInfo.RegionSize;
            State->TotalMemData += State->MemInfo.RegionSize;
        }

        if (Left > USER_DUMP_MEMORY_BUFFER)
        {
            Left = USER_DUMP_MEMORY_BUFFER;
        }
        if (CurReadAllVirtual(State->MemInfo.BaseAddress +
                              State->MemBufDone, State->MemBuf,
                              (ULONG)Left) != S_OK)
        {
            ErrOut(_T("ReadVirtual at %s failed. Dump may be corrupt.\n"),
                   FormatAddr64(State->MemInfo.BaseAddress +
                                State->MemBufDone));
            return FALSE;
        }

        State->MemBufDone += Left;

        *Data = State->MemBuf;
        *DataLength = (ULONG)Left;
        break;
    }

    return TRUE;
}

HRESULT
UserFullDumpTargetInfo::Write(HANDLE hFile, DUMP_WRITE_ARGS* Args)
{
    dprintf(_T("user full dump\n"));
    FlushCallbacks();

    if (!IS_LIVE_USER_TARGET(g_Target))
    {
        ErrOut(_T("User full dumps can only be written in ")
               _T("live user-mode sessions\n"));
        return E_UNEXPECTED;
    }
    if (Args->CommentA != NULL || Args->CommentW != NULL)
    {
        ErrOut(_T("User full dumps do not support comments\n"));
        return E_INVALIDARG;
    }

    CREATE_USER_DUMP_STATE* State;

    State = (CREATE_USER_DUMP_STATE*)calloc(1, sizeof(*State));
    if (State == NULL)
    {
        ErrOut(_T("Unable to allocate memory for dump state\n"));
        return E_OUTOFMEMORY;
    }

    State->DumpFileHandle = hFile;

    HRESULT Status;

    if (!DbgHelpCreateUserDump(NULL, CreateUserDumpCallback, State))
    {
        Status = WIN32_LAST_STATUS();
        ErrOut(_T("Dump creation failed, %s\n    \"%s\"\n"),
               FormatStatusCode(Status), FormatStatus(Status));
    }
    else
    {
        Status = S_OK;
    }

    free(State);
    return Status;
}

//----------------------------------------------------------------------------
//
// UserMiniDumpTargetInfo::Write.
//
//----------------------------------------------------------------------------

class DbgSystemProvider : public MiniDumpSystemProvider
{
public:
    DbgSystemProvider(void);
    ~DbgSystemProvider(void);

    virtual void Release(void);
    virtual HRESULT GetCurrentTimeDate(OUT PULONG TimeDate);
    virtual HRESULT GetCpuType(OUT PULONG Type,
                               OUT PBOOL BackingStore);
    virtual HRESULT GetCpuInfo(OUT PUSHORT Architecture,
                               OUT PUSHORT Level,
                               OUT PUSHORT Revision,
                               OUT PUCHAR NumberOfProcessors,
                               OUT PCPU_INFORMATION Info);
    virtual void GetContextSizes(OUT PULONG Size,
                                 OUT PULONG RegScanStart,
                                 OUT PULONG RegScanCount);
    virtual void GetPointerSize(OUT PULONG Size);
    virtual void GetPageSize(OUT PULONG Size);
    virtual void GetFunctionTableSizes(OUT PULONG TableSize,
                                       OUT PULONG EntrySize);
    virtual void GetInstructionWindowSize(OUT PULONG Size);
    virtual HRESULT GetOsInfo(OUT PULONG PlatformId,
                              OUT PULONG Major,
                              OUT PULONG Minor,
                              OUT PULONG BuildNumber,
                              OUT PUSHORT ProductType,
                              OUT PUSHORT SuiteMask);
    virtual HRESULT GetOsCsdString(OUT PWSTR Buffer,
                                   IN ULONG BufferChars);
    virtual HRESULT OpenMapping(IN PCWSTR FilePath,
                                OUT PULONG Size,
                                OUT PWSTR LongPath,
                                IN ULONG LongPathChars,
                                OUT PVOID* Mapping);
    virtual void    CloseMapping(PVOID Mapping);
    virtual HRESULT GetImageHeaderInfo(IN HANDLE Process,
                                       IN PCWSTR FilePath,
                                       IN ULONG64 ImageBase,
                                       OUT PULONG MachineType,
                                       OUT PULONG Size,
                                       OUT PULONG CheckSum,
                                       OUT PULONG TimeDateStamp,
                                       OUT PULONG TlsDirRva);
    virtual HRESULT GetImageVersionInfo(IN HANDLE Process,
                                        IN PCWSTR FilePath,
                                        IN ULONG64 ImageBase,
                                        OUT VS_FIXEDFILEINFO* Info);
    virtual HRESULT GetImageDebugRecord(IN HANDLE Process,
                                        IN PCWSTR FilePath,
                                        IN ULONG64 ImageBase,
                                        IN ULONG RecordType,
                                        OUT OPTIONAL PVOID Data,
                                        IN OUT PULONG DataLen);
    virtual HRESULT EnumImageSections(IN HANDLE Process,
                                      IN PCWSTR FilePath,
                                      IN ULONG64 ImageBase,
                                      IN ULONG WriteFlags,
                                      IN MiniDumpProviderCallbacks*
                                      Callback);
    virtual HRESULT OpenThread(IN ULONG DesiredAccess,
                               IN BOOL InheritHandle,
                               IN ULONG ThreadId,
                               OUT PHANDLE Handle);
    virtual void    CloseThread(IN HANDLE Handle);
    virtual ULONG   GetCurrentThreadId(void);
    virtual ULONG   SuspendThread(IN HANDLE Thread);
    virtual ULONG   ResumeThread(IN HANDLE Thread);
    virtual HRESULT GetThreadContext(IN HANDLE Thread,
                                     OUT PVOID Context,
                                     IN ULONG ContextSize,
                                     OUT PULONG64 CurrentPc,
                                     OUT PULONG64 CurrentStack,
                                     OUT PULONG64 CurrentStore);
    virtual HRESULT GetTeb(IN HANDLE Thread,
                           OUT PULONG64 Offset,
                           OUT PULONG Size);
    virtual HRESULT GetThreadTebInfo(IN HANDLE Process,
                                     IN HANDLE Thread,
                                     OUT PULONG64 Teb,
                                     OUT PULONG SizeOfTeb,
                                     OUT PULONG64 StackBase,
                                     OUT PULONG64 StackLimit,
                                     OUT PULONG64 StoreBase,
                                     OUT PULONG64 StoreLimit,
                                     OUT PULONG64 StaticTlsPointer);
    virtual HRESULT GetThreadOsInfo(IN HANDLE Process,
                                    IN HANDLE Thread,
                                    IN OUT PUMINIPROV_THREAD_INFO Info);
    virtual HRESULT GetPeb(IN HANDLE Process,
                           OUT PULONG64 Offset,
                           OUT PULONG Size);
    virtual HRESULT GetProcessTimes(IN HANDLE Process,
                                    OUT LPFILETIME Create,
                                    OUT LPFILETIME User,
                                    OUT LPFILETIME Kernel);
    virtual HRESULT ReadVirtual(IN HANDLE Process,
                                IN ULONG64 Offset,
                                OUT PVOID Buffer,
                                IN ULONG Request,
                                OUT PULONG Done);
    virtual HRESULT ReadAllVirtual(IN HANDLE Process,
                                   IN ULONG64 Offset,
                                   OUT PVOID Buffer,
                                   IN ULONG Request);
    virtual HRESULT QueryVirtual(IN HANDLE Process,
                                 IN ULONG64 Offset,
                                 OUT PMINIDUMP_MEMORY_INFO Info);
    virtual HRESULT GetValidVirtualRange(IN HANDLE Process,
                                         IN ULONG64 Start,
                                         IN ULONG Size,
                                         OUT PULONG64 ValidStart,
                                         OUT PULONG ValidSize);
    virtual HRESULT StartProcessEnum(IN HANDLE Process,
                                     IN ULONG ProcessId);
    virtual HRESULT EnumThreads(OUT PULONG ThreadId);
    virtual HRESULT EnumModules(OUT PULONG64 Base,
                                OUT PWSTR Path,
                                IN ULONG PathChars);
    virtual HRESULT EnumFunctionTables(OUT PULONG64 MinAddress,
                                       OUT PULONG64 MaxAddress,
                                       OUT PULONG64 BaseAddress,
                                       OUT PULONG EntryCount,
                                       OUT PVOID RawTable,
                                       IN ULONG RawTableSize,
                                       OUT PVOID* RawEntryHandle);
    virtual HRESULT EnumFunctionTableEntries(IN PVOID RawTable,
                                             IN ULONG RawTableSize,
                                             IN PVOID RawEntryHandle,
                                             OUT PVOID RawEntries,
                                             IN ULONG RawEntriesSize);
    virtual HRESULT EnumFunctionTableEntryMemory(IN ULONG64 TableBase,
                                                 IN PVOID RawEntries,
                                                 IN ULONG Index,
                                                 OUT PULONG64 Start,
                                                 OUT PULONG Size);
    virtual HRESULT EnumUnloadedModules(OUT PWSTR Path,
                                        IN ULONG PathChars,
                                        OUT PULONG64 BaseOfModule,
                                        OUT PULONG SizeOfModule,
                                        OUT PULONG CheckSum,
                                        OUT PULONG TimeDateStamp);
    virtual void    FinishProcessEnum(void);
    virtual HRESULT StartHandleEnum(IN HANDLE Process,
                                    IN ULONG ProcessId,
                                    OUT PULONG Count);
    virtual HRESULT EnumHandles(OUT PULONG64 Handle,
                                OUT PULONG Attributes,
                                OUT PULONG GrantedAccess,
                                OUT PULONG HandleCount,
                                OUT PULONG PointerCount,
                                OUT PWSTR TypeName,
                                IN ULONG TypeNameChars,
                                OUT PWSTR ObjectName,
                                IN ULONG ObjectNameChars);
    virtual void    FinishHandleEnum(void);

    virtual HRESULT EnumPebMemory(IN HANDLE Process,
                                  IN ULONG64 PebOffset,
                                  IN ULONG PebSize,
                                  IN MiniDumpProviderCallbacks* Callback);
    virtual HRESULT EnumTebMemory(IN HANDLE Process,
                                  IN HANDLE Thread,
                                  IN ULONG64 TebOffset,
                                  IN ULONG TebSize,
                                  IN MiniDumpProviderCallbacks* Callback);

    virtual HRESULT GetClrEnum(IN PWSTR DacDllName,
                               IN struct ICLRDataTarget* Target,
                               OUT struct ICLRDataEnumMemoryRegions** Enum);
    virtual void    ReleaseClrEnum(IN struct ICLRDataEnumMemoryRegions* Enum);

    virtual HRESULT WriteKernelMinidump(IN HANDLE File,
                                        IN HANDLE Process,
                                        IN ULONG NumThreads,
                                        IN HANDLE* ThreadHandles,
                                        IN ULONG BugCheckCode,
                                        IN ULONG Flags);

protected:
    ThreadInfo* m_Thread;
    ImageInfo* m_Image;
    UnloadedModuleInfo* m_UnlEnum;
    ULONG m_Handle;
    ULONG64 m_FuncTableStart;
    ULONG64 m_FuncTableHandle;
};

DbgSystemProvider::DbgSystemProvider(void)
{
}

DbgSystemProvider::~DbgSystemProvider(void)
{
}

void
DbgSystemProvider::Release(void)
{
    delete this;
}

HRESULT
DbgSystemProvider::GetCurrentTimeDate(OUT PULONG TimeDate)
{
    *TimeDate = FileTimeToTimeDateStamp(g_Target->GetCurrentTimeDateN());
    return S_OK;
}

HRESULT
DbgSystemProvider::GetCpuType(OUT PULONG Type,
                              OUT PBOOL BackingStore)
{
    *Type = g_Target->m_MachineType;
    *BackingStore = *Type == IMAGE_FILE_MACHINE_IA64;
    return S_OK;
}

HRESULT
DbgSystemProvider::GetCpuInfo(OUT PUSHORT Architecture,
                              OUT PUSHORT Level,
                              OUT PUSHORT Revision,
                              OUT PUCHAR NumberOfProcessors,
                              OUT PCPU_INFORMATION Info)
{
    DEBUG_PROCESSOR_IDENTIFICATION_ALL ProcId;
    ULONG64 ProcFeatures[4];
    ULONG NumVals;

    *Architecture = (USHORT)ImageMachineToProcArch(g_Target->m_MachineType);
    *NumberOfProcessors = (UCHAR)g_Target->m_NumProcessors;

    //
    // We've set the basic processor type so that the dump
    // can be interpreted correctly.  Any other failures should
    // not be considered fatal.
    //

    *Level = 0;
    *Revision = 0;
    ZeroMemory(Info, sizeof(*Info));

    if (g_Target->GetProcessorId(0, &ProcId) != S_OK)
    {
        return S_OK;
    }

    switch(g_Target->m_MachineType)
    {
    case IMAGE_FILE_MACHINE_I386:
        *Level = (USHORT)ProcId.X86.Family;
        *Revision = ((USHORT)ProcId.X86.Model << 8) |
            (USHORT)ProcId.X86.Stepping;

        memcpy(Info->X86CpuInfo.VendorId, ProcId.X86.VendorString,
               sizeof(Info->X86CpuInfo.VendorId));
        if (SUCCEEDED(g_Target->
                      GetSpecificProcessorFeatures(0,
                                                   ProcFeatures,
                                                   DIMA(ProcFeatures),
                                                   &NumVals)) &&
            NumVals >= 2)
        {
            Info->X86CpuInfo.VersionInformation =
                (ULONG32)ProcFeatures[0];
            Info->X86CpuInfo.FeatureInformation =
                (ULONG32)ProcFeatures[1];

            if (NumVals >= 3)
            {
                Info->X86CpuInfo.AMDExtendedCpuFeatures =
                    (ULONG32)ProcFeatures[2];
            }
        }
        break;

    case IMAGE_FILE_MACHINE_IA64:
        *Level = (USHORT)ProcId.Ia64.Model;
        *Revision = (USHORT)ProcId.Ia64.Revision;
        break;

    case IMAGE_FILE_MACHINE_AMD64:
        *Level = (USHORT)ProcId.Amd64.Family;
        *Revision = ((USHORT)ProcId.Amd64.Model << 8) |
            (USHORT)ProcId.Amd64.Stepping;
        break;
    }

    if (g_Target->m_MachineType != IMAGE_FILE_MACHINE_I386 &&
        SUCCEEDED(g_Target->
                  GetGenericProcessorFeatures(0,
                                              ProcFeatures,
                                              DIMA(ProcFeatures),
                                              &NumVals)))
    {
        C_ASSERT(sizeof(Info->OtherCpuInfo.ProcessorFeatures) <=
                 sizeof(ProcFeatures));

        if (NumVals < DIMA(ProcFeatures))
        {
            ZeroMemory(ProcFeatures + NumVals,
                       (DIMA(ProcFeatures) - NumVals) *
                       sizeof(ProcFeatures[0]));
        }

        memcpy(Info->OtherCpuInfo.ProcessorFeatures, ProcFeatures,
               sizeof(Info->OtherCpuInfo.ProcessorFeatures));
    }

    return S_OK;
}

void
DbgSystemProvider::GetContextSizes(OUT PULONG Size,
                                   OUT PULONG RegScanOffset,
                                   OUT PULONG RegScanCount)
{
    *Size = g_Target->m_TypeInfo.SizeTargetContext;
    // Default reg scan.
    *RegScanOffset = -1;
    *RegScanCount = -1;
}

void
DbgSystemProvider::GetPointerSize(OUT PULONG Size)
{
    *Size = g_Machine->m_Ptr64 ? 8 : 4;
}

void
DbgSystemProvider::GetPageSize(OUT PULONG Size)
{
    *Size = g_Machine->m_PageSize;
}

void
DbgSystemProvider::GetFunctionTableSizes(OUT PULONG TableSize,
                                         OUT PULONG EntrySize)
{
    *TableSize = g_Target->m_TypeInfo.SizeDynamicFunctionTable;
    *EntrySize = g_Target->m_TypeInfo.SizeRuntimeFunction;
}

void
DbgSystemProvider::GetInstructionWindowSize(OUT PULONG Size)
{
    // Default window.
    *Size = -1;
}

HRESULT
DbgSystemProvider::GetOsInfo(OUT PULONG PlatformId,
                             OUT PULONG Major,
                             OUT PULONG Minor,
                             OUT PULONG BuildNumber,
                             OUT PUSHORT ProductType,
                             OUT PUSHORT SuiteMask)
{
    *PlatformId = g_Target->m_PlatformId;
    *Major = g_Target->m_Win32Major;
    *Minor = g_Target->m_Win32Minor;
    *BuildNumber = g_Target->m_BuildNumber;
    *ProductType = (USHORT)g_Target->m_ProductType;
    *SuiteMask = (USHORT)g_Target->m_SuiteMask;
    return S_OK;
}

HRESULT
DbgSystemProvider::GetOsCsdString(OUT PWSTR Buffer,
                                  IN ULONG BufferChars)
{
    return CopyStringW(Buffer, g_Target->m_ServicePackString, BufferChars) ?
        S_OK : E_INVALIDARG;
}

HRESULT
DbgSystemProvider::OpenMapping(IN PCWSTR FilePath,
                               OUT PULONG Size,
                               OUT PWSTR LongPath,
                               IN ULONG LongPathChars,
                               OUT PVOID* ViewRet)
{
    // We could potentially support this via image file
    // location but the minidump code is deliberately
    // written to not rely to mappings.
    return E_NOTIMPL;
}

void
DbgSystemProvider::CloseMapping(PVOID Mapping)
{
    // No mapping support.
    DBG_ASSERT(!Mapping);
}

HRESULT
DbgSystemProvider::GetImageHeaderInfo(IN HANDLE _Process,
                                      IN PCWSTR FilePath,
                                      IN ULONG64 ImageBase,
                                      OUT PULONG MachineType,
                                      OUT PULONG Size,
                                      OUT PULONG CheckSum,
                                      OUT PULONG TimeDateStamp,
                                      OUT PULONG TlsDirRva)
{
    ProcessInfo* Process = (ProcessInfo*)_Process;
    
    ImageInfo* Image = Process->
        FindImageByOffset(ImageBase, FALSE);
    if (!Image)
    {
        return E_NOINTERFACE;
    }

    Image->UpdateHeaderInfo(IMINFO_BASE_HEADER_ALL);

    *MachineType = Image->m_HeaderInfo.MachineType;
    *Size = Image->m_SizeOfImage;
    *CheckSum = Image->m_HeaderInfo.CheckSum;
    *TimeDateStamp = Image->m_HeaderInfo.TimeDateStamp;

    IMAGE_NT_HEADERS64 Hdrs;

    if (Process->m_Target->
        ReadImageNtHeaders(Process, ImageBase, &Hdrs) == S_OK &&
        Hdrs.OptionalHeader.NumberOfRvaAndSizes > IMAGE_DIRECTORY_ENTRY_TLS &&
        Hdrs.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_TLS].Size > 0)
    {
        *TlsDirRva = Hdrs.OptionalHeader.
            DataDirectory[IMAGE_DIRECTORY_ENTRY_TLS].VirtualAddress;
    }
    else
    {
        *TlsDirRva = 0;
    }
    
    return S_OK;
}

HRESULT
DbgSystemProvider::GetImageVersionInfo(IN HANDLE Process,
                                       IN PCWSTR FilePath,
                                       IN ULONG64 ImageBase,
                                       OUT VS_FIXEDFILEINFO* Info)
{
    return g_Target->
        GetImageVersionInformation((ProcessInfo*)Process,
                                   FilePath, ImageBase, L"\\",
                                   Info, sizeof(*Info), NULL);
}

HRESULT
DbgSystemProvider::GetImageDebugRecord(IN HANDLE Process,
                                       IN PCWSTR FilePath,
                                       IN ULONG64 ImageBase,
                                       IN ULONG RecordType,
                                       IN OUT OPTIONAL PVOID Data,
                                       OUT PULONG DataLen)
{
    // We can rely on the default processing.
    return E_NOINTERFACE;
}

HRESULT
DbgSystemProvider::EnumImageSections(IN HANDLE Process,
                                     IN PCWSTR FilePath,
                                     IN ULONG64 ImageBase,
                                     IN ULONG WriteFlags,
                                     IN MiniDumpProviderCallbacks*
                                     Callback)
{
    // We can rely on the default processing.
    return E_NOINTERFACE;
}

HRESULT
DbgSystemProvider::OpenThread(IN ULONG DesiredAccess,
                              IN BOOL InheritHandle,
                              IN ULONG ThreadId,
                              OUT PHANDLE Handle)
{
    // Just use the thread pointer as the "handle".
    *Handle = g_Process->FindThreadBySystemId(ThreadId);
    return *Handle ? S_OK : E_NOINTERFACE;
}

void
DbgSystemProvider::CloseThread(IN HANDLE Handle)
{
    // "Handle" is just a pointer so nothing to do.
}

ULONG
DbgSystemProvider::GetCurrentThreadId(void)
{
    // The minidump code uses the current thread ID
    // to avoid suspending the thread running the dump
    // code.  That's not a problem for the debugger,
    // so return an ID that will never match.
    // SuspendThread will always be called so all
    // suspend counts will be set properly.
    return 0;
}

ULONG
DbgSystemProvider::SuspendThread(IN HANDLE Thread)
{
    return ((ThreadInfo*)Thread)->m_SuspendCount;
}

ULONG
DbgSystemProvider::ResumeThread(IN HANDLE Thread)
{
    return ((ThreadInfo*)Thread)->m_SuspendCount;
}

HRESULT
DbgSystemProvider::GetThreadContext(IN HANDLE Thread,
                                    OUT PVOID Context,
                                    IN ULONG ContextSize,
                                    OUT PULONG64 CurrentPc,
                                    OUT PULONG64 CurrentStack,
                                    OUT PULONG64 CurrentStore)
{
    HRESULT Status;
    ADDR Addr;

    g_Target->ChangeRegContext((ThreadInfo*)Thread);
    if ((Status = g_Machine->
         GetContextState(MCTX_CONTEXT)) != S_OK ||
        (Status = g_Machine->
         ConvertCanonContextToTarget(&g_Machine->m_Context,
                                     g_Target->m_TypeInfo.SizeTargetContext,
                                     Context)) != S_OK)
    {
        return Status;
    }

    g_Machine->GetPC(&Addr);
    *CurrentPc = Flat(Addr);
    g_Machine->GetSP(&Addr);
    *CurrentStack = Flat(Addr);
    if (g_Target->m_MachineType == IMAGE_FILE_MACHINE_IA64)
    {
        *CurrentStore = g_Machine->m_Context.IA64Context.RsBSP;
    }
    else
    {
        *CurrentStore = 0;
    }

    return S_OK;
}

HRESULT
DbgSystemProvider::GetTeb(IN HANDLE Thread,
                          OUT PULONG64 Offset,
                          OUT PULONG Size)
{
    // Always save a whole page for the TEB.
    *Size = g_Machine->m_PageSize;
    return g_Target->
        GetThreadInfoTeb((ThreadInfo*)Thread, 0, 0, Offset);
}

HRESULT
DbgSystemProvider::GetThreadTebInfo(IN HANDLE Process,
                                    IN HANDLE Thread,
                                    OUT PULONG64 Teb,
                                    OUT PULONG SizeOfTeb,
                                    OUT PULONG64 StackBase,
                                    OUT PULONG64 StackLimit,
                                    OUT PULONG64 StoreBase,
                                    OUT PULONG64 StoreLimit,
                                    OUT PULONG64 StaticTlsPointer)
{
    HRESULT Status;
    MEMORY_BASIC_INFORMATION64 MemInfo;
    ULONG64 MemHandle;

    if ((Status = g_Target->
         GetThreadInfoTeb((ThreadInfo*)Thread, 0, 0, Teb)) != S_OK)
    {
        return Status;
    }

    //
    // Try and save a whole page for the TEB.  If that's
    // not possible, save as much as is there.
    //

    MemHandle = *Teb;
    if ((Status = g_Target->
         QueryMemoryRegion((ProcessInfo*)Process, &MemHandle,
                           &MemInfo)) != S_OK)
    {
        return Status;
    }

    *SizeOfTeb = g_Machine->m_PageSize;
    if (*Teb + *SizeOfTeb > MemInfo.BaseAddress + MemInfo.RegionSize)
    {
        *SizeOfTeb = (ULONG)
            ((MemInfo.BaseAddress + MemInfo.RegionSize) - *Teb);
    }

    //
    // Read the TIB for TLS information.
    //

    ULONG PtrSize = g_Machine->m_Ptr64 ? 8 : 4;

    if ((Status = g_Target->
         ReadPointer((ProcessInfo*)Process, g_Machine, *Teb + 11 * PtrSize,
                     StaticTlsPointer)) != S_OK)
    {
        return Status;
    }

    THREAD_STACK_BOUNDS Bounds;
    
    if ((Status = g_Target->
         GetThreadStackBounds((ThreadInfo*)Thread, *Teb, &Bounds)) != S_OK)
    {
        return Status;
    }

    *StackBase = Bounds.StackBase;
    *StackLimit = Bounds.StackLimit;
    *StoreBase = Bounds.StoreBase;
    *StoreLimit = Bounds.StoreLimit;

    return S_OK;
}

HRESULT
DbgSystemProvider::GetThreadOsInfo(IN HANDLE Process,
                                   IN HANDLE _Thread,
                                   IN OUT PUMINIPROV_THREAD_INFO Info)
{
    HRESULT Status;
    ThreadInfo* Thread = (ThreadInfo*)_Thread;

    if ((Status = Thread->GetBasicInfo(DEBUG_TBINFO_ALL)) != S_OK)
    {
        return Status;
    }

    Info->ExitStatus = Thread->m_ExitStatus;

    if (Thread->m_BasicInfo.Valid & DEBUG_TBINFO_PRIORITY_CLASS)
    {
        Info->PriorityClass = Thread->m_BasicInfo.PriorityClass;
    }
    if (Thread->m_BasicInfo.Valid & DEBUG_TBINFO_PRIORITY)
    {
        Info->Priority = Thread->m_BasicInfo.Priority;
    }
    if (Thread->m_BasicInfo.Valid & DEBUG_TBINFO_TIMES)
    {
        Info->CreateTime = Thread->m_BasicInfo.CreateTime;
        Info->ExitTime = Thread->m_BasicInfo.ExitTime;
        Info->KernelTime = Thread->m_BasicInfo.KernelTime;
        Info->UserTime = Thread->m_BasicInfo.UserTime;
    }
    if (Thread->m_BasicInfo.Valid & DEBUG_TBINFO_START_OFFSET)
    {
        Info->StartAddress = Thread->m_BasicInfo.StartOffset;
    }
    if (Thread->m_BasicInfo.Valid & DEBUG_TBINFO_AFFINITY)
    {
        Info->Affinity = Thread->m_BasicInfo.Affinity;
    }

    return S_OK;
}

HRESULT
DbgSystemProvider::GetPeb(IN HANDLE Process,
                          OUT PULONG64 Offset,
                          OUT PULONG Size)
{
    HRESULT Status;
    MEMORY_BASIC_INFORMATION64 MemInfo;
    ULONG64 MemHandle;

    // The passed in process isn't very useful but we know
    // that we're dumping the current state so always
    // retrieve the PEB for the current thread.
    if ((Status = g_Target->
         GetProcessInfoPeb(g_Thread, 0, 0, Offset)) != S_OK)
    {
        return Status;
    }

    //
    // Try and save a whole page for the PEB.  If that's
    // not possible, save as much as is there.
    //

    MemHandle = *Offset;
    if ((Status = g_Target->
         QueryMemoryRegion((ProcessInfo*)Process, &MemHandle,
                           &MemInfo)) != S_OK)
    {
        return Status;
    }

    *Size = g_Machine->m_PageSize;
    if (*Offset + *Size > MemInfo.BaseAddress + MemInfo.RegionSize)
    {
        *Size = (ULONG)
            ((MemInfo.BaseAddress + MemInfo.RegionSize) - *Offset);
    }

    return S_OK;
}

HRESULT
DbgSystemProvider::GetProcessTimes(IN HANDLE Process,
                                   OUT LPFILETIME Create,
                                   OUT LPFILETIME User,
                                   OUT LPFILETIME Kernel)
{
    HRESULT Status;
    ULONG64 Create64, Exit64, User64, Kernel64;

    if ((Status = g_Target->GetProcessTimes((ProcessInfo*)Process,
                                            &Create64, &Exit64,
                                            &Kernel64, &User64)) != S_OK)
    {
        return Status;
    }

    Create->dwHighDateTime = (ULONG)(Create64 >> 32);
    Create->dwLowDateTime = (ULONG)Create64;
    User->dwHighDateTime = (ULONG)(User64 >> 32);
    User->dwLowDateTime = (ULONG)User64;
    Kernel->dwHighDateTime = (ULONG)(Kernel64 >> 32);
    Kernel->dwLowDateTime = (ULONG)Kernel64;

    return S_OK;
}

HRESULT
DbgSystemProvider::ReadVirtual(IN HANDLE Process,
                               IN ULONG64 Offset,
                               OUT PVOID Buffer,
                               IN ULONG Request,
                               OUT PULONG Done)
{
    return g_Target->ReadVirtual((ProcessInfo*)Process, Offset,
                                 Buffer, Request, Done);
}

HRESULT
DbgSystemProvider::ReadAllVirtual(IN HANDLE Process,
                                  IN ULONG64 Offset,
                                  OUT PVOID Buffer,
                                  IN ULONG Request)
{
    return g_Target->ReadAllVirtual((ProcessInfo*)Process, Offset,
                                    Buffer, Request);
}

HRESULT
DbgSystemProvider::QueryVirtual(IN HANDLE Process,
                                IN ULONG64 Offset,
                                OUT PMINIDUMP_MEMORY_INFO Info)
{
    DBG_ASSERT(sizeof(MEMORY_BASIC_INFORMATION64) == sizeof(*Info));

    return g_Target->
        QueryMemoryRegion((ProcessInfo*)Process, &Offset,
                          (PMEMORY_BASIC_INFORMATION64)Info);
}

HRESULT
DbgSystemProvider::GetValidVirtualRange(IN HANDLE _Process,
                                        IN ULONG64 Start,
                                        IN ULONG Size,
                                        OUT PULONG64 ValidStart,
                                        OUT PULONG ValidSize)
{
    ProcessInfo* Process = (ProcessInfo*)_Process;
    return g_Target->GetValidRegionVirtual(Process, Start, Size,
                                           ValidStart, ValidSize);
}

HRESULT
DbgSystemProvider::StartProcessEnum(IN HANDLE Process,
                                    IN ULONG ProcessId)
{
    m_Thread = ((ProcessInfo*)Process)->m_ThreadHead;
    m_Image = ((ProcessInfo*)Process)->m_ImageHead;

    // Unloaded modules aren't critical, so just
    // ignore them if the enumerator fails.
    if (((ProcessInfo*)Process)->m_Target->
        GetUnloadedModuleInfo(FALSE, &m_UnlEnum) != S_OK ||
        m_UnlEnum->Initialize(g_Thread, MODULE_INFO_ALL_STD) != S_OK)
    {
        m_UnlEnum = NULL;
    }

    m_FuncTableStart = 0;
    m_FuncTableHandle = 0;

    return S_OK;
}

HRESULT
DbgSystemProvider::EnumThreads(OUT PULONG ThreadId)
{
    if (!m_Thread)
    {
        return S_FALSE;
    }

    *ThreadId = m_Thread->m_SystemId;
    m_Thread = m_Thread->m_Next;
    return S_OK;
}

HRESULT
DbgSystemProvider::EnumModules(OUT PULONG64 Base,
                               OUT PWSTR Path,
                               IN ULONG PathChars)
{
    if (!m_Image)
    {
        return S_FALSE;
    }

    *Base = m_Image->m_BaseOfImage;

#ifdef UNICODE
    CopyStringW(Path, m_Image->m_ImagePath, PathChars);
#else
    if (!MultiByteToWideChar(CP_ACP, 0,
                             m_Image->m_ImagePath, -1,
                             Path, PathChars))
    {
        return WIN32_LAST_STATUS();
    }
#endif

    m_Image = m_Image->m_Next;
    return S_OK;
}

HRESULT
DbgSystemProvider::EnumFunctionTables(OUT PULONG64 MinAddress,
                                      OUT PULONG64 MaxAddress,
                                      OUT PULONG64 BaseAddress,
                                      OUT PULONG EntryCount,
                                      OUT PVOID RawTable,
                                      IN ULONG RawTableSize,
                                      OUT PVOID* RawEntryHandle)
{
    HRESULT Status;
    CROSS_PLATFORM_DYNAMIC_FUNCTION_TABLE CpTable;

    if ((Status = g_Target->
         EnumFunctionTables(g_Process,
                            &m_FuncTableStart,
                            &m_FuncTableHandle,
                            MinAddress,
                            MaxAddress,
                            BaseAddress,
                            EntryCount,
                            &CpTable,
                            RawEntryHandle)) != S_OK)
    {
        return Status;
    }

    memcpy(RawTable, &CpTable, RawTableSize);
    return S_OK;
}

HRESULT
DbgSystemProvider::EnumFunctionTableEntries(IN PVOID RawTable,
                                            IN ULONG RawTableSize,
                                            IN PVOID RawEntryHandle,
                                            OUT PVOID RawEntries,
                                            IN ULONG RawEntriesSize)
{
    memcpy(RawEntries, RawEntryHandle, RawEntriesSize);
    free(RawEntryHandle);
    return S_OK;
}

HRESULT
DbgSystemProvider::EnumFunctionTableEntryMemory(IN ULONG64 TableBase,
                                                IN PVOID RawEntries,
                                                IN ULONG Index,
                                                OUT PULONG64 Start,
                                                OUT PULONG Size)
{
    return g_Machine->GetUnwindInfoBounds(g_Process,
                                          TableBase,
                                          RawEntries,
                                          Index,
                                          Start,
                                          Size);
}

HRESULT
DbgSystemProvider::EnumUnloadedModules(OUT PWSTR Path,
                                       IN ULONG PathChars,
                                       OUT PULONG64 BaseOfModule,
                                       OUT PULONG SizeOfModule,
                                       OUT PULONG CheckSum,
                                       OUT PULONG TimeDateStamp)
{
    TCHAR UnlName[MAX_INFO_UNLOADED_NAME];
    DEBUG_MODULE_PARAMETERS Params;

    if (!m_UnlEnum ||
        m_UnlEnum->GetEntry(UnlName, &Params) != S_OK)
    {
        return S_FALSE;
    }

#ifdef UNICODE
    CopyStringW(Path, UnlName, PathChars);
#else
    if (!MultiByteToWideChar(CP_ACP, 0,
                             UnlName, -1,
                             Path, PathChars))
    {
        return WIN32_LAST_STATUS();
    }
#endif

    *BaseOfModule = Params.Base;
    *SizeOfModule = Params.Size;
    *CheckSum = Params.Checksum;
    *TimeDateStamp = Params.TimeDateStamp;

    return S_OK;
}

void
DbgSystemProvider::FinishProcessEnum(void)
{
    // Nothing to do.
}

HRESULT
DbgSystemProvider::StartHandleEnum(IN HANDLE Process,
                                   IN ULONG ProcessId,
                                   OUT PULONG Count)
{
    m_Handle = 4;

    // If the target doesn't have handle data don't make
    // it a fatal error, just don't enumerate anything.
    if (g_Target->
        ReadHandleData((ProcessInfo*)Process, 0,
                       DEBUG_HANDLE_DATA_TYPE_HANDLE_COUNT,
                       Count, sizeof(*Count), NULL) != S_OK)
    {
        *Count = 0;
    }

    return S_OK;
}

HRESULT
DbgSystemProvider::EnumHandles(OUT PULONG64 Handle,
                               OUT PULONG Attributes,
                               OUT PULONG GrantedAccess,
                               OUT PULONG HandleCount,
                               OUT PULONG PointerCount,
                               OUT PWSTR TypeName,
                               IN ULONG TypeNameChars,
                               OUT PWSTR ObjectName,
                               IN ULONG ObjectNameChars)
{
    DEBUG_HANDLE_DATA_BASIC BasicInfo;

    for (;;)
    {
        if (m_Handle >= (1 << 24))
        {
            return S_FALSE;
        }

        // If we can't get the basic info and type there isn't much
        // point in writing anything out so skip the handle.
        if (g_Target->
            ReadHandleData(g_Process, m_Handle,
                           DEBUG_HANDLE_DATA_TYPE_BASIC,
                           &BasicInfo, sizeof(BasicInfo), NULL) == S_OK &&
            SUCCEEDED(g_Target->
                      ReadHandleData(g_Process, m_Handle,
                                     DEBUG_HANDLE_DATA_TYPE_TYPE_NAME_WIDE,
                                     TypeName,
                                     TypeNameChars * sizeof(*TypeName),
                                     NULL)))
        {
            break;
        }

        m_Handle += 4;
    }

    // Try and get the object name.
    if (FAILED(g_Target->
               ReadHandleData(g_Process, m_Handle,
                              DEBUG_HANDLE_DATA_TYPE_OBJECT_NAME_WIDE,
                              ObjectName,
                              ObjectNameChars * sizeof(*ObjectName),
                              NULL)))
    {
        *ObjectName = 0;
    }

    *Handle = m_Handle;
    *Attributes = BasicInfo.Attributes;
    *GrantedAccess = BasicInfo.GrantedAccess;
    *HandleCount = BasicInfo.HandleCount;
    *PointerCount = BasicInfo.PointerCount;

    m_Handle += 4;
    return S_OK;
}

void
DbgSystemProvider::FinishHandleEnum(void)
{
    // Nothing to do.
}

HRESULT
DbgSystemProvider::EnumPebMemory(IN HANDLE Process,
                                 IN ULONG64 PebOffset,
                                 IN ULONG PebSize,
                                 IN MiniDumpProviderCallbacks* Callback)
{
    if (g_Target->m_SystemVersion <= NT_SVER_START ||
        g_Target->m_SystemVersion >= NT_SVER_END)
    {
        // Basic Win32 doesn't have a defined PEB.
        return S_OK;
    }

    // XXX drewb - This requires a whole set of constants
    // to abstract data structure locations.  Leave it
    // for when we really need it.
    return S_OK;
}

HRESULT
DbgSystemProvider::EnumTebMemory(IN HANDLE Process,
                                 IN HANDLE Thread,
                                 IN ULONG64 TebOffset,
                                 IN ULONG TebSize,
                                 IN MiniDumpProviderCallbacks* Callback)
{
    if (g_Target->m_SystemVersion <= NT_SVER_START ||
        g_Target->m_SystemVersion >= NT_SVER_END)
    {
        // Basic Win32 doesn't have a defined TEB beyond
        // the TIB.  The TIB can reference fiber data but
        // that's NT-specific.
        return S_OK;
    }

    // XXX drewb - This requires a whole set of constants
    // to abstract data structure locations.  Leave it
    // for when we really need it.
    return S_OK;
}

HRESULT
DbgSystemProvider::GetClrEnum(IN PWSTR DacDllName,
                              IN struct ICLRDataTarget* Target,
                              OUT struct ICLRDataEnumMemoryRegions** Enum)
{
    HRESULT Status;

    // We're providing all of the system services to
    // the minidump code so we know that its state
    // matches what's available directly from the debugger's
    // state.  Just ignore the given DLL name and
    // service interface in favor of the one the
    // debugger already has.
    if ((Status = g_Process->LoadClrDebugDll(NULL)) != S_OK ||
        (Status = g_Process->m_ClrProcess->
         QueryInterface(__uuidof(ICLRDataEnumMemoryRegions),
                        (void**)Enum)) != S_OK)
    {
        return Status;
    }

    return S_OK;
}

void
DbgSystemProvider::ReleaseClrEnum(IN struct ICLRDataEnumMemoryRegions* Enum)
{
    Enum->Release();
}

HRESULT
DbgSystemProvider::WriteKernelMinidump(IN HANDLE File,
                                       IN HANDLE Process,
                                       IN ULONG NumThreads,
                                       IN PHANDLE ThreadHandles,
                                       IN ULONG BugCheckCode,
                                       IN ULONG Flags)
{
    // We could support this in the live user debug case
    // by remoting NtSystemDebugControl via IUserDebugServices
    // but it's not important right now.
    return E_NOINTERFACE;
}

class DbgStatusProvider : public MiniDumpStatusProvider
{
public:
    DbgStatusProvider(ULONG Filter)
    {
        m_Filter = Filter;
    }

    virtual void Release(void);

    virtual void Status(ULONG Flags, PCSTR Format, ...);

    ULONG m_Filter;
};

void
DbgStatusProvider::Release(void)
{
    delete this;
}

void
DbgStatusProvider::Status(ULONG Flags, PCSTR Format, ...)
{
    if (Flags & m_Filter)
    {
        ULONG Mask = 0;
        va_list Args;

        if (Flags & UMINIPROV_ERROR)
        {
            Mask |= DEBUG_OUTPUT_ERROR;
        }
        if (Flags & (UMINIPROV_WARNING | UMINIPROV_DATA_MISSING))
        {
            Mask |= DEBUG_OUTPUT_WARNING;
        }
        if (Flags & UMINIPROV_STATUS)
        {
            Mask |= DEBUG_OUTPUT_NORMAL;
        }

        va_start(Args, Format);
        MaskOutVaA(Mask, Format, Args, FALSE);
        va_end(Args);
        MaskOut(Mask, _T("\n"));
    }
}

PMINIDUMP_EXCEPTION_INFORMATION64
CreateMiniExceptionInformation(DUMP_WRITE_ARGS* Args,
                               PMINIDUMP_EXCEPTION_INFORMATION64 ExInfo,
                               PVOID ExRecord,
                               PCROSS_PLATFORM_CONTEXT Context)
{
    MachineInfo* Machine;
    JIT_DEBUG_INFO64 Info;
    BOOL ExEvent = 
        g_LastEventType == DEBUG_EVENT_EXCEPTION &&
        g_Process == g_EventProcess;
    
    // If the last event was not an exception and
    // we don't have override information don't
    // put any exception info in the dump.
    if (!Args->JitDebugInfoAddr &&
        (!Args->ExContextAddr || !Args->ExRecordAddr) &&
        !ExEvent)
    {
        return NULL;
    }

    //
    // If the user provided the address of a JIT_DEBUG_INFO
    // use that as the exception information to carry in
    // the minidump.  This makes it possible to take a minidump
    // from AeDebug with JIT_DEBUG_INFO that is the same as
    // if the process had been debugged and JIT_DEBUG_INFO hadn't been used.
    //
    
    if (Args->JitDebugInfoAddr)
    {
        if (g_Target->ReadJitDebugInfo(g_Process, Args->JitDebugInfoAddr,
                                       &Machine, &Info) != S_OK)
        {
            ErrOut(_T("Unable to read JIT_DEBUG_INFO at %s\n"),
                   FormatAddr64(Args->JitDebugInfoAddr));
            return NULL;
        }
        if (!Info.ContextRecord ||
            !Info.ExceptionRecord)
        {
            ErrOut(_T("JIT_DEBUG_INFO is invalid\n"));
            return NULL;
        }
        if (Machine != g_Machine)
        {
            ErrOut(_T("JIT_DEBUG_INFO machine must ")
                   _T("match the native machine\n"));
            return NULL;
        }
    }
    else
    {
        Info.ContextRecord = Args->ExContextAddr;
        Info.ExceptionRecord = Args->ExRecordAddr;
        Info.dwThreadID = Args->ExThreadId ?
            Args->ExThreadId : g_EventThreadSysId;
    }

    if (Info.ContextRecord)
    {
        if (g_Target->ReadAllVirtual(g_Process, Info.ContextRecord,
                                     Context, g_Target->m_TypeInfo.
                                     SizeTargetContext) != S_OK)
        {
            ErrOut(_T("Unable to read context record at %s\n"),
                   FormatAddr64(Info.ContextRecord));
            return NULL;
        }
    }
    else
    {
        g_Target->ChangeRegContext(g_EventThread);
        if (g_Machine->GetContextState(MCTX_CONTEXT) != S_OK)
        {
            ErrOut(_T("Unable to get event thread context\n"));
            return NULL;
        }
        *Context = g_Machine->m_Context;
    }

    if (Info.ExceptionRecord)
    {
        if (g_Target->ReadAllVirtual(g_Process, Info.ExceptionRecord,
                                     ExRecord, g_Machine->m_Ptr64 ?
                                     sizeof(EXCEPTION_RECORD64) :
                                     sizeof(EXCEPTION_RECORD32)) != S_OK)
        {
            ErrOut(_T("Unable to read exception record at %s\n"),
                   FormatAddr64(Info.ExceptionRecord));
            return NULL;
        }
    }
    else
    {
        if (g_EventMachine->m_Ptr64)
        {
            memcpy(ExRecord, &g_LastEventInfo.Exception.ExceptionRecord,
                   sizeof(g_LastEventInfo.Exception.ExceptionRecord));
        }
        else
        {
            ExceptionRecord64To32(&g_LastEventInfo.Exception.ExceptionRecord,
                                  (PEXCEPTION_RECORD32)ExRecord);
        }
    }
        
    ExInfo->ThreadId = Info.dwThreadID;
    ExInfo->ClientPointers = FALSE;
    ExInfo->ExceptionRecord = (LONG_PTR)ExRecord;
    ExInfo->ContextRecord = (LONG_PTR)Context;

    return ExInfo;
}

BOOL WINAPI
UserMiniCallback(IN PVOID CallbackParam,
                 IN CONST PMINIDUMP_CALLBACK_INPUT CallbackInput,
                 IN OUT PMINIDUMP_CALLBACK_OUTPUT CallbackOutput)
{
    DUMP_WRITE_ARGS* Args = (DUMP_WRITE_ARGS*)CallbackParam;
    
    switch(CallbackInput->CallbackType)
    {
    case IncludeModuleCallback:
        if (Args->InternalFlags & UMINI_MICRO)
        {
            // Mask off all flags other than the basic write flag.
            CallbackOutput->ModuleWriteFlags &= ModuleWriteModule;
        }
        break;
    case ModuleCallback:
        if (Args->InternalFlags & UMINI_MICRO)
        {
            // Eliminate all unreferenced modules.
            if (!(CallbackOutput->ModuleWriteFlags & ModuleReferencedByMemory))
            {
                CallbackOutput->ModuleWriteFlags = 0;
            }
        }
        break;
    case IncludeThreadCallback:
        if (Args->InternalFlags & UMINI_MICRO)
        {
            if (CallbackInput->IncludeThread.ThreadId != g_EventThreadSysId)
            {
                return FALSE;
            }

            // Reduce write to the minimum of information
            // necessary for a stack walk.
            CallbackOutput->ThreadWriteFlags &= ~ThreadWriteInstructionWindow;
        }
        break;

    case CancelCallback:
        CallbackOutput->Cancel = CheckUserInterrupt();
        CallbackOutput->CheckCancel = TRUE;
        break;

    case WriteKernelMinidumpCallback:
        if (Args->InternalFlags & UMINI_WRITE_KMINI)
        {
            Args->ExtraFileHandle =
                CreateFile(Args->ExtraFileName, GENERIC_WRITE, 0,
                           NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
            if (Args->ExtraFileHandle == INVALID_HANDLE_VALUE ||
                !Args->ExtraFileHandle)
            {
                Args->ExtraFileHandle = NULL;
                ErrOut(_T("Unable to create kernel minidump file, %s\n"),
                       FormatStatusCode(WIN32_LAST_STATUS()));
            }
            else
            {
                CallbackOutput->Handle = Args->ExtraFileHandle;
            }
        }
        break;

    case KernelMinidumpStatusCallback:
        if (Args->ExtraFileHandle)
        {
            CloseHandle(Args->ExtraFileHandle);
            Args->ExtraFileHandle = NULL;

            if (CallbackInput->Status == S_OK)
            {
                dprintf(_T("Wrote kernel minidump '%s'\n"),
                        Args->ExtraFileName);
            }
            else
            {
                WideDeleteFile(Args->ExtraFileName);
            }
        }
        break;
    }

    return TRUE;
}

HRESULT
UserMiniDumpTargetInfo::Write(HANDLE hFile, DUMP_WRITE_ARGS* Args)
{
    if (!IS_USER_TARGET(g_Target))
    {
        ErrOut(_T("User minidumps can only be written ")
               _T("in user-mode sessions\n"));
        return E_UNEXPECTED;
    }

    dprintf(_T("mini user dump\n"));
    FlushCallbacks();

    // Clear interrupt status.
    CheckUserInterrupt();
    
    HRESULT Status;
    MINIDUMP_EXCEPTION_INFORMATION64 ExInfoBuf, *ExInfo;
    EXCEPTION_RECORD64 ExRecord;
    CROSS_PLATFORM_CONTEXT Context;
    ULONG MiniType;
    MINIDUMP_USER_STREAM UserStreams[2];
    MINIDUMP_USER_STREAM_INFORMATION UserStreamInfo;
    MINIDUMP_CALLBACK_INFORMATION CallbackBuffer;

    MiniType = MiniDumpNormal;
    if (Args->FormatFlags & DEBUG_FORMAT_USER_SMALL_FULL_MEMORY)
    {
        MiniType |= MiniDumpWithFullMemory;
    }
    if (Args->FormatFlags & DEBUG_FORMAT_USER_SMALL_HANDLE_DATA)
    {
        MiniType |= MiniDumpWithHandleData;
    }
    if (Args->FormatFlags & DEBUG_FORMAT_USER_SMALL_UNLOADED_MODULES)
    {
        MiniType |= MiniDumpWithUnloadedModules;
    }
    if (Args->FormatFlags & DEBUG_FORMAT_USER_SMALL_INDIRECT_MEMORY)
    {
        MiniType |= MiniDumpWithIndirectlyReferencedMemory;
    }
    if (Args->FormatFlags & DEBUG_FORMAT_USER_SMALL_DATA_SEGMENTS)
    {
        MiniType |= MiniDumpWithDataSegs;
    }
    if (Args->FormatFlags & DEBUG_FORMAT_USER_SMALL_FILTER_MEMORY)
    {
        MiniType |= MiniDumpFilterMemory;
    }
    if (Args->FormatFlags & DEBUG_FORMAT_USER_SMALL_FILTER_PATHS)
    {
        MiniType |= MiniDumpFilterModulePaths;
    }
    if (Args->FormatFlags & DEBUG_FORMAT_USER_SMALL_PROCESS_THREAD_DATA)
    {
        MiniType |= MiniDumpWithProcessThreadData;
    }
    if (Args->FormatFlags & DEBUG_FORMAT_USER_SMALL_PRIVATE_READ_WRITE_MEMORY)
    {
        MiniType |= MiniDumpWithPrivateReadWriteMemory;
    }
    if (Args->FormatFlags & DEBUG_FORMAT_USER_SMALL_NO_OPTIONAL_DATA)
    {
        MiniType |= MiniDumpWithoutOptionalData;
    }
    if (Args->FormatFlags & DEBUG_FORMAT_USER_SMALL_FULL_MEMORY_INFO)
    {
        MiniType |= MiniDumpWithFullMemoryInfo;
    }
    if (Args->FormatFlags & DEBUG_FORMAT_USER_SMALL_THREAD_INFO)
    {
        MiniType |= MiniDumpWithThreadInfo;
    }
    if (Args->FormatFlags & DEBUG_FORMAT_USER_SMALL_CODE_SEGMENTS)
    {
        MiniType |= MiniDumpWithCodeSegs;
    }

    UserStreamInfo.UserStreamCount = 0;
    UserStreamInfo.UserStreamArray = UserStreams;
    if (Args->CommentA != NULL)
    {
        UserStreams[UserStreamInfo.UserStreamCount].Type =
            CommentStreamA;
        UserStreams[UserStreamInfo.UserStreamCount].BufferSize =
            strlen(Args->CommentA) + 1;
        UserStreams[UserStreamInfo.UserStreamCount].Buffer =
            (PVOID)Args->CommentA;
        UserStreamInfo.UserStreamCount++;
    }
    if (Args->CommentW != NULL)
    {
        UserStreams[UserStreamInfo.UserStreamCount].Type =
            CommentStreamW;
        UserStreams[UserStreamInfo.UserStreamCount].BufferSize =
            (wcslen(Args->CommentW) + 1) * sizeof(WCHAR);
        UserStreams[UserStreamInfo.UserStreamCount].Buffer =
            (PVOID)Args->CommentW;
        UserStreamInfo.UserStreamCount++;
    }

    ExInfo = CreateMiniExceptionInformation(Args, &ExInfoBuf,
                                            &ExRecord, &Context);

    CallbackBuffer.CallbackRoutine = UserMiniCallback;
    CallbackBuffer.CallbackParam = Args;
    
    if (Args->InternalFlags & UMINI_MICRO)
    {
        // This case isn't expected to be used by users,
        // it's for testing of the microdump support.
        ExInfo = NULL;
        MiniType |= MiniDumpScanMemory;
    }

    HANDLE ProcHandle;
    MiniDumpSystemProvider* SysProv = NULL;
    MiniDumpOutputProvider* OutProv = NULL;
    MiniDumpAllocationProvider* AllocProv = NULL;
    DbgStatusProvider StatusProv(UMINIPROV_WARNING |
                                 UMINIPROV_ERROR);

    if ((Status =
         MiniDumpCreateLiveAllocationProvider(&AllocProv)) != S_OK ||
        (Status =
         MiniDumpCreateFileOutputProvider(AllocProv, hFile,
                                          &OutProv)) != S_OK)
    {
        goto Exit;
    }

    //
    // If we're live we can let the official minidump
    // code do all the work.  If not, hook up a provider
    // that uses debugger information.  This provider
    // could always be used but the default live-system
    // provider offers slightly more information so
    // check and use that if possible.
    //

    if (IS_LIVE_USER_TARGET(g_Target) &&
        ((LiveUserTargetInfo*)g_Target)->m_Local &&
        !(Args->InternalFlags & UMINI_FORCE_DBG))
    {
        if ((Status =
             MiniDumpCreateLiveSystemProvider(AllocProv, &SysProv)) != S_OK)
        {
            goto Exit;
        }

        ProcHandle = OS_HANDLE(g_Process->m_SysHandle);
    }
    else
    {
        DbgSystemProvider* DbgSysProv = new DbgSystemProvider;
        if (!DbgSysProv)
        {
            Status = E_OUTOFMEMORY;
            goto Exit;
        }

        SysProv = DbgSysProv;
        ProcHandle = (HANDLE)g_Process;
    }

    Status = MiniDumpProvideDump(ProcHandle, g_Process->m_SystemId,
                                 SysProv, OutProv, AllocProv, &StatusProv,
                                 MiniType, ExInfo,
                                 &UserStreamInfo, &CallbackBuffer);

 Exit:

    if (Status != S_OK)
    {
        ErrOut(_T("Dump creation failed, %s\n    \"%s\"\n"),
               FormatStatusCode(Status), FormatStatus(Status));
    }

    if (SysProv)
    {
        SysProv->Release();
    }
    if (OutProv)
    {
        OutProv->Release();
    }
    if (AllocProv)
    {
        AllocProv->Release();
    }

    // Reset the current register context in case
    // it was changed at some point.
    g_Target->ChangeRegContext(g_Thread);

    return Status;
}

//-------------------------------------------------------------------
//  initialize the dump headers
//

#define MINIDUMP_BUGCHECK 0x10000000

void
KernelDumpTargetInfo::InitDumpHeader32(PDUMP_HEADER32 Hdr,
                                       PCSTR CommentA,
                                       PCWSTR CommentW,
                                       ULONG BugCheckCodeModifier)
{
    ULONG64 Data[4];
    PULONG  FillPtr = (PULONG)Hdr;

    while (FillPtr < (PULONG)(Hdr + 1))
    {
        *FillPtr++ = DUMP_SIGNATURE32;
    }

    Hdr->Signature           = DUMP_SIGNATURE32;
    Hdr->ValidDump           = DUMP_VALID_DUMP32;
    Hdr->MajorVersion        = g_Target->m_KdVersion.MajorVersion;
    Hdr->MinorVersion        = g_Target->m_KdVersion.MinorVersion;

    g_Target->ReadDirectoryTableBase(Data);
    Hdr->DirectoryTableBase  = (ULONG)Data[0];

    Hdr->PfnDataBase         =
        (ULONG)g_Target->m_KdDebuggerData.MmPfnDatabase;
    Hdr->PsLoadedModuleList  =
        (ULONG)g_Target->m_KdDebuggerData.PsLoadedModuleList;
    Hdr->PsActiveProcessHead =
        (ULONG)g_Target->m_KdDebuggerData.PsActiveProcessHead;
    Hdr->MachineImageType    = g_Target->m_KdVersion.MachineType;
    Hdr->NumberProcessors    = g_Target->m_NumProcessors;

    g_Target->ReadBugCheckData(&(Hdr->BugCheckCode), Data);
    Hdr->BugCheckCode       |= BugCheckCodeModifier;
    Hdr->BugCheckParameter1  = (ULONG)Data[0];
    Hdr->BugCheckParameter2  = (ULONG)Data[1];
    Hdr->BugCheckParameter3  = (ULONG)Data[2];
    Hdr->BugCheckParameter4  = (ULONG)Data[3];

    Hdr->PaeEnabled          = g_Target->m_KdDebuggerData.PaeEnabled;
    Hdr->KdDebuggerDataBlock = (ULONG)g_Target->m_KdDebuggerDataOffset;

    if (IS_CONTEXT_POSSIBLE(g_Target))
    {
        g_Machine->GetContextState(MCTX_CONTEXT);
        g_Machine->ConvertCanonContextToTarget(&g_Machine->m_Context,
                                               sizeof(Hdr->ContextRecord),
                                               Hdr->ContextRecord);
    }
    else
    {
        ZeroMemory(Hdr->ContextRecord, sizeof(Hdr->ContextRecord));
    }

    if (g_LastEventType == DEBUG_EVENT_EXCEPTION)
    {
        // Use the exception record from the last event.
        ExceptionRecord64To32(&g_LastEventInfo.Exception.ExceptionRecord,
                              &Hdr->Exception);
    }
    else
    {
        ADDR PcAddr;

        // Fake a breakpoint exception.
        ZeroMemory(&Hdr->Exception, sizeof(Hdr->Exception));
        Hdr->Exception.ExceptionCode = STATUS_BREAKPOINT;
        if (IS_CONTEXT_POSSIBLE(g_Target))
        {
            g_Machine->GetPC(&PcAddr);
            Hdr->Exception.ExceptionAddress = (ULONG)Flat(PcAddr);
        }
    }

    Hdr->RequiredDumpSpace.QuadPart = TRIAGE_DUMP_SIZE32 +
        DumpGetTaggedDataSize();

    Hdr->SystemTime.QuadPart = g_Target->GetCurrentTimeDateN();
    Hdr->SystemUpTime.QuadPart = g_Target->GetCurrentSystemUpTimeN();

    if (g_Target->m_ProductType != INVALID_PRODUCT_TYPE)
    {
        Hdr->ProductType = g_Target->m_ProductType;
        Hdr->SuiteMask = g_Target->m_SuiteMask;
    }

    PSTR ConvComment = NULL;

    if (!CommentA && CommentW)
    {
        if (WideToAnsi(CommentW, &ConvComment) != S_OK)
        {
            ConvComment = NULL;
        }
        else
        {
            CommentA = ConvComment;
        }
    }
    if (CommentA != NULL && CommentA[0])
    {
        CopyString(Hdr->Comment, CommentA, DIMA(Hdr->Comment));
    }
    FreeAnsi(ConvComment);
}

void
KernelDumpTargetInfo::InitDumpHeader64(PDUMP_HEADER64 Hdr,
                                       PCSTR CommentA,
                                       PCWSTR CommentW,
                                       ULONG BugCheckCodeModifier)
{
    ULONG64 Data[4];
    PULONG  FillPtr = (PULONG)Hdr;

    while (FillPtr < (PULONG)(Hdr + 1))
    {
        *FillPtr++ = DUMP_SIGNATURE32;
    }

    Hdr->Signature           = DUMP_SIGNATURE64;
    Hdr->ValidDump           = DUMP_VALID_DUMP64;
    Hdr->MajorVersion        = g_Target->m_KdVersion.MajorVersion;
    Hdr->MinorVersion        = g_Target->m_KdVersion.MinorVersion;

    // IA64 has several page directories.  The defined
    // behavior is to put the kernel page directory
    // in the dump header as that's the one that can
    // be most useful when first initializing the dump.
    if (g_Target->m_EffMachineType == IMAGE_FILE_MACHINE_IA64)
    {
        ULONG Next;

        if (g_Machine->
            SetPageDirectory(g_Thread, PAGE_DIR_KERNEL, 0, &Next) != S_OK)
        {
            ErrOut(_T("Unable to update the kernel dirbase\n"));
            Data[0] = 0;
        }
        else
        {
            Data[0] = g_Machine->m_PageDirectories[PAGE_DIR_KERNEL];
        }
    }
    else
    {
        g_Target->ReadDirectoryTableBase(Data);
    }
    Hdr->DirectoryTableBase  = Data[0];

    Hdr->PfnDataBase         =
        g_Target->m_KdDebuggerData.MmPfnDatabase;
    Hdr->PsLoadedModuleList  =
        g_Target->m_KdDebuggerData.PsLoadedModuleList;
    Hdr->PsActiveProcessHead =
        g_Target->m_KdDebuggerData.PsActiveProcessHead;
    Hdr->MachineImageType    = g_Target->m_KdVersion.MachineType;
    Hdr->NumberProcessors    = g_Target->m_NumProcessors;

    g_Target->ReadBugCheckData(&(Hdr->BugCheckCode), Data);
    Hdr->BugCheckCode       |= BugCheckCodeModifier;
    Hdr->BugCheckParameter1  = Data[0];
    Hdr->BugCheckParameter2  = Data[1];
    Hdr->BugCheckParameter3  = Data[2];
    Hdr->BugCheckParameter4  = Data[3];

    Hdr->KdDebuggerDataBlock = g_Target->m_KdDebuggerDataOffset;

    if (IS_CONTEXT_POSSIBLE(g_Target))
    {
        g_Machine->GetContextState(MCTX_CONTEXT);
        g_Machine->ConvertCanonContextToTarget(&g_Machine->m_Context,
                                               sizeof(Hdr->ContextRecord),
                                               Hdr->ContextRecord);
    }
    else
    {
        ZeroMemory(Hdr->ContextRecord, sizeof(Hdr->ContextRecord));
    }

    if (g_LastEventType == DEBUG_EVENT_EXCEPTION)
    {
        // Use the exception record from the last event.
        Hdr->Exception = g_LastEventInfo.Exception.ExceptionRecord;
    }
    else
    {
        ADDR PcAddr;

        // Fake a breakpoint exception.
        ZeroMemory(&Hdr->Exception, sizeof(Hdr->Exception));
        Hdr->Exception.ExceptionCode = STATUS_BREAKPOINT;
        if (IS_CONTEXT_POSSIBLE(g_Target))
        {
            g_Machine->GetPC(&PcAddr);
            Hdr->Exception.ExceptionAddress = Flat(PcAddr);
        }
    }

    Hdr->RequiredDumpSpace.QuadPart = TRIAGE_DUMP_SIZE64 +
        DumpGetTaggedDataSize();

    Hdr->SystemTime.QuadPart = g_Target->GetCurrentTimeDateN();
    Hdr->SystemUpTime.QuadPart = g_Target->GetCurrentSystemUpTimeN();

    if (g_Target->m_ProductType != INVALID_PRODUCT_TYPE)
    {
        Hdr->ProductType = g_Target->m_ProductType;
        Hdr->SuiteMask = g_Target->m_SuiteMask;
    }

    PSTR ConvComment = NULL;

    if (!CommentA && CommentW)
    {
        if (WideToAnsi(CommentW, &ConvComment) != S_OK)
        {
            ConvComment = NULL;
        }
        else
        {
            CommentA = ConvComment;
        }
    }
    if (CommentA != NULL && CommentA[0])
    {
        CopyString(Hdr->Comment, CommentA, DIMA(Hdr->Comment));
    }
    FreeAnsi(ConvComment);
}

#define DUMP_BLOB_ALIGN 8

ULONG
KernelDumpTargetInfo::DumpGetTaggedDataSize(void)
{
    HRESULT Status;
    ULONG64 HdrOffs;
    DUMP_BLOB_HEADER BlobHdr;
    ULONG Size;

    if ((Status = GetFirstBlobHeaderOffset(&HdrOffs)) != S_OK)
    {
        return 0;
    }

    Size = sizeof(DUMP_BLOB_FILE_HEADER);

    //
    // Loop over every entry and accumulate the size.
    // We assume only 8-byte-alignment post-padding
    // will be necessary.
    //

    while (ReadBlobHeaderAtOffset(HdrOffs, &BlobHdr) == S_OK)
    {
        Size += sizeof(BlobHdr) +
            ((BlobHdr.DataSize + (DUMP_BLOB_ALIGN - 1)) &
             ~(DUMP_BLOB_ALIGN - 1));
        
        HdrOffs += BlobHdr.HeaderSize + BlobHdr.PrePad +
            BlobHdr.DataSize + BlobHdr.PostPad;
    }

    return Size;
}

HRESULT
KernelDumpTargetInfo::DumpWriteTaggedData(HANDLE File)
{
    HRESULT Status;
    ULONG64 HdrOffs;
    DUMP_BLOB_FILE_HEADER FileHdr;
    DUMP_BLOB_HEADER BlobHdr;
    BYTE Buffer[8192];

    C_ASSERT((sizeof(FileHdr) & (DUMP_BLOB_ALIGN - 1)) == 0);
    C_ASSERT((sizeof(BlobHdr) & (DUMP_BLOB_ALIGN - 1)) == 0);
    
    if ((Status = GetFirstBlobHeaderOffset(&HdrOffs)) != S_OK)
    {
        // E_NOINTERFACE indicates no tagged data, so
        // we trivially succeed in that case.
        return Status == E_NOINTERFACE ? S_OK : Status;
    }

    //
    // We have tagged data, so write a file header.
    //

    FileHdr.Signature1 = DUMP_BLOB_SIGNATURE1;
    FileHdr.Signature2 = DUMP_BLOB_SIGNATURE2;
    FileHdr.HeaderSize = sizeof(FileHdr);
    FileHdr.BuildNumber = g_Target->m_BuildNumber |
        (g_Target->m_CheckedBuild << 28);

    if ((Status = DumpWriteFile(File, &FileHdr, sizeof(FileHdr))) != S_OK)
    {
        return Status;
    }

    //
    // Loop over every entry and copy the data into the
    // dump file being written.
    //

    while (ReadBlobHeaderAtOffset(HdrOffs, &BlobHdr) == S_OK)
    {
        DUMP_BLOB_HEADER WriteBlobHdr;

        //
        // We're always going to write out data packed so the
        // only padding we need is a post-pad on the data to
        // keep headers 8-byte-aligned.
        //

        ZeroMemory(&WriteBlobHdr, sizeof(WriteBlobHdr));
        memcpy(&WriteBlobHdr, &BlobHdr, BlobHdr.HeaderSize);
        WriteBlobHdr.HeaderSize = sizeof(WriteBlobHdr);
        WriteBlobHdr.PrePad = 0;
        WriteBlobHdr.PostPad =
            ((BlobHdr.DataSize + (DUMP_BLOB_ALIGN - 1)) &
             ~(DUMP_BLOB_ALIGN - 1)) - BlobHdr.DataSize;
        
        if ((Status = DumpWriteFile(File, &WriteBlobHdr,
                                    sizeof(WriteBlobHdr))) != S_OK)
        {
            return Status;
        }

        //
        // Copy the raw data.
        //
        
        HdrOffs += BlobHdr.HeaderSize + BlobHdr.PrePad;

        while (BlobHdr.DataSize)
        {
            ULONG Req = BlobHdr.DataSize < sizeof(Buffer) ?
                BlobHdr.DataSize : sizeof(Buffer);
            if ((Status = g_Target->
                 ReadTagged(HdrOffs, Buffer, Req)) != S_OK ||
                (Status = DumpWriteFile(File, Buffer, Req)) != S_OK)
            {
                return Status;
            }

            BlobHdr.DataSize -= Req;
            HdrOffs += Req;
        }

        //
        // Write postpad.
        //

        if (WriteBlobHdr.PostPad)
        {
            ZeroMemory(Buffer, WriteBlobHdr.PostPad);
            if ((Status = DumpWriteFile(File, Buffer,
                                        WriteBlobHdr.PostPad)) != S_OK)
            {
                return Status;
            }
        }
        
        HdrOffs += BlobHdr.PostPad;
    }

    return S_OK;
}

//----------------------------------------------------------------------------
//
// Kernel full dumps.
//
//----------------------------------------------------------------------------

HRESULT
KernelDumpTargetInfo::DumpWritePhysicalMemoryToFile(HANDLE File,
                                                    DUMP_WRITE_ARGS* Args,
                                                    PVOID PhysDesc,
                                                    BOOL Ptr64)
{
    HRESULT Status;
    DbgKdTransport* KdTrans;
    HANDLE PhysMemFile = NULL;
    PUCHAR PageBuffer = NULL;

    //
    // Write the physical memory out to disk.
    // There are three sources of physical memory data:
    // 1. The user specified a file to open and read.  The
    //    file offsets must correspond to physical addresses.
    // 2. Some transports, such as 1394, directly support access
    //    to physical memory.
    // 3. The target's physical memory read routine.
    //

    if (IS_CONN_KERNEL_TARGET(g_Target))
    {
        KdTrans = ((ConnLiveKernelTargetInfo*)g_Target)->m_Transport;
    }
    else
    {
        KdTrans = NULL;
    }

    if (Args->PhysMemFileName)
    {
        PhysMemFile = CreateFileT(Args->PhysMemFileName,
                                  GENERIC_READ, FILE_SHARE_READ,
                                  NULL, OPEN_EXISTING,
                                  FILE_ATTRIBUTE_NORMAL, NULL);
        if (!PhysMemFile || PhysMemFile == INVALID_HANDLE_VALUE)
        {
            PhysMemFile = NULL;
            Status = WIN32_LAST_STATUS();
            ErrOut(_T("Unable to open physical memory file '%s' - %s\n    ")
                   _T("\"%s\"\n"),
                   Args->PhysMemFileName,
                   FormatStatusCode(Status), FormatStatus(Status));
            goto Exit;
        }
    }
            
    PageBuffer = (PUCHAR)malloc(g_Machine->m_PageSize);
    if (!PageBuffer)
    {
        ErrOut(_T("Unable to allocate page buffer\n"));
        Status = E_OUTOFMEMORY;
        goto Exit;
    }

    ULONG NumRuns;
    ULONG64 NumPages;
    PPHYSICAL_MEMORY_RUN64 Run64;
    PPHYSICAL_MEMORY_RUN32 Run32;

    if (Ptr64)
    {
        PPHYSICAL_MEMORY_DESCRIPTOR64 Phys =
            (PPHYSICAL_MEMORY_DESCRIPTOR64)PhysDesc;

        NumRuns = Phys->NumberOfRuns;
        NumPages = Phys->NumberOfPages;
        Run64 = Phys->Run;
    }
    else
    {
        PPHYSICAL_MEMORY_DESCRIPTOR32 Phys =
            (PPHYSICAL_MEMORY_DESCRIPTOR32)PhysDesc;

        NumRuns = Phys->NumberOfRuns;
        NumPages = Phys->NumberOfPages;
        Run32 = Phys->Run;
    }
        
    ULONG64 CurrentPagesWritten = 0;
    ULONG Percent = 0;

    for (ULONG Run = 0; Run < NumRuns; Run++)
    {
        ULONG64 RunPages;
        ULONG64 Offset;
        
        if (Ptr64)
        {
            RunPages = Run64[Run].PageCount;
            Offset = Run64[Run].BasePage * g_Machine->m_PageSize;
        }
        else
        {
            RunPages = Run32[Run].PageCount;
            Offset = (ULONG64)Run32[Run].BasePage * g_Machine->m_PageSize;
        }

        for (ULONG64 Page = 0; Page < RunPages; Page++)
        {
            if (CheckUserInterrupt())
            {
                ErrOut(_T("--Interrupt\n"));
                Status = HRESULT_FROM_NT(STATUS_CONTROL_C_EXIT);
                goto Exit;
            }

            if ((CurrentPagesWritten * 100) / NumPages ==
                Percent)
            {
                dprintf(_T("Percent written %d\n"), Percent);
                FlushCallbacks();
                
                if (PhysMemFile ||
                    (KdTrans &&
                     KdTrans->m_DirectPhysicalMemory))
                {
                    Percent += 5;
                }
                else
                {
                    Percent += 1;
                }
            }

            ULONG Done;
            PCTSTR SourceName;

            if (PhysMemFile)
            {
                SourceName = _T("physical memory file");
                
                LONG High = (LONG)(Offset >> 32);
                ULONG Low = (ULONG)Offset;

                Low = SetFilePointer(PhysMemFile, Low, &High, FILE_BEGIN);
                if (Low == INVALID_SET_FILE_POINTER && GetLastError())
                {
                    Status = WIN32_LAST_STATUS();
                }
                else if (!ReadFile(PhysMemFile, PageBuffer,
                                   g_Machine->m_PageSize, &Done, NULL))
                {
                    Status = WIN32_LAST_STATUS();
                }
                else if (Done < g_Machine->m_PageSize)
                {
                    Status = HRESULT_FROM_WIN32(ERROR_READ_FAULT);
                }
                else
                {
                    Status = S_OK;
                }
            }
            else if (KdTrans &&
                     KdTrans->m_DirectPhysicalMemory)
            {
                SourceName = _T("transport physical memory");

                Status = KdTrans->
                    ReadTargetPhysicalMemory(Offset,
                                             PageBuffer,
                                             g_Machine->m_PageSize,
                                             &Done);
                if (Status == S_OK && Done < g_Machine->m_PageSize)
                {
                    Status = HRESULT_FROM_WIN32(ERROR_READ_FAULT);
                }
            }
            else
            {
                SourceName = _T("target physical memory");
                
                Status = g_Target->ReadPhysical(Offset,
                                                PageBuffer,
                                                g_Machine->m_PageSize,
                                                PHYS_FLAG_DEFAULT,
                                                &Done);
                if (Status == S_OK && Done < g_Machine->m_PageSize)
                {
                    Status = HRESULT_FROM_WIN32(ERROR_READ_FAULT);
                }
            }

            if (Status != S_OK)
            {
                ErrOut(_T("Failed reading %s at %I64x, %s\n"),
                       SourceName, Offset, FormatStatusCode(Status));
                goto Exit;
            }

            if ((Status = DumpWriteFile(File, PageBuffer,
                                        g_Machine->m_PageSize)) != S_OK)
            {
                goto Exit;
            }

            Offset += g_Machine->m_PageSize;
            CurrentPagesWritten++;
        }
    }

    Status = S_OK;
    
 Exit:
    if (PhysMemFile)
    {
        CloseHandle(PhysMemFile);
    }
    free(PageBuffer);
    return Status;
}

HRESULT
KernelFull64DumpTargetInfo::Write(HANDLE File, DUMP_WRITE_ARGS* Args)
{
    HRESULT Status;
    PDUMP_HEADER64 DumpHeader;
    ULONG64 Offset;
    PPHYSICAL_MEMORY_DESCRIPTOR64 PhysMem;
    ULONG BytesRead;

    if (!IS_REMOTE_KERNEL_TARGET(g_Target) &&
        !IS_KERNEL_FULL_DUMP(g_Target) &&
        !IS_LOCAL_KERNEL_TARGET(g_Target))
    {
        ErrOut(_T("\nKernel full dumps can only be ")
               _T("written when all of physical ")
               _T("memory is accessible - aborting now\n"));
        return E_INVALIDARG;
    }

    DumpHeader = (PDUMP_HEADER64)malloc(sizeof(*DumpHeader));
    if (!DumpHeader)
    {
        ErrOut(_T("Unable to allocate dump header buffer\n"));
        return E_OUTOFMEMORY;
    }

    dprintf(_T("Full kernel dump\n"));
    FlushCallbacks();

    KernelDumpTargetInfo::InitDumpHeader64(DumpHeader,
                                           Args->CommentA,
                                           Args->CommentW,
                                           0);
    DumpHeader->DumpType = DUMP_TYPE_FULL;
    DumpHeader->WriterStatus = DUMP_DBGENG_SUCCESS;

    //
    // Copy the memory descriptor list to our header.
    // First get the pointer VA.
    //

    if ((Status = g_Target->
         ReadPointer(g_Process, g_Target->m_Machine,
                     g_Target->m_KdDebuggerData.
                     MmPhysicalMemoryBlock,
                     &Offset)) != S_OK ||
        !Offset)
    {
        ErrOut(_T("Unable to read MmPhysicalMemoryBlock\n"));
        Status = Status != S_OK ? Status : E_INVALIDARG;
        goto Exit;
    }

    //
    // First read the memory descriptor size.
    //

    PhysMem = &DumpHeader->PhysicalMemoryBlock;
    if ((Status = g_Target->
         ReadVirtual(g_Process, Offset,
                     PhysMem, DMP_PHYSICAL_MEMORY_BLOCK_SIZE_64,
                     &BytesRead)) != S_OK ||
        BytesRead < sizeof(*PhysMem) +
        (sizeof(PhysMem->Run[0]) * (PhysMem->NumberOfRuns - 1)))
    {
        ErrOut(_T("Unable to read MmPhysicalMemoryBlock\n"));
        Status = Status != S_OK ? Status : E_INVALIDARG;
        goto Exit;
    }
    
    //
    // Calculate total dump file size.
    //

    DumpHeader->RequiredDumpSpace.QuadPart =
        sizeof(*DumpHeader) +
        DumpHeader->PhysicalMemoryBlock.NumberOfPages *
        g_Machine->m_PageSize +
        DumpGetTaggedDataSize();

    //
    // Write dump header to crash dump file.
    //

    if ((Status = DumpWriteFile(File, DumpHeader,
                                sizeof(*DumpHeader))) != S_OK)
    {
        goto Exit;
    }

    Status = DumpWritePhysicalMemoryToFile(File, Args, PhysMem, TRUE);

    if (Status == S_OK)
    {
        Status = DumpWriteTaggedData(File);
    }

 Exit:
    free(DumpHeader);
    return Status;
}

HRESULT
KernelFull32DumpTargetInfo::Write(HANDLE File, DUMP_WRITE_ARGS* Args)
{
    HRESULT Status;
    PDUMP_HEADER32 DumpHeader;
    ULONG64 Offset;
    PPHYSICAL_MEMORY_DESCRIPTOR32 PhysMem;
    ULONG BytesRead;

    if (!IS_REMOTE_KERNEL_TARGET(g_Target) &&
        !IS_KERNEL_FULL_DUMP(g_Target) &&
        !IS_LOCAL_KERNEL_TARGET(g_Target))
    {
        ErrOut(_T("\nKernel full dumps can only be ")
               _T("written when all of physical ")
               _T("memory is accessible - aborting now\n"));
        return E_INVALIDARG;
    }

    DumpHeader = (PDUMP_HEADER32)malloc(sizeof(*DumpHeader));
    if (!DumpHeader)
    {
        ErrOut(_T("Unable to allocate dump header buffer\n"));
        return E_OUTOFMEMORY;
    }

    dprintf(_T("Full kernel dump\n"));
    FlushCallbacks();

    KernelDumpTargetInfo::InitDumpHeader32(DumpHeader,
                                           Args->CommentA,
                                           Args->CommentW,
                                           0);
    DumpHeader->DumpType = DUMP_TYPE_FULL;
    DumpHeader->WriterStatus = DUMP_DBGENG_SUCCESS;

    //
    // Copy the memory descriptor list to our header.
    // First get the pointer VA.
    //

    if ((Status = g_Target->
         ReadPointer(g_Process, g_Target->m_Machine,
                     g_Target->m_KdDebuggerData.
                     MmPhysicalMemoryBlock,
                     &Offset)) != S_OK ||
        !Offset)
    {
        ErrOut(_T("Unable to read MmPhysicalMemoryBlock\n"));
        Status = Status != S_OK ? Status : E_INVALIDARG;
        goto Exit;
    }

    //
    // First read the memory descriptor size.
    //

    PhysMem = &DumpHeader->PhysicalMemoryBlock;
    if ((Status = g_Target->
         ReadVirtual(g_Process, Offset,
                     PhysMem, DMP_PHYSICAL_MEMORY_BLOCK_SIZE_32,
                     &BytesRead)) != S_OK ||
        BytesRead < sizeof(*PhysMem) +
        (sizeof(PhysMem->Run[0]) * (PhysMem->NumberOfRuns - 1)))
    {
        ErrOut(_T("Unable to read MmPhysicalMemoryBlock\n"));
        Status = Status != S_OK ? Status : E_INVALIDARG;
        goto Exit;
    }
    
    //
    // Calculate total dump file size.
    //

    DumpHeader->RequiredDumpSpace.QuadPart =
        sizeof(*DumpHeader) +
        DumpHeader->PhysicalMemoryBlock.NumberOfPages *
        g_Machine->m_PageSize +
        DumpGetTaggedDataSize();

    //
    // Write dump header to crash dump file.
    //

    if ((Status = DumpWriteFile(File, DumpHeader,
                                sizeof(*DumpHeader))) != S_OK)
    {
        goto Exit;
    }

    Status = DumpWritePhysicalMemoryToFile(File, Args, PhysMem, FALSE);

    if (Status == S_OK)
    {
        Status = DumpWriteTaggedData(File);
    }

 Exit:
    free(DumpHeader);
    return Status;
}

enum
{
    GNME_ENTRY,
    GNME_DONE,
    GNME_NO_NAME,
    GNME_CORRUPT,
};

ULONG
GetNextModuleEntry(ModuleInfo *ModIter,
                   MODULE_INFO_ENTRY *ModEntry)
{
    HRESULT Status;

    ModEntry->Reset();
    if ((Status = ModIter->GetEntry(ModEntry)) != S_OK)
    {
        return Status == S_FALSE ? GNME_DONE : GNME_CORRUPT;
    }

    if (ModEntry->NameBytes > (MAX_IMAGE_PATH - 1) * ModEntry->NameCharBytes)
    {
        ErrOut(_T("Module list is corrupt."));
        if (IS_KERNEL_TARGET(g_Target))
        {
            ErrOut(_T("  Check your kernel symbols.\n"));
        }
        else
        {
            ErrOut(_T("  Loader list may be invalid\n"));
        }
        return GNME_CORRUPT;
    }

    // If this entry has no name just skip it.
    if (!ModEntry->NamePtr || !ModEntry->NameBytes)
    {
        ErrOut(_T("  Module List has empty entry in it - skipping\n"));
        return GNME_NO_NAME;
    }

    // If the image header information couldn't be read
    // we end up with placeholder values for certain entries.
    // The kernel writes out zeroes in this case so copy
    // its behavior so that there's one consistent value
    // for unknown.
    if (ModEntry->CheckSum == UNKNOWN_CHECKSUM)
    {
        ModEntry->CheckSum = 0;
    }
    if (ModEntry->TimeDateStamp == UNKNOWN_TIMESTAMP)
    {
        ModEntry->TimeDateStamp = 0;
    }

    return GNME_ENTRY;
}

//----------------------------------------------------------------------------
//
// Shared triage writing things.
//
//----------------------------------------------------------------------------

#define ExtractValue(NAME, val)  {                                        \
    if (!g_Target->m_KdDebuggerData.NAME) {                               \
        val = 0;                                                          \
        ErrOut(_T("KdDebuggerData.") _T(#NAME) _T(" is NULL\n"));         \
    } else {                                                              \
        g_Target->ReadAllVirtual(g_Process,                               \
                                 g_Target->m_KdDebuggerData.NAME, &(val), \
                                 sizeof(val));                            \
    }                                                                     \
}

inline ALIGN_8(unsigned Offset)
{
    return (Offset + 7) & 0xfffffff8;
}

const unsigned MAX_TRIAGE_STACK_SIZE32 = 16 * 1024;
const unsigned MAX_TRIAGE_STACK_SIZE64 = 32 * 1024;
const unsigned MAX_TRIAGE_BSTORE_SIZE = 16 * 4096;  // as defined in ntia64.h
const ULONG TRIAGE_DRIVER_NAME_SIZE_GUESS = 0x40;

typedef struct _TRIAGE_PTR_DATA_BLOCK
{
    ULONG64 MinAddress;
    ULONG64 MaxAddress;
} TRIAGE_PTR_DATA_BLOCK, *PTRIAGE_PTR_DATA_BLOCK;

// A triage dump is sixteen pages long.  Some of that is
// header information and at least a few other pages will
// be used for basic dump information so limit the number
// of extra data blocks to something less than sixteen
// to save array space.
#define IO_MAX_TRIAGE_DUMP_DATA_BLOCKS 64

ULONG IopNumTriageDumpDataBlocks;
TRIAGE_PTR_DATA_BLOCK IopTriageDumpDataBlocks[IO_MAX_TRIAGE_DUMP_DATA_BLOCKS];

//
// If space is available in a triage dump it's possible
// to add "interesting" data pages referenced by runtime
// information such as context registers.  The following
// lists are offsets into the CONTEXT structure of pointers
// which usually point to interesting data.  They are
// in priority order.
//

#define IOP_LAST_CONTEXT_OFFSET 0xffff

USHORT IopRunTimeContextOffsetsX86[] =
{
    FIELD_OFFSET(X86_NT5_CONTEXT, Ebx),
    FIELD_OFFSET(X86_NT5_CONTEXT, Esi),
    FIELD_OFFSET(X86_NT5_CONTEXT, Edi),
    FIELD_OFFSET(X86_NT5_CONTEXT, Ecx),
    FIELD_OFFSET(X86_NT5_CONTEXT, Edx),
    FIELD_OFFSET(X86_NT5_CONTEXT, Eax),
    FIELD_OFFSET(X86_NT5_CONTEXT, Eip),
    IOP_LAST_CONTEXT_OFFSET
};

USHORT IopRunTimeContextOffsetsIa64[] =
{
    FIELD_OFFSET(IA64_CONTEXT, IntS0),
    FIELD_OFFSET(IA64_CONTEXT, IntS1),
    FIELD_OFFSET(IA64_CONTEXT, IntS2),
    FIELD_OFFSET(IA64_CONTEXT, IntS3),
    FIELD_OFFSET(IA64_CONTEXT, StIIP),
    IOP_LAST_CONTEXT_OFFSET
};

USHORT IopRunTimeContextOffsetsAmd64[] =
{
    FIELD_OFFSET(AMD64_CONTEXT, Rbx),
    FIELD_OFFSET(AMD64_CONTEXT, Rsi),
    FIELD_OFFSET(AMD64_CONTEXT, Rdi),
    FIELD_OFFSET(AMD64_CONTEXT, Rcx),
    FIELD_OFFSET(AMD64_CONTEXT, Rdx),
    FIELD_OFFSET(AMD64_CONTEXT, Rax),
    FIELD_OFFSET(AMD64_CONTEXT, Rip),
    IOP_LAST_CONTEXT_OFFSET
};

USHORT IopRunTimeContextOffsetsEmpty[] =
{
    IOP_LAST_CONTEXT_OFFSET
};

BOOLEAN
IopIsAddressRangeValid(
    IN ULONG64 VirtualAddress,
    IN ULONG Length
    )
{
    VirtualAddress = PAGE_ALIGN(g_Machine, VirtualAddress);
    Length = (Length + g_Machine->m_PageSize - 1) >> g_Machine->m_PageShift;
    while (Length > 0)
    {
        UCHAR Data;

        if (CurReadAllVirtual(VirtualAddress, &Data, sizeof(Data)) != S_OK)
        {
            return FALSE;
        }

        VirtualAddress += g_Machine->m_PageSize;
        Length--;
    }

    return TRUE;
}

BOOLEAN
IoAddTriageDumpDataBlock(
    IN ULONG64 Address,
    IN ULONG Length
    )
{
    ULONG i;
    PTRIAGE_PTR_DATA_BLOCK Block;
    ULONG64 MinAddress, MaxAddress;

    // Check against SIZE32 for both 32 and 64-bit dumps
    // as no data block needs to be larger than that.
    if (Length >= TRIAGE_DUMP_SIZE32 ||
        !IopIsAddressRangeValid(Address, Length))
    {
        return FALSE;
    }

    MinAddress = Address;
    MaxAddress = MinAddress + Length;

    //
    // Minimize overlap between the new block and existing blocks.
    // Blocks cannot simply be merged as blocks are inserted in
    // priority order for storage in the dump.  Combining a low-priority
    // block with a high-priority block could lead to a medium-
    // priority block being bumped improperly from the dump.
    //

    Block = IopTriageDumpDataBlocks;
    for (i = 0; i < IopNumTriageDumpDataBlocks; i++, Block++)
    {
        if (MinAddress >= Block->MaxAddress ||
            MaxAddress <= Block->MinAddress)
        {
            // No overlap.
            continue;
        }

        //
        // Trim overlap out of the new block.  If this
        // would split the new block into pieces don't
        // trim to keep things simple.  Content may then
        // be duplicated in the dump.
        //

        if (MinAddress >= Block->MinAddress)
        {
            if (MaxAddress <= Block->MaxAddress)
            {
                // New block is completely contained.
                return TRUE;
            }

            // New block extends above the current block
            // so trim off the low-range overlap.
            MinAddress = Block->MaxAddress;
        }
        else if (MaxAddress <= Block->MaxAddress)
        {
            // New block extends below the current block
            // so trim off the high-range overlap.
            MaxAddress = Block->MinAddress;
        }
    }

    if (IopNumTriageDumpDataBlocks >= IO_MAX_TRIAGE_DUMP_DATA_BLOCKS)
    {
        return FALSE;
    }

    Block = IopTriageDumpDataBlocks + IopNumTriageDumpDataBlocks++;
    Block->MinAddress = MinAddress;
    Block->MaxAddress = MaxAddress;

    return TRUE;
}

VOID
IopAddRunTimeTriageDataBlocks(
    IN PCROSS_PLATFORM_CONTEXT Context,
    IN ULONG64 StackMin,
    IN ULONG64 StackMax,
    IN ULONG64 StoreMin,
    IN ULONG64 StoreMax
    )
{
    PUSHORT ContextOffset;

    switch(g_Target->m_MachineType)
    {
    case IMAGE_FILE_MACHINE_I386:
        ContextOffset = IopRunTimeContextOffsetsX86;
        break;
    case IMAGE_FILE_MACHINE_IA64:
        ContextOffset = IopRunTimeContextOffsetsIa64;
        break;
    case IMAGE_FILE_MACHINE_AMD64:
        ContextOffset = IopRunTimeContextOffsetsAmd64;
        break;
    default:
        ContextOffset = IopRunTimeContextOffsetsEmpty;
        break;
    }

    while (*ContextOffset < IOP_LAST_CONTEXT_OFFSET)
    {
        ULONG64 Ptr;

        //
        // Retrieve possible pointers from the context
        // registers.
        //

        if (g_Machine->m_Ptr64)
        {
            Ptr = *(PULONG64)((PUCHAR)Context + *ContextOffset);
        }
        else
        {
            Ptr = EXTEND64(*(PULONG)((PUCHAR)Context + *ContextOffset));
        }

        // Stack and backing store memory is already saved
        // so ignore any pointers that fall into those ranges.
        if ((Ptr < StackMin || Ptr >= StackMax) &&
            (Ptr < StoreMin || Ptr >= StoreMax))
        {
            IoAddTriageDumpDataBlock(PAGE_ALIGN(g_Machine, Ptr),
                                     g_Machine->m_PageSize);
        }

        ContextOffset++;
    }
}

void
AddInMemoryTriageDataBlocks(void)
{
    //
    // Look at the global data for nt!IopTriageDumpDataBlocks
    // and include the same data blocks so that dump conversion
    // preserves data blocks.
    //

    // If we don't know where IopTriageDumpDataBlocks is then
    // we don't have anything to do.
    if (!g_Target->m_KdDebuggerData.IopNumTriageDumpDataBlocks ||
        !g_Target->m_KdDebuggerData.IopTriageDumpDataBlocks)
    {
        return;
    }

    ULONG NumBlocks;

    if (g_Target->
        ReadAllVirtual(g_Process, g_Target->
                       m_KdDebuggerData.IopNumTriageDumpDataBlocks,
                       &NumBlocks, sizeof(NumBlocks)) != S_OK)
    {
        return;
    }

    if (NumBlocks > IO_MAX_TRIAGE_DUMP_DATA_BLOCKS)
    {
        NumBlocks = IO_MAX_TRIAGE_DUMP_DATA_BLOCKS;
    }

    ULONG64 BlockDescOffs =
        g_Target->m_KdDebuggerData.IopTriageDumpDataBlocks;
    TRIAGE_PTR_DATA_BLOCK BlockDesc;
    ULONG i;
    ULONG PtrSize = g_Machine->m_Ptr64 ? 8 : 4;

    for (i = 0; i < NumBlocks; i++)
    {
        if (g_Target->ReadPointer(g_Process, g_Machine,
                                  BlockDescOffs,
                                  &BlockDesc.MinAddress) != S_OK ||
            g_Target->ReadPointer(g_Process, g_Machine,
                                  BlockDescOffs + PtrSize,
                                  &BlockDesc.MaxAddress) != S_OK)
        {
            return;
        }

        BlockDescOffs += 2 * PtrSize;

        IoAddTriageDumpDataBlock(BlockDesc.MinAddress,
                                 (LONG)(BlockDesc.MaxAddress -
                                        BlockDesc.MinAddress));
    }
}

ULONG
IopSizeTriageDumpDataBlocks(
    ULONG Offset,
    ULONG BufferSize,
    PULONG StartOffset,
    PULONG Count
    )
{
    ULONG i;
    ULONG Size;
    PTRIAGE_PTR_DATA_BLOCK Block;

    *Count = 0;

    Block = IopTriageDumpDataBlocks;
    for (i = 0; i < IopNumTriageDumpDataBlocks; i++, Block++)
    {
        Size = ALIGN_8(sizeof(TRIAGE_DATA_BLOCK)) +
            ALIGN_8((ULONG)(Block->MaxAddress - Block->MinAddress));
        if (Offset + Size >= BufferSize)
        {
            break;
        }

        if (i == 0)
        {
            *StartOffset = Offset;
        }

        Offset += Size;
        (*Count)++;
    }

    return Offset;
}

VOID
IopWriteTriageDumpDataBlocks(
    ULONG StartOffset,
    ULONG Count,
    PUCHAR BufferAddress
    )
{
    ULONG i;
    PTRIAGE_PTR_DATA_BLOCK Block;
    PUCHAR DataBuffer;
    PTRIAGE_DATA_BLOCK DumpBlock;

    DumpBlock = (PTRIAGE_DATA_BLOCK)
        (BufferAddress + StartOffset);
    DataBuffer = (PUCHAR)(DumpBlock + Count);

    Block = IopTriageDumpDataBlocks;
    for (i = 0; i < Count; i++, Block++)
    {
        DumpBlock->Address = Block->MinAddress;
        DumpBlock->Offset = (ULONG)(DataBuffer - BufferAddress);
        DumpBlock->Size = (ULONG)(Block->MaxAddress - Block->MinAddress);

        CurReadAllVirtual(Block->MinAddress, DataBuffer, DumpBlock->Size);

        DataBuffer += DumpBlock->Size;
        DumpBlock++;
    }
}

void
KernelTriageDumpTargetInfo::WriteDriverList32(PUCHAR DataBase,
                                              TRIAGE_DUMP32* TriageHdr)
{
    PDUMP_DRIVER_ENTRY32 pdde;
    PDUMP_STRING pds;
    ModuleInfo* ModIter;
    ULONG MaxEntries = TriageHdr->DriverCount;

    TriageHdr->DriverCount = 0;

    if (((ModIter = g_Target->GetModuleInfo(FALSE)) == NULL) ||
        ((ModIter->Initialize(g_Thread, MODULE_INFO_ALL_STD)) != S_OK))
    {
        return;
    }

    // pointer to first driver entry to write out
    pdde = (PDUMP_DRIVER_ENTRY32) (DataBase + TriageHdr->DriverListOffset);

    // pointer to first module name to write out
    pds = (PDUMP_STRING) (DataBase + TriageHdr->StringPoolOffset);

    while ((PUCHAR)(pds + 1) < DataBase + TRIAGE_DUMP_SIZE32 &&
           TriageHdr->DriverCount < MaxEntries)
    {
        MODULE_INFO_ENTRY ModEntry;

        ULONG EntryRet = GetNextModuleEntry(ModIter, &ModEntry);

        if (EntryRet == GNME_CORRUPT ||
            EntryRet == GNME_DONE)
        {
            break;
        }
        else if (EntryRet == GNME_NO_NAME)
        {
            continue;
        }

        pdde->LdrEntry.DllBase       = (ULONG)(ULONG_PTR)ModEntry.Base;
        pdde->LdrEntry.SizeOfImage   = ModEntry.SizeOfImage;
        pdde->LdrEntry.CheckSum      = ModEntry.CheckSum;
        pdde->LdrEntry.TimeDateStamp = ModEntry.TimeDateStamp;

        if (ModEntry.NameCharBytes == sizeof(WCHAR))
        {
            // Convert length from bytes to characters.
            pds->Length = ModEntry.NameBytes / sizeof(WCHAR);
            if ((PUCHAR)pds->Buffer + pds->Length + sizeof(WCHAR) >
                DataBase + TRIAGE_DUMP_SIZE32)
            {
                break;
            }

            CopyMemory(pds->Buffer,
                       ModEntry.NamePtr,
                       ModEntry.NameBytes);
        }
        else
        {
            pds->Length = ModEntry.NameBytes;
            if ((PUCHAR)pds->Buffer + pds->Length + sizeof(WCHAR) >
                DataBase + TRIAGE_DUMP_SIZE32)
            {
                break;
            }

            MultiByteToWideChar(CP_ACP, 0,
                                (PSTR)ModEntry.NamePtr, ModEntry.NameBytes,
                                pds->Buffer, ModEntry.NameBytes);
        }

        // null terminate string
        pds->Buffer[pds->Length] = 0;

        pdde->DriverNameOffset = (ULONG)
            ((ULONG_PTR) pds - (ULONG_PTR) DataBase);

        // get pointer to next string
        pds = (PDUMP_STRING)
            (DataBase + ALIGN_8(pdde->DriverNameOffset +
                                sizeof(DUMP_STRING) +
                                sizeof(WCHAR) * (pds->Length + 1)));

        pdde = (PDUMP_DRIVER_ENTRY32)(((PUCHAR) pdde) + sizeof(*pdde));

        TriageHdr->DriverCount++;
    }

    TriageHdr->StringPoolSize = (ULONG)
        ((ULONG_PTR)pds -
         (ULONG_PTR)(DataBase + TriageHdr->StringPoolOffset));
}

PUCHAR
KernelTriageDumpTargetInfo::WriteUnloadedDrivers32(PUCHAR Data)
{
    ULONG i;
    ULONG Index;
    UNLOADED_DRIVERS32 ud;
    PDUMP_UNLOADED_DRIVERS32 pdud;
    ULONG64 pvMiUnloadedDrivers = 0;
    ULONG ulMiLastUnloadedDriver = 0;
    PUCHAR CountInData = Data;
    ULONG Written = 0;

    *((PULONG) CountInData) = 0;
    Data += sizeof(ULONG);

    //
    // find location of unloaded drivers
    //

    if (!g_Target->m_KdDebuggerData.MmUnloadedDrivers ||
        !g_Target->m_KdDebuggerData.MmLastUnloadedDriver)
    {
        return Data;
    }

    if (g_Target->ReadPointer(g_Process, g_Target->m_Machine,
                              g_Target->m_KdDebuggerData.MmUnloadedDrivers,
                              &pvMiUnloadedDrivers) != S_OK ||
        CurReadAllVirtual(g_Target->m_KdDebuggerData.MmLastUnloadedDriver,
                          &ulMiLastUnloadedDriver,
                          sizeof(ULONG)) != S_OK)
    {
        return Data;
    }

    if (pvMiUnloadedDrivers == NULL || ulMiLastUnloadedDriver == 0)
    {
        return Data;
    }

    // point to last unloaded drivers
    pdud = (PDUMP_UNLOADED_DRIVERS32)Data;

    //
    // Write the list with the most recently unloaded driver first to the
    // least recently unloaded driver last.
    //

    Index = ulMiLastUnloadedDriver - 1;

    for (i = 0; i < MAX_KERNEL_UNLOADED_DRIVERS; i++, Index--)
    {
        if (Index >= MAX_KERNEL_UNLOADED_DRIVERS)
        {
            Index = MAX_KERNEL_UNLOADED_DRIVERS - 1;
        }

        // read in unloaded driver
        if (CurReadAllVirtual(pvMiUnloadedDrivers +
                              Index * sizeof(UNLOADED_DRIVERS32),
                              &ud, sizeof(ud)) != S_OK)
        {
            ErrOut(_T("Can't read memory from %s\n"),
                   FormatAddr64(pvMiUnloadedDrivers +
                                Index * sizeof(UNLOADED_DRIVERS32)));
            continue;
        }

        // copy name lengths
        pdud->Name.MaximumLength = ud.Name.MaximumLength;
        pdud->Name.Length = ud.Name.Length;
        if (ud.Name.Buffer == NULL)
        {
            continue;
        }

        // copy start and end address
        pdud->StartAddress = ud.StartAddress;
        pdud->EndAddress = ud.EndAddress;

        // restrict name length and maximum name length to 12 characters
        if (pdud->Name.Length > MAX_UNLOADED_NAME_LENGTH)
        {
            pdud->Name.Length = MAX_UNLOADED_NAME_LENGTH;
        }
        if (pdud->Name.MaximumLength > MAX_UNLOADED_NAME_LENGTH)
        {
            pdud->Name.MaximumLength = MAX_UNLOADED_NAME_LENGTH;
        }
        // Can't store pointers in the dump so just zero it.
        pdud->Name.Buffer = 0;
        // Read in name.
        if (CurReadAllVirtual(EXTEND64(ud.Name.Buffer),
                              pdud->DriverName,
                              pdud->Name.MaximumLength) != S_OK)
        {
            ErrOut(_T("Can't read memory at address %s\n"),
                   FormatAddr64(ud.Name.Buffer));
            continue;
        }

        // move to previous driver
        pdud += 1;
        Written++;
    }

    // number of drivers in the list
    *((PULONG) CountInData) = Written;
    return (PUCHAR)pdud;
}

PUCHAR
KernelTriageDumpTargetInfo::WriteMmTriageInformation32(PUCHAR Data)
{
    DUMP_MM_STORAGE32 TriageInformation;
    ULONG64 pMmVerifierData;
    ULONG64 pvMmPagedPoolInfo;
    ULONG cbNonPagedPool;
    ULONG cbPagedPool;

    // version information
    TriageInformation.Version = 1;

    // size information
    TriageInformation.Size = sizeof(TriageInformation);

    // get special pool tag
    ExtractValue(MmSpecialPoolTag, TriageInformation.MmSpecialPoolTag);

    // get triage action taken
    ExtractValue(MmTriageActionTaken, TriageInformation.MiTriageActionTaken);
    pMmVerifierData = g_Target->m_KdDebuggerData.MmVerifierData;

    // read in verifier level
    // BUGBUG - should not read internal data structures in MM
    //if (pMmVerifierData)
    //    DmpReadMemory(
    //        (ULONG64) &((MM_DRIVER_VERIFIER_DATA *) pMmVerifierData)->Level,
    //        &TriageInformation.MmVerifyDriverLevel,
    //        sizeof(TriageInformation.MmVerifyDriverLevel));
    //else
        TriageInformation.MmVerifyDriverLevel = 0;

    // read in verifier
    ExtractValue(KernelVerifier, TriageInformation.KernelVerifier);

    // read non paged pool info
    ExtractValue(MmMaximumNonPagedPoolInBytes, cbNonPagedPool);
    TriageInformation.MmMaximumNonPagedPool =
        cbNonPagedPool / g_Target->m_Machine->m_PageSize;
    ExtractValue(MmAllocatedNonPagedPool,
                 TriageInformation.MmAllocatedNonPagedPool);

    // read paged pool info
    ExtractValue(MmSizeOfPagedPoolInBytes, cbPagedPool);
    TriageInformation.PagedPoolMaximum =
        cbPagedPool / g_Target->m_Machine->m_PageSize;
    pvMmPagedPoolInfo = g_Target->m_KdDebuggerData.MmPagedPoolInformation;

    // BUGBUG - should not read internal data structures in MM
    //if (pvMmPagedPoolInfo)
    //    DmpReadMemory(
    //        (ULONG64) &((MM_PAGED_POOL_INFO *) pvMmPagedPoolInfo)->AllocatedPagedPool,
    //        &TriageInformation.PagedPoolAllocated,
    //        sizeof(TriageInformation.PagedPoolAllocated));
    //else
        TriageInformation.PagedPoolAllocated = 0;

    // read committed pages info
    ExtractValue(MmTotalCommittedPages, TriageInformation.CommittedPages);
    ExtractValue(MmPeakCommitment, TriageInformation.CommittedPagesPeak);
    ExtractValue(MmTotalCommitLimitMaximum,
                 TriageInformation.CommitLimitMaximum);
    memcpy(Data, &TriageInformation, sizeof(TriageInformation));
    return Data + sizeof(TriageInformation);
}

void
KernelTriageDumpTargetInfo::WriteDriverList64(PUCHAR DataBase,
                                              TRIAGE_DUMP64* TriageHdr)
{
    PDUMP_DRIVER_ENTRY64 pdde;
    PDUMP_STRING pds;
    ModuleInfo* ModIter;
    ULONG MaxEntries = TriageHdr->DriverCount;

    TriageHdr->DriverCount = 0;

    if (((ModIter = g_Target->GetModuleInfo(FALSE)) == NULL) ||
        ((ModIter->Initialize(g_Thread, MODULE_INFO_ALL_STD)) != S_OK))
    {
        return;
    }

    // pointer to first driver entry to write out
    pdde = (PDUMP_DRIVER_ENTRY64) (DataBase + TriageHdr->DriverListOffset);

    // pointer to first module name to write out
    pds = (PDUMP_STRING) (DataBase + TriageHdr->StringPoolOffset);

    while ((PUCHAR)(pds + 1) < DataBase + TRIAGE_DUMP_SIZE64 &&
           TriageHdr->DriverCount < MaxEntries)
    {
        MODULE_INFO_ENTRY ModEntry;

        ULONG EntryRet = GetNextModuleEntry(ModIter, &ModEntry);

        if (EntryRet == GNME_CORRUPT ||
            EntryRet == GNME_DONE)
        {
            break;
        }
        else if (EntryRet == GNME_NO_NAME)
        {
            continue;
        }

        pdde->LdrEntry.DllBase       = ModEntry.Base;
        pdde->LdrEntry.SizeOfImage   = ModEntry.SizeOfImage;
        pdde->LdrEntry.CheckSum      = ModEntry.CheckSum;
        pdde->LdrEntry.TimeDateStamp = ModEntry.TimeDateStamp;

        if (ModEntry.NameCharBytes == sizeof(WCHAR))
        {
            // Convert length from bytes to characters.
            pds->Length = ModEntry.NameBytes / sizeof(WCHAR);
            if ((PUCHAR)pds->Buffer + pds->Length + sizeof(WCHAR) >
                DataBase + TRIAGE_DUMP_SIZE64)
            {
                break;
            }

            CopyMemory(pds->Buffer,
                       ModEntry.NamePtr,
                       ModEntry.NameBytes);
        }
        else
        {
            pds->Length = ModEntry.NameBytes;
            if ((PUCHAR)pds->Buffer + pds->Length + sizeof(WCHAR) >
                DataBase + TRIAGE_DUMP_SIZE64)
            {
                break;
            }

            MultiByteToWideChar(CP_ACP, 0,
                                (PSTR)ModEntry.NamePtr, ModEntry.NameBytes,
                                pds->Buffer, ModEntry.NameBytes);
        }

        // null terminate string
        pds->Buffer[pds->Length] = 0;

        pdde->DriverNameOffset = (ULONG)
            ((ULONG_PTR) pds - (ULONG_PTR) DataBase);

        // get pointer to next string
        pds = (PDUMP_STRING)
            (DataBase + ALIGN_8(pdde->DriverNameOffset +
                                sizeof(DUMP_STRING) +
                                sizeof(WCHAR) * (pds->Length + 1)));

        pdde = (PDUMP_DRIVER_ENTRY64)(((PUCHAR) pdde) + sizeof(*pdde));

        TriageHdr->DriverCount++;
    }

    TriageHdr->StringPoolSize = (ULONG)
        ((ULONG_PTR)pds -
         (ULONG_PTR)(DataBase + TriageHdr->StringPoolOffset));
}

PUCHAR
KernelTriageDumpTargetInfo::WriteUnloadedDrivers64(PUCHAR Data)
{
    ULONG i;
    ULONG Index;
    UNLOADED_DRIVERS64 ud;
    PDUMP_UNLOADED_DRIVERS64 pdud;
    ULONG64 pvMiUnloadedDrivers = 0;
    ULONG ulMiLastUnloadedDriver = 0;
    PUCHAR CountInData = Data;
    ULONG Written = 0;

    *((PULONG64) CountInData) = 0;
    Data += sizeof(ULONG64);

    //
    // find location of unloaded drivers
    //

    if (!g_Target->m_KdDebuggerData.MmUnloadedDrivers ||
        !g_Target->m_KdDebuggerData.MmLastUnloadedDriver)
    {
        return Data;
    }

    if (g_Target->ReadPointer(g_Process, g_Target->m_Machine,
                              g_Target->m_KdDebuggerData.MmUnloadedDrivers,
                              &pvMiUnloadedDrivers) != S_OK ||
        CurReadAllVirtual(g_Target->m_KdDebuggerData.MmLastUnloadedDriver,
                          &ulMiLastUnloadedDriver,
                          sizeof(ULONG)) != S_OK)
    {
        return Data;
    }

    if (pvMiUnloadedDrivers == NULL || ulMiLastUnloadedDriver == 0)
    {
        return Data;
    }

    // point to last unloaded drivers
    pdud = (PDUMP_UNLOADED_DRIVERS64)Data;

    //
    // Write the list with the most recently unloaded driver first to the
    // least recently unloaded driver last.
    //

    Index = ulMiLastUnloadedDriver - 1;

    for (i = 0; i < MAX_KERNEL_UNLOADED_DRIVERS; i++, Index--)
    {
        if (Index >= MAX_KERNEL_UNLOADED_DRIVERS)
        {
            Index = MAX_KERNEL_UNLOADED_DRIVERS - 1;
        }

        // read in unloaded driver
        if (CurReadAllVirtual(pvMiUnloadedDrivers +
                              Index * sizeof(UNLOADED_DRIVERS64),
                              &ud, sizeof(ud)) != S_OK)
        {
            ErrOut(_T("Can't read memory from %s\n"),
                   FormatAddr64(pvMiUnloadedDrivers +
                                Index * sizeof(UNLOADED_DRIVERS64)));
            continue;
        }

        // copy name lengths
        pdud->Name.MaximumLength = ud.Name.MaximumLength;
        pdud->Name.Length = ud.Name.Length;
        if (ud.Name.Buffer == NULL)
        {
            continue;
        }

        // copy start and end address
        pdud->StartAddress = ud.StartAddress;
        pdud->EndAddress = ud.EndAddress;

        // restrict name length and maximum name length to 12 characters
        if (pdud->Name.Length > MAX_UNLOADED_NAME_LENGTH)
        {
            pdud->Name.Length = MAX_UNLOADED_NAME_LENGTH;
        }
        if (pdud->Name.MaximumLength > MAX_UNLOADED_NAME_LENGTH)
        {
            pdud->Name.MaximumLength = MAX_UNLOADED_NAME_LENGTH;
        }
        // Can't store pointers in the dump so just zero it.
        pdud->Name.Buffer = 0;
        // Read in name.
        if (CurReadAllVirtual(ud.Name.Buffer,
                              pdud->DriverName,
                              pdud->Name.MaximumLength) != S_OK)
        {
            ErrOut(_T("Can't read memory at address %s\n"),
                   FormatAddr64(ud.Name.Buffer));
            continue;
        }

        // move to previous driver
        pdud += 1;
        Written++;
    }

    // number of drivers in the list
    *((PULONG) CountInData) = Written;
    return (PUCHAR)pdud;
}

PUCHAR
KernelTriageDumpTargetInfo::WriteMmTriageInformation64(PUCHAR Data)
{
    DUMP_MM_STORAGE64 TriageInformation;
    ULONG64 pMmVerifierData;
    ULONG64 pvMmPagedPoolInfo;
    ULONG cbNonPagedPool;
    ULONG cbPagedPool;

    // version information
    TriageInformation.Version = 1;

    // size information
    TriageInformation.Size = sizeof(TriageInformation);

    // get special pool tag
    ExtractValue(MmSpecialPoolTag, TriageInformation.MmSpecialPoolTag);

    // get triage action taken
    ExtractValue(MmTriageActionTaken, TriageInformation.MiTriageActionTaken);
    pMmVerifierData = g_Target->m_KdDebuggerData.MmVerifierData;

    // read in verifier level
    // BUGBUG - should not read internal data structures in MM
    //if (pMmVerifierData)
    //    DmpReadMemory(
    //        (ULONG64) &((MM_DRIVER_VERIFIER_DATA *) pMmVerifierData)->Level,
    //        &TriageInformation.MmVerifyDriverLevel,
    //        sizeof(TriageInformation.MmVerifyDriverLevel));
    //else
        TriageInformation.MmVerifyDriverLevel = 0;

    // read in verifier
    ExtractValue(KernelVerifier, TriageInformation.KernelVerifier);

    // read non paged pool info
    ExtractValue(MmMaximumNonPagedPoolInBytes, cbNonPagedPool);
    TriageInformation.MmMaximumNonPagedPool =
        cbNonPagedPool / g_Target->m_Machine->m_PageSize;
    ExtractValue(MmAllocatedNonPagedPool,
                 TriageInformation.MmAllocatedNonPagedPool);

    // read paged pool info
    ExtractValue(MmSizeOfPagedPoolInBytes, cbPagedPool);
    TriageInformation.PagedPoolMaximum =
        cbPagedPool / g_Target->m_Machine->m_PageSize;
    pvMmPagedPoolInfo = g_Target->m_KdDebuggerData.MmPagedPoolInformation;

    // BUGBUG - should not read internal data structures in MM
    //if (pvMmPagedPoolInfo)
    //    DmpReadMemory(
    //        (ULONG64) &((MM_PAGED_POOL_INFO *) pvMmPagedPoolInfo)->AllocatedPagedPool,
    //        &TriageInformation.PagedPoolAllocated,
    //        sizeof(TriageInformation.PagedPoolAllocated));
    //else
        TriageInformation.PagedPoolAllocated = 0;

    // read committed pages info
    ExtractValue(MmTotalCommittedPages, TriageInformation.CommittedPages);
    ExtractValue(MmPeakCommitment, TriageInformation.CommittedPagesPeak);
    ExtractValue(MmTotalCommitLimitMaximum,
                 TriageInformation.CommitLimitMaximum);
    memcpy(Data, &TriageInformation, sizeof(TriageInformation));
    return Data + sizeof(TriageInformation);
}

//----------------------------------------------------------------------------
//
// KernelTriage32DumpTargetInfo::Write.
//
//----------------------------------------------------------------------------

HRESULT
KernelTriage32DumpTargetInfo::Write(HANDLE File, DUMP_WRITE_ARGS* Args)
{
    HRESULT Status;
    PMEMORY_DUMP32 NewHeader;
    PUCHAR DumpData;
    ULONG64 ThreadAddr, ProcAddr;
    BOOL ThreadAddrValid = FALSE;
    ULONG CodeMod = 0;
    ULONG BugCheckCode;
    ULONG64 BugCheckData[4];
    ULONG64 SaveDataPage = 0;
    ULONG64 PrcbAddr;
    // Set a special marker to indicate there is no pushed context.
    ContextSave* PushedContext = (ContextSave*)&PushedContext;
    ULONG i;
    ULONG Done;

    if (!IS_KERNEL_TARGET(g_Target))
    {
        ErrOut(_T("kernel minidumps can only be written ")
               _T("in kernel-mode sessions\n"));
        return E_UNEXPECTED;
    }

    dprintf(_T("mini kernel dump\n"));
    FlushCallbacks();

    NewHeader = (PMEMORY_DUMP32)malloc(TRIAGE_DUMP_SIZE32);
    if (NewHeader == NULL)
    {
        ErrOut(_T("Unable to allocate dump buffer\n"));
        return E_OUTOFMEMORY;
    }
    DumpData = (PUCHAR)NewHeader;

    //
    // For some bugchecks the interesting thread is a different
    // thread than the current thread, so make the following code
    // generic so it handles any thread.
    //

    if ((Status = g_Target->
         ReadBugCheckData(&BugCheckCode, BugCheckData)) != S_OK)
    {
        ErrOut(_T("Unable to read bugcheck data\n"));
        goto NewHeader;
    }

    if (BugCheckCode == THREAD_STUCK_IN_DEVICE_DRIVER)
    {
        CROSS_PLATFORM_CONTEXT Context;

        // Modify the bugcheck code to indicate this
        // minidump represents a special state.
        CodeMod = MINIDUMP_BUGCHECK;

        // The interesting thread is the first bugcheck parameter.
        ThreadAddr = BugCheckData[0];
        ThreadAddrValid = TRUE;

        // We need to make the thread's context the current
        // machine context for the duration of dump generation.
        if ((Status = g_Target->
             GetContextFromThreadStack(ThreadAddr, &Context, FALSE)) != S_OK)
        {
            ErrOut(_T("Unable to get context for thread %s\n"),
                   FormatAddr64(ThreadAddr));
            goto NewHeader;
        }

        PushedContext = g_Machine->PushContext(&Context);
    }
    else if (BugCheckCode == SYSTEM_THREAD_EXCEPTION_NOT_HANDLED)
    {
        //
        // System thread stores a context record as the 4th parameter.
        // use that.
        // Also save the context record in case someone needs to look
        // at it.
        //

        if (BugCheckData[3])
        {
            CROSS_PLATFORM_CONTEXT TargetContext, Context;

            if (CurReadAllVirtual(BugCheckData[3], &TargetContext,
                                  g_Target->m_TypeInfo.
                                  SizeTargetContext) == S_OK &&
                g_Machine->
                ConvertTargetContextToCanon(g_Target->
                                            m_TypeInfo.SizeTargetContext,
                                            &TargetContext,
                                            &Context) == S_OK)
            {
                CodeMod = MINIDUMP_BUGCHECK;
                PushedContext = g_Machine->PushContext(&Context);
                SaveDataPage = BugCheckData[3];
            }
        }
    }
    else if (BugCheckCode == KERNEL_MODE_EXCEPTION_NOT_HANDLED)
    {
        CROSS_PLATFORM_CONTEXT Context;

        //
        // 3rd parameter is a trap frame.
        //
        // Build a context record out of that only if it's a kernel mode
        // failure because esp may be wrong in that case ???.
        //
        if (BugCheckData[2] &&
            g_Machine->GetContextFromTrapFrame(BugCheckData[2], &Context,
                                               FALSE) == S_OK)
        {
            CodeMod = MINIDUMP_BUGCHECK;
            PushedContext = g_Machine->PushContext(&Context);
            SaveDataPage = BugCheckData[2];
        }
    }
    else if (BugCheckCode == UNEXPECTED_KERNEL_MODE_TRAP)
    {
        CROSS_PLATFORM_CONTEXT Context;

        //
        // Double fault
        //
        // The thread is correct in this case.
        // Second parameter is the TSS.  If we have a TSS, convert
        // the context and mark the bugcheck as converted.
        //

        if (BugCheckData[0] == 8 &&
            BugCheckData[1] &&
            g_Machine->GetContextFromTaskSegment(BugCheckData[1], &Context,
                                                 FALSE) == S_OK)
        {
            CodeMod = MINIDUMP_BUGCHECK;
            PushedContext = g_Machine->PushContext(&Context);
        }
    }

    if (!ThreadAddrValid &&
        (Status = g_Process->
         GetImplicitThreadData(g_Thread, &ThreadAddr)) != S_OK)
    {
        ErrOut(_T("Unable to get current thread\n"));
        goto NewHeader;
    }

    if (PushedContext == (ContextSave*)&PushedContext)
    {
        // If an explicit context wasn't pushed we need
        // to make sure that the dump thread's context
        // is active as it may not be the current thread.
        if ((Status = GetRegSourceContext(GetDefRegSource(), TRUE,
                                          NULL, &PushedContext)) != S_OK)
        {
            goto NewHeader;
        }

        // If there wasn't a push it gets NULLed, so
        // translate that to our special no-push marker.
        if (!PushedContext)
        {
            PushedContext = (ContextSave*)&PushedContext;
        }
    }
    
    //
    // Set up the main header.
    //

    KernelDumpTargetInfo::InitDumpHeader32(&NewHeader->Header,
                                           Args->CommentA,
                                           Args->CommentW,
                                           CodeMod);
    NewHeader->Header.DumpType = DUMP_TYPE_TRIAGE;
    NewHeader->Header.MiniDumpFields = TRIAGE_DUMP_BASIC_INFO;
    NewHeader->Header.WriterStatus = DUMP_DBGENG_SUCCESS;

    //
    // Triage dump header begins after dump header.
    //

    TRIAGE_DUMP32 *TriageHdr = &NewHeader->Triage;

    ZeroMemory(TriageHdr, sizeof(*TriageHdr));
    
    TriageHdr->ServicePackBuild = g_Target->m_ServicePackNumber;
    TriageHdr->SizeOfDump = TRIAGE_DUMP_SIZE32;

    TriageHdr->ContextOffset = FIELD_OFFSET(DUMP_HEADER32, ContextRecord);
    TriageHdr->ExceptionOffset = FIELD_OFFSET(DUMP_HEADER32, Exception);

    //
    // Starting offset in triage dump follows the triage dump header.
    //

    unsigned Offset =
        ALIGN_8(sizeof(DUMP_HEADER32) + sizeof(TRIAGE_DUMP32));

    //
    // Write MM information for Win2K and above only.
    //

    if (g_Target->m_SystemVersion >= NT_SVER_W2K)
    {
        TriageHdr->MmOffset = Offset;
        Offset =
            ALIGN_8((ULONG)(WriteMmTriageInformation32(DumpData + Offset) -
                            DumpData));
    }

    //
    // Write unloaded drivers.
    //

    TriageHdr->UnloadedDriversOffset = Offset;
    Offset = ALIGN_8((ULONG)(WriteUnloadedDrivers32(DumpData + Offset) -
                             DumpData));

    //
    // Write processor control block (KPRCB).
    //

    if ((Status = g_Target->
         GetProcessorSystemDataOffset(CURRENT_PROC,
                                      DEBUG_DATA_KPRCB_OFFSET,
                                      &PrcbAddr)) != S_OK ||
        (Status = CurReadAllVirtual(PrcbAddr,
                                    DumpData + Offset,
                                    g_Target->
                                    m_KdDebuggerData.SizePrcb)) != S_OK)
    {
        ErrOut(_T("Unable to get current PRCB\n"));
        goto NewHeader;
    }

    TriageHdr->PrcbOffset = Offset;
    Offset += ALIGN_8(g_Target->m_KdDebuggerData.SizePrcb);

    //
    // Write the thread and process data structures.
    //

    if (g_Target->ReadPointer(g_Process, g_Machine,
                              ThreadAddr + g_Target->
                              m_KdDebuggerData.OffsetKThreadApcProcess,
                              &ProcAddr) == S_OK &&
        CurReadAllVirtual(ProcAddr,
                          DumpData + Offset,
                          g_Target->m_KdDebuggerData.SizeEProcess) == S_OK)
    {
        TriageHdr->ProcessOffset = Offset;
        Offset += ALIGN_8(g_Target->m_KdDebuggerData.SizeEProcess);
    }
    else
    {
        ProcAddr = 0;
    }
    
    if (CurReadAllVirtual(ThreadAddr,
                          DumpData + Offset,
                          g_Target->m_KdDebuggerData.SizeEThread) == S_OK)
    {
        TriageHdr->ThreadOffset = Offset;
        Offset += ALIGN_8(g_Target->m_KdDebuggerData.SizeEThread);
    }

    //
    // Write the call stack.
    //

    ADDR StackPtr;
    ULONG64 StackBase;
    BOOL MustReadAll = TRUE;

    g_Machine->GetSP(&StackPtr);
    TriageHdr->TopOfStack = (ULONG)(ULONG_PTR)Flat(StackPtr);

    if (g_Target->
        ReadPointer(g_Process, g_Machine,
                    g_Target->m_KdDebuggerData.OffsetKThreadInitialStack +
                    ThreadAddr,
                    &StackBase) != S_OK)
    {
        StackBase = Flat(StackPtr) + MAX_TRIAGE_STACK_SIZE32;
        MustReadAll = FALSE;
    }

    //
    // There may have been a stack switch (DPCs, double-faults, etc.)
    // so the current stack pointer may not be within the
    // stack bounds.  If that's the case just try and read
    // as much of the stack as possible.
    //

    if (StackBase <= Flat(StackPtr) ||
        Flat(StackPtr) < StackBase - 16 * g_Machine->m_PageSize)
    {
        TriageHdr->SizeOfCallStack = MAX_TRIAGE_STACK_SIZE32;
        MustReadAll = FALSE;
    }
    else
    {
        TriageHdr->SizeOfCallStack =
            min((ULONG)(ULONG_PTR)(StackBase - Flat(StackPtr)),
                MAX_TRIAGE_STACK_SIZE32);
    }

    if (TriageHdr->SizeOfCallStack)
    {
        if ((Status =
             CurReadVirtual(EXTEND64(TriageHdr->TopOfStack),
                            DumpData + Offset,
                            TriageHdr->SizeOfCallStack,
                            &Done)) != S_OK)
        {
            ErrOut(_T("Unable to read thread stack at %s\n"),
                   FormatAddr64(TriageHdr->TopOfStack));
            goto NewHeader;
        }
        if (MustReadAll && Done < TriageHdr->SizeOfCallStack)
        {
            ErrOut(_T("Unable to read full thread stack at %s\n"),
                   FormatAddr64(TriageHdr->TopOfStack));
            Status = HRESULT_FROM_WIN32(ERROR_READ_FAULT);
            goto NewHeader;
        }

        TriageHdr->CallStackOffset = Offset;
        TriageHdr->SizeOfCallStack = Done;
        Offset += ALIGN_8(TriageHdr->SizeOfCallStack);
    }

    //
    // Write debugger data.
    //

    if (g_Target->m_SystemVersion >= NT_SVER_XP &&
        g_Target->m_KdDebuggerDataOffset &&
        (!IS_KERNEL_TRIAGE_DUMP(g_Target) ||
         ((KernelTriageDumpTargetInfo*)g_Target)->m_HasDebuggerData) &&
        Offset +
        ALIGN_8(sizeof(g_Target->m_KdDebuggerData)) < TRIAGE_DUMP_SIZE32)
    {
        NewHeader->Header.MiniDumpFields |= TRIAGE_DUMP_DEBUGGER_DATA;
        TriageHdr->DebuggerDataSize = sizeof(g_Target->m_KdDebuggerData);
        memcpy(DumpData + Offset,
               &g_Target->m_KdDebuggerData,
               sizeof(g_Target->m_KdDebuggerData));
        TriageHdr->DebuggerDataOffset = Offset;
        Offset += ALIGN_8(sizeof(g_Target->m_KdDebuggerData));
    }

    //
    // Write loaded driver list.
    //

    ModuleInfo* ModIter;
    ULONG MaxEntries;

    // Use a heuristic to guess how many entries we
    // can pack into the remaining space.
    MaxEntries = (TRIAGE_DUMP_SIZE32 - Offset) /
        (sizeof(DUMP_DRIVER_ENTRY32) + TRIAGE_DRIVER_NAME_SIZE_GUESS);

    TriageHdr->DriverCount = 0;
    if (ModIter = g_Target->GetModuleInfo(FALSE))
    {
        if ((Status = ModIter->
             Initialize(g_Thread, MODULE_INFO_ALL_STD)) == S_OK)
        {
            while (TriageHdr->DriverCount < MaxEntries)
            {
                MODULE_INFO_ENTRY ModEntry;

                ULONG EntryRet = GetNextModuleEntry(ModIter, &ModEntry);

                if (EntryRet == GNME_CORRUPT ||
                    EntryRet == GNME_DONE)
                {
                    if (EntryRet == GNME_CORRUPT)
                    {
                        NewHeader->Header.WriterStatus =
                            DUMP_DBGENG_CORRUPT_MODULE_LIST;
                    }
                    break;
                }
                else if (EntryRet == GNME_NO_NAME)
                {
                    continue;
                }

                TriageHdr->DriverCount++;
            }
        }
        else
        {
            NewHeader->Header.WriterStatus =
                DUMP_DBGENG_NO_MODULE_LIST;
        }
    }

    TriageHdr->DriverListOffset = Offset;
    Offset += ALIGN_8(TriageHdr->DriverCount * sizeof(DUMP_DRIVER_ENTRY32));
    TriageHdr->StringPoolOffset = Offset;
    TriageHdr->BrokenDriverOffset = 0;

    WriteDriverList32(DumpData, TriageHdr);

    Offset = TriageHdr->StringPoolOffset + TriageHdr->StringPoolSize;
    Offset = ALIGN_8(Offset);

    //
    // For XP and above add in any additional data pages and write out
    // whatever fits.
    //

    if (g_Target->m_SystemVersion >= NT_SVER_XP)
    {
        if (SaveDataPage)
        {
            IoAddTriageDumpDataBlock(PAGE_ALIGN(g_Machine, SaveDataPage),
                                     g_Machine->m_PageSize);
        }

        // If there are other interesting data pages, such as
        // alternate stacks for DPCs and such, pick them up.
        if (PrcbAddr)
        {
            ADDR_RANGE AltData[MAX_ALT_ADDR_RANGES];

            ZeroMemory(AltData, sizeof(AltData));
            if (g_Machine->GetAlternateTriageDumpDataRanges(PrcbAddr,
                                                            ThreadAddr,
                                                            AltData) == S_OK)
            {
                for (i = 0; i < MAX_ALT_ADDR_RANGES; i++)
                {
                    if (AltData[i].Base)
                    {
                        IoAddTriageDumpDataBlock(AltData[i].Base,
                                                 AltData[i].Size);
                    }
                }
            }
        }

        // Add any data blocks that were registered
        // in the debuggee.
        AddInMemoryTriageDataBlocks();

        // Add data blocks which might be referred to by
        // the context or other runtime state.
        IopAddRunTimeTriageDataBlocks(&g_Machine->m_Context,
                                      EXTEND64(TriageHdr->TopOfStack),
                                      EXTEND64(TriageHdr->TopOfStack +
                                               TriageHdr->SizeOfCallStack),
                                      0, 0);

        // Check which data blocks fit and write them.
        Offset = IopSizeTriageDumpDataBlocks(Offset, TRIAGE_DUMP_SIZE32,
                                             &TriageHdr->DataBlocksOffset,
                                             &TriageHdr->DataBlocksCount);
        Offset = ALIGN_8(Offset);
        if (TriageHdr->DataBlocksCount)
        {
            NewHeader->Header.MiniDumpFields |= TRIAGE_DUMP_DATA_BLOCKS;
            IopWriteTriageDumpDataBlocks(TriageHdr->DataBlocksOffset,
                                         TriageHdr->DataBlocksCount,
                                         DumpData);
        }
    }

    //
    // All options are enabled.
    //

    TriageHdr->TriageOptions = 0xffffffff;

    //
    // End of triage dump validated.
    //

    TriageHdr->ValidOffset = TRIAGE_DUMP_SIZE32 - sizeof(ULONG);
    *(PULONG)(DumpData + TriageHdr->ValidOffset) = TRIAGE_DUMP_VALID;

    //
    // Write it out to the file.
    //

    if ((Status = DumpWriteFile(File, DumpData,
                                TRIAGE_DUMP_SIZE32)) == S_OK)
    {
        Status = DumpWriteTaggedData(File);
    }

 NewHeader:
    if (PushedContext != (ContextSave*)&PushedContext)
    {
        g_Machine->PopContext(PushedContext);
    }
    free(NewHeader);
    return Status;
}

//----------------------------------------------------------------------------
//
// KernelTriage64DumpTargetInfo::Write.
//
//----------------------------------------------------------------------------

HRESULT
KernelTriage64DumpTargetInfo::Write(HANDLE File, DUMP_WRITE_ARGS* Args)
{
    HRESULT Status;
    PMEMORY_DUMP64 NewHeader;
    PUCHAR DumpData;
    ULONG64 ThreadAddr, ProcAddr;
    BOOL ThreadAddrValid = FALSE;
    ULONG CodeMod = 0;
    ULONG BugCheckCode;
    ULONG64 BugCheckData[4];
    ULONG64 SaveDataPage = 0;
    ULONG64 BStoreBase = 0;
    ULONG BStoreSize = 0;
    ULONG64 BStoreLimit;
    ULONG64 PrcbAddr;
    // Set a special marker to indicate there is no pushed context.
    ContextSave* PushedContext = (ContextSave*)&PushedContext;
    ULONG i;
    ULONG Done;

    if (!IS_KERNEL_TARGET(g_Target))
    {
        ErrOut(_T("kernel minidumps can only be written ")
               _T("in kernel-mode sessions\n"));
        return E_UNEXPECTED;
    }

    dprintf(_T("mini kernel dump\n"));
    FlushCallbacks();

    NewHeader = (PMEMORY_DUMP64)malloc(TRIAGE_DUMP_SIZE64);
    if (NewHeader == NULL)
    {
        ErrOut(_T("Unable to allocate dump buffer\n"));
        return E_OUTOFMEMORY;
    }
    DumpData = (PUCHAR)NewHeader;

    //
    // For some bugchecks the interesting thread is a different
    // thread than the current thread, so make the following code
    // generic so it handles any thread.
    //

    if ((Status = g_Target->
         ReadBugCheckData(&BugCheckCode, BugCheckData)) != S_OK)
    {
        ErrOut(_T("Unable to read bugcheck data\n"));
        goto NewHeader;
    }

    if (BugCheckCode == THREAD_STUCK_IN_DEVICE_DRIVER)
    {
        CROSS_PLATFORM_CONTEXT Context;

        // Modify the bugcheck code to indicate this
        // minidump represents a special state.
        CodeMod = MINIDUMP_BUGCHECK;

        // The interesting thread is the first bugcheck parameter.
        ThreadAddr = BugCheckData[0];
        ThreadAddrValid = TRUE;

        // We need to make the thread's context the current
        // machine context for the duration of dump generation.
        if ((Status = g_Target->
             GetContextFromThreadStack(ThreadAddr, &Context, FALSE)) != S_OK)
        {
            ErrOut(_T("Unable to get context for thread %s\n"),
                   FormatAddr64(ThreadAddr));
            goto NewHeader;
        }

        PushedContext = g_Machine->PushContext(&Context);
    }
    else if (BugCheckCode == SYSTEM_THREAD_EXCEPTION_NOT_HANDLED)
    {
        //
        // System thread stores a context record as the 4th parameter.
        // use that.
        // Also save the context record in case someone needs to look
        // at it.
        //

        if (BugCheckData[3])
        {
            CROSS_PLATFORM_CONTEXT TargetContext, Context;

            if (CurReadAllVirtual(BugCheckData[3], &TargetContext,
                                  g_Target->
                                  m_TypeInfo.SizeTargetContext) == S_OK &&
                g_Machine->
                ConvertTargetContextToCanon(g_Target->
                                            m_TypeInfo.SizeTargetContext,
                                            &TargetContext,
                                            &Context) == S_OK)
            {
                CodeMod = MINIDUMP_BUGCHECK;
                PushedContext = g_Machine->PushContext(&Context);
                SaveDataPage = BugCheckData[3];
            }
        }
    }
    else if (BugCheckCode == KERNEL_MODE_EXCEPTION_NOT_HANDLED)
    {
        CROSS_PLATFORM_CONTEXT Context;

        //
        // 3rd parameter is a trap frame.
        //
        // Build a context record out of that only if it's a kernel mode
        // failure because esp may be wrong in that case ???.
        //
        if (BugCheckData[2] &&
            g_Machine->GetContextFromTrapFrame(BugCheckData[2], &Context,
                                               FALSE) == S_OK)
        {
            CodeMod = MINIDUMP_BUGCHECK;
            PushedContext = g_Machine->PushContext(&Context);
            SaveDataPage = BugCheckData[2];
        }
    }

    if (!ThreadAddrValid &&
        (Status = g_Process->
         GetImplicitThreadData(g_Thread, &ThreadAddr)) != S_OK)
    {
        ErrOut(_T("Unable to get current thread\n"));
        goto NewHeader;
    }

    if (PushedContext == (ContextSave*)&PushedContext)
    {
        // If an explicit context wasn't pushed we need
        // to make sure that the dump thread's context
        // is active as it may not be the current thread.
        if ((Status = GetRegSourceContext(GetDefRegSource(), TRUE,
                                          NULL, &PushedContext)) != S_OK)
        {
            goto NewHeader;
        }

        // If there wasn't a push it gets NULLed, so
        // translate that to our special no-push marker.
        if (!PushedContext)
        {
            PushedContext = (ContextSave*)&PushedContext;
        }
    }
    
    //
    // Set up the main header.
    //

    KernelDumpTargetInfo::InitDumpHeader64(&NewHeader->Header,
                                           Args->CommentA,
                                           Args->CommentW,
                                           CodeMod);
    NewHeader->Header.DumpType = DUMP_TYPE_TRIAGE;
    NewHeader->Header.MiniDumpFields = TRIAGE_DUMP_BASIC_INFO;
    NewHeader->Header.WriterStatus = DUMP_DBGENG_SUCCESS;

    //
    // Triage dump header begins after dump header.
    //

    TRIAGE_DUMP64 *TriageHdr = &NewHeader->Triage;

    ZeroMemory(TriageHdr, sizeof(*TriageHdr));
    
    TriageHdr->ServicePackBuild = g_Target->m_ServicePackNumber;
    TriageHdr->SizeOfDump = TRIAGE_DUMP_SIZE64;

    TriageHdr->ContextOffset = FIELD_OFFSET(DUMP_HEADER64, ContextRecord);
    TriageHdr->ExceptionOffset = FIELD_OFFSET(DUMP_HEADER64, Exception);

    //
    // Starting offset in triage dump follows the triage dump header.
    //

    unsigned Offset =
        ALIGN_8(sizeof(DUMP_HEADER64) + sizeof(TRIAGE_DUMP64));

    //
    // Write MM information.
    //

    TriageHdr->MmOffset = Offset;
    Offset =
        ALIGN_8((ULONG)(WriteMmTriageInformation64(DumpData + Offset) -
                        DumpData));

    //
    // Write unloaded drivers.
    //

    TriageHdr->UnloadedDriversOffset = Offset;
    Offset = ALIGN_8((ULONG)(WriteUnloadedDrivers64(DumpData + Offset) -
                             DumpData));

    //
    // Write processor control block (KPRCB).
    //

    if ((Status = g_Target->
         GetProcessorSystemDataOffset(CURRENT_PROC,
                                      DEBUG_DATA_KPRCB_OFFSET,
                                      &PrcbAddr)) != S_OK ||
        (Status = CurReadAllVirtual(PrcbAddr,
                                    DumpData + Offset,
                                    g_Target->
                                    m_KdDebuggerData.SizePrcb)) != S_OK)
    {
        ErrOut(_T("Unable to get current PRCB\n"));
        goto NewHeader;
    }
    
    TriageHdr->PrcbOffset = Offset;
    Offset += ALIGN_8(g_Target->m_KdDebuggerData.SizePrcb);

    //
    // Write the thread and process data structures.
    //

    if (g_Target->ReadPointer(g_Process, g_Machine,
                              ThreadAddr + g_Target->
                              m_KdDebuggerData.OffsetKThreadApcProcess,
                              &ProcAddr) == S_OK &&
        CurReadAllVirtual(ProcAddr,
                          DumpData + Offset,
                          g_Target->m_KdDebuggerData.SizeEProcess) == S_OK)
    {
        TriageHdr->ProcessOffset = Offset;
        Offset += ALIGN_8(g_Target->m_KdDebuggerData.SizeEProcess);
    }
    else
    {
        ProcAddr = 0;
    }
    
    if (CurReadAllVirtual(ThreadAddr,
                          DumpData + Offset,
                          g_Target->m_KdDebuggerData.SizeEThread) == S_OK)
    {
        TriageHdr->ThreadOffset = Offset;
        Offset += ALIGN_8(g_Target->m_KdDebuggerData.SizeEThread);
    }

    //
    // Write the call stack.
    //

    ADDR StackPtr;
    ULONG64 StackBase;
    BOOL MustReadAll = TRUE;

    g_Machine->GetSP(&StackPtr);
    TriageHdr->TopOfStack = Flat(StackPtr);

    if (g_Target->
        ReadPointer(g_Process, g_Machine,
                    g_Target->m_KdDebuggerData.OffsetKThreadInitialStack +
                    ThreadAddr,
                    &StackBase) != S_OK)
    {
        StackBase = Flat(StackPtr) + MAX_TRIAGE_STACK_SIZE64;
        MustReadAll = FALSE;
    }

    //
    // There may have been a stack switch (DPCs, double-faults, etc.)
    // so the current stack pointer may not be within the
    // stack bounds.  If that's the case just try and read
    // as much of the stack as possible.
    //

    if (StackBase <= Flat(StackPtr) ||
        Flat(StackPtr) < StackBase - 16 * g_Machine->m_PageSize)
    {
        TriageHdr->SizeOfCallStack = MAX_TRIAGE_STACK_SIZE64;
        MustReadAll = FALSE;
    }
    else
    {
        TriageHdr->SizeOfCallStack =
            min((ULONG)(ULONG_PTR)(StackBase - Flat(StackPtr)),
                MAX_TRIAGE_STACK_SIZE64);
    }

    if (TriageHdr->SizeOfCallStack)
    {
        if ((Status =
             CurReadVirtual(TriageHdr->TopOfStack,
                            DumpData + Offset,
                            TriageHdr->SizeOfCallStack,
                            &Done)) != S_OK)
        {
            ErrOut(_T("Unable to read thread stack at %s\n"),
                   FormatAddr64(TriageHdr->TopOfStack));
            goto NewHeader;
        }
        if (MustReadAll && Done < TriageHdr->SizeOfCallStack)
        {
            ErrOut(_T("Unable to read full thread stack at %s\n"),
                   FormatAddr64(TriageHdr->TopOfStack));
            Status = HRESULT_FROM_WIN32(ERROR_READ_FAULT);
            goto NewHeader;
        }
    
        TriageHdr->CallStackOffset = Offset;
        TriageHdr->SizeOfCallStack = Done;
        Offset += ALIGN_8(TriageHdr->SizeOfCallStack);
    }

    //
    // The IA64 contains two call stacks. The first is the normal
    // call stack, and the second is a scratch region where
    // the processor can spill registers. It is this latter stack,
    // the backing-store, that we now save.
    //

    if (g_Target->m_MachineType == IMAGE_FILE_MACHINE_IA64 &&
        g_Target->ReadPointer(g_Process, g_Machine,
                              ThreadAddr +
                              g_Target->m_KdDebuggerData.OffsetKThreadBStore,
                              &BStoreBase) == S_OK &&
        g_Target->ReadPointer(g_Process, g_Machine,
                              ThreadAddr +
                              g_Target->
                              m_KdDebuggerData.OffsetKThreadBStoreLimit,
                              &BStoreLimit) == S_OK)
    {
        BStoreSize = min((ULONG)(BStoreLimit - BStoreBase),
                         MAX_TRIAGE_BSTORE_SIZE);

        if (BStoreSize &&
            CurReadAllVirtual(BStoreBase,
                              DumpData + Offset,
                              BStoreSize) == S_OK)
        {
            TriageHdr->ArchitectureSpecific.Ia64.BStoreOffset = Offset;
            TriageHdr->ArchitectureSpecific.Ia64.LimitOfBStore = BStoreLimit;
            TriageHdr->ArchitectureSpecific.Ia64.SizeOfBStore = BStoreSize;
            Offset += ALIGN_8(BStoreSize);
        }
    }

    //
    // Write debugger data.
    //

    if (g_Target->m_SystemVersion >= NT_SVER_XP &&
        g_Target->m_KdDebuggerDataOffset &&
        (!IS_KERNEL_TRIAGE_DUMP(g_Target) ||
         ((KernelTriageDumpTargetInfo*)g_Target)->m_HasDebuggerData) &&
        Offset +
        ALIGN_8(sizeof(g_Target->m_KdDebuggerData)) < TRIAGE_DUMP_SIZE64)
    {
        NewHeader->Header.MiniDumpFields |= TRIAGE_DUMP_DEBUGGER_DATA;
        TriageHdr->DebuggerDataSize = sizeof(g_Target->m_KdDebuggerData);
        memcpy(DumpData + Offset,
               &g_Target->m_KdDebuggerData,
               sizeof(g_Target->m_KdDebuggerData));
        TriageHdr->DebuggerDataOffset = Offset;
        Offset += ALIGN_8(sizeof(g_Target->m_KdDebuggerData));
    }

    //
    // Write loaded driver list.
    //

    ModuleInfo* ModIter;
    ULONG MaxEntries;

    // Use a heuristic to guess how many entries we
    // can pack into the remaining space.
    MaxEntries = (TRIAGE_DUMP_SIZE64 - Offset) /
        (sizeof(DUMP_DRIVER_ENTRY64) + TRIAGE_DRIVER_NAME_SIZE_GUESS);

    TriageHdr->DriverCount = 0;
    if (ModIter = g_Target->GetModuleInfo(FALSE))
    {
        if ((Status = ModIter->
             Initialize(g_Thread, MODULE_INFO_ALL_STD)) == S_OK)
        {
            while (TriageHdr->DriverCount < MaxEntries)
            {
                MODULE_INFO_ENTRY ModEntry;

                ULONG EntryRet = GetNextModuleEntry(ModIter, &ModEntry);

                if (EntryRet == GNME_CORRUPT ||
                    EntryRet == GNME_DONE)
                {
                    if (EntryRet == GNME_CORRUPT)
                    {
                        NewHeader->Header.WriterStatus =
                            DUMP_DBGENG_CORRUPT_MODULE_LIST;
                    }
                    break;
                }
                else if (EntryRet == GNME_NO_NAME)
                {
                    continue;
                }

                TriageHdr->DriverCount++;
            }
        }
        else
        {
            NewHeader->Header.WriterStatus =
                DUMP_DBGENG_NO_MODULE_LIST;
        }
    }

    TriageHdr->DriverListOffset = Offset;
    Offset += ALIGN_8(TriageHdr->DriverCount * sizeof(DUMP_DRIVER_ENTRY64));
    TriageHdr->StringPoolOffset = Offset;
    TriageHdr->BrokenDriverOffset = 0;

    WriteDriverList64(DumpData, TriageHdr);

    Offset = TriageHdr->StringPoolOffset + TriageHdr->StringPoolSize;
    Offset = ALIGN_8(Offset);

    //
    // For XP and above add in any additional data pages and write out
    // whatever fits.
    //

    if (g_Target->m_SystemVersion >= NT_SVER_XP)
    {
        if (SaveDataPage)
        {
            IoAddTriageDumpDataBlock(PAGE_ALIGN(g_Machine, SaveDataPage),
                                     g_Machine->m_PageSize);
        }

        // If there are other interesting data pages, such as
        // alternate stacks for DPCs and such, pick them up.
        if (PrcbAddr)
        {
            ADDR_RANGE AltData[MAX_ALT_ADDR_RANGES];

            ZeroMemory(AltData, sizeof(AltData));
            if (g_Machine->GetAlternateTriageDumpDataRanges(PrcbAddr,
                                                            ThreadAddr,
                                                            AltData) == S_OK)
            {
                for (i = 0; i < MAX_ALT_ADDR_RANGES; i++)
                {
                    if (AltData[i].Base)
                    {
                        IoAddTriageDumpDataBlock(AltData[i].Base,
                                                 AltData[i].Size);
                    }
                }
            }
        }

        // Add any data blocks that were registered
        // in the debuggee.
        AddInMemoryTriageDataBlocks();

        // Add data blocks which might be referred to by
        // the context or other runtime state.
        IopAddRunTimeTriageDataBlocks(&g_Machine->m_Context,
                                      TriageHdr->TopOfStack,
                                      TriageHdr->TopOfStack +
                                      TriageHdr->SizeOfCallStack,
                                      BStoreBase,
                                      BStoreSize);

        // Check which data blocks fit and write them.
        Offset = IopSizeTriageDumpDataBlocks(Offset, TRIAGE_DUMP_SIZE64,
                                             &TriageHdr->DataBlocksOffset,
                                             &TriageHdr->DataBlocksCount);
        Offset = ALIGN_8(Offset);
        if (TriageHdr->DataBlocksCount)
        {
            NewHeader->Header.MiniDumpFields |= TRIAGE_DUMP_DATA_BLOCKS;
            IopWriteTriageDumpDataBlocks(TriageHdr->DataBlocksOffset,
                                         TriageHdr->DataBlocksCount,
                                         (PUCHAR)NewHeader);
        }
    }

    //
    // All options are enabled.
    //

    TriageHdr->TriageOptions = 0xffffffff;

    //
    // End of triage dump validated.
    //

    TriageHdr->ValidOffset = TRIAGE_DUMP_SIZE64 - sizeof(ULONG);
    *(PULONG)(DumpData + TriageHdr->ValidOffset) = TRIAGE_DUMP_VALID;

    //
    // Write it out to the file.
    //

    if ((Status = DumpWriteFile(File, DumpData,
                                TRIAGE_DUMP_SIZE64)) == S_OK)
    {
        Status = DumpWriteTaggedData(File);
    }

 NewHeader:
    if (PushedContext != (ContextSave*)&PushedContext)
    {
        g_Machine->PopContext(PushedContext);
    }
    free(NewHeader);
    return Status;
}

//----------------------------------------------------------------------------
//
// Functions.
//
//----------------------------------------------------------------------------

HRESULT
WriteDumpFile(PCWSTR FileName, ULONG64 FileHandle,
              DUMP_WRITE_ARGS* Args)
{
    ULONG DumpType = DTYPE_COUNT;
    DumpTargetInfo* WriteTarget;
    HRESULT Status;
    ULONG OldMachine;
    WCHAR TempFile[2 * MAX_PATH];
    PCWSTR DumpWriteFile;
    HANDLE DumpWriteHandle;

    if (!IS_CUR_MACHINE_ACCESSIBLE())
    {
        return E_UNEXPECTED;
    }

    if (IS_KERNEL_TARGET(g_Target))
    {
        DbgKdTransport* KdTrans;

        if (Args->FormatFlags & ~GENERIC_FORMATS)
        {
            return E_INVALIDARG;
        }

        //
        // not much we can do without the processor block
        // or at least the PRCB for the current process in a minidump.
        //

        if (!g_Target->m_KdDebuggerData.KiProcessorBlock &&
            IS_DUMP_TARGET(g_Target) &&
            !((KernelDumpTargetInfo*)g_Target)->m_KiProcessors[CURRENT_PROC])
        {
            ErrOut(_T("Cannot find KiProcessorBlock - ")
                   _T("can not create dump file\n"));

            return E_FAIL;
        }

        if (IS_CONN_KERNEL_TARGET(g_Target))
        {
            KdTrans = ((ConnLiveKernelTargetInfo*)g_Target)->m_Transport;
        }
        else
        {
            KdTrans = NULL;
        }

        switch(Args->Qualifier)
        {
        case DEBUG_KERNEL_SMALL_DUMP:
            DumpType = g_Target->m_Machine->m_Ptr64 ?
                DTYPE_KERNEL_TRIAGE64 : DTYPE_KERNEL_TRIAGE32;
            break;
        case DEBUG_KERNEL_FULL_DUMP:
            if (KdTrans != NULL &&
                KdTrans->m_DirectPhysicalMemory == FALSE)
            {
                WarnOut(_T("Creating a full kernel dump over the COM port is ")
                        _T("a VERY VERY slow operation.\n")
                        _T("This command may take many HOURS to complete.  ")
                        _T("Ctrl-C if you want to terminate the command.\n"));
            }
            DumpType = g_Target->m_Machine->m_Ptr64 ?
                DTYPE_KERNEL_FULL64 : DTYPE_KERNEL_FULL32;
            break;
        default:
            // Other formats are not supported.
            return E_INVALIDARG;
        }
    }
    else
    {
        DBG_ASSERT(IS_USER_TARGET(g_Target));

        switch(Args->Qualifier)
        {
        case DEBUG_USER_WINDOWS_SMALL_DUMP:
            if (Args->FormatFlags & ~(GENERIC_FORMATS |
                                      UMINI_FORMATS))
            {
                return E_INVALIDARG;
            }

            DumpType = (Args->FormatFlags &
                        DEBUG_FORMAT_USER_SMALL_FULL_MEMORY) ?
                DTYPE_USER_MINI_FULL : DTYPE_USER_MINI_PARTIAL;
            break;

        case DEBUG_USER_WINDOWS_DUMP:
            if (Args->FormatFlags & ~GENERIC_FORMATS)
            {
                return E_INVALIDARG;
            }

            DumpType = g_Target->m_Machine->m_Ptr64 ?
                DTYPE_USER_FULL64 : DTYPE_USER_FULL32;
            break;
        default:
            // Other formats are not supported.
            return E_INVALIDARG;
        }
    }

    WriteTarget = NewDumpTargetInfo(DumpType);
    if (WriteTarget == NULL)
    {
        ErrOut(_T("Unable to create dump write target\n"));
        return E_OUTOFMEMORY;
    }

    // Ensure that the dump is always written according to the
    // target machine type and not any emulated machine.
    OldMachine = g_Target->m_EffMachineType;
    g_Target->SetEffMachine(g_Target->m_MachineType, FALSE);

    // Flush context first so that the minidump reads the
    // same register values the debugger has.
    g_Target->FlushRegContext();

    //
    // If we're producing a CAB put the dump in a temp file.
    //

    if (Args->FormatFlags & DEBUG_FORMAT_WRITE_CAB)
    {
        if (FileHandle)
        {
            Status = E_INVALIDARG;
            goto Exit;
        }

        if (!GetTempPathW(DIMA(TempFile), TempFile))
        {
            wcscpy(TempFile, L".\\");
        }
        // Use the CAB name as the dump file name so the
        // name in the CAB will match.
        CatStringW(TempFile, PathTailW(FileName), DIMA(TempFile));
        CatStringW(TempFile, L".dmp", DIMA(TempFile));

        DumpWriteFile = TempFile;
        Args->FormatFlags &= ~DEBUG_FORMAT_NO_OVERWRITE;
    }
    else
    {
        DumpWriteFile = FileName;
        if (!DumpWriteFile)
        {
            DumpWriteFile = L"<HandleOnly>";
        }
    }

    if (FileHandle)
    {
        DumpWriteHandle = OS_HANDLE(FileHandle);
        if (!DumpWriteHandle || DumpWriteHandle == INVALID_HANDLE_VALUE)
        {
            Status = E_INVALIDARG;
        }
        else
        {
            Status = S_OK;
        }
    }
    else
    {
        if (g_SymOptions & SYMOPT_SECURE)
        {
            ErrOut(_T("SECURE: Dump writing disallowed\n"));
            return E_ACCESSDENIED;
        }

        // Dumps are almost always written sequentially so
        // add that hint to the file flags.
        DumpWriteHandle =
            WideCreateFile(DumpWriteFile,
                           GENERIC_READ | GENERIC_WRITE,
                           0,
                           NULL,
                           (Args->FormatFlags & DEBUG_FORMAT_NO_OVERWRITE) ?
                           CREATE_NEW : CREATE_ALWAYS,
                           FILE_ATTRIBUTE_NORMAL |
                           FILE_FLAG_SEQUENTIAL_SCAN,
                           NULL);
        if (!DumpWriteHandle || DumpWriteHandle == INVALID_HANDLE_VALUE)
        {
            Status = WIN32_LAST_STATUS();
            ErrOut(_T("Unable to create file '%ws' - %s\n    \"%s\"\n"),
                   DumpWriteFile,
                   FormatStatusCode(Status), FormatStatus(Status));
        }
        else
        {
            Status = S_OK;
        }
    }

    if (Status == S_OK)
    {
        dprintf(_T("Creating %ws - "), DumpWriteFile);
        Status = WriteTarget->Write(DumpWriteHandle, Args);

        if (Status == S_OK)
        {
            dprintf(_T("Dump successfully written\n"));
        }

        if (!FileHandle)
        {
            CloseHandle(DumpWriteHandle);
            if (Status != S_OK)
            {
                WideDeleteFile(DumpWriteFile);
            }
        }
    }

    if (Status == S_OK && (Args->FormatFlags & DEBUG_FORMAT_WRITE_CAB))
    {
        GCONV_WA(DumpWriteFile, MAX_PATH, TRUE,
                 Status = E_OUTOFMEMORY; goto Exit);
        GCONV_WA(FileName, MAX_PATH, TRUE,
                 Status = E_OUTOFMEMORY; goto Exit);

        Status = CreateCabFromDump(DumpWriteFileA, FileNameA,
                                   Args->FormatFlags);

        WideDeleteFile(TempFile);
    }

 Exit:
    g_Target->SetEffMachine(OldMachine, FALSE);
    delete WriteTarget;
    return Status;
}

void
DotDump(PDOT_COMMAND Cmd, DebugClient* Client)
{
    BOOL Usage = FALSE;
    DUMP_WRITE_ARGS ArgsBuffer, *Args = &ArgsBuffer;

    ZeroMemory(Args, sizeof(*Args));
    
    //
    // Default to minidumps
    //

    if (IS_KERNEL_TARGET(g_Target))
    {
        Args->Qualifier = DEBUG_KERNEL_SMALL_DUMP;
    }
    else
    {
        Args->Qualifier = DEBUG_USER_WINDOWS_SMALL_DUMP;
    }
    Args->FormatFlags = DEBUG_FORMAT_DEFAULT | DEBUG_FORMAT_NO_OVERWRITE;

    //
    // Scan for options.
    //

    TCHAR Save;
    PTSTR FileName;
    BOOL SubLoop;
    PTSTR CommentEnd = NULL;
    PTSTR PhysMemFileNameEnd = NULL;
    BOOL Unique = FALSE;
    ProcessInfo* DumpProcess = g_Process;
    ULONG64 Addr;

    for (;;)
    {
        if (PeekChar() == '-' || *g_CurCmd == '/')
        {
            SubLoop = TRUE;

            g_CurCmd++;
            switch(*g_CurCmd)
            {
            case 'a':
                DumpProcess = NULL;
                g_CurCmd++;
                break;

            case 'b':
                Args->FormatFlags |= DEBUG_FORMAT_WRITE_CAB;
                g_CurCmd++;
                if (*g_CurCmd == 'a')
                {
                    Args->FormatFlags |= DEBUG_FORMAT_CAB_SECONDARY_FILES;
                    g_CurCmd++;
                }
                break;

            case 'c':
                g_CurCmd++;
#ifdef UNICODE
                Args->CommentW = StringValue(STRV_SPACE_IS_SEPARATOR |
                                             STRV_TRIM_TRAILING_SPACE, &Save);
#else
                Args->CommentA = StringValue(STRV_SPACE_IS_SEPARATOR |
                                             STRV_TRIM_TRAILING_SPACE, &Save);
#endif
                *g_CurCmd = Save;
                CommentEnd = g_CurCmd;
                break;

            case 'f':
                if (IS_KERNEL_TARGET(g_Target))
                {
                    Args->Qualifier = DEBUG_KERNEL_FULL_DUMP;
                }
                else
                {
                    Args->Qualifier = DEBUG_USER_WINDOWS_DUMP;
                    ErrOut(_T("*****************************************************************************\n"));
                    ErrOut(_T("* .dump /ma is the recommend method of creating a complete memory dump      *\n"));
                    ErrOut(_T("* of a user mode process.                                                   *\n"));
                    ErrOut(_T("*****************************************************************************\n"));

                }
                g_CurCmd++;
                break;

            case 'j':
                g_CurCmd++;
                Args->JitDebugInfoAddr =
                    GetTermExpression(_T("JIT_DEBUG_INFO address ")
                                      _T("missing from"));
                break;

            case 'k':
                if (g_CurCmd[1] == 'p' &&
                    g_CurCmd[2] == 'm' &&
                    g_CurCmd[3] == 'f' &&
                    IsNzCmdSep(g_CurCmd[4]))
                {
                    g_CurCmd += 4;
                    Args->PhysMemFileName =
                        StringValue(STRV_SPACE_IS_SEPARATOR |
                                    STRV_TRIM_TRAILING_SPACE, &Save);
                    *g_CurCmd = Save;
                    PhysMemFileNameEnd = g_CurCmd;
                }
                else
                {
                    goto UnknownOption;
                }
                break;
                
            case 'm':
                g_CurCmd++;
                if (IS_KERNEL_TARGET(g_Target))
                {
                    Args->Qualifier = DEBUG_KERNEL_SMALL_DUMP;

                    if (*g_CurCmd != '/' &&
                        *g_CurCmd != '-' &&
                        !IsSpaceT(*g_CurCmd))
                    {
                        Usage = TRUE;
                        break;
                    }
                }
                else
                {
                    Args->Qualifier = DEBUG_USER_WINDOWS_SMALL_DUMP;

                    for (;;)
                    {
                        switch(*g_CurCmd)
                        {
                        case 'a':
                            // Synthetic flag meaning "save the
                            // maximum amount of data."
                            Args->FormatFlags |=
                                DEBUG_FORMAT_USER_SMALL_FULL_MEMORY |
                                DEBUG_FORMAT_USER_SMALL_HANDLE_DATA |
                                DEBUG_FORMAT_USER_SMALL_UNLOADED_MODULES |
                                DEBUG_FORMAT_USER_SMALL_FULL_MEMORY_INFO |
                                DEBUG_FORMAT_USER_SMALL_THREAD_INFO;
                            break;
                        case 'c':
                            Args->FormatFlags |=
                                DEBUG_FORMAT_USER_SMALL_CODE_SEGMENTS;
                            break;
                        case 'C':
                            // Flag to test microdump code.
                            Args->InternalFlags |= UMINI_MICRO;
                            break;
                        case 'd':
                            Args->FormatFlags |=
                                DEBUG_FORMAT_USER_SMALL_DATA_SEGMENTS;
                            break;
                        case 'D':
                            // Flag to test debugger provider.
                            Args->InternalFlags |= UMINI_FORCE_DBG;
                            break;
                        case 'f':
                            Args->FormatFlags |=
                                DEBUG_FORMAT_USER_SMALL_FULL_MEMORY;
                            break;
                        case 'F':
                            Args->FormatFlags |=
                                DEBUG_FORMAT_USER_SMALL_FULL_MEMORY_INFO;
                            break;
                        case 'h':
                            Args->FormatFlags |=
                                DEBUG_FORMAT_USER_SMALL_HANDLE_DATA;
                            break;
                        case 'i':
                            Args->FormatFlags |=
                                DEBUG_FORMAT_USER_SMALL_INDIRECT_MEMORY;
                            break;
                        case 'k':
                            g_CurCmd++;
                            SubLoop = FALSE;
                            Args->InternalFlags |= UMINI_WRITE_KMINI;
                            Args->ExtraFileName =
                                StringValue(STRV_REQUIRE_QUOTES, &Save);
                            // We required an opening quote so we should
                            // be past a close quote, which we can just
                            // leave zeroed.
                            g_CurCmd++;
                            break;
                        case 'p':
                            Args->FormatFlags |=
                                DEBUG_FORMAT_USER_SMALL_PROCESS_THREAD_DATA;
                            break;
                        case 'r':
                            Args->FormatFlags |=
                                DEBUG_FORMAT_USER_SMALL_FILTER_MEMORY;
                            break;
                        case 'R':
                            Args->FormatFlags |=
                                DEBUG_FORMAT_USER_SMALL_FILTER_PATHS;
                            break;
                        case 't':
                            Args->FormatFlags |=
                                DEBUG_FORMAT_USER_SMALL_THREAD_INFO;
                            break;
                        case 'u':
                            Args->FormatFlags |=
                                DEBUG_FORMAT_USER_SMALL_UNLOADED_MODULES;
                            break;
                        case 'w':
                            Args->FormatFlags |=
                            DEBUG_FORMAT_USER_SMALL_PRIVATE_READ_WRITE_MEMORY;
                            break;
                        default:
                            SubLoop = FALSE;
                            break;
                        }

                        if (SubLoop)
                        {
                            g_CurCmd++;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                break;

            case 'o':
                Args->FormatFlags &= ~DEBUG_FORMAT_NO_OVERWRITE;
                g_CurCmd++;
                break;

            case 'u':
                Unique = TRUE;
                g_CurCmd++;
                break;

            case 'x':
                g_CurCmd++;
                switch(*g_CurCmd)
                {
                case 'c':
                    g_CurCmd++;
                    Args->ExContextAddr =
                        GetTermExpression(_T("Context record address ")
                                          _T("missing from"));
                    break;
                case 'p':
                    g_CurCmd++;
                    Addr = GetTermExpression(_T("Exception pointers address ")
                                             _T("missing from"));
                    if (g_Target->ReadPointer(g_Process, g_Machine, Addr,
                                              &Args->ExRecordAddr) != S_OK)
                    {
                        error(MEMORY);
                    }
                    Addr += g_Machine->m_Ptr64 ? 8 : 4;
                    if (g_Target->ReadPointer(g_Process, g_Machine, Addr,
                                              &Args->ExContextAddr) != S_OK)
                    {
                        error(MEMORY);
                    }
                    break;
                case 'r':
                    g_CurCmd++;
                    Args->ExRecordAddr =
                        GetTermExpression(_T("Exception record address ")
                                          _T("missing from"));
                    break;
                case 't':
                    g_CurCmd++;
                    Args->ExThreadId = (ULONG)
                        GetTermExpression(_T("Thread ID ")
                                          _T("missing from"));
                    break;
                default:
                    error(SYNTAX);
                }
                break;

            case '?':
                Usage = TRUE;
                g_CurCmd++;
                break;

            default:
            UnknownOption:
                ErrOut(_T("Unknown option '%c'\n"), *g_CurCmd);
                Usage = TRUE;
                g_CurCmd++;
                break;
            }
        }
        else
        {
            FileName = StringValue(STRV_TRIM_TRAILING_SPACE, &Save);
            if (*FileName)
            {
                break;
            }
            else
            {
                *g_CurCmd = Save;
                Usage = TRUE;
            }
        }

        if (Usage)
        {
            break;
        }
    }

    if (DumpProcess == NULL && !Unique)
    {
        Usage = TRUE;
    }

    if (Usage)
    {
        ErrOut(_T("Usage: .dump [options] filename\n"));
        ErrOut(_T("Options are:\n"));
        ErrOut(_T("  /a - Create dumps for all processes (requires -u)\n"));
        ErrOut(_T("  /b[a] - Package dump in a CAB and delete dump\n"));
        ErrOut(_T("  /c <comment> - Add a comment ")
               _T("(not supported in all formats)\n"));
        ErrOut(_T("  /j <addr> - Provide a JIT_DEBUG_INFO address\n"));
        if (IS_KERNEL_TARGET(g_Target))
        {
            ErrOut(_T("  /f - Create a full dump\n"));
            ErrOut(_T("  /m - Create a minidump (default)\n"));
        }
        else
        {
            ErrOut(_T("  /f - Create a legacy style full dump\n"));
            ErrOut(_T("  /m[acdfFhiprRtuw] - Create a minidump (default)\n"));
        }
        ErrOut(_T("  /o - Overwrite any existing file\n"));
        ErrOut(_T("  /u - Append unique identifier to dump name\n"));

        ErrOut(_T("\nUse \".hh .dump\" or open debugger.chm in the ")
               _T("debuggers directory to get\n")
               _T("detailed documentation on this command.\n\n"));

        return;
    }

    if (IS_LOCAL_KERNEL_TARGET(g_Target))
    {
        // It's possible to allow lkd to capture dump files
        // but because the system is running during the capture
        // the dump state will be a mishmash of different data.
        // We could mark the dump headers and warn when such
        // dumps are opened but it still seems too risky.
        // Leave this disabled for now.
#ifdef ALLOW_LOCAL_KD_DUMP
        if (Args->Qualifier != DEBUG_KERNEL_FULL_DUMP)
        {
            error(SESSIONNOTSUP);
        }

        WarnOut(_T("***************************************")
                _T("***************************************\n"));
        WarnOut(_T("WARNING: System state is changing as ")
                _T("local kd is running.\n"));
        WarnOut(_T("         Dump state will not be consistent.\n"));
        WarnOut(_T("***************************************")
                _T("***************************************\n"));
#else
        error(SESSIONNOTSUP);
#endif
    }

    if (CommentEnd)
    {
        *CommentEnd = 0;
    }
    if (PhysMemFileNameEnd)
    {
        *PhysMemFileNameEnd = 0;
    }

    ThreadInfo* OldThread = g_Thread;
    TargetInfo* Target;
    ProcessInfo* Process;

    ForAllLayersToProcess()
    {
        PTSTR DumpFileName;
        TCHAR UniqueName[2 * MAX_PATH];

        if (DumpProcess != NULL && Process != DumpProcess)
        {
            continue;
        }

        if (Process != g_Process)
        {
            SetCurrentThread(Process->m_ThreadHead, TRUE);
        }

        if (Unique)
        {
            MakeFileNameUnique(FileName, UniqueName, DIMA(UniqueName),
                               TRUE, g_Process);
            DumpFileName = UniqueName;
        }
        else
        {
            DumpFileName = FileName;
        }

#ifdef UNICODE
        WriteDumpFile(DumpFileName, 0, Args);
#else
        PWSTR WideName;
        
        if (AnsiToWide(DumpFileName, &WideName) == S_OK)
        {
            WriteDumpFile(WideName, 0, Args);
            FreeWide(WideName);
        }
        else
        {
            ErrOut(_T("Unable to convert dump filename\n"));
        }
#endif
    }

    if (!OldThread || OldThread->m_Process != g_Process)
    {
        SetCurrentThread(OldThread, TRUE);
    }

    *g_CurCmd = Save;
}

BOOL
DumpCabAdd(PCSTR File)
{
    HRESULT Status;

    dprintf(_T("  Adding %hs - "), File);
    FlushCallbacks();
    if ((Status = AddToDumpCab(File)) != S_OK)
    {
        ErrOut(_T("%s\n"), FormatStatusCode(Status));
    }
    else
    {
        dprintf(_T("added\n"));
    }

    if (CheckUserInterrupt())
    {
        return FALSE;
    }
    FlushCallbacks();
    return TRUE;
}

HRESULT
CreateCabFromDump(PCSTR DumpFile, PCSTR CabFile, ULONG Flags)
{
    HRESULT Status;

    if ((Status = CreateDumpCab(CabFile)) != S_OK)
    {
        ErrOut(_T("Unable to create CAB, %s\n"), FormatStatusCode(Status));
        return Status;
    }


    WarnOut(_T("Creating a cab file can take a VERY VERY long time\n.")
            _T("Ctrl-C can only interrupt the command after a file ")
             _T("has been added to the cab.\n"));
    //
    // First add all base dump files.
    //

    if (!DumpFile)
    {
        DumpTargetInfo* Dump = (DumpTargetInfo*)g_Target;
        ULONG i;

        for (i = DUMP_INFO_DUMP; i < DUMP_INFO_COUNT; i++)
        {
            if (Dump->m_InfoFiles[i].m_File)
            {
                if (!DumpCabAdd(Dump->m_InfoFiles[i].m_FileNameA))
                {
                    Status = E_UNEXPECTED;
                    goto Exit;
                }
            }
        }
    }
    else
    {
        if (!DumpCabAdd(DumpFile))
        {
            Status = E_UNEXPECTED;
            goto Exit;
        }
    }

    if (Flags & DEBUG_FORMAT_CAB_SECONDARY_FILES)
    {
        ImageInfo* Image;

        //
        // Add all symbols and images.
        //

        if (g_Process->m_ClrDebugDll)
        {
            char ClrPath[MAX_PATH];

            if (GetModuleFileNameA(g_Process->m_ClrDebugDll,
                                   ClrPath, DIMA(ClrPath)))
            {
                if (!DumpCabAdd(ClrPath))
                {
                    Status = E_UNEXPECTED;
                    goto Exit;
                }
            }
        }
        
        for (Image = g_Process->m_ImageHead; Image; Image = Image->m_Next)
        {
            if (Image->m_MappedImagePath[0])
            {
                PTSTR MapPath = Image->m_MappedImagePath;
                GCONV_TA(MapPath, MAX_IMAGE_PATH, TRUE,
                         Status = E_OUTOFMEMORY; break);
                if (!DumpCabAdd(MapPathA))
                {
                    Status = E_UNEXPECTED;
                    break;
                }
            }

            IMAGEHLP_MODULE64 ModInfo;

            ModInfo.SizeOfStruct = sizeof(ModInfo);
            if (SymGetModuleInfo64(g_Process->m_SymHandle,
                                   Image->m_BaseOfImage, &ModInfo))
            {
                ULONG Len;
                PTSTR Str;

                // The loaded image name often refers directly to the
                // image.  Only save the loaded image file if it
                // refers to a .dbg file.
                if (ModInfo.LoadedImageName[0] &&
                    (Len = _tcslen(ModInfo.LoadedImageName)) > 4 &&
                    !_tcsicmp(ModInfo.LoadedImageName + (Len - 4), _T(".dbg")))
                {
                    Str = ModInfo.LoadedImageName;
                    GCONV_TA(Str, MAX_PATH, TRUE,
                             Status = E_OUTOFMEMORY; break);
                    
                    if (!DumpCabAdd(StrA))
                    {
                        Status = E_UNEXPECTED;
                        break;
                    }
                }

                // Save any PDB that was opened.
                if (ModInfo.LoadedPdbName[0])
                {
                    Str = ModInfo.LoadedPdbName;
                    GCONV_TA(Str, MAX_PATH, TRUE,
                             Status = E_OUTOFMEMORY; break);
                    
                    if (!DumpCabAdd(StrA))
                    {
                        Status = E_UNEXPECTED;
                        break;
                    }
                }
            }
        }
    }

 Exit:
    CloseDumpCab();

    if (Status == S_OK)
    {
        dprintf(_T("Wrote %hs\n"), CabFile);
    }

    return Status;
}
