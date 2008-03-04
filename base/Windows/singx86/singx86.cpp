/////////////////////////////////////////////////////////////////////////////
//
//  singx86.cpp - Singularity Debugger Extension.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  For more information, see http://ddkslingshot/webs/debugexw/
//
#include "singx86.h"
#include <strsafe.h>

extern bool fCachedLogStateValid;

ULONG   TargetMachine;
BOOL    Connected;

PDEBUG_ADVANCED       g_ExtAdvanced;
PDEBUG_CLIENT         g_ExtClient;
PDEBUG_CONTROL4       g_ExtControl;
PDEBUG_DATA_SPACES4   g_ExtData;
PDEBUG_REGISTERS2     g_ExtRegisters;
PDEBUG_SYMBOLS        g_ExtSymbols = NULL;
PDEBUG_SYSTEM_OBJECTS g_ExtSystem;

// Queries for all debugger interfaces.
HRESULT
ExtQuery(PDEBUG_CLIENT Client)
{
    HRESULT status;

    //
    // Required interfaces.
    //

    if ((status = Client->QueryInterface(__uuidof(IDebugAdvanced),
                                         (void **)&g_ExtAdvanced)) != S_OK) {
        goto Fail;
    }
    if ((status = Client->QueryInterface(__uuidof(IDebugControl4),
                                         (void **)&g_ExtControl)) != S_OK) {
        goto Fail;
    }
    if ((status = Client->QueryInterface(__uuidof(IDebugDataSpaces4),
                                         (void **)&g_ExtData)) != S_OK) {
        goto Fail;
    }
    if ((status = Client->QueryInterface(__uuidof(IDebugRegisters2),
                                         (void **)&g_ExtRegisters)) != S_OK) {
        goto Fail;
    }
    if ((status = Client->QueryInterface(__uuidof(IDebugSymbols),
                                         (void **)&g_ExtSymbols)) != S_OK) {
        goto Fail;
    }
    if ((status = Client->QueryInterface(__uuidof(IDebugSystemObjects),
                                         (void **)&g_ExtSystem)) != S_OK) {
        goto Fail;
    }

    g_ExtClient = Client;

    return S_OK;

 Fail:
    ExtRelease();
    return status;
}

// Cleans up all debugger interfaces.
void
ExtRelease(void)
{
    g_ExtClient = NULL;
    EXT_RELEASE(g_ExtAdvanced);
    EXT_RELEASE(g_ExtControl);
    EXT_RELEASE(g_ExtData);
    EXT_RELEASE(g_ExtRegisters);
    EXT_RELEASE(g_ExtSymbols);
    EXT_RELEASE(g_ExtSystem);
}

// Normal output.
void __cdecl
ExtOut(PCSTR Format, ...)
{
    va_list Args;

    va_start(Args, Format);
    g_ExtControl->OutputVaList(DEBUG_OUTPUT_NORMAL, Format, Args);
    va_end(Args);
}

// Error output.
void __cdecl
ExtErr(PCSTR Format, ...)
{
    va_list Args;

    va_start(Args, Format);
    g_ExtControl->OutputVaList(DEBUG_OUTPUT_ERROR, Format, Args);
    va_end(Args);
}

// Warning output.
void __cdecl
ExtWarn(PCSTR Format, ...)
{
    va_list Args;

    va_start(Args, Format);
    g_ExtControl->OutputVaList(DEBUG_OUTPUT_WARNING, Format, Args);
    va_end(Args);
}

// Verbose output.
void __cdecl
ExtVerb(PCSTR Format, ...)
{
    va_list Args;

    va_start(Args, Format);
    g_ExtControl->OutputVaList(DEBUG_OUTPUT_VERBOSE, Format, Args);
    va_end(Args);
}

HRESULT
ExtEvalU64(PCSTR* Str, PULONG64 Val)
{
    HRESULT status;
    DEBUG_VALUE FullVal;
    ULONG EndIdx;

    if ((status = g_ExtControl->
         Evaluate(*Str, DEBUG_VALUE_INT64, &FullVal, &EndIdx)) != S_OK) {
        return status;
    }

    *Val = FullVal.I64;
    (*Str) += EndIdx;

    while (**Str == ' ' || **Str == '\t' ||
           **Str == '\n' || **Str == '\r') {
        (*Str)++;
    }

    return S_OK;
}

