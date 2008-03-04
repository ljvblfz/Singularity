//////////////////////////////////////////////////////////////////////////////
//
//  pxe.cpp - Get files via PXE from SINGLDR
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#include "pxe.h"
#include "fnames.h"

#pragma warning(disable: 4505) // Compiler generated constructor unreferenced

//////////////////////////////////////////////////////////////////////////////
//
// Private Functions

// The only way to talk to the hardware for PXE boot:
void PxeCall(int16 api, LPVOID args)
{
    if (s_fpPxenv) {
        __asm {
            mov bx, api;
            les di, args;
            call s_fpPxenv;
        }
    }
    else if (s_fpPxe) {
        (*s_fpPxe)(api, args);
    }
}

static uint32 parse_ip_addr(LPCHAR psz)
{
    uint32 addr = 0;
    uint16 num = 0;
    for (; (*psz >= '0' && *psz <= '9') || *psz == '.'; psz++) {
        if (*psz >= '0' && *psz <= '9') {
            num = num * 10 + (*psz - '0');
        }
        else if (*psz == '.') {
            addr = (addr >> 8) + ((uint32)num << 24);
            num = 0;
        }
    }
    addr = (addr >> 8) + ((uint32)num << 24);
    return addr;
}

//////////////////////////////////////////////////////////////////////////////
//
// Public Functions

int PxeDevice::OpenDevice() __far
{
    reply = (DHCP_PACKET __far *)alloc(1500, 4);

    // Get PXE Configuration.
    if (pxe->Signature[0] == '!' &&
        pxe->Signature[1] == 'P' &&
        pxe->Signature[2] == 'X' &&
        pxe->Signature[3] == 'E') {

        printf("PXE Signature   :   %c%c%c%c %d\n",
               pxe->Signature[0],
               pxe->Signature[1],
               pxe->Signature[2],
               pxe->Signature[3],
               pxe->StructRev);
        s_fpPxe = pxe->EntryPoint;
    }
    else if (pxenv->Signature[0] == 'P' &&
             pxenv->Signature[1] == 'X' &&
             pxenv->Signature[2] == 'E' &&
             pxenv->Signature[3] == 'N' &&
             pxenv->Signature[4] == 'V' &&
             pxenv->Signature[5] == '+')
    {
        printf("PXE Signature:      %c%c%c%c%c%c version %d.%d\n",
               pxenv->Signature[0],
               pxenv->Signature[1],
               pxenv->Signature[2],
               pxenv->Signature[3],
               pxenv->Signature[4],
               pxenv->Signature[5],
               pxenv->Version >> 8,
               pxenv->Version&0xff);

        s_fpPxenv = pxenv->EntryPoint;
    }

    reply = (DHCP_PACKET __far *)alloc(1500, 4);
    CmdLine = (uint16 __far *)alloc(2048, 2);

    for (int j = 3; j > 0; j--) {
        CHAR szTemp[128];   // Needed so string is near pointer.
        PXENV_GET_CACHED_INFO pgci;
        pgci.PacketType = (UINT16)j; //CACHED_REPLY;
        pgci.Buffer = reply;
        pgci.BufferSize = 1500;
        pgci.BufferLimit = 1500;
        memzero(reply, 1500);

        printf("  PXE GetCachedInfo (Type=%d):\n", j);
        PxeCall(GET_CACHED_INFO, &pgci);

        if (pgci.Status != 0) {
            printf("    Error 0x%04x on request.\n", pgci.Status);
            continue;
        }

        printf("    client: %d.%d.%d.%d, dhcp: %d.%d.%d.%d, gate: %d.%d.%d.%d (%d bytes)\n",
               ((uint8 __far *)&reply->yiaddr)[0],
               ((uint8 __far *)&reply->yiaddr)[1],
               ((uint8 __far *)&reply->yiaddr)[2],
               ((uint8 __far *)&reply->yiaddr)[3],
               ((uint8 __far *)&reply->siaddr)[0],
               ((uint8 __far *)&reply->siaddr)[1],
               ((uint8 __far *)&reply->siaddr)[2],
               ((uint8 __far *)&reply->siaddr)[3],
               ((uint8 __far *)&reply->giaddr)[0],
               ((uint8 __far *)&reply->giaddr)[1],
               ((uint8 __far *)&reply->giaddr)[2],
               ((uint8 __far *)&reply->giaddr)[3],
               pgci.BufferSize);
        if (reply->sname[0] != '\0') {
            strcpy(szTemp, reply->sname);
            printf("    server: [%s]\n", szTemp);
        }
        if (reply->file[0] != '\0') {
            strcpy(szTemp, reply->file);
            printf("    file:   [%s]\n", szTemp);
        }
        if (reply->cookie[0] == 0x63 &&
            reply->cookie[1] == 0x82 &&
            reply->cookie[2] == 0x53 &&
            reply->cookie[3] == 0x63) {

            DHCP_OPTIONS __far *options = &reply->options;
            DHCP_OPTIONS __far *optionsEnd
                = (DHCP_OPTIONS __far *)reply + pgci.BufferSize;

            while (options < optionsEnd) {
                if (options->Code == DHCP_OPTION_COOKIE_SERVERS) {
                    uint16 __far * pwzCmdLine = CmdLine;
                    for (int i = 0; i < options->Length; i++) {
                        *pwzCmdLine++ = options->Data[i];
                    }
                    *pwzCmdLine++ = '\0';
                    printf("    cmd: %s\n", CmdLine);
                    break;
                }

                if (options->Code == 0) {
                    options = (DHCP_OPTIONS __far *)(((CHAR __far *)options) + 1);
                }
                else {
                    options = (DHCP_OPTIONS __far *)(((CHAR __far *)options)
                                                     + options->Length + 2);
                }
            }
        }
        else {
            printf("    cookie: %02x %02x %02x %02x\n",
                   reply->cookie[0],
                   reply->cookie[1],
                   reply->cookie[2],
                   reply->cookie[3]);
        }

        if (reply->sname[0] != '\0') {
            break;
        }
    }
    ServerIP = parse_ip_addr(reply->sname);
    return 0;
}

