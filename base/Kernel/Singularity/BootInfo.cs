//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Microsoft.Singularity
{
    [StructLayout(LayoutKind.Sequential, Pack=4)]
    [CLSCompliant(false)]
    public struct BootInfo
    {
        //
        // These constants control the gross layout of physical memory.
        //
        // Physical Addresses
        // 00000000..000fffff    Reserved for boot loaders
        // 00007000..00017fff    16-bit segment
        // 00007b00..0000efff    16-bit real-mode code
        // 0000f000..00017fff    16-bit stack
        // 00018000..0005ffff    boot heap
        // 00060000..000affff    32-bit boot loader code (undump)
        //
        // 00100000..001fffff    Reserved for ? (1MB..)
        //
        // 00200000..002fffff    Kernel stack   (1MB..2MB)
        // 00310000..004fffff    Kernel code & static Data (2MB..4MB...)
        //
        // 00500000..??          Mp ABI stub (haryadi)
        //
        // 04000000..07ffffff    Runtime heaps preferred memory (64MB..128MB)
        //
        // top-size..top ram     minidump load.
        //
        // fe000000..ffffffff    Uncached mapped memory

        // Memory Layout Constants
        [AccessedByRuntime("referenced in c++")]
        // 4KB is currently the only supported size
        internal const uint PAGE_SIZE           = 0x00001000; // 4KB

        // Make sure zero through this address is always unmapped
        [AccessedByRuntime("referenced in c++")]
        internal const uint PHYSICAL_DISABLED   = 0x00004000;

        [AccessedByRuntime("referenced in c++")]
        internal const uint REAL_CODE_BASE      = 0x00007b00;
        [AccessedByRuntime("referenced in c++")]
        internal const uint REAL_STACK          = 0x00017FF0;
        [AccessedByRuntime("referenced in c++")]
        internal const uint REAL_PBOOTINFO      = 0x00017FF8;
        [AccessedByRuntime("referenced in c++")]
        internal const uint REAL_HEAP           = 0x00018000;

        [AccessedByRuntime("referenced in c++")]
        internal const uint DUMP_CODE_BASE      = 0x00060000;

        // Addresses for hypervisor overlay pages
        [AccessedByRuntime("referenced in c++")]
        internal const uint HYPERCALL_PAGE      = 0x00100000;
        [AccessedByRuntime("referenced in c++")]
        internal const uint HYPERVISOR_SIM_PAGE = 0x00101000;
        [AccessedByRuntime("referenced in c++")]
        internal const uint HYPERVISOR_SIEF_PAGE= 0x00102000;

        [AccessedByRuntime("referenced in c++")]
        internal const uint KERNEL_LOGREC_BEGIN = 0x00200000;
        [AccessedByRuntime("referenced in c++")]
        internal const uint KERNEL_LOGREC_LIMIT = 0x00220000;
        [AccessedByRuntime("referenced in c++")]
        internal const uint KERNEL_LOGTXT_BEGIN = 0x00220000;
        [AccessedByRuntime("referenced in c++")]
        internal const uint KERNEL_LOGTXT_LIMIT = 0x00240000;

        [AccessedByRuntime("referenced in c++")]
        internal const uint KERNEL_STACK_BEGIN  = 0x00240000;
        [AccessedByRuntime("referenced in c++")]
        internal const uint KERNEL_STACK        = 0x002ff000;
        [AccessedByRuntime("referenced in c++")]
        internal const uint KERNEL_STACK_LIMIT  = 0x00300000;

        [AccessedByRuntime("referenced in c++")]
        internal const uint KERNEL_BASE         = 0x00310000;

        // haryadi -- this is the ABI Stub (MpSyscalls.x86)
        [AccessedByRuntime("referenced in c++")]
        internal const uint MP_ABI_BASE         = 0x00600000;

        // The physical address and extent of the high-memory
        // range to map into the Kernel space, and mark
        // "uncached". This window is for communicating with
        // hardware.
        [AccessedByRuntime("referenced in c++")]
        internal const uint UNCACHED_PHYSICAL   = 0xFE000000;
        [AccessedByRuntime("referenced in c++")]
        internal const uint UNCACHED_MAPPED     = 0x02000000;

        // This is the amount of *contiguous, physical* memory
        // to reserve at boot for use as I/O memory
        internal const uint IO_MEMORY_SIZE      = 0x00800000; // 8MB

        //
        // These constants control the gross layout of *virtual*
        // memory
        //

        // This is the maximum size of a communication-heap
        // (used to reserve enough space in virtual address
        // spaces to map in communication heaps as necessary)
        // For ease of mapping, this should be a multiple of 2MB
        internal const uint COMM_HEAP_MAX_SIZE  = 0x02000000; // 32MB

        // This determines where the kernel/user boundary is.
        // Currently, it needs to be multiple of 1GB.
        internal const uint KERNEL_BOUNDARY     = 0x40000000; // 1GB

        // This determines the maximum virtual address
        // we will use. Setting this to less than the
        // machine's maximum pointer size can reduce the
        // overhead of paging structures.
        //
        // NOTE we are not currently safe to use the top
        // bit of addresses (because of the "mark" bit in the
        // multi-use word header), so restrict ourselves to the
        // bottom 2GB.
        [AccessedByRuntime("referenced in c++")]
        internal const uint MAX_VIRTUAL_ADDR    = 0x80000000; // 2GB

        // Exit Codes:
        [AccessedByRuntime("referenced in c++")]
        internal const int EXIT_AND_RESTART     = 0x1fff;
        [AccessedByRuntime("referenced in c++")]
        internal const int EXIT_AND_SHUTDOWN    = 0x1ffe;
        [AccessedByRuntime("referenced in c++")]
        internal const int EXIT_AND_WARMBOOT    = 0x1ffd;
        [AccessedByRuntime("referenced in c++")]
        internal const int EXIT_AND_HALT        = 0x1ffc;

        // Sanity Check
        [AccessedByRuntime("referenced in c++")]
        internal uint       RecSize;

        // IDT and PIC
        [AccessedByRuntime("referenced in c++")]
        internal X86.IDTP   BiosIdtPtr;
        [AccessedByRuntime("referenced in c++")]
        internal ushort     BiosPicMask;
        [AccessedByRuntime("referenced in c++")]
        internal byte       BiosWarmResetCmos;
        [AccessedByRuntime("referenced in c++")]
        internal uint       BiosWarmResetVector;
        [AccessedByRuntime("referenced in c++")]
        internal uint       Info16;

        // Debug Stub Information
        [AccessedByRuntime("referenced in c++")]
        internal ushort     DebugBasePort;

        // Temporary IDT
        [AccessedByRuntime("referenced in c++")]
        internal ulong    IdtEnter0;
        [AccessedByRuntime("referenced in c++")]
        internal ulong    IdtEnter1;
        [AccessedByRuntime("referenced in c++")]
        internal ulong    IdtEnterN;
        [AccessedByRuntime("referenced in c++")]
        internal ulong    IdtTarget;

        // Self-descriptive information
        [AccessedByRuntime("referenced in c++")]
        internal ulong    Info32;
        [AccessedByRuntime("referenced in c++")]
        internal ulong    Kill32;
        [AccessedByRuntime("referenced in c++")]
        internal uint       KillAction;

        // MP specific variables
        [AccessedByRuntime("referenced in c++")]
        public   ulong    MpEnter32;          // Entry point
        [AccessedByRuntime("referenced in c++")]
        public   uint       MpCpuCount;         // No of AP's booted
        [AccessedByRuntime("referenced in c++")]
        public   uint       MpStatus32;         // Error indicator
        [AccessedByRuntime("referenced in c++")]
        public   ulong    MpStartupLock32;    // Pointer to MP init lock var
        [AccessedByRuntime("referenced in c++")]
        public   ulong    MpBootInfo32;       // Pointer to MpBootInfo

        // Per CPU state - in initialization order (CPU0 = BSP, CPUX = AP)
        [AccessedByRuntime("referenced in c++")]
        public   CpuInfo    Cpu0;
#if SINGULARITY_MP
        [AccessedByRuntime("referenced in c++")]
        public   CpuInfo    Cpu1;
        [AccessedByRuntime("referenced in c++")]
        public   CpuInfo    Cpu2;
        [AccessedByRuntime("referenced in c++")]
        public   CpuInfo    Cpu3;
#  if !MAX_CPU2 && !MAX_CPU3 && !MAX_CPU4
#    error "MAX_CPUx value is either not defined or not supported."
#  endif // MAX_CPUx
#endif // SINGULARITY_MP

        [AccessedByRuntime("referenced in c++")]
        internal ulong Pdpt32;

        [AccessedByRuntime("referenced in c++")]
        internal ulong    Undump;

        // Location (in high memory) of the executable
        // images
        [AccessedByRuntime("referenced in c++")]
        internal ulong    DumpAddr32;

        // Extent of that data
        [AccessedByRuntime("referenced in c++")]
        internal uint       DumpSize32;

        [AccessedByRuntime("referenced in c++")]
        internal ulong    DumpRemainder;

        // Start of the undumped kernel image
        [AccessedByRuntime("referenced in c++")]
        internal ulong    DumpBase;

        // Marks the highest address used by the
        // kernel image (undumped from high memory)
        [AccessedByRuntime("referenced in c++")]
        internal ulong    DumpLimit;

        //
        [AccessedByRuntime("referenced in c++")]
        internal ulong    Heap32;

        // PCI Information (V2.0+)
        [AccessedByRuntime("referenced in c++")]
        internal uint       PciBiosAX;
        [AccessedByRuntime("referenced in c++")]
        internal uint       PciBiosBX;
        [AccessedByRuntime("referenced in c++")]
        internal uint       PciBiosCX;
        [AccessedByRuntime("referenced in c++")]
        internal uint       PciBiosEDX;

        // BIOS Information
        [AccessedByRuntime("referenced in c++")]
        public   ulong    AcpiRoot32;
        [AccessedByRuntime("referenced in c++")]
        internal ulong    PnpNodesAddr32;
        [AccessedByRuntime("referenced in c++")]
        internal uint       PnpNodesSize32;
        [AccessedByRuntime("referenced in c++")]
        internal ulong    SmbiosRoot32;
        [AccessedByRuntime("referenced in c++")]
        internal ulong    DmiRoot32;
        [AccessedByRuntime("referenced in c++")]
        internal uint       IsaCsns;
        [AccessedByRuntime("referenced in c++")]
        internal ushort     IsaReadPort;
        [AccessedByRuntime("referenced in c++")]
        internal uint       Ebda32;
        [AccessedByRuntime("referenced in c++")]
        public   uint       MpFloat32;

        // SMAP Information
        [AccessedByRuntime("referenced in c++")]
        internal uint       SmapCount;
        [AccessedByRuntime("referenced in c++")]
        internal ulong    SmapData32;

        // 1394 Information
        [AccessedByRuntime("referenced in c++")]
        internal ulong    Ohci1394Base;
        [AccessedByRuntime("referenced in c++")]
        internal ulong    Ohci1394BufferAddr32;
        [AccessedByRuntime("referenced in c++")]
        internal uint       Ohci1394BufferSize32;

        // File image table
        [AccessedByRuntime("referenced in c++")]
        internal ulong    FileImageTableBase32;
        [AccessedByRuntime("referenced in c++")]
        internal uint       FileImageTableEntries;

        // BOOT Information
        [AccessedByRuntime("referenced in c++")]
        internal ulong    CmdLine32;
        [AccessedByRuntime("referenced in c++")]
        internal uint       BootCount;

        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(8)]
        [NoHeapAllocation]
        internal static unsafe extern BootInfo * HalGetBootInfo();

        [NoHeapAllocation]
        public static unsafe BootInfo GetBootInfo()
        {
            BootInfo *ptr;

            ptr = HalGetBootInfo();
            return *ptr;
        }

        [NoHeapAllocation]
        public unsafe CpuInfo GetCpuInfo(int processorId)
        {
            fixed (CpuInfo* ci = &Cpu0)
            {
                return *(ci + processorId);
            }
        }
    }
}