HRESULT
ExtDefPointer(ULONG64 address, PULONG64 val)
{
    *val = 0;

    return g_ExtData->ReadPointersVirtual(1, address, val);
}

//////////////////////////////////////////////////////////////////////////////
//
class MyEventCallbacks : public DebugBaseEventCallbacks
{
    ULONG count;

  public:
    MyEventCallbacks()
    {
        count = 1;
    }

    STDMETHOD_(ULONG, AddRef)(THIS)
    {
        count++;
        return count;
    }

    STDMETHOD_(ULONG, Release)(THIS)
    {
        count--;
        return count;
    }

    STDMETHOD(GetInterestMask)(
        THIS_
        __out PULONG Mask)
    {
        *Mask = DEBUG_EVENT_CHANGE_DEBUGGEE_STATE;
        return S_OK;
    }

    STDMETHOD(ChangeDebuggeeState)(
        THIS_
        __in ULONG Flags,
        __in ULONG64 Argument
        )
    {
        UNREFERENCED_PARAMETER(Flags);
        UNREFERENCED_PARAMETER(Argument);

        PDEBUG_CLIENT DebugClient;
        PDEBUG_CONTROL DebugControl;
        HRESULT Hr;

        if ((Hr = DebugCreate(__uuidof(IDebugClient),
                              (void **)&DebugClient)) != S_OK) {
            return Hr;
        }

        if ((Hr = DebugClient->QueryInterface(__uuidof(IDebugControl),
                                              (void **)&DebugControl)) == S_OK) {
            return Hr;
        }

        fCachedLogStateValid = false;

        EXT_RELEASE(DebugControl);
        EXT_RELEASE(DebugClient);

        return S_OK;
    }
};

static MyEventCallbacks g_Callback;

//////////////////////////////////////////////////////////////////////////////
//

extern "C"
HRESULT
CALLBACK
DebugExtensionInitialize(PULONG Version, PULONG Flags)
{
    PDEBUG_CLIENT DebugClient;
    PDEBUG_CONTROL DebugControl;
    HRESULT Hr;

    *Version = DEBUG_EXTENSION_VERSION(1, 0);
    *Flags = 0;
    Hr = S_OK;

    if ((Hr = DebugCreate(__uuidof(IDebugClient),
                          (void **)&DebugClient)) != S_OK) {
        return Hr;
    }

    if ((Hr = DebugClient->QueryInterface(__uuidof(IDebugControl),
                                          (void **)&DebugControl)) == S_OK) {
    }

    Hr = DebugClient->SetEventCallbacks(&g_Callback);

    ULONG execStatus = 0;
    if ((Hr = DebugControl->GetExecutionStatus(&execStatus)) == S_OK) {

        // #define DEBUG_STATUS_NO_CHANGE         0
        // #define DEBUG_STATUS_GO                1
        // #define DEBUG_STATUS_GO_HANDLED        2
        // #define DEBUG_STATUS_GO_NOT_HANDLED    3
        // #define DEBUG_STATUS_STEP_OVER         4
        // #define DEBUG_STATUS_STEP_INTO         5
        // #define DEBUG_STATUS_BREAK             6
        // #define DEBUG_STATUS_NO_DEBUGGEE       7
        // #define DEBUG_STATUS_STEP_BRANCH       8
        // #define DEBUG_STATUS_IGNORE_EVENT      9
        // #define DEBUG_STATUS_RESTART_REQUESTED 10

    }

    EXT_RELEASE(DebugControl);
    EXT_RELEASE(DebugClient);
    return Hr;
}


extern "C"
void
CALLBACK
DebugExtensionNotify(ULONG Notify, ULONG64 Argument)
{
    UNREFERENCED_PARAMETER(Argument);

    //
    // The first time we actually connect to a target
    //

    if ((Notify == DEBUG_NOTIFY_SESSION_ACCESSIBLE) && (!Connected)) {
        IDebugClient *DebugClient;
        HRESULT Hr;
        PDEBUG_CONTROL DebugControl;

        if ((Hr = DebugCreate(__uuidof(IDebugClient),
                              (void **)&DebugClient)) == S_OK) {
            //
            // Get the architecture type.
            //

            if ((Hr = DebugClient->QueryInterface(__uuidof(IDebugControl),
                                                  (void **)&DebugControl)) == S_OK) {
                if ((Hr = DebugControl->GetActualProcessorType(&TargetMachine)) == S_OK) {
                    Connected = TRUE;
                }
                OnTargetAccessible(DebugClient);
                DebugControl->Release();
            }
            DebugClient->Release();
        }
    }

    if (Notify == DEBUG_NOTIFY_SESSION_INACTIVE) {
        Connected = FALSE;
        TargetMachine = 0;
    }
}

