//////////////////////////////////////////////////////////////////////////////
//
//  pxe.h - implementation of BootDevice class for pxe boot
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#ifndef __PXE_H__
#define __PXE_H__

#include "singldr.h"
#include "bootdevice.h"

//////////////////////////////////////////////////////////////////////////////
//
// Various declarations that should probably be private
// (unmodified from original implementation)

#define IN
#define OUT
#define INOUT

// MAC_ADDR Hardware address.
#define MAC_ADDR_LEN 16
typedef UINT8 MAC_ADDR[MAC_ADDR_LEN];

//////////////////////////////////////////////////////////////////////////////

struct PXE
{
    UINT8   Signature[4];
    UINT8   StructLength;
    UINT8   StructCksum;
    UINT8   StructRev;
    UINT8   reserved1;
    LPVOID  UndiRomID;
    LPVOID  BaseRomID;
    FPENTRY EntryPoint;
    LPVOID  EntryPointProt;
    LPVOID  StatusCallout;
    UINT8   reserved2;
};

struct PXENV
{
    UINT8   Signature[6];
    UINT16  Version;
    UINT8   Length;
    UINT8   Checksum;
    FPENTRY EntryPoint;
    UINT32  ProtOffset;
    UINT16  ProtSelector;
    UINT16  StackSeg;
    UINT16  StackSize;
    UINT16  BC_CodeSeg;
    UINT16  BC_CodeSize;
    UINT16  UndiDataSeg;
    UINT16  UndiDataSize;
    UINT16  UndiCodeSeg;
    UINT16  UndiCodeSize;
    PXE __far * PXEPtr;
};

//////////////////////////////////////////////////////////////////////////////

enum PXENV_COMMAND {
    TFTP_READ_FILE      = 0x23,
    TFTP_GET_FSIZE      = 0x25,
    UDP_OPEN            = 0x30,
    UDP_CLOSE           = 0x31,
    UDP_READ            = 0x32,
    UDP_WRITE           = 0x33,
    GET_CACHED_INFO     = 0x71
};

enum PXENV_PACKET_TYPE {
    DHCP_DISCOVER       = 1,
    DHCP_ACK            = 2,
    CACHED_REPLY        = 3
};

struct PXENV_GET_CACHED_INFO
{
    OUT     UINT16 Status;
    IN      UINT16 PacketType;
    INOUT   UINT16 BufferSize;
    INOUT   LPVOID Buffer;
    OUT     UINT16 BufferLimit;
};

struct PXENV_TFTP_GET_FSIZE
{
    OUT     UINT16  Status;
    IN      UINT32  ServerIpAddress;
    IN      UINT32  GatewayIpAddress;
    IN      CHAR    FileName[128];
    OUT     UINT32  FileSize;
};

struct PXENV_TFTP_READ_FILE
{
    OUT     UINT16  Status;
    IN      CHAR    FileName[128];
    IN      UINT32  BufferSize;
    IN      ADDR32  Buffer;
    IN      UINT32  ServerIpAddress;
    IN      UINT32  GatewayIpAddress;
    IN      UINT32  McastIpAddress;
    IN      UINT16  TftpClntPort;
    IN      UINT16  TftpSrvPort;
    IN      UINT16  TftpOpenTimeOut;
    IN      UINT16  TftpReopenDelay;
};

struct PXENV_UDP_OPEN
{
    OUT     UINT16  Status;
    IN      UINT32  SelfAddress;
};

struct PXENV_UDP_CLOSE
{
    OUT     UINT16  Status;
};

struct PXENV_UDP_READ
{
    OUT     UINT16  Status;
    OUT     UINT32  ServerIpAddress;
    IN      UINT32  SelfIpAddress;
    OUT     UINT16  ServerPort;
    IN      UINT16  SelfPort;
    INOUT   UINT16  BufferSize;
    IN      LPVOID  Buffer;
};

struct PXENV_UDP_WRITE
{
    OUT     UINT16  Status;
    IN      UINT32  ServerIpAddress;
    IN      UINT32  GatewayIpAddress;
    IN      UINT16  SelfPort;
    IN      UINT16  ServerPort;
    IN      UINT16  BufferSize;
    IN      LPVOID  Buffer;
};

