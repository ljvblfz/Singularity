/////////////////////////////////////////////////////////////////////////////
//
//  dump.cpp - Extension to dump a Singularity core.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
#include "singx86.h"
#include "minidump.h"

HRESULT GetValue(PVOID pvData, ULONG Size, ULONG64 *valueOut)
{
    switch (Size) {
      case 1:
        *valueOut = *(BYTE *)pvData;
        return S_OK;
      case 2:
        *valueOut = *(USHORT *)pvData;
        return S_OK;
      case 4:
        *valueOut = *(ULONG *)pvData;
        return S_OK;
      case 8:
        *valueOut = *(ULONG64 *)pvData;
        return S_OK;
      default:
        ExtErr("GetValue: Invalid size: %d\n", Size);
        return E_FAIL;
    }
}

struct FieldAccessor
{
    ULONG64 Module;
    ULONG   TypeId;
    ULONG   Offset;
    ULONG   Size;
    PCSTR   Field;

    HRESULT Init(ULONG64 Module, ULONG TypeId, PCSTR Field)
    {
        HRESULT status = S_OK;

        this->Module = Module;
        this->TypeId = TypeId;
        this->Field = Field;

        EXT_CHECK(g_ExtSymbols->GetFieldOffset(Module, TypeId, Field, &Offset));
        EXT_CHECK(g_ExtSymbols->GetFieldOffset(Module, TypeId, Field, &Offset));

      Exit:
        return status;
    }

    HRESULT GetValueIn(ULONG64 vaStruct, ULONG64 *valueOut)
    {
        HRESULT status = S_OK;
        BYTE buffer[8];

        EXT_CHECK(g_ExtData->ReadVirtual(vaStruct + Offset, &buffer, sizeof(buffer), NULL));
        EXT_CHECK(GetValue(&buffer, Size, valueOut));

      Exit:
        return status;
    }

    HRESULT GetValueIn(PVOID pvStruct, ULONG64 *valueOut)
    {
        return GetValue((PBYTE)pvStruct + Offset, Size, valueOut);
    }
};

EXT_DECL(dump) // Defines: PDEBUG_CLIENT Client, PCSTR args
{
    EXT_ENTER();    // Defines: HRESULT status = S_OK;
    PCSTR pszFile = NULL;
    BOOL fOverwrite = FALSE;

    while (*args != '\0') {
        // skip whitespace
        while (*args == ' ' || *args == '\t') {
            args++;
        }

        // process argument
        if (*args == '-' || *args == '/') {
            args++;
            switch (*args++) {
              case 'o': // detail
              case 'O':
                fOverwrite = !fOverwrite;
                break;
              case '?': // Help
              case 'h':
              case 'H':
                status = S_FALSE;
                goto Exit;
            }
            while (*args != ' ') {
                args++;
            }
        }
        else {
            if (*args == '\"') {
                pszFile = ++args;
                while (*args != '\0' && *args != '\"') {
                    args++;
                }
                if (*args == '\"') {
                    *((PCHAR&)args)++ = '\0';
                }
            }
            else {
                while (*args != '\0' && *args != ' ' && *args != '\t') {
                    args++;
                }
                if (*args != '\0') {
                    *((PCHAR&)args)++ = '\0';
                }
            }
            break;
        }
    }

    ULONG64 vaBootInfo = GetStaticPointer("nt!g_pBootInfo");

    ULONG TypeId;
    EXT_CHECK(g_ExtSymbols->GetTypeId(0, "nt!g_pBootInfo", &TypeId));
    ExtOut("nt!g_pBootInfo = %08x\n", TypeId);
    EXT_CHECK(g_ExtSymbols->GetSymbolTypeId("nt!Struct_Microsoft_Singularity_BootInfo", &TypeId, NULL));
    ExtOut("nt!Struct_Microsoft_Singularity_BootInfo = %08x\n", TypeId);
    EXT_CHECK(g_ExtSymbols->GetSymbolTypeId("nt!Struct_Microsoft_Singularity_BootInfo._SmapData32", &TypeId, NULL));
    ExtOut("nt!Struct_Microsoft_Singularity_BootInfo._SmapData32 = %08x\n", TypeId);


    ExtOut("vaBootInfo=%p\n", vaBootInfo);

#if 0
    // Dumps are almost always written sequentially so
    // add that hint to the file flags.
    HANDLE hFile = CreateFile(pszFile,
                              GENERIC_READ | GENERIC_WRITE,
                              0,
                              NULL,
                              fOverwrite ? CREATE_ALWAYS : CREATE_NEW,
                              FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN,
                              NULL);
    if (hFile == INVALID_HANDLE_VALUE) {
        status = HRESULT_FROM_WIN32(GetLastError());
        ExtErr("Unable to create file '%s' - %d\n",
               pszFile, GetLastError());
        goto Exit;
    }
#endif

#if 0
    Class_System_Dumping_Dump data;
    ULONG ret;

    EXT_CHECK(g_ExtData->ReadVirtual(dump, &data, sizeof(data), &ret));
    ExtOut("  %p { tid=%03x pid=%03x ebp=%p, esp=%p, eip=%p }\n",
           dump,
           data.context.tid == -1 ? 0xfff : data.context.tid,
           data.context.pid == -1 ? 0xfff : data.context.pid,
           (ULONG64)data.context.eip,
           (ULONG64)data.context.ebp,
           (ULONG64)data.context.esp);

    CONTEXT context;

    EXT_CHECK(g_ExtSymbols->GetScope(NULL, NULL, &context, sizeof(context)));
    context.Eip = data.context.eip;
    context.Esp = data.context.esp;
    context.Ebp = data.context.ebp;
    context.Eax = data.context.eax;
    context.Ebx = data.context.ebx;
    context.Ecx = data.context.ecx;
    context.Edx = data.context.edx;
    context.Esi = data.context.esi;
    context.Edi = data.context.edi;
    context.EFlags = data.context.efl;

    // CONTEXT_FLOATING_POINT
    context.FloatSave.ControlWord = data.context.mmx.fcw;
    context.FloatSave.StatusWord = data.context.mmx.fsw;
    context.FloatSave.TagWord = data.context.mmx.ftw;
    context.FloatSave.ErrorOffset = data.context.mmx.cs;
    context.FloatSave.ErrorSelector = data.context.mmx.eip;
    context.FloatSave.DataOffset = data.context.mmx.ds;
    context.FloatSave.DataSelector = data.context.mmx.dp;

    memcpy((uint8*)context.FloatSave.RegisterArea+0, &data.context.mmx.st0, 10);
    memcpy((uint8*)context.FloatSave.RegisterArea+10, &data.context.mmx.st1, 10);
    memcpy((uint8*)context.FloatSave.RegisterArea+20, &data.context.mmx.st2, 10);
    memcpy((uint8*)context.FloatSave.RegisterArea+30, &data.context.mmx.st3, 10);
    memcpy((uint8*)context.FloatSave.RegisterArea+40, &data.context.mmx.st4, 10);
    memcpy((uint8*)context.FloatSave.RegisterArea+50, &data.context.mmx.st5, 10);
    memcpy((uint8*)context.FloatSave.RegisterArea+60, &data.context.mmx.st6, 10);
    memcpy((uint8*)context.FloatSave.RegisterArea+70, &data.context.mmx.st7, 10);

    memcpy(context.ExtendedRegisters, &data.context.mmx, 512);

    EXT_CHECK(g_ExtSymbols->SetScope(0, NULL, &context, sizeof(context)));
#endif

    EXT_LEAVE();    // Macro includes: return status;
}