//////////////////////////////////////////////////////////////////////////////

/*
  This gets called (by DebugExtensionNotify when target is halted and is accessible
*/
HRESULT
OnTargetAccessible(PDEBUG_CLIENT client)
{
    EXT_ENTER();    // Defines: HRESULT status = S_OK;

    ExtOut("**** SingX86.dll [" __DATE__ " " __TIME__ "] detected a break");
    if (Connected) {
        ExtOut(" connected to ");
        switch (TargetMachine) {
          case IMAGE_FILE_MACHINE_I386:
            ExtOut("X86");
            break;
          case IMAGE_FILE_MACHINE_IA64:
            ExtOut("IA64");
            break;
          default:
            ExtOut("Other");
            break;
        }
    }
    ExtOut(" ****\n");

    EXT_CHECK(StructType::InitializeRegistered());

    EXT_LEAVE();    // Macro includes: return status;
}

extern "C"
void
CALLBACK
DebugExtensionUninitialize(void)
{
    PDEBUG_CLIENT DebugClient;
    HRESULT Hr;

    if ((Hr = DebugCreate(__uuidof(IDebugClient), (void **)&DebugClient)) != S_OK) {
        return;
    }

    Hr = DebugClient->SetEventCallbacks(NULL);

    EXT_RELEASE(DebugClient);
}

extern "C"
HRESULT
CALLBACK
KnownStructOutput(
    ULONG Flag,
    ULONG64 Address,
    PSTR StructName,
    PSTR Buffer,
    PULONG BufferSize
    )
{
    IDebugClient *Client;
    HRESULT status;

    if ((status = DebugCreate(__uuidof(IDebugClient),
                              (void **)&Client)) == S_OK) {
        switch (Flag) {
          case DEBUG_KNOWN_STRUCT_GET_NAMES:
            status = OnGetKnownStructNames(Client, Buffer, BufferSize);
            break;

          case DEBUG_KNOWN_STRUCT_GET_SINGLE_LINE_OUTPUT:
            status = OnGetSingleLineOutput(Client,
                                           Address,
                                           StructName,
                                           Buffer,
                                           BufferSize);
            break;

          case DEBUG_KNOWN_STRUCT_SUPPRESS_TYPE_NAME:
            status = OnGetSuppressTypeName(Client,
                                           StructName);
            break;

          default:
            status = E_INVALIDARG;
            break;
        }
    }

    EXT_RELEASE(Client);

    return status;
}

ULONG64 GetStaticPointer(PCSTR name)
{
    HRESULT status = S_OK;
    ULONG64 address;
    ULONG64 value = 0;
    EXT_CHECK(g_ExtSymbols->GetOffsetByName(name, &address));
    EXT_CHECK(g_ExtData->ReadPointersVirtual(1, address, &value));
  Exit:
    return value;
}

///////////////////////////////////// Remote to Local conversions for structs.
//
StructType * StructType::registered = NULL;

StructType::StructType(PCSTR name, ULONG localSize, struct FieldType *fields)
{
    this->next = registered;
    registered = this;

    this->name = name;
    this->localSize = localSize;
    this->fields = fields;

    this->fieldCount = 0;
    this->module = 0;
    this->type = 0;
    this->size = 0;
    this->temp = NULL;
}

HRESULT StructType::InitializeRegistered()
{
    for (StructType *next = registered; next != NULL; next = next->next) {
        next->Initialize();
    }
    return S_OK;
}

