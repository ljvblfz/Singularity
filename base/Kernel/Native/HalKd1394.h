//////////////////////////////////////////////////////////////////////////////
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// kd1394.h - 1394 Kernel Debugger DLL

//
// Various OHCI definitions
//
#define PHY_INITIATE_BUS_RESET              0x40        // IBR @ Address 1

///////////////////////////////////////////////////////// Register Structures.
//
union VERSION_REGISTER {
    struct {
        ULONG       Revision:8;             // bits 0-7
        ULONG       Reserved:8;             // bits 8-15
        ULONG       Version:8;              // bits 16-23
        ULONG       GUID_ROM:1;             // bit  24
        ULONG       Reserved1:7;            // bits 25-31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(VERSION_REGISTER) == 4);

union VENDOR_ID_REGISTER {
    struct {
        ULONG       VendorCompanyId:24;     // bits 0-23
        ULONG       VendorUnique:8;         // bits 24-31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(VENDOR_ID_REGISTER) == 4);

union GUID_ROM_REGISTER {
    struct {
        ULONG       Reserved0:16;           // bits 0-15
        ULONG       RdData:8;               // bits 16-23
        ULONG       Reserved1:1;            // bit  24
        ULONG       RdStart:1;              // bit  25
        ULONG       Reserved2:5;            // bits 26-30
        ULONG       AddrReset:1;            // bits 31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(GUID_ROM_REGISTER) == 4);

union AT_RETRIES_REGISTER {
    struct {
        ULONG       MaxATReqRetries:4;      // bits 0-3
        ULONG       MaxATRespRetries:4;     // bits 4-7
        ULONG       MaxPhysRespRetries:4;   // bits 8-11
        ULONG       Reserved:4;             // bits 12-15
        ULONG       CycleLimit:13;          // bits 16-28
        ULONG       SecondLimit:3;          // bits 29-31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(AT_RETRIES_REGISTER) == 4);

union CSR_CONTROL_REGISTER {
    struct {
        ULONG       CsrSel:2;               // bits 0-1
        ULONG       Reserved:29;            // bits 2-30
        ULONG       CsrDone:1;              // bit  31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(CSR_CONTROL_REGISTER) == 4);

union CONFIG_ROM_HEADER_REGISTER {
    struct {
        ULONG       Rom_crc_value:16;       // bits 0-15
        ULONG       Crc_length:8;           // bits 16-23
        ULONG       Info_length:8;          // bits 24-31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(CONFIG_ROM_HEADER_REGISTER) == 4);

union BUS_OPTIONS_REGISTER {
    struct {
        ULONG       Link_spd:3;             // bits 0-2
        ULONG       Reserved0:3;            // bits 3-5
        ULONG       g:2;                    // bits 6-7
        ULONG       Reserved1:4;            // bits 8-11
        ULONG       Max_rec:4;              // bits 12-15
        ULONG       Cyc_clk_acc:8;          // bits 16-23
        ULONG       Reserved2:3;            // bits 24-26
        ULONG       Pmc:1;                  // bit  27
        ULONG       Bmc:1;                  // bit  28
        ULONG       Isc:1;                  // bit  29
        ULONG       Cmc:1;                  // bit  30
        ULONG       Irmc:1;                 // bit  31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(BUS_OPTIONS_REGISTER) == 4);

union HC_CONTROL_REGISTER {
    struct {
        ULONG       Reserved:16;            // bits 0-15
        ULONG       SoftReset:1;            // bit  16
        ULONG       LinkEnable:1;           // bit  17
        ULONG       PostedWriteEnable:1;    // bit  18
        ULONG       Lps:1;                  // bit  19
        ULONG       Reserved2:2;            // bits 20-21
        ULONG       APhyEnhanceEnable:1;    // bit  22
        ULONG       ProgramPhyEnable:1;     // bit  23
        ULONG       Reserved3:6;            // bits 24-29
        ULONG       NoByteSwapData:1;       // bit  30
        ULONG       Reserved4:1;            // bit  31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(HC_CONTROL_REGISTER) == 4);

union FAIRNESS_CONTROL_REGISTER {
    struct {
        ULONG       Pri_req:8;              // bits 0-7
        ULONG       Reserved0:24;           // bits 8-31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(FAIRNESS_CONTROL_REGISTER) == 4);

union LINK_CONTROL_REGISTER {
    struct {
        ULONG       Reserved0:4;            // bits 0-3
        ULONG       CycleSyncLReqEnable:1;  // bit  4
        ULONG       Reserved1:4;            // bits 5-8
        ULONG       RcvSelfId:1;            // bit  9
        ULONG       RcvPhyPkt:1;            // bit  10
        ULONG       Reserved2:9;            // bits 11-19
        ULONG       CycleTimerEnable:1;     // bit  20
        ULONG       CycleMaster:1;          // bit  21
        ULONG       CycleSource:1;          // bit  22
        ULONG       Reserved3:9;            // bits 23-31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(LINK_CONTROL_REGISTER) == 4);

union NODE_ID_REGISTER {
    struct {
        ULONG       NodeId:6;               // bits 0-5
        ULONG       BusId:10;               // bits 6-15
        ULONG       Reserved1:11;           // bits 16-26
        ULONG       Cps:1;                  // bit  27
        ULONG       Reserved2:2;            // bits 28-29
        ULONG       Root:1;                 // bit  30
        ULONG       IdValid:1;              // bit  31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(NODE_ID_REGISTER) == 4);

union SELF_ID_BUFFER_REGISTER {
    ULONG SelfIdBufferPointer;
    struct {
        ULONG       Reserved0:11;           // bits 0-10
        ULONG       SelfIdBuffer:21;        // bits 11-32
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(SELF_ID_BUFFER_REGISTER) == 4);

union SELF_ID_COUNT_REGISTER {
    struct {
        ULONG       Reserved0:2;            // bits 0-1
        ULONG       SelfIdSize:11;          // bits 2-12
        ULONG       Reserved1:3;            // bits 13-15
        ULONG       SelfIdGeneration:8;     // bits 16-23
        ULONG       Reserved2:7;            // bits 24-30
        ULONG       SelfIdError:1;          // bit  31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(SELF_ID_COUNT_REGISTER) == 4);

union PHY_CONTROL_REGISTER {
    struct {
        ULONG       WrData:8;               // bits 0-7
        ULONG       RegAddr:4;              // bits 8-11
        ULONG       Reserved0:2;            // bits 12-13
        ULONG       WrReg:1;                // bit  14
        ULONG       RdReg:1;                // bit  15
        ULONG       RdData:8;               // bits 16-23
        ULONG       RdAddr:4;               // bits 24-27
        ULONG       Reserved1:3;            // bits 28-30
        ULONG       RdDone:1;               // bit  31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(PHY_CONTROL_REGISTER) == 4);

union ISOCH_CYCLE_TIMER_REGISTER {
    struct {
        ULONG       CycleOffset:12;         // bits 0-11
        ULONG       CycleCount:13;          // bits 12-24
        ULONG       CycleSeconds:7;         // bits 25-31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(ISOCH_CYCLE_TIMER_REGISTER) == 4);

union INT_EVENT_MASK_REGISTER {
    struct {
        ULONG       ReqTxComplete:1;        // bit  0
        ULONG       RspTxComplete:1;        // bit  1
        ULONG       ARRQ:1;                 // bit  2
        ULONG       ARRS:1;                 // bit  3
        ULONG       RQPkt:1;                // bit  4
        ULONG       RSPPkt:1;               // bit  5
        ULONG       IsochTx:1;              // bit  6
        ULONG       IsochRx:1;              // bit  7
        ULONG       PostedWriteErr:1;       // bit  8
        ULONG       LockRespErr:1;          // bit  9
        ULONG       Reserved0:6;            // bits 10-15
        ULONG       SelfIdComplete:1;       // bit  16
        ULONG       BusReset:1;             // bit  17
        ULONG       Reserved1:1;            // bit  18
        ULONG       Phy:1;                  // bit  19
        ULONG       CycleSynch:1;           // bit  20
        ULONG       Cycle64Secs:1;          // bit  21
        ULONG       CycleLost:1;            // bit  22
        ULONG       CycleInconsistent:1;    // bit  23
        ULONG       UnrecoverableError:1;   // bit  24
        ULONG       CycleTooLong:1;         // bit  25
        ULONG       PhyRegRcvd:1;           // bit  26
        ULONG       Reserved2:3;            // bits 27-29
        ULONG       VendorSpecific:1;       // bit  30
        ULONG       MasterIntEnable:1;      // bit  31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(INT_EVENT_MASK_REGISTER) == 4);

union COMMAND_POINTER_REGISTER {
    struct {
        ULONG       Z:4;                    // bits 0-3
        ULONG       DescriptorAddr:28;      // bits 4-31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(COMMAND_POINTER_REGISTER) == 4);

union CONTEXT_CONTROL_REGISTER {
    struct {
        ULONG       EventCode:5;            // bits 0-4
        ULONG       Spd:3;                  // bits 5-7
        ULONG       Reserved0:2;            // bits 8-9
        ULONG       Active:1;               // bit  10
        ULONG       Dead:1;                 // bit  11
        ULONG       Wake:1;                 // bit  12
        ULONG       Reserved1:2;            // bits 13-14
        ULONG       Run:1;                  // bit  15
        ULONG       Reserved2:16;           // bits 16-31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(CONTEXT_CONTROL_REGISTER) == 4);

union IT_CONTEXT_CONTROL_REGISTER {
    struct {
        ULONG       EventCode:5;            // bits 0-4
        ULONG       Spd:3;                  // bits 5-7
        ULONG       Reserved0:2;            // bits 8-9
        ULONG       Active:1;               // bit  10
        ULONG       Dead:1;                 // bit  11
        ULONG       Wake:1;                 // bit  12
        ULONG       Reserved1:2;            // bits 13-14
        ULONG       Run:1;                  // bit  15
        ULONG       CycleMatch:15;          // bits 16-30
        ULONG       CycleMatchEnable:1;     // bit  31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(IT_CONTEXT_CONTROL_REGISTER) == 4);

union IR_CONTEXT_CONTROL_REGISTER {
    struct {
        ULONG       EventCode:5;            // bits 0-4
        ULONG       Spd:3;                  // bits 5-7
        ULONG       Reserved0:2;            // bits 8-9
        ULONG       Active:1;               // bit  10
        ULONG       Dead:1;                 // bit  11
        ULONG       Wake:1;                 // bit  12
        ULONG       Reserved1:2;            // bits 13-14
        ULONG       Run:1;                  // bit  15
        ULONG       CycleMatch:12;          // bits 16-27
        ULONG       MultiChanMode:1;        // bit  28
        ULONG       CycleMatchEnable:1;     // bit  29
        ULONG       IsochHeader:1;          // bit  30
        ULONG       BufferFill:1;           // bit  31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(IR_CONTEXT_CONTROL_REGISTER) == 4);

union CONTEXT_MATCH_REGISTER {
    struct {
        ULONG       ChannelNumber:6;        // bits 0-5
        ULONG       Reserved:1;             // bit  6
        ULONG       Tag1SyncFilter:1;       // bit  7
        ULONG       Sync:4;                 // bits 8-11
        ULONG       CycleMatch:13;          // bits 12-24
        ULONG       Reserved1:3;            // bits 25-27
        ULONG       Tag:4;                  // bit  28-31
    };
    ULONG all;
};
STATIC_ASSERT(sizeof(CONTEXT_MATCH_REGISTER) == 4);

/////////////////////////////////////////////////////////////// Register Sets.
//
struct DMA_CONTEXT_REGISTERS {
    CONTEXT_CONTROL_REGISTER    ContextControlSet;
    CONTEXT_CONTROL_REGISTER    ContextControlClear;
    ULONG                       Reserved0[1];
    COMMAND_POINTER_REGISTER    CommandPtr;
    ULONG                       Reserved1[4];
};

struct DMA_ISOCH_RCV_CONTEXT_REGISTERS {
    IR_CONTEXT_CONTROL_REGISTER ContextControlSet;
    IR_CONTEXT_CONTROL_REGISTER ContextControlClear;
    ULONG                       Reserved0[1];
    COMMAND_POINTER_REGISTER    CommandPtr;
    CONTEXT_MATCH_REGISTER      ContextMatch;
    ULONG                       Reserved1[3];
};

struct DMA_ISOCH_XMIT_CONTEXT_REGISTERS {
    IT_CONTEXT_CONTROL_REGISTER ContextControlSet;
    IT_CONTEXT_CONTROL_REGISTER ContextControlClear;
    ULONG                       Reserved0[1];
    COMMAND_POINTER_REGISTER    CommandPtr;
};

struct OHCI_REGISTER_MAP {
    VERSION_REGISTER            Version;                // @ 0
    GUID_ROM_REGISTER           GUID_ROM;               // @ 4
    AT_RETRIES_REGISTER         ATRetries;              // @ 8
    ULONG                       CsrData;                // @ C
    ULONG                       CsrCompare;             // @ 10
    CSR_CONTROL_REGISTER        CsrControl;             // @ 14
    CONFIG_ROM_HEADER_REGISTER  ConfigRomHeader;        // @ 18
    ULONG                       BusId;                  // @ 1C
    BUS_OPTIONS_REGISTER        BusOptions;             // @ 20
    ULONG                       GuidHi;                 // @ 24
    ULONG                       GuidLo;                 // @ 28
    ULONG                       Reserved0[2];           // @ 2C
    ULONG                       ConfigRomMap;           // @ 34

