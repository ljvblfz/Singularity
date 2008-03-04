////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   MemoryManager.cs - Main entry points for OS memory management
//
//  Note:
//


using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.GCs;

using Microsoft.Singularity;

namespace Microsoft.Singularity.Memory
{
    [NoCCtor]
    [CLSCompliant(false)]
    public class MemoryManager {

        /////////////////////////////////////
        // STATIC FIELDS
        /////////////////////////////////////

#if PAGING
        private static PhysicalAddress IOMemoryBaseAddr;
        private static PhysicalHeap KernelIOMemoryHeap;
        private static VirtualMemoryRange_struct KernelRange;
        private static VirtualMemoryRange KernelRangeWrapper;

        [AccessedByRuntime("referenced from halkd.cpp")]
        private static bool isInitialized;
#endif

        /////////////////////////////////////
        // CONSTANTS
        /////////////////////////////////////
        // 4K pages!
        internal const byte  PageBits = 12;
        internal const uint  PageSize = 1 << PageBits;
        internal const uint  PageMask = PageSize - 1;

        //
        // These constants define the layout of the virtual-page tables
        // present in each VirtualMemoryRange (and that correspond to
        // the FlatPages page table)
        //
        internal const uint    SystemPage          = 0xffff0000u;
        internal const uint    SystemPageMask      = 0xffff000fu;
        internal const uint    ProcessPageMask     = 0xffff0000u;
        internal const uint    ExtraMask           = 0x0000fff0u;
        internal const uint    TypeMask            = 0x0000000fu;

        internal const uint    PageUnknown         = SystemPage + (uint)PageType.Unknown;
        internal const uint    PageShared          = SystemPage + (uint)PageType.Shared;
        internal const uint    PageFree            = SystemPage + (uint)PageType.Unallocated;
        internal const uint    PageFreeFirst       = PageFree + 0xfff0;

        internal const uint    KernelPage          = 0x00010000u;

        internal const uint    KernelPageNonGC     = KernelPage + (uint)PageType.NonGC;
        internal const uint    KernelPageImage     = KernelPage + (uint)PageType.System;
        internal const uint    KernelPageStack     = KernelPage + (uint)PageType.Stack;

        /////////////////////////////////////
        // PUBLIC METHODS
        /////////////////////////////////////

        internal static void Initialize()
        {
            DebugStub.WriteLine("Initializing memory subsystem...");

#if PAGING
            // Set up the hardware-pages table and reserve a range for
            // I/O memory
            IOMemoryBaseAddr = PhysicalPages.Initialize(BootInfo.IO_MEMORY_SIZE);

            // Set up the I/O memory heap
            KernelIOMemoryHeap = new PhysicalHeap((UIntPtr)IOMemoryBaseAddr.Value,
                                                  (UIntPtr)(IOMemoryBaseAddr.Value + BootInfo.IO_MEMORY_SIZE));

            // Set up virtual memory. ** This enables paging ** !
            VMManager.Initialize();

            // Set up the kernel's memory ranges.
            //
            // The kernel's general-purpose range is special because
            // it *describes* low memory as well as the GC range proper
            // so the kernel's GC doesn't get confused by pointers to
            // static data in the kernel image.
            KernelRange = new VirtualMemoryRange_struct(
                VMManager.KernelHeapBase,
                VMManager.KernelHeapLimit,
                UIntPtr.Zero,
                VMManager.KernelHeapLimit,
                null); // no concurrent access to page descriptors yet

            // Mark the kernel's special areas
            KernelRange.SetPages(0x0, BootInfo.KERNEL_STACK_BEGIN, KernelPageImage);
            KernelRange.SetPages(BootInfo.GetBootInfo().DumpBase,
                                 BootInfo.GetBootInfo().DumpLimit,
                                 MemoryManager.KernelPageNonGC);
            KernelRange.SetPages(BootInfo.KERNEL_STACK_BEGIN, BootInfo.KERNEL_STACK_LIMIT,
                                 MemoryManager.KernelPageStack);

            DebugStub.WriteLine("MemoryManager initialized with {0} physical pages still free",
                                __arglist(PhysicalPages.GetFreePageCount()));

            isInitialized = true;
#else
            FlatPages.Initialize();
#endif // PAGING
        }

        internal static void Finalize()
        {
#if PAGING
            VMManager.Finalize();
#endif // PAGING
        }