HRESULT StructType::Initialize()
{
    HRESULT status = S_OK;

    ExtVerb("Initializing: %s [%p]\n", name, fields);
    EXT_CHECK(g_ExtSymbols->GetSymbolTypeId(name, &type, &module));
    EXT_CHECK(g_ExtSymbols->GetTypeSize(module, type, &size));
    ExtVerb("Initializing: %s [size=%d]\n", name, size);
    if (temp != NULL) {
        delete[] temp;
    }
    temp = new BYTE [size];
    ZeroMemory(temp, size);

    fieldCount = 0;
    for (FieldType *field = fields; field->name != NULL; field++) {
        CHAR fieldName[512];

        EXT_CHECK(StringCbPrintf(fieldName, sizeof(fieldName), "%s.%s", name, field->name));
        status = g_ExtSymbols->GetSymbolTypeId(fieldName, &field->type, &field->module);
        if (status == S_OK) {
            EXT_CHECK(g_ExtSymbols->GetTypeSize(field->module, field->type, &field->size));
            EXT_CHECK(g_ExtSymbols->GetFieldOffset(module, type, field->name, &field->offset));
            ExtVerb("Initializing: %s [offset=%d,size=%d]\n", fieldName, field->offset, field->size);
        }
        else {
            ExtErr("Can't find: %s\n", fieldName);
            field->size = 0;
            field->offset = 0;
        }

        field->parent = this;
        fieldCount++;
    }

  Exit:
    ExtVerb("** Exited with %08x\n", status);
    return status;
}

HRESULT StructType::RemoteOffsetFromLocal(ULONG localOffset, ULONG *remoteOffset)
{
    for (ULONG f = 0; f < fieldCount; f++) {
        FieldType *field = &fields[f];

        if (field->localOffset == localOffset) {
            if (remoteOffset != NULL) {
                *remoteOffset = field->offset;
                return S_OK;
            }
            return S_FALSE;
        }
    }
    return E_FAIL;
}

HRESULT StructType::RawAccess(ULONG remoteOffset, PVOID *raw)
{
    *raw = temp + remoteOffset;
    return S_OK;
}

HRESULT StructType::Read(ULONG64 address, PVOID local)
{
    HRESULT status = S_OK;

    ZeroMemory(temp, size);
    ZeroMemory(local, localSize);

    EXT_CHECK(g_ExtData->ReadVirtual(address, temp, size, NULL));

    for (ULONG f = 0; f < fieldCount; f++) {
        FieldType *field = &fields[f];

        PBYTE pbLocal = ((PBYTE)local) + field->localOffset;
        PBYTE pbTemp = &temp[field->offset];

        switch (field->size) {
          case 1:
            *(ULONG64 *)pbLocal = *(BYTE *)pbTemp;
            break;
          case 2:
            *(ULONG64 *)pbLocal = *(USHORT *)pbTemp;
            break;
          case 4:
            *(ULONG64 *)pbLocal = *(ULONG *)pbTemp;
            break;
          case 8:
            *(ULONG64 *)pbLocal = *(ULONG64 *)pbTemp;
            break;
          default:
#if 0
              // We allow bigger sizes, for raw access only.
            ExtOut("Unknown size: %d, in field %s\n", field->size, field->name);
#endif
            break;
        }
    }

  Exit:
    return status;
}

HRESULT StructType::Clear()
{
    HRESULT status = S_OK;

    ZeroMemory(temp, size);

    return status;
}

HRESULT StructType::Update(PVOID local)
{
    HRESULT status = S_OK;

    for (ULONG f = 0; f < fieldCount; f++) {
        FieldType *field = &fields[f];

        PBYTE pbLocal = ((PBYTE)local) + field->localOffset;
        PBYTE pbTemp = &temp[field->offset];

        switch (field->size) {
          case 1:
            *(BYTE *)pbTemp = (BYTE)*(ULONG64 *)pbLocal;
            break;
          case 2:
            *(USHORT *)pbTemp = (USHORT)*(ULONG64 *)pbLocal;
            break;
          case 4:
            *(ULONG *)pbTemp = (ULONG)*(ULONG64 *)pbLocal;
            break;
          case 8:
            *(ULONG64 *)pbTemp = *(ULONG64 *)pbLocal;
            break;
        }
    }

    return status;
}

HRESULT StructType::Flush(ULONG64 address)
{
    HRESULT status = S_OK;

    EXT_CHECK(g_ExtData->WriteVirtual(address, temp, size, NULL));

  Exit:
    return status;
}


//
///////////////////////////////////////////////////////////////// End of File.