struct DHCP_OPTIONS {
    UINT8   Code;
    UINT8   Length;
    UINT8   Data[1];
};

//////////////////////////////////////////////////////////////////////////////

struct DHCP_PACKET {
    UINT8   op;
    UINT8   htype;
    UINT8   hlen;
    UINT8   hops;
    UINT32  xid;
    UINT16  secs;
    UINT16  flags;  // UNUSED for BOOTP
    UINT32  ciaddr;
    UINT32  yiaddr;
    UINT32  siaddr;
    UINT32  giaddr;
    UINT8   chaddr[16];
    CHAR    sname[64];
    CHAR    file[128];
    UINT8   cookie[4];
    DHCP_OPTIONS options;
};

/*
* DHCP Standard Options.
*/
#define DHCP_OPTION_PAD                      0
#define DHCP_OPTION_SUBNET_MASK              1
#define DHCP_OPTION_TIME_OFFSET              2
#define DHCP_OPTION_ROUTER_ADDRESS           3
#define DHCP_OPTION_TIME_SERVERS             4
#define DHCP_OPTION_IEN116_NAME_SERVERS      5
#define DHCP_OPTION_DOMAIN_NAME_SERVERS      6
#define DHCP_OPTION_LOG_SERVERS              7
#define DHCP_OPTION_COOKIE_SERVERS           8
#define DHCP_OPTION_LPR_SERVERS              9
#define DHCP_OPTION_IMPRESS_SERVERS          10
#define DHCP_OPTION_RLP_SERVERS              11
#define DHCP_OPTION_HOST_NAME                12
#define DHCP_OPTION_BOOT_FILE_SIZE           13
#define DHCP_OPTION_MERIT_DUMP_FILE          14
#define DHCP_OPTION_DOMAIN_NAME              15
#define DHCP_OPTION_SWAP_SERVER              16
#define DHCP_OPTION_ROOT_DISK                17
#define DHCP_OPTION_EXTENSIONS_PATH          18

/*
* IP Layer Parameters - per host
*/
#define DHCP_OPTION_BE_A_ROUTER              19
#define DHCP_OPTION_NON_LOCAL_SOURCE_ROUTING 20
#define DHCP_OPTION_POLICY_FILTER_FOR_NLSR   21
#define DHCP_OPTION_MAX_REASSEMBLY_SIZE      22
#define DHCP_OPTION_DEFAULT_TTL              23
#define DHCP_OPTION_PMTU_AGING_TIMEOUT       24
#define DHCP_OPTION_PMTU_PLATEAU_TABLE       25

/*
* IP Layer Parameters - per interface.
*/
#define DHCP_OPTION_MTU                      26
#define DHCP_OPTION_ALL_SUBNETS_MTU          27
#define DHCP_OPTION_BROADCAST_ADDRESS        28
#define DHCP_OPTION_PERFORM_MASK_DISCOVERY   29
#define DHCP_OPTION_BE_A_MASK_SUPPLIER       30
#define DHCP_OPTION_PERFORM_ROUTER_DISCOVERY 31
#define DHCP_OPTION_ROUTER_SOLICITATION_ADDR 32
#define DHCP_OPTION_STATIC_ROUTES            33
#define DHCP_OPTION_TRAILERS                 34
#define DHCP_OPTION_ARP_CACHE_TIMEOUT        35
#define DHCP_OPTION_ETHERNET_ENCAPSULATION   36

/*
* TCP Parameters - per host
*/
#define DHCP_OPTION_TTL                      37
#define DHCP_OPTION_KEEP_ALIVE_INTERVAL      38
#define DHCP_OPTION_KEEP_ALIVE_DATA_SIZE     39

/*
* Application Layer Parameters
*/
#define DHCP_OPTION_NETWORK_INFO_SERVICE_DOM 40
#define DHCP_OPTION_NETWORK_INFO_SERVERS     41
#define DHCP_OPTION_NETWORK_TIME_SERVERS     42

/*
* Vender Specific Information Option
*/
#define DHCP_OPTION_VENDOR_SPEC_INFO         43