        internal static unsafe void PostGCInitialize()
        {
#if PAGING
            VMManager.PostGCInitialize();

            // Create the wrapper for the kernel range. The fixed
            // statement is safe since KernelRange is a static
            // struct.
            fixed (VirtualMemoryRange_struct* pKernelRange = &KernelRange) {
                KernelRangeWrapper = new VirtualMemoryRange(pKernelRange,
                                                            ProtectionDomain.DefaultDomain);
            }
#endif
        }

        // haryadi
        internal static void InitializeProcessorAddressSpace()
        {
#if PAGING
            // non-implemented yet for paging
#else
            FlatPages.InitializeProcessorAddressSpace();
#endif
        }


        /////////////////////////////////////
        // PUBLIC PAGING-SPECIFIC METHODS
        /////////////////////////////////////

#if PAGING
        internal static unsafe VirtualMemoryRange GetKernelRange()
        {
            DebugStub.Assert(KernelRangeWrapper != null);
            return KernelRangeWrapper;
        }

        //
        // Get a new physical page and map it to the provided virtual address
        //
        //
        private static bool CommitAndMapNewPage(UIntPtr virtualAddr,
                                                ProtectionDomain inDomain)
        {
            DebugStub.Assert(IsPageAligned(virtualAddr));
            PhysicalAddress newPage = PhysicalPages.AllocPage();

            if (newPage == PhysicalAddress.Null) {
                // Failed.
                return false;
            }

            VMManager.MapPage(newPage, virtualAddr, inDomain);
            return true;
        }

        //
        // Get and map multiple new pages. On failure, no pages are allocated.
        //
        internal static bool CommitAndMapRange(UIntPtr virtualAddr,
                                               UIntPtr limitAddr,
                                               ProtectionDomain inDomain)
        {
            DebugStub.Assert(IsPageAligned(virtualAddr));
            DebugStub.Assert(IsPageAligned(limitAddr));

            for ( UIntPtr step = virtualAddr;
                  step < limitAddr;
                  step += PageSize) {

                if (!CommitAndMapNewPage(step, inDomain)) {
                    // Uh oh; we failed.
                    for ( UIntPtr unmapStep = virtualAddr;
                          unmapStep < virtualAddr;
                          unmapStep += PageSize) {

                        UnmapAndReleasePage(unmapStep);
                    }

                    return false;
                }
            }

            return true;
        }

        //
        // Unmap the page at the provided virtual address and release its
        // underlying physical page
        //
        internal static void UnmapAndReleasePage(UIntPtr virtualAddr)
        {
            DebugStub.Assert(VMManager.IsPageMapped(virtualAddr),
                             "Trying to unmap an unmapped page");
            PhysicalAddress phys = VMManager.UnmapPage(virtualAddr);
            DebugStub.Assert(phys != PhysicalAddress.Null);
            PhysicalPages.FreePage(phys);
        }

        //
        // Unmap and release an entire range
        //
        internal static void UnmapAndReleaseRange(UIntPtr virtualAddr,
                                                  UIntPtr limitAddr)
        {
            DebugStub.Assert(IsPageAligned(virtualAddr));
            DebugStub.Assert(IsPageAligned(limitAddr));

            for ( UIntPtr step = virtualAddr;
                  step < limitAddr;
                  step += PageSize) {

                UnmapAndReleasePage(step);
            }
        }


        /////////////////////////////////////
        // PAGING-ENABLED KERNEL MEMORY OPERATIONS
        /////////////////////////////////////

        internal static PhysicalAddress IOMemoryBase {
            get {
                return IOMemoryBaseAddr;
            }
        }

        [AccessedByRuntime("referenced from halkd.cpp")]
        internal static UIntPtr KernelMapPhysicalMemory(PhysicalAddress physStart,
                                                        UIntPtr numBytes)
        {
            UIntPtr permaLoc = VMManager.TranslatePhysicalRange(physStart, numBytes);

            if (permaLoc != UIntPtr.Zero) {
                // This location has a permanent mapping
                return permaLoc;
            } else {
                // This location must be mapped on the fly
                return VMManager.MapPhysicalMemory(KernelRangeWrapper,
                                                   Process.kernelProcess,
                                                   physStart, numBytes);
            }
        }

        [AccessedByRuntime("referenced from halkd.cpp")]
        internal static void KernelUnmapPhysicalMemory(UIntPtr startAddr,
                                                       UIntPtr limitAddr)
        {
            if (VMManager.IsPermaMapped(startAddr, limitAddr)) {
                return; // nothing to do
            } else {
                VMManager.UnmapPhysicalMemory(KernelRangeWrapper,
                                              Process.kernelProcess,
                                              startAddr, limitAddr);
            }
        }