    ULONG                       PostedWriteAddressLo;   // @ 38
    ULONG                       PostedWriteAddressHi;   // @ 3C

    VENDOR_ID_REGISTER          VendorId;               // @ 40
    ULONG                       Reserved1[3];           // @ 44

    HC_CONTROL_REGISTER         HCControlSet;           // @ 50
    HC_CONTROL_REGISTER         HCControlClear;         // @ 54

    ULONG                       Reserved2[3];           // @ 58

    SELF_ID_BUFFER_REGISTER     SelfIdBufferPtr;        // @ 64
    SELF_ID_COUNT_REGISTER      SelfIdCount;            // @ 68

    ULONG                       Reserved3[1];           // @ 6C

    ULONG                       IRChannelMaskHiSet;     // @ 70
    ULONG                       IRChannelMaskHiClear;   // @ 74
    ULONG                       IRChannelMaskLoSet;     // @ 78
    ULONG                       IRChannelMaskLoClear;   // @ 7C

    INT_EVENT_MASK_REGISTER     IntEventSet;            // @ 80
    INT_EVENT_MASK_REGISTER     IntEventClear;          // @ 84

    INT_EVENT_MASK_REGISTER     IntMaskSet;             // @ 88
    INT_EVENT_MASK_REGISTER     IntMaskClear;           // @ 8C