int PxeDevice::CloseDevice() __far
{
    // create a packet that will fail
    PXENV_TFTP_GET_FSIZE ptfg;
    LPCHAR terminate = "end.:"; // request an invalid filename :)
    memzero(&ptfg, sizeof(ptfg));
    strcpy(ptfg.FileName, terminate);
    ptfg.Status = ~0u;
    ptfg.GatewayIpAddress = reply->giaddr;
    ptfg.ServerIpAddress = ServerIP;

    printf("  Size: %d.%d.%d.%d [%s] (to close)\n",
           ((uint8 __far *)&ptfg.ServerIpAddress)[0],
           ((uint8 __far *)&ptfg.ServerIpAddress)[1],
           ((uint8 __far *)&ptfg.ServerIpAddress)[2],
           ((uint8 __far *)&ptfg.ServerIpAddress)[3],
           ptfg.FileName);

    PxeCall(TFTP_GET_FSIZE, &ptfg);

    return 0;
}

int PxeDevice::GetFileProperties(LPCHAR /* filename */,
                                 FilePtr /* file */,
                                 FilePtr /* directory */) __far
{
    // We could call PxeCall(TFTP_GET_FSIZE here, but some PXE
    // implementations fetch the entire file to determine its
    // size.  Instead we rely on size information in Singboot.ini.

#if DEPRECATED
    // consume leading '/' chars in filename
    LPCHAR fname = filename;
    while (fname[0]=='/') {
        fname++;
    }

    // build the message packet
    PXENV_TFTP_GET_FSIZE ptfg;
    memzero(&ptfg, sizeof(ptfg));

    FNameToCStr(fname, ptfg.FileName, sizeof(ptfg.FileName));

    ptfg.Status = ~0u;
    ptfg.GatewayIpAddress = reply->giaddr;
    ptfg.ServerIpAddress = ServerIP;

    PxeCall(TFTP_GET_FSIZE, &ptfg);

    if (ptfg.Status != 0) {
        printf("PXE: Size: %d.%d.%d.%d ",
               ((uint8 __far *)&ptfg.ServerIpAddress)[0],
               ((uint8 __far *)&ptfg.ServerIpAddress)[1],
               ((uint8 __far *)&ptfg.ServerIpAddress)[2],
               ((uint8 __far *)&ptfg.ServerIpAddress)[3]);
        PutFName(filename);
        printf("\n    Error 0x%04x on request.\n", ptfg.Status);
        return -1;
    }
    //    file->Size = ptfg.FileSize;
#endif
    return 0;
}

INT16 PxeDevice::ReadFileLow(LPCHAR filename,
                             FilePtr /* file */,
                             uint8 __far * buffer) __far
{
    // consume leading '/' chars
    LPCHAR fname = filename;
    while (fname[0] == '/') {
        fname++;
    }

    // build the message packet
    PXENV_TFTP_READ_FILE ptrf;
    memzero(&ptrf, sizeof(ptrf));
    strcpy(ptrf.FileName, fname);
    ptrf.Status           = ~0u;
    ptrf.BufferSize       = 0x8000;
    ptrf.Buffer           = PointerToUint32(buffer);
    ptrf.GatewayIpAddress = reply->giaddr;
    ptrf.ServerIpAddress  = ServerIP;

    PxeCall(TFTP_READ_FILE, &ptrf);

    if (ptrf.Status != 0) {
        printf("PXE: Loading low: %s", filename);
        printf("\n    Error 0x%04x on request.\n", ptrf.Status);
        return -1;
    }
    return 0;
}

UINT32 PxeDevice::ReadFileHigh(LPCHAR  filename,
                               FilePtr file,
                               uint32  destinationAddress,
                               uint32  cbDestinationAddress) __far
{
    // consume leading '/' chars
    LPCHAR fname = filename;
    while (fname[0] == '/') {
        fname++;
    }

    // build the message packet
    PXENV_TFTP_READ_FILE ptrf;
    memzero(&ptrf, sizeof(ptrf));
    FNameToCStr(fname, ptrf.FileName, sizeof(ptrf.FileName));
    ptrf.Status           = ~0u;
    ptrf.BufferSize       = cbDestinationAddress;
    ptrf.Buffer           = destinationAddress;
    ptrf.GatewayIpAddress = reply->giaddr;
    ptrf.ServerIpAddress  = ServerIP;

    PxeCall(TFTP_READ_FILE, &ptrf);

    if (ptrf.Status != 0) {
        printf("PXE: Loading high: %s", ptrf.FileName);
        printf("\n    Error 0x%04x on request.\n", ptrf.Status);
        return -1;
    }
    return file->Size;
}