        internal static UIntPtr KernelAllocate(UIntPtr numPages, Process process,
                                               uint extra, PageType type)
        {
            //
            if (KernelRangeWrapper != null) {
                return KernelRangeWrapper.Allocate(numPages, process, extra, type);
            } else {
                // Very early in the initialization sequence; ASSUME there is not
                // yet any concurrent access to paging descriptors, and allocate
                // memory without a paging-descriptor lock.
                return KernelRange.Allocate(numPages, process, extra, type, null);
            }
        }

        internal static UIntPtr KernelExtend(UIntPtr addr, UIntPtr numPages, Process process,
                                             PageType type)
        {
            // TODO: Extend not yet implemented
            DebugStub.Break();
            return UIntPtr.Zero;
        }

        internal static PageType KernelQuery(UIntPtr startAddr,
                                             out UIntPtr regionAddr,
                                             out UIntPtr regionSize)
        {
            // TODO: Query not yet implemented
            DebugStub.Break();
            regionAddr = UIntPtr.Zero;
            regionSize = UIntPtr.Zero;
            return PageType.Unknown;
        }

        internal static void KernelFree(UIntPtr startAddr, UIntPtr numPages, Process process)
        {
            KernelRange.Free(startAddr, numPages, process);
        }

        internal static UIntPtr AllocateIOMemory(UIntPtr limitAddr,
                                                 UIntPtr bytes,
                                                 UIntPtr alignment,
                                                 Process process)
        {
            return KernelIOMemoryHeap.Allocate(limitAddr, bytes, alignment, process);
        }

        internal static void FreeIOMemory(UIntPtr addr, UIntPtr size, Process process)
        {
            KernelIOMemoryHeap.Free(addr, size, process);
        }

        internal static UIntPtr KernelBaseAddr
        {
            get {
                return KernelRange.BaseAddress;
            }
        }

        internal static unsafe uint* KernelPageTable
        {
            get {
                return KernelRange.PageTable;
            }
        }

        internal static UIntPtr KernelPageCount
        {
            get {
                return KernelRange.PageCount;
            }
        }

        /////////////////////////////////////
        // PAGING-ENABLED USER MEMORY OPERATIONS
        /////////////////////////////////////

        internal static UIntPtr UserBaseAddr
        {
            get {
                return ProtectionDomain.CurrentDomain.UserRange.BaseAddress;
            }
        }

        internal static UIntPtr UserMapPhysicalMemory(PhysicalAddress physStart,
                                                      UIntPtr numBytes)
        {
            return VMManager.MapPhysicalMemory(ProtectionDomain.CurrentDomain.UserRange,
                                               Thread.CurrentProcess, physStart, numBytes);
        }

        internal static void UserUnmapPhysicalMemory(UIntPtr startAddr,
                                                     UIntPtr limitAddr)
        {
            VMManager.UnmapPhysicalMemory(ProtectionDomain.CurrentDomain.UserRange,
                                          Thread.CurrentProcess, startAddr, limitAddr);
        }

        internal static UIntPtr UserAllocate(UIntPtr numPages,
                                             Process process,
                                             uint extra,
                                             PageType type)
        {
            return ProtectionDomain.CurrentDomain.UserRange.Allocate(
                numPages, process, extra, type);
        }

        internal static UIntPtr UserExtend(UIntPtr addr, UIntPtr numPages, Process process,
                                           PageType type)
        {
            // TODO: Extend not yet implemented
            DebugStub.Break();
            return UIntPtr.Zero;
        }

        internal static void UserFree(UIntPtr addr, UIntPtr numPages, Process process)
        {
            ProtectionDomain.CurrentDomain.UserRange.Free(addr, numPages, process);
        }

        internal static PageType UserQuery(UIntPtr startAddr,
                                           out UIntPtr regionAddr,
                                           out UIntPtr regionSize)
        {
            // TODO: Query not yet implemented
            DebugStub.Break();
            regionAddr = UIntPtr.Zero;
            regionSize = UIntPtr.Zero;
            return PageType.Unknown;
        }

        internal static UIntPtr FreeProcessMemory(Process process)
        {
            return ProtectionDomain.CurrentDomain.UserRange.FreeAll(process);
        }

        internal static unsafe uint* UserPageTable
        {
            get {
                return ProtectionDomain.CurrentDomain.UserRange.PageTable;
            }
        }