    ULONG                       IsoXmitIntEventSet;     // @ 90
    ULONG                       IsoXmitIntEventClear;   // @ 94

    ULONG                       IsoXmitIntMaskSet;      // @ 98
    ULONG                       IsoXmitIntMaskClear;    // @ 9C

    ULONG                       IsoRecvIntEventSet;     // @ A0
    ULONG                       IsoRecvIntEventClear;   // @ A4

    ULONG                       IsoRecvIntMaskSet;      // @ A8
    ULONG                       IsoRecvIntMaskClear;    // @ AC

    ULONG                       Reserved4[11];          // @ B0

    FAIRNESS_CONTROL_REGISTER   FairnessControl;        // @ DC

    LINK_CONTROL_REGISTER       LinkControlSet;         // @ E0
    LINK_CONTROL_REGISTER       LinkControlClear;       // @ E4

    NODE_ID_REGISTER            NodeId;                 // @ E8
    PHY_CONTROL_REGISTER        PhyControl;             // @ EC

    ISOCH_CYCLE_TIMER_REGISTER  IsochCycleTimer;        // @ F0

    ULONG                       Reserved5[3];           // @ F4

    ULONG                       AsynchReqFilterHiSet;   // @ 100
    ULONG                       AsynchReqFilterHiClear; // @ 104

