//////////////////////////////////////////////////////////////////////////////
//
//  PCI Structures and declarations.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

//////////////////////////////////////////////////////////////////////////////
//
//
// A PCI driver can read the complete 256 bytes of configuration
// information for any PCI device.
//      A return value of 0 means that the specified PCI bus does not exist.
//      A return value of 2, with a VendorID of PCI_INVALID_VENDORID means
//      that the PCI bus does exist, but there is no device at the specified
//      VirtualSlotNumber (PCI Device/Function number).

#define PCI_TYPE0_ADDRESSES             6
#define PCI_TYPE1_ADDRESSES             2

struct PCI_COMMON_CONFIG {
    UINT16  VendorID;                   // (ro)
    UINT16  DeviceID;                   // (ro)
    UINT16  Command;                    // Device control
    UINT16  Status;
    UINT8   RevisionID;                 // (ro)
    UINT8   ProgIf;                     // (ro)
    UINT8   SubClass;                   // (ro)
    UINT8   BaseClass;                  // (ro)
    UINT8   CacheLineSize;              // (ro+)
    UINT8   LatencyTimer;               // (ro+)
    UINT8   HeaderType;                 // (ro)
    UINT8   BIST;                       // Built in self test

    // 0x10:
    union {
        // Devices: (HeaderType & ~PCI_MULTIFUNCTION) == PCI_DEVICE_TYPE
        struct _PCI_HEADER_TYPE_0 {
            // 0x10:
            UINT32  BaseAddresses[PCI_TYPE0_ADDRESSES];
            // 0x28:
            UINT32  Reserved1; // => UINT32 CardBusCisPtr;
            // 0x2C:
            UINT16  SubVendorID;
            // 0x2E:
            UINT16  SubSystemID;
            // 0x30:
            UINT32  ROMBaseAddress;
            // 0x34:
            UINT32  Reserved2[2];
            // 0x3c:
            UINT8   InterruptLine;      //
            UINT8   InterruptPin;       // (ro)
            UINT8   MinimumGrant;       // (ro)
            UINT8   MaximumLatency;     // (ro)
            // 0x40:
        } type0;

        // Bridges: (HeaderType & ~PCI_MULTIFUNCTION) == PCI_BRIDGE_TYPE
        struct _PCI_HEADER_TYPE_1 {
            // 0x10:
            UINT32  BaseAddresses[PCI_TYPE1_ADDRESSES];
            // 0x18:
            UINT8   PrimaryBus;
            UINT8   SecondaryBus;
            UINT8   SubordinateBus;
            UINT8   SecondaryLatency;
            // 0x1c:
            UINT8   IOBase;
            UINT8   IOLimit;
            UINT16  SecondaryStatus;
            // 0x20:
            UINT16  MemoryBase;
            UINT16  MemoryLimit;
            // 0x24:
            UINT16  PrefetchBase;
            UINT16  PrefetchLimit;
            // 0x28:
            UINT32  PrefetchBaseUpper32;
            // 0x2c:
            UINT32  PrefetchLimitUpper32;
            // 0x30:
            UINT16  IOBaseUpper16;
            UINT16  IOLimitUpper16;
            // 0x34:
            UINT32  Reserved;
            // 0x38:
            UINT32  ROMBaseAddress;
            // 0x3c:
            UINT8   InterruptLine;
            UINT8   InterruptPin;
            UINT16  BridgeControl;
            // 0x40:
        } type1;
    };

    // 0x40:
    UINT8   DeviceSpecific[192];
};


#define PCI_FIXED_HDR_LENGTH                16          // Through BIST
#define PCI_COMMON_HDR_LENGTH               64          // Through union

#define PCI_MAX_BUSES                       128
#define PCI_MAX_DEVICES                     32
#define PCI_MAX_FUNCTION                    8

#define PCI_INVALID_VENDORID                0xFFFF

//
// Bit encodings for  PCI_COMMON_CONFIG.HeaderType
//

#define PCI_MULTIFUNCTION                   0x80
#define PCI_DEVICE_TYPE                     0x00
#define PCI_BRIDGE_TYPE                     0x01

//
// Bit encodings for PCI_COMMON_CONFIG.Command
//