        internal static UIntPtr UserPageCount
        {
            get {
                return ProtectionDomain.CurrentDomain.UserRange.PageCount;
            }
        }

        /////////////////////////////////////
        // PAGING-ENABLED DIAGNOSTICS
        /////////////////////////////////////

        public static ulong GetFreePhysicalMemory()
        {
            return PhysicalPages.GetFreeMemory();
        }

        public static ulong GetUsedPhysicalMemory()
        {
            return PhysicalPages.GetUsedMemory();
        }

        public static ulong GetMaxPhysicalMemory()
        {
            return PhysicalPages.GetMaxMemory();
        }

        internal static void GetUserStatistics(out ulong allocatedCount,
                                               out ulong allocatedBytes,
                                               out ulong freedCount,
                                               out ulong freedBytes)
        {
            ProtectionDomain.CurrentDomain.UserRange.GetUsageStatistics(
                out allocatedCount,
                out allocatedBytes,
                out freedCount,
                out freedBytes);
        }

#else // PAGING

        /////////////////////////////////////
        // NO-PAGING KERNEL MEMORY OPERATIONS
        /////////////////////////////////////

        internal static UIntPtr KernelMapPhysicalMemory(PhysicalAddress physStart,
                                                        UIntPtr numBytes)
        {
            // identity map without paging
            return unchecked((UIntPtr)physStart.Value);
        }

        internal static void KernelUnmapPhysicalMemory(UIntPtr startAddr,
                                                       UIntPtr limitAddr)
        {
            // do nothing
        }

        internal static UIntPtr KernelAllocate(UIntPtr numPages, Process process,
                                               uint extra, PageType type)
        {
            return FlatPages.Allocate(BytesFromPages(numPages),
                                        UIntPtr.Zero,
                                        MemoryManager.PageSize,
                                        process, extra, type);
        }

        internal static UIntPtr KernelExtend(UIntPtr addr, UIntPtr numPages, Process process,
                                             PageType type)
        {
            return FlatPages.AllocateExtend(addr, BytesFromPages(numPages), process, 0, type);
        }

        internal static void KernelFree(UIntPtr startAddr, UIntPtr numPages, Process process)
        {
            FlatPages.Free(startAddr, MemoryManager.BytesFromPages(numPages), process);
        }

        internal static PageType KernelQuery(UIntPtr startAddr, out UIntPtr regionAddr,
                                             out UIntPtr regionSize)
        {
            return FlatPages.Query(startAddr, Process.kernelProcess, out regionAddr,
                                   out regionSize);
        }

        internal static UIntPtr AllocateIOMemory(UIntPtr limitAddr,
                                                 UIntPtr bytes,
                                                 UIntPtr alignment,
                                                 Process process)
        {
            if (limitAddr > 0) {
                return FlatPages.AllocateBelow(limitAddr, bytes, alignment, process, 0, PageType.NonGC);
            } else {
                return FlatPages.Allocate(bytes, bytes, alignment, process, 0, PageType.NonGC);
            }
        }

        internal static void FreeIOMemory(UIntPtr addr, UIntPtr size, Process process)
        {
            FlatPages.Free(addr, size, process);
        }

        internal static UIntPtr KernelBaseAddr
        {
            get {
                return UIntPtr.Zero;
            }
        }

        internal static unsafe uint* KernelPageTable
        {
            get {
                return FlatPages.PageTable;
            }
        }

        internal static UIntPtr KernelPageCount
        {
            get {
                return FlatPages.PageCount;
            }
        }

        /////////////////////////////////////
        // NO-PAGING USER MEMORY OPERATIONS
        /////////////////////////////////////

        internal static UIntPtr UserBaseAddr
        {
            get {
                return UIntPtr.Zero;
            }
        }

        internal static UIntPtr UserMapPhysicalMemory(PhysicalAddress physStart,
                                                      UIntPtr numBytes)
        {
            return unchecked((UIntPtr)physStart.Value);
        }

        internal static void UserUnmapPhysicalMemory(UIntPtr startAddr,
                                                     UIntPtr limitAddr)
        {
            // do nothing
        }

        internal static UIntPtr UserAllocate(UIntPtr numPages,
                                             Process process,
                                             uint extra,
                                             PageType type)
        {
            return FlatPages.Allocate(BytesFromPages(numPages),
                                        UIntPtr.Zero,
                                        MemoryManager.PageSize,
                                        process, extra, type);
        }

        internal static UIntPtr UserExtend(UIntPtr addr, UIntPtr numPages, Process process,
                                           PageType type)
        {
            return FlatPages.AllocateExtend(addr, BytesFromPages(numPages), process, 0, type);
        }