    ULONG                       AsynchReqFilterLoSet;   // @ 108
    ULONG                       AsynchReqFilterLoClear; // @ 10C

    ULONG                       PhyReqFilterHiSet;      // @ 110
    ULONG                       PhyReqFilterHiClear;    // @ 114

    ULONG                       PhyReqFilterLoSet;      // @ 118
    ULONG                       PhyReqFilterLoClear;    // @ 11C

    ULONG                       PhysicalUpperBound;     // @ 120
    ULONG                       Reserved6[23];          // @ 124

    DMA_CONTEXT_REGISTERS       AsynchContext[4];       // @ 180
    // ATRsp_Context;   // @ 1A0
    // ARReq_Context;   // @ 1C0
    // ARRsp_Context;   // @ 1E0

    DMA_ISOCH_XMIT_CONTEXT_REGISTERS IT_Context[32];    // @ 200

    DMA_ISOCH_RCV_CONTEXT_REGISTERS IR_Context[32];     // @ 400
};
STATIC_ASSERT(sizeof(OHCI_REGISTER_MAP) == 2048);

//
// IEEE 1212 Configuration Rom header definition
//
union CONFIG_ROM_INFO {
    struct {
        union {
            USHORT          CRI_CRC_Value:16;
            struct {
                UCHAR       CRI_Saved_Info_Length;
                UCHAR       CRI_Saved_CRC_Length;
            } Saved;
        };
        UCHAR               CRI_CRC_Length;
        UCHAR               CRI_Info_Length;
    };
    ULONG   all;
};
STATIC_ASSERT(sizeof(CONFIG_ROM_INFO) == 4);

//
// IEEE 1212 Immediate entry definition
//
union IMMEDIATE_ENTRY {
    struct {
        ULONG               IE_Value:24;
        ULONG               IE_Key:8;
    };
    ULONG   all;
};
STATIC_ASSERT(sizeof(IMMEDIATE_ENTRY) == 4);

//
// IEEE 1212 Directory definition
//
union DIRECTORY_INFO {
    struct {
        union {
            USHORT          DI_CRC;
            USHORT          DI_Saved_Length;
        };
        USHORT              DI_Length;
    };
    ULONG   all;
};
STATIC_ASSERT(sizeof(DIRECTORY_INFO) == 4);

//
///////////////////////////////////////////////////////////////// End of File.