/*
* NetBIOS Over TCP/IP Name Server Options
*/
#define DHCP_OPTION_NETBIOS_NAME_SERVER      44
#define DHCP_OPTION_NETBIOS_DATAGRAM_SERVER  45
#define DHCP_OPTION_NETBIOS_NODE_TYPE        46
#define DHCP_OPTION_NETBIOS_SCOPE_OPTION     47

/*
* X Window System Options
*/
#define DHCP_OPTION_XWINDOW_FONT_SERVER      48
#define DHCP_OPTION_XWINDOW_DISPLAY_MANAGER  49

/*
* DHCP Extensions
*/
#define DHCP_OPTION_REQUESTED_ADDRESS        50
#define DHCP_OPTION_LEASE_TIME               51
#define DHCP_OPTION_OK_TO_OVERLAY            52
#define DHCP_OPTION_MESSAGE_TYPE             53
#define DHCP_OPTION_SERVER_IDENTIFIER        54
#define DHCP_OPTION_PARAMETER_REQUEST_LIST   55
#define DHCP_OPTION_MESSAGE                  56
#define DHCP_OPTION_MESSAGE_LENGTH           57
#define DHCP_OPTION_RENEWAL_TIME             58      /* T1 */
#define DHCP_OPTION_REBIND_TIME              59      /* T2 */
#define DHCP_OPTION_CLIENT_CLASS_INFO        60
#define DHCP_OPTION_CLIENT_ID                61

/*
* More Application Layer Parameters
*/
#define DHCP_OPTION_NIS_PLUS_DOM             64
#define DHCP_OPTION_NIS_PLUS                 65

/*
* Overlayed Header Field Replacements
*/
#define DHCP_OPTION_TFTP_SERVER_NAME         66
#define DHCP_OPTION_TFTP_BOOTFILE_NAME       67

/*
* Even More Application Layer Parameters
*/
#define DHCP_OPTION_MOBILE_IP_HOME_AGENTS    68
#define DHCP_OPTION_SMTP_SERVERS             69
#define DHCP_OPTION_POP_SERVERS              70
#define DHCP_OPTION_NNTP_SERVERS             71
#define DHCP_OPTION_WWW_SERVERS              72
#define DHCP_OPTION_FINGER_SERVERS           73
#define DHCP_OPTION_IRC_SERVERS              74
#define DHCP_OPTION_STREETTALK_SERVERS       75
#define DHCP_OPTION_STREETTALK_DIR_SERVERS   76

/*
* Option codes from 77 to 127 are reserved through the
* Internet Assigned Numbers Authority (iana@isi.edu).
*/

/*
* PXE Parameters
*/
#define DHCP_OPTION_PXE_CLIENT_ARCH_ID       93
#define DHCP_OPTION_PXE_CLIENT_NIC_ID        94
#define DHCP_OPTION_PXE_CLIENT_ID            97


/*
* Option codes from 128 to 254 are for site-specific options.
*/
#define DHCP_OPTION_END                      255

//////////////////////////////////////////////////////////////////////////////
//
// static info from original implementation

static FPENTRY s_fpPxe = NULL;
static FPENTRY s_fpPxenv = NULL;

//////////////////////////////////////////////////////////////////////////////
//
// Declaration for class PxeDevice

struct __near PxeDevice : BootDevice
{
private:

    PXE __far *pxe;          // on pxe boot, these two pointers are given
    PXENV __far * pxenv;     // to SINGLDR as params to BootPhase1

    DHCP_PACKET __far *reply;

    uint32 ServerIP;

public:
    // constructor
    PxeDevice(PXE __far *pxePtr, PXENV __far * pxenvPtr) __far
    {
        // set private fields based on params
        this->pxe = pxePtr;
        this->pxenv = pxenvPtr;
    }

    // implement the virtual functions from the base class
    int __near OpenDevice() __far;

    int __near CloseDevice() __far;

    int __near GetFileProperties(LPCHAR  filename,
                                 FilePtr file,
                                 FilePtr directory) __far;

    INT16 __near ReadFileLow(LPCHAR        filename,
                             FilePtr       file,
                             uint8 __far * buffer) __far;

    UINT32 __near ReadFileHigh(LPCHAR  filename,
                               FilePtr file,
                               uint32  pBuffer,
                               uint32  cbBuffer) __far;
};

#endif