#define PCI_ENABLE_IO_SPACE                 0x0001
#define PCI_ENABLE_MEMORY_SPACE             0x0002
#define PCI_ENABLE_BUS_MASTER               0x0004
#define PCI_ENABLE_SPECIAL_CYCLES           0x0008
#define PCI_ENABLE_WRITE_AND_INVALIDATE     0x0010
#define PCI_ENABLE_VGA_COMPATIBLE_PALETTE   0x0020
#define PCI_ENABLE_PARITY                   0x0040  // (ro+)
#define PCI_ENABLE_WAIT_CYCLE               0x0080  // (ro+)
#define PCI_ENABLE_SERR                     0x0100  // (ro+)
#define PCI_ENABLE_FAST_BACK_TO_BACK        0x0200  // (ro)

//
// Bit encodings for PCI_COMMON_CONFIG.Status
//

#define PCI_STATUS_FAST_BACK_TO_BACK        0x0080  // (ro)
#define PCI_STATUS_DATA_PARITY_DETECTED     0x0100
#define PCI_STATUS_DEVSEL                   0x0600  // 2 bits wide
#define PCI_STATUS_SIGNALED_TARGET_ABORT    0x0800
#define PCI_STATUS_RECEIVED_TARGET_ABORT    0x1000
#define PCI_STATUS_RECEIVED_MASTER_ABORT    0x2000
#define PCI_STATUS_SIGNALED_SYSTEM_ERROR    0x4000
#define PCI_STATUS_DETECTED_PARITY_ERROR    0x8000


// Bit encodes for PCI_COMMON_CONFIG.u.type0.BaseAddresses
//
#define PCI_BAR_MEMORY_TYPE_MASK            0x00000006  // (ro)
#define PCI_BAR_MEMORY_TYPE_32BIT           0x00000000
#define PCI_BAR_MEMORY_TYPE_64BIT           0x00000004
#define PCI_BAR_MEMORY_PREFETCHABLE         0x00000008  // (ro)
#define PCI_BAR_TYPE_MASK                   0x00000003
#define PCI_BAR_TYPE_IO_SPACE               0x00000001  // (ro)
#define PCI_BAR_TYPE_MEMORY                 0x00000000
#define PCI_BAR_ADDRESS_MASK                0xfffffff0


//
// Bit encodes for PCI_COMMON_CONFIG.u.type0.ROMBaseAddresses
//

#define PCI_ROMADDRESS_ENABLED              0x00000001
#define PCI_ROMADDRESS_MASK                 0xfffff800

//
// Reference notes for PCI configuration fields:
//
// ro   these field are read only.  changes to these fields are ignored
//
// ro+  these field are intended to be read only and should be initialized
//      by the system to their proper values.  However, driver may change
//      these settings.
//
// ---
//
//      All resources consumed by a PCI device start as uninitialized
//      under NT.  An uninitialized memory or I/O base address can be
//      determined by checking its corresponding enabled bit in the
//      PCI_COMMON_CONFIG.Command value.  An InterruptLine is uninitialized
//      if it contains the value of -1.
//

//
// Bit encodes for PCI_COMMON_CONFIG.u.type1.BridgeControl
//

#define PCI_ENABLE_BRIDGE_PARITY_ERROR        0x0001
#define PCI_ENABLE_BRIDGE_SERR                0x0002
#define PCI_ENABLE_BRIDGE_ISA                 0x0004
#define PCI_ENABLE_BRIDGE_VGA                 0x0008
#define PCI_ENABLE_BRIDGE_MASTER_ABORT_SERR   0x0020
#define PCI_ASSERT_BRIDGE_RESET               0x0040
#define PCI_ENABLE_BRIDGE_FAST_BACK_TO_BACK   0x0080

//
//  Definitions needed for Access to Hardware Type 1
//

#define PCI_ADDR_PORT     0xcf8
#define PCI_DATA_PORT     0xcfc

union PCI_CONFIG_BITS {
    struct {
        UINT32   offset     : 8;
        UINT32   function   : 3;
        UINT32   device     : 5;
        UINT32   bus        : 15;
        UINT32   enable     : 1;
    } bits;

    UINT32   value;
};

///////////////////////////////////////////////////////////////// End of File.