        internal static void UserFree(UIntPtr addr, UIntPtr numPages, Process process)
        {
            FlatPages.Free(addr, BytesFromPages(numPages), process);
        }

        internal static PageType UserQuery(UIntPtr startAddr, out UIntPtr regionAddr,
                                           out UIntPtr regionSize)
        {
            return FlatPages.Query(startAddr, Thread.CurrentProcess,
                                   out regionAddr, out regionSize);
        }

        internal static UIntPtr FreeProcessMemory(Process process)
        {
            return FlatPages.FreeAll(process);
        }

        internal static unsafe uint* UserPageTable
        {
            get {
                return FlatPages.PageTable;
            }
        }

        internal static UIntPtr UserPageCount
        {
            get {
                return FlatPages.PageCount;
            }
        }

        /////////////////////////////////////
        // NO-PAGING DIAGNOSTICS
        /////////////////////////////////////

        public static ulong GetFreePhysicalMemory()
        {
            return (ulong)FlatPages.GetFreeMemory();
        }

        public static ulong GetUsedPhysicalMemory()
        {
            return (ulong)FlatPages.GetUsedMemory();
        }

        public static ulong GetMaxPhysicalMemory()
        {
            return (ulong)FlatPages.GetMaxMemory();
        }

        internal static void GetUserStatistics(out ulong allocatedCount,
                                               out ulong allocatedBytes,
                                               out ulong freedCount,
                                               out ulong freedBytes)
        {
            FlatPages.GetUsageStatistics(out allocatedCount, out allocatedBytes,
                                         out freedCount, out freedBytes);
        }

#endif

        // Simpler overload
        internal static UIntPtr AllocateIOMemory(UIntPtr bytes, Process process)
        {
            return AllocateIOMemory(UIntPtr.Zero, bytes, PageSize, process);
        }

        /////////////////////////////////////
        // PUBLIC UTILITY METHODS
        /////////////////////////////////////

        [Inline]
        internal static ulong PagePad(ulong addr) {
            return ((addr + PageMask) & ~((ulong)PageMask));
        }

        [Inline]
        internal static UIntPtr PagePad(UIntPtr addr) {
            return ((addr + PageMask) & ~PageMask);
        }

        [Inline]
        internal static ulong BytesNotAligned(ulong data, ulong size) {
            return ((data) & (size - 1));
        }

        [Inline]
        internal static UIntPtr BytesNotAligned(UIntPtr data, UIntPtr size) {
            return ((data) & (size - 1));
        }

        [Inline]
        internal static UIntPtr Pad(UIntPtr data, UIntPtr align) {
            return ((data + align - 1) & ~(align - 1));
        }

        [Inline]
        internal static UIntPtr Trunc(UIntPtr addr, UIntPtr align) {
            return addr - BytesNotAligned(addr, align);
        }

        [Inline]
        internal static ulong Trunc(ulong addr, ulong align) {
            return addr - BytesNotAligned(addr, align);
        }

        [Inline]
        internal static UIntPtr PageTrunc(UIntPtr addr) {
            return (addr & ~PageMask);
        }

        [Inline]
        internal static UIntPtr PageFromAddr(UIntPtr addr) {
            return (addr >> PageBits);
        }

        [Inline]
        internal static UIntPtr AddrFromPage(UIntPtr pageIdx) {
            return (pageIdx << PageBits);
        }

        [Inline]
        internal static ulong PagesFromBytes(ulong size) {
            return ((size + PageMask) >> PageBits);
        }

        [Inline]
        internal static UIntPtr PagesFromBytes(UIntPtr size) {
            return ((size + PageMask) >> PageBits);
        }

        [Inline]
        internal static UIntPtr BytesFromPages(UIntPtr pages) {
            return (UIntPtr)(pages << PageBits);
        }

        [Inline]
        internal static UIntPtr BytesFromPages(ulong pages) {
            return (UIntPtr)(pages << PageBits);
        }

        [Inline]
        [AccessedByRuntime("referenced from halkd.cpp")]
        internal static UIntPtr PageAlign(UIntPtr addr) {
            return (addr & ~PageMask);
        }

        [Inline]
        internal static bool IsPageAligned(UIntPtr addr) {
            return ((addr & PageMask) == 0);
        }

        [Inline]
        internal static bool IsPageAligned(ulong addr) {
            return ((addr & (ulong)PageMask) == 0);
        }
    }
}
