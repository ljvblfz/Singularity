////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   VMManager.cs -- Virtual Memory Manager
//
//  Notes:
//
//  This module manages page-translation tables for virtual memory
//

#if PAGING

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using Microsoft.Singularity;
using Microsoft.Singularity.X86;

using PageType = System.GCs.PageType;

namespace Microsoft.Singularity.Memory
{
    [NoCCtor]
    [CLSCompliant(false)]
    public class VMManager
    {
        //
        // NOTES
        //
        // Currently, this class is hand-written to deal with 4KB paging
        // on the AMD x64 in 32-bit mode, with PAE enabled. The
        // three-tier structure of the descriptor tables is baked into
        // the code; we will need to factor and expand this module to
        // deal with 64-bit mode, or with a different processor.
        //
        // This code is also written on the assumption that the user/kernel
        // boundary is 1GB-aligned, such that one, two or three of the
        // top-level Page-Directory-Pointer-Table (PDPE) entries fully
        // describe the kernel space.
        //
        // Obviously, memory pages are used to describe the mapping of
        // virtual memory to physical memory. This gets a little confusing.
        // We want there to be a mapping for the pages that contain
        // the page tables (!) so we have virtual addresses we can
        // use to manipulate the page tables!
        //
        // The kernel's page table pages must always be mapped, but the
        // page tables for the user space must get switched when
        // we change address spaces. Therefore, the pages used to
        // describe the mappings for the kernel's space live at a
        // certain virtual address (kernelPageTableBase), and the
        // pages that describe the mapping for user-space live at
        // an address in the user-space (userPageTableBase). This way,
        // the user's page table mappings get swapped along with the
        // rest of the user address space.
        //
        // A minimally usable address space must include a PDPT, and
        // PDTs and PTs sufficient to be able to map in all page-
        // table structures necessary to describe the space, including
        // user-space if the user-space range will be used.
        //
        // Additionally, it is a tricky stunt to set up a new address
        // space when paging is already turned on, since it is difficult
        // to "talk about" the page-table pages for the new space.
        // There are two pages of reserved VM space at scratchAddress1/2
        // that are used to temporarily map in single pages of
        // "foreign" memory so we can build a new address space without
        // having to turn off paging.
        //

        // These are the PDPT entries for the kernel space. Whether we
        // are using one, two or three of them is determined by
        // BootInfo.KERNEL_BOUNDARY
        private static PhysicalAddress kernelPDPE1;
        private static PhysicalAddress kernelPDPE2;
        private static PhysicalAddress kernelPDPE3;

        // How many of the kernelPDPEs are we using?
        private static uint numKernelPDPEs;

        // The address space we create during boot
        private static AddressSpace BootstrapSpace;

        // Here is the limit below which memory is identity-mapped
        private static UIntPtr identityMapLimit;

        // Here is the virtual address where the page-table pages that
        // describe the kernel's address space are always accessible
        private static UIntPtr kernelPageTableBase;
        private static UIntPtr kernelPageTableLimit;
        private static UIntPtr kernelPDTOffset;

        // Here is the same thing for the user space. This address is
        // above KERNEL_BOUNDARY.
        private static UIntPtr userPageTableBase;
        private static UIntPtr userPageTableLimit;
        private static UIntPtr userPDTOffset;

        // The PDPT always lives in the user space, so its mapping gets
        // switched naturally when we change spaces
        private static PDPT* pdptAddr;

        // Here are two page of VM space we keep available for scratch use.
        // We must take care to use them atomically!
        private static UIntPtr scratchAddress1;
        private static UIntPtr scratchAddress2;
        private static SpinLock scratchSpin;

        // Here is where we map the uncached-memory window that
        // corresponds to the high-memory range that we use to
        // communicate with hardware (like the APIC)
        private static UIntPtr uncachedWindowBase;
        private static UIntPtr uncachedWindowSize;

        // Here is where we have situated the executable images that
        // are actually in high physical memory (where they get put
        // by the boot loader)
        private static UIntPtr execImageBase;
        private static UIntPtr execImageSize;

#if RESERVE_SMAP
        // Here is where we remap areas of physical memory that are
        // reported as reserved by the SMAP (in the order they appear
        // in the SMAP)
        private static UIntPtr smapRemapBase;
        private static UIntPtr smapRemapSize;
#endif

        // Here are the bounds of the VM space that the kernel should use
        // for its GC heap
        private static UIntPtr kernelHeapBase;
        private static UIntPtr kernelHeapLimit;

        // Same for user-land
        private static UIntPtr userHeapBase;
        private static UIntPtr userHeapLimit;

        // A protective lock for the kernel's page mappings
        private static SmartSpinlock KernelMappingLock;

        /////////////////////////////////////
        // CONSTANTS
        /////////////////////////////////////

        // Each PT describes 512 4K pages
        private const uint SpacePerPT             = 4 * 1024 * 512; // 2MB

        // Each PDT describes 512 PTs
        private const uint SpacePerPDT            = 512 * SpacePerPT; // 1GB

        /////////////////////////////////////
        // PROPERTIES
        /////////////////////////////////////

        internal static UIntPtr KernelHeapBase {
            get {
                return kernelHeapBase;
            }
        }

        internal static UIntPtr KernelHeapLimit {
            get {
                return kernelHeapLimit;
            }
        }

        internal static UIntPtr UserHeapBase {
            get {
                return userHeapBase;
            }
        }

        internal static UIntPtr UserHeapLimit {
            get {
                return userHeapLimit;
            }
        }

        /////////////////////////////////////
        // PUBLIC METHODS
        /////////////////////////////////////

        //
        // This must be called with paging TURNED OFF, and it has the
        // side effect of TURNING PAGING ON!
        //
        internal static unsafe void Initialize()
        {
            // How big is the kernel space?
            DebugStub.Assert(MemoryManager.BytesNotAligned(BootInfo.KERNEL_BOUNDARY,SpacePerPDT) == 0);
            numKernelPDPEs = (uint)(BootInfo.KERNEL_BOUNDARY / SpacePerPDT);
            DebugStub.Assert(numKernelPDPEs <= 3);

            // Figure out where the kernel page-table pages will live.
            // We'll stick it immediately above the IO memory heap, on
            // a page boundary.
            kernelPageTableBase = (UIntPtr)MemoryManager.IOMemoryBase.Value;
            kernelPageTableBase += BootInfo.IO_MEMORY_SIZE;
            kernelPageTableBase = MemoryManager.PagePad(kernelPageTableBase);

            // Everything below this limit is identity-mapped
            identityMapLimit = kernelPageTableBase;

            // Now figure out the kernel's page-table layout
            uint numPTs = BootInfo.KERNEL_BOUNDARY / SpacePerPT;
            kernelPDTOffset = MemoryManager.BytesFromPages(numPTs);
            // numKernelPDPEs == the number of PDTs, by definition
            kernelPageTableLimit = kernelPageTableBase + MemoryManager.BytesFromPages(numPTs + numKernelPDPEs);

            // Stick the user-space page-table mapping right at the
            // beginning of the user's address space
            userPageTableBase = BootInfo.KERNEL_BOUNDARY;
            UIntPtr userSpaceSize = BootInfo.MAX_VIRTUAL_ADDR - BootInfo.KERNEL_BOUNDARY + 1;
            numPTs = (uint)(userSpaceSize / SpacePerPT);
            userPDTOffset = MemoryManager.BytesFromPages(numPTs);
            uint numPDTs = (uint)(userSpaceSize / SpacePerPDT);
            pdptAddr = (PDPT*)(userPageTableBase + MemoryManager.BytesFromPages(numPTs + numPDTs));

            // Count the PDPT as part of the user's page table so we don't
            // step on the PDPT mapping later
            userPageTableLimit = (UIntPtr)pdptAddr + MemoryManager.PageSize;

            // Define the limits of general-purpose user space
            userHeapBase = userPageTableLimit;
            userHeapLimit = BootInfo.MAX_VIRTUAL_ADDR;
            userHeapLimit = MemoryManager.Trunc(userHeapLimit, MemoryManager.PageSize);

            // Locate the uncached window immediately after the kernel's page-table space
            uncachedWindowBase = kernelPageTableLimit;
            uncachedWindowSize = BootInfo.UNCACHED_MAPPED;

            BootInfo bi = BootInfo.GetBootInfo();

            // Locate the executable images right after the uncached-window
            execImageBase = MemoryManager.PagePad(uncachedWindowBase + uncachedWindowSize);
            execImageSize = bi.DumpSize32;

#if RESERVE_SMAP
            // Locate the SMAP window next
            smapRemapBase = MemoryManager.PagePad(execImageBase + execImageSize);
            smapRemapSize = 0;

            // Calculate its size
            SMAPINFO *smap = (SMAPINFO*)bi.SmapData32;

            for (uint i = 0; i < bi.SmapCount; i++) {
                if ((smap[i].type != (ulong)SMAPINFO.AddressType.Free) &&
                    (smap[i].addr >= identityMapLimit)) {
                    smapRemapSize += smap[i].size;
                }
            }
            // Locate the one-page scratch window immediately above the smap area
            scratchAddress1 = MemoryManager.PagePad(smapRemapBase + smapRemapSize);
#else
            scratchAddress1 = MemoryManager.PagePad(execImageBase + execImageSize);
#endif

            scratchAddress2 = scratchAddress1 + MemoryManager.PageSize;
            scratchSpin = new SpinLock();

            // That's all for our fixed low-memory structures
            kernelHeapBase = scratchAddress2 + MemoryManager.PageSize;
            kernelHeapLimit = BootInfo.KERNEL_BOUNDARY;
            DebugStub.Assert(kernelHeapLimit > kernelHeapBase);

            // Now we can create the kernel's address space!
            BootstrapSpace = CreateBootstrapSpace();

            // TURN ON PAGING!
            Processor.EnablePaging(BootstrapSpace);

#if DEBUG
            // Sanity checks
            DebugStub.Assert(IsUserDescriptorPageMapped((UIntPtr)pdptAddr));
            DebugStub.Assert(((*pdptAddr)[0])->Valid);

            // We expect there to be sufficient PTs to describe the entire
            // kernel page table
            for (UIntPtr step = MemoryManager.Trunc(kernelPageTableBase, SpacePerPT);
                 step < MemoryManager.Pad(kernelPageTableLimit, SpacePerPT);
                 step += SpacePerPT) {

                PT* pt = GetPT(step);
                DebugStub.Assert(IsKernelDescriptorPageMapped((UIntPtr)pt));
            }

            // Obviously, the PDT for this space should be mapped too
            DebugStub.Assert(
                IsKernelDescriptorPageMapped(
                    (UIntPtr)GetPDT(
                        MemoryManager.Trunc(kernelPageTableBase, SpacePerPDT))));

            // Same checks for user-land
            for (UIntPtr step = MemoryManager.Trunc(userPageTableBase, SpacePerPT);
                 step < MemoryManager.Pad(userPageTableLimit, SpacePerPT);
                 step += SpacePerPT) {

                PT* pt = GetPT(step);
                DebugStub.Assert(IsUserDescriptorPageMapped((UIntPtr)pt));
            }

            DebugStub.Assert(
                IsUserDescriptorPageMapped(
                    (UIntPtr)GetPDT(
                        MemoryManager.Trunc(userPageTableBase, SpacePerPDT))));
#endif

            DebugStub.WriteLine("VMManager initialized with {0} physical pages still free",
                                __arglist(PhysicalPages.GetFreePageCount()));
        }

        internal static void Finalize()
        {
            // Restore the PDPT that the boot loader created
            Processor.ChangeAddressSpace(new AddressSpace(
                                             new PhysicalAddress(BootInfo.GetBootInfo().Pdpt32)));
        }

        internal static void PostGCInitialize()
        {
            // Create a spinlock to protect the kernel's page-mapping
            // structures
            KernelMappingLock = new SmartSpinlock();
        }

        //
        // This gets called during the boot sequence when we're ready to
        // start using user-space for the first time. We flesh out our
        // bootstrap address space with a user range and hand it out for the
        // first time.
        //
        internal static AddressSpace GetBootstrapSpace()
        {
            return BootstrapSpace;
        }

        //
        // Call to create a whole new address space from scratch
        //
        public static AddressSpace CreateNewAddressSpace()
        {
            AddressSpace retval = CreateNewKernelSpace();
            InitializeUserRange(retval);
            return retval;
        }

        //
        // Writes a new virtual -> physical address into the page table,
        // ensuring all necessary PDTs and PTs are allocated and mapped
        //
        // The caller must specify the protection domain that owns the mapped
        // pages, for locking purposes. When mapping pages into the kernel
        // range this parameter can be null.
        //
        // What are the "locking purposes"? Routines that may *add* nodes
        // to the page-mapping tree need to be protected against concurrency.
        //
        public static unsafe void MapPage(PhysicalAddress physAddr,
                                          UIntPtr virtualAddr,
                                          ProtectionDomain inDomain)
        {
            DebugStub.Assert(MemoryManager.IsPageAligned(virtualAddr));
            DebugStub.Assert(!IsPageMapped(virtualAddr), "Attempt to map over a mapped page");

            SmartSpinlock mappingLock = null;

            if (virtualAddr < BootInfo.KERNEL_BOUNDARY) {
                mappingLock = KernelMappingLock; // Might be null during early boot
            } else {
                DebugStub.Assert(inDomain != null);
                mappingLock = inDomain.UserMappingLock;
                DebugStub.Assert(mappingLock != null);
                DebugStub.Assert(inDomain.AddressSpace == Processor.GetCurrentAddressSpace());
            }

            bool iflag = false;

            if (mappingLock != null) {
                iflag = mappingLock.Lock();
            }

            try {
                MapPageNoLock(physAddr, virtualAddr);
            }
            finally {
                if (mappingLock != null) {
                    mappingLock.Unlock(iflag);
                }
            }
        }

        private static unsafe void MapPageNoLock(PhysicalAddress physAddr,
                                                 UIntPtr virtualAddr)
        {
            DebugStub.Assert(MemoryManager.IsPageAligned(virtualAddr));
            DebugStub.Assert(!IsPageMapped(virtualAddr), "Attempt to map over a mapped page");
            EnsurePTE(virtualAddr);
            MapPageInternal(physAddr, virtualAddr);
        }

        //
        // The caller must specify the protection domain that owns the mapped
        // pages, for locking purposes. When mapping pages into the kernel
        // range this parameter can be null.
        //
        // What are the "locking purposes"? Routines that may *add* nodes
        // to the page-mapping tree need to be protected against concurrency.
        //
        public static unsafe void MapRange(PhysicalAddress physAddr,
                                           UIntPtr virtualAddr,
                                           UIntPtr numPages,
                                           ProtectionDomain inDomain)
        {
            DebugStub.Assert(MemoryManager.IsPageAligned(virtualAddr));
            ulong stepPhys = physAddr.Value;
            UIntPtr stepVirt = virtualAddr;
            UIntPtr limitAddr = virtualAddr + MemoryManager.BytesFromPages(numPages);

            SmartSpinlock mappingLock = null;

            if (limitAddr <= BootInfo.KERNEL_BOUNDARY) {
                mappingLock = KernelMappingLock; // Might be null during early boot
            } else {
                DebugStub.Assert(inDomain != null);
                mappingLock = inDomain.UserMappingLock;
                DebugStub.Assert(mappingLock != null);
                DebugStub.Assert(inDomain.AddressSpace == Processor.GetCurrentAddressSpace());
            }

            bool iflag = false;

            if (mappingLock != null) {
                iflag = mappingLock.Lock();
            }

            try {
                for( UIntPtr i = 0; i < numPages; i++ ) {
                    MapPageNoLock(new PhysicalAddress(stepPhys), stepVirt);
                    stepPhys += MemoryManager.PageSize;
                    stepVirt += MemoryManager.PageSize;
                }
            } finally {
                if (mappingLock != null) {
                    mappingLock.Unlock(iflag);
                }
            }
        }

        //
        // Unmaps an existing virtual-memory page.
        // Does NOT release the underlying physical page
        // (whose address is returned)
        //
        public static unsafe PhysicalAddress UnmapPage(UIntPtr virtualAddr)
        {
            DebugStub.Assert(MemoryManager.IsPageAligned(virtualAddr));
            PTE* pte = GetPTE(virtualAddr);

            if (pte->Valid) {
                PhysicalAddress phys = new PhysicalAddress(pte->Address);
                pte->Valid = false;
                pte->Address = 0;
                Processor.InvalidateTLBEntry(virtualAddr);
                return phys;
            }
            else {
                DebugStub.Assert(false, "Attempt to unmap an unmapped page");
                return PhysicalAddress.Null;
            }
        }

        public static unsafe void UnmapRange(UIntPtr virtualAddr,
                                             UIntPtr numPages)
        {
            DebugStub.Assert(MemoryManager.IsPageAligned(virtualAddr));
            UIntPtr stepVirt = virtualAddr;

            for( UIntPtr i = 0; i < numPages; i++ ) {
                UnmapPage(stepVirt);
                stepVirt += MemoryManager.PageSize;
            }
        }

        //
        // Returns the physical address that a given virtual address is
        // currently mapped to (or PhysicalAddress.Null if none)
        //
        public static unsafe PhysicalAddress GetPageMapping(UIntPtr virtualAddr)
        {
            DebugStub.Assert(MemoryManager.IsPageAligned(virtualAddr));
            PTE* pte = GetPTE(virtualAddr);

            if (pte->Valid) {
                return new PhysicalAddress(pte->Address);
            } else {
                return PhysicalAddress.Null;
            }
        }

        // Examines the page-table structures to determine whether the page
        // at pageAddr is mapped or not.
        //
        // REVIEW: use a processor instruction instead to get a more definitive
        // answer?
        [AccessedByRuntime("referenced from halkd.cpp")]
        public static unsafe bool IsPageMapped(UIntPtr pageAddr)
        {
            DebugStub.Assert(MemoryManager.IsPageAligned(pageAddr));

            if ((pageAddr >= kernelPageTableBase) &&
                (pageAddr < kernelPageTableLimit)) {
                // This is the address of a kernel page-table page.
                // There should always be sufficient PTs to describe these.
                return IsKernelDescriptorPageMapped(pageAddr);
            } else if ((pageAddr >= userPageTableBase) &&
                       (pageAddr < userPageTableLimit)) {
                // User-land page-table page
                return IsUserDescriptorPageMapped(pageAddr);
            } else {
                // This page isn't part of the page table, so its
                // PT may be missing. Check that.
                PT* pt = GetPT(MemoryManager.Trunc(pageAddr, SpacePerPT));

                if (pageAddr >= BootInfo.KERNEL_BOUNDARY) {
                    if (!IsUserDescriptorPageMapped((UIntPtr)pt)) {
                        // The PT for this address isn't even mapped,
                        // so the address itself can't be mapped!
                        return false;
                    }
                } else {
                    if (!IsKernelDescriptorPageMapped((UIntPtr)pt)) {
                        return false;
                    }
                }

                // There's a PT but it might say that this page isn't
                // mapped.
                PTE* pte = GetPTE(pageAddr);
                return pte->Valid;
            }
        }

        //
        // A complement to TranslatePhysicalRange. Returns true if
        // the virtual address provided falls into one of the
        // virtual ranges we have reserved for permanent mappings
        // of hardware.
        //
        internal static unsafe bool IsPermaMapped(UIntPtr virtualAddr,
                                                  UIntPtr limitAddr)
        {
            if (limitAddr <= identityMapLimit) {
                return true;
            }

            if ((virtualAddr >= execImageBase) &&
                (limitAddr <= execImageBase + execImageSize)) {
                return true;
            }

#if RESERVE_SMAP
            if ((virtualAddr >= smapRemapBase) &&
                (limitAddr <= smapRemapBase + smapRemapSize)) {
                return true;
            }
#endif

            if ((virtualAddr >= uncachedWindowBase) &&
                (limitAddr <= uncachedWindowBase + uncachedWindowSize)) {
                return true;
            }

            return false;
        }

        //
        // This call returns non-null if the physical address provided
        // is already well-known and mapped into a stable area of
        // memory.
        //
        internal static unsafe UIntPtr TranslatePhysicalRange(PhysicalAddress physAddress,
                                                              UIntPtr numBytes)
        {
            BootInfo bi = BootInfo.GetBootInfo();
            ulong physAddr = physAddress.Value;
            ulong physLimit = physAddr + (ulong)numBytes;

            // Some physical pointers are identity-mapped
            if (physLimit < identityMapLimit) {
                return physAddr;
            }

            // Is this an address into the executable-image range
            // created by the loader?
            if ((physAddr >= bi.DumpAddr32) &&
                (physLimit <= bi.DumpAddr32 + execImageSize)) {
                // Assume we move the exec image lower in memory
                DebugStub.Assert(bi.DumpAddr32 > execImageBase);
                return (UIntPtr)(physAddr - (bi.DumpAddr32 - execImageBase));
            }

#if RESERVE_SMAP
            // Is this an address into an SMAP-reserved area?
            SMAPINFO *smap = (SMAPINFO*)bi.SmapData32;
            UIntPtr stepVirtual = smapRemapBase;

            for (uint i = 0; i < bi.SmapCount; i++) {
                if ((smap[i].type != (ulong)SMAPINFO.AddressType.Free) &&
                    (smap[i].addr >= identityMapLimit)) {

                    if ((physAddr >= smap[i].addr) &&
                        (physLimit <= smap[i].addr + smap[i].size)) {
                        // Match!
                        return stepVirtual + (physAddr - smap[i].addr)
                    }

                    stepVirtual += smap[i].size;
                }
            }
#endif

            // Is this a pointer into the uncached window at the
            // top of physical memory?
            ulong uncachedLimit = (ulong)BootInfo.UNCACHED_PHYSICAL +
                (ulong)BootInfo.UNCACHED_MAPPED;

            if ((physAddr >= BootInfo.UNCACHED_PHYSICAL) &&
                (physLimit <= uncachedLimit)) {
                return uncachedWindowBase + (UIntPtr)(physAddr - BootInfo.UNCACHED_PHYSICAL);
            }

            // No match
            return UIntPtr.Zero;
        }

        //
        // Finds a range of virtual space in the provided address range
        // and maps in the requested physical range on the fly
        //
        internal static UIntPtr MapPhysicalMemory(VirtualMemoryRange range,
                                                  Process process,
                                                  PhysicalAddress physStart,
                                                  UIntPtr numBytes)
        {
            ulong startPhysPage = MemoryManager.Trunc(physStart.Value, MemoryManager.PageSize);
            ulong physLimit = MemoryManager.PagePad(physStart.Value + (ulong)numBytes);
            UIntPtr numPages = MemoryManager.PagesFromBytes((UIntPtr)(physLimit - startPhysPage));

            // Reserve an area to accomplish the mapping in
            UIntPtr mapPoint = range.Reserve(numPages, process, 0, PageType.System);

            DebugStub.Assert(mapPoint != UIntPtr.Zero,
                             "Out of virtual memory trying to map a physical range");

            // Perform the mapping
            MapRange(new PhysicalAddress(startPhysPage), mapPoint, numPages, range.ParentDomain);

            // Take care to point into the mapped virtual memory properly
            return mapPoint + (UIntPtr)MemoryManager.BytesNotAligned(
                physStart.Value, MemoryManager.PageSize);
        }

        //
        // Reverses the operation of MapPhysicalMemory
        //
        internal static void UnmapPhysicalMemory(VirtualMemoryRange range,
                                                 Process process,
                                                 UIntPtr startAddr,
                                                 UIntPtr limitAddr)
        {
            UIntPtr startPageAddr = MemoryManager.Trunc(startAddr, MemoryManager.PageSize);
            UIntPtr limitPageAddr = MemoryManager.PagePad(limitAddr);
            uint numPages = (uint)MemoryManager.PagesFromBytes(limitPageAddr - startPageAddr);

            // First undo the mapping
            UnmapRange(startPageAddr, numPages);

            // Now mark all the pages as free
            range.Unreserve(startPageAddr, numPages, process);
        }

        /////////////////////////////////////
        // PRIVATE BOOTSTRAP METHODS
        /////////////////////////////////////

        // This function sets up the initial memory space from scratch.
        // It assumes paging is NOT on.
        private static unsafe AddressSpace CreateBootstrapSpace()
        {
            //
            // The kernel's address space serves as the template for all other
            // address spaces, because everything it maps must also be mapped
            // in other spaces.
            //
            // We need mapping for a bunch of things before we can turn on
            // paging. Here's the list:
            //
            // BELOW KERNEL_BOUNDARY:
            //
            // Zero through BootInfo.PHYSICAL_DISABLED is always kept unmapped
            //
            // Memory from PHYSICAL_DISABLED to the top of the I/O heap is
            // identity-mapped (we could probably stand to punch some holes
            // in this)
            //
            // Next comes the page-table area, starting at kernelPageTableBase.
            // It is large enough to cover all the pages that could be
            // needed to describe the kernel's address space.
            //
            // Next comes a window that maps the executable images downloaded
            // by the boot loader (these usually get stuck in high memory)
            // into the kernel's space, which is a more convenient location.
            //
            // Mappings above KERNEL_BOUNDARY are free for use by processes,
            // except that from UNCACHED_PHYSICAL to the top (4GB) is marked
            // uncached.
            //

            // To get started, we need a PDPT.
            PhysicalAddress phys = PhysicalPages.AllocPage();
            DebugStub.Assert(phys != PhysicalAddress.Null);
            PDPT* pdpt = (PDPT*)phys.Value;
            pdpt->Init();

            // Identity-map the range from PHYSICAL_DISABLED to the top
            // of the IO heap
            BootstrapMapRange(pdpt,
                              (UIntPtr)BootInfo.PHYSICAL_DISABLED, identityMapLimit,
                              new PhysicalAddress(BootInfo.PHYSICAL_DISABLED),
                              true, // writeable
                              false, // not write-through
                              false); // don't disable caching

            // Map the uncached area of high memory down into the kernel space
            BootstrapMapRange(pdpt, uncachedWindowBase,
                              MemoryManager.PagePad(uncachedWindowBase + uncachedWindowSize),
                              new PhysicalAddress(BootInfo.UNCACHED_PHYSICAL),
                              true, // writeable
                              true, // write-through
                              true); // disable caching

            // Map the area of high memory that contains executable images
            // down into its kernel-space window
            BootInfo bi = BootInfo.GetBootInfo();
            PhysicalAddress physStart = new PhysicalAddress(bi.DumpAddr32);

            BootstrapMapRange(pdpt, execImageBase,
                              MemoryManager.PagePad(execImageBase + execImageSize),
                              physStart,
                              false, // read-only
                              false, // not write-through
                              false); // don't disable caching

#if RESERVE_SMAP
            // Walk the SMAP and remap any reserved areas into the smap window
            SMAPINFO *smap = (SMAPINFO*)bi.SmapData32;
            UIntPtr stepVirtual = smapRemapBase;

            for (uint i = 0; i < bi.SmapCount; i++) {
                if ((smap[i].type != (ulong)SMAPINFO.AddressType.Free) &&
                    (smap[i].addr >= identityMapLimit)) {
                    DebugStub.Assert(MemoryManager.IsPageAligned(smap[i].size));
                    BootstrapMapRange(pdpt, stepVirtual, stepVirtual + smap[i].size,
                                      new PhysicalAddress(smap[i].addr),
                                      true, // writeable
                                      true, // write-through,
                                      true); // disable caching
                    stepVirtual += smap[i].size;
                }
            }

            DebugStub.Assert((stepVirtual - smapRemapBase) == smapRemapSize);
#endif

            // Make sure we have committed a PDT for each kernel PDPE slot, so
            // other address spaces can alias the kernel range sensibly
            BootstrapEnsurePDTs(pdpt, UIntPtr.Zero, BootInfo.KERNEL_BOUNDARY);

            kernelPDPE1 = new PhysicalAddress(((*pdpt)[0])->Address);
            DebugStub.Assert(kernelPDPE1 != PhysicalAddress.Null);

            if (numKernelPDPEs >= 2) {
                kernelPDPE2 = new PhysicalAddress(((*pdpt)[1])->Address);
                DebugStub.Assert(kernelPDPE2 != PhysicalAddress.Null);
            }

            if (numKernelPDPEs >=3 ) {
                kernelPDPE3 = new PhysicalAddress(((*pdpt)[2])->Address);
                DebugStub.Assert(kernelPDPE3 != PhysicalAddress.Null);
            }

            // Create the mappings for all the page-table structures
            // we just set up so we can still manipulate them after
            // we turn on paging!
            BootstrapMapPageTable(pdpt);

            return new AddressSpace(new PhysicalAddress((ulong)pdpt));
        }

        // For use during startup. Creates a mapping for all the physical
        // pages currently being used to describe memory mappings (yes,
        // this is confusing).
        //
        // Assumes paging is NOT on so that physical addresses can be
        // used as-is
        private static unsafe void BootstrapMapPageTable(PDPT* pdpt)
        {
            BootstrapEnsurePTs(pdpt, kernelPageTableBase, kernelPageTableLimit);
            BootstrapEnsurePTs(pdpt, userPageTableBase, userPageTableLimit);

            BootstrapMapPage(pdpt, (UIntPtr)pdptAddr, new PhysicalAddress((ulong)pdpt), true);

            // First map the kernel page table pages
            for (uint i = 0; i < numKernelPDPEs; i++) {
                if (((*pdpt)[i])->Valid) {
                    PDT* pdt = (PDT*)(((*pdpt)[i])->Address);

                    BootstrapMapPage(pdpt,
                                     kernelPageTableBase + kernelPDTOffset + MemoryManager.BytesFromPages(i),
                                     new PhysicalAddress((ulong)pdt), true);

                    for ( uint j = 0; j < 512; j++) {
                        if (((*pdt)[j])->Valid) {
                            PT* pt = (PT*)(((*pdt)[j])->Address);

                            BootstrapMapPage(pdpt,
                                             kernelPageTableBase + MemoryManager.BytesFromPages((i * 512) + j),
                                             new PhysicalAddress((ulong)pt), true);
                        }
                    }
                }
            }

            // Now map user-space page-table pages
            for (uint i = 0; i < (4 - numKernelPDPEs); i++) {
                if (((*pdpt)[numKernelPDPEs + i])->Valid) {
                    PDT* pdt = (PDT*)(((*pdpt)[numKernelPDPEs + i])->Address);

                    BootstrapMapPage(pdpt,
                                     userPageTableBase + userPDTOffset + MemoryManager.BytesFromPages(i),
                                     new PhysicalAddress((ulong)pdt), true);

                    for ( uint j = 0; j < 512; j++) {
                        if (((*pdt)[j])->Valid) {
                            PT* pt = (PT*)(((*pdt)[j])->Address);

                            BootstrapMapPage(pdpt,
                                             userPageTableBase + MemoryManager.BytesFromPages((i * 512) + j),
                                             new PhysicalAddress((ulong)pt), true);
                        }
                    }
                }
            }
        }

        // For use during startup. Makes sure the given PDPT points to
        // enough PDTs (which are allocated as necessary) to describe the
        // address range specified.
        //
        // Assumes paging is NOT on.
        private static unsafe void BootstrapEnsurePDTs(PDPT* pdpt,
                                                       UIntPtr startAddr,
                                                       UIntPtr limitAddr)
        {
            limitAddr = MemoryManager.Pad(limitAddr, SpacePerPDT);
            uint stepPDTidx = (uint)(startAddr / SpacePerPDT);
            UIntPtr stepAddr = startAddr;

            while (stepAddr < limitAddr) {
                if (!((*pdpt)[stepPDTidx])->Valid) {
                    PhysicalAddress newPDT = PhysicalPages.AllocPage();
                    ((PDT*)newPDT.Value)->Init();
                    ((*pdpt)[stepPDTidx])->Address = newPDT.Value;
                    ((*pdpt)[stepPDTidx])->Valid = true;
                }

                stepPDTidx++;
                stepAddr += SpacePerPDT;
            }
        }


        // For use during startup. Makes sure there are enough PTs
        // (which are allocated as necessary) to describe the address
        // range specified.
        //
        // Assumes paging is NOT on.
        private static unsafe void BootstrapEnsurePTs(PDPT* pdpt,
                                                      UIntPtr startAddr,
                                                      UIntPtr limitAddr)
        {
            limitAddr = MemoryManager.Pad(limitAddr, SpacePerPT);
            BootstrapEnsurePDTs(pdpt, startAddr, limitAddr);

            uint stepPDTidx = (uint)(startAddr / SpacePerPDT);
            UIntPtr stepAddr = startAddr;

            while (stepAddr < limitAddr) {
                DebugStub.Assert(((*pdpt)[stepPDTidx])->Valid); // ensured above
                PDT* pdt = (PDT*)(((*pdpt)[stepPDTidx])->Address);
                UIntPtr offsetInPDT = MemoryManager.BytesNotAligned(stepAddr, SpacePerPDT);

                uint stepPTidx = (uint)(offsetInPDT / SpacePerPT);

                while ((stepAddr < limitAddr) && (stepPTidx < 512)) {
                    if (!((*pdt)[stepPTidx])->Valid) {
                        PhysicalAddress newPT = PhysicalPages.AllocPage();
                        ((PT*)newPT.Value)->Init();
                        ((*pdt)[stepPTidx])->Address = newPT.Value;
                        ((*pdt)[stepPTidx])->User = true; // Ring3
                        ((*pdt)[stepPTidx])->Valid = true;
                        // Up to each page to set its writability
                        ((*pdt)[stepPTidx])->Writeable = true;
                    }

                    stepPTidx++;
                    stepAddr += SpacePerPT;
                }

                stepPDTidx++;
            }
        }

        // For use during startup. Map a range of addresses.
        //
        // Assumes paging is NOT on.
        private static unsafe void BootstrapMapRange(PDPT* pdpt,
                                                     UIntPtr startAddr,
                                                     UIntPtr limitAddr,
                                                     PhysicalAddress physicalStart,
                                                     bool writeable,
                                                     bool writeThrough,
                                                     bool cacheDisable)
        {
            DebugStub.Assert(MemoryManager.IsPageAligned(startAddr));
            DebugStub.Assert(MemoryManager.IsPageAligned(limitAddr));
            DebugStub.Assert(MemoryManager.IsPageAligned(physicalStart.Value));

            BootstrapEnsurePTs(pdpt, startAddr, limitAddr);

            uint stepPDTidx = (uint)(startAddr / SpacePerPDT);
            UIntPtr stepAddr = startAddr;
            ulong stepPhysical = physicalStart.Value;

            while ((stepAddr < limitAddr) && (stepPDTidx < 4)) {
                DebugStub.Assert(((*pdpt)[stepPDTidx])->Valid); // ensured above
                PDT* pdt = (PDT*)((*pdpt)[stepPDTidx])->Address;

                UIntPtr offsetInPDT = MemoryManager.BytesNotAligned(stepAddr, SpacePerPDT);
                DebugStub.Assert(MemoryManager.IsPageAligned(offsetInPDT));
                uint stepPTidx = (uint)(offsetInPDT / SpacePerPT);

                while ((stepAddr < limitAddr) && (stepPTidx < 512)) {
                    DebugStub.Assert(((*pdt)[stepPTidx])->Valid); // ensured above
                    PT* pt = (PT*)(((*pdt)[stepPTidx])->Address);

                    UIntPtr offsetInPT = MemoryManager.BytesNotAligned(stepAddr, SpacePerPT);
                    DebugStub.Assert(MemoryManager.IsPageAligned(offsetInPT));
                    uint pageIndex = (uint)MemoryManager.PagesFromBytes(offsetInPT);

                    while ((stepAddr < limitAddr) && (pageIndex < 512)) {
                        DebugStub.Assert(!((*pt)[pageIndex])->Valid);
                        ((*pt)[pageIndex])->Address = stepPhysical;
                        ((*pt)[pageIndex])->User = true; // Ring3
                        ((*pt)[pageIndex])->Valid = true;
                        ((*pt)[pageIndex])->Writeable = writeable;
                        ((*pt)[pageIndex])->Global = stepAddr < BootInfo.KERNEL_BOUNDARY;
                        ((*pt)[pageIndex])->WriteThrough = writeThrough;
                        ((*pt)[pageIndex])->CacheDisable = cacheDisable;

                        stepPhysical += MemoryManager.PageSize;
                        pageIndex++;
                        stepAddr += MemoryManager.PageSize;
                    }

                    stepPTidx++;
                }

                stepPDTidx++;
            }

            DebugStub.Assert(stepAddr >= limitAddr);
        }

        // For use during startup. Maps a single page of memory.
        //
        // Assumes paging is NOT on.
        private static unsafe void BootstrapMapPage(PDPT* pdpt,
                                                    UIntPtr pageAddr,
                                                    PhysicalAddress physPage,
                                                    bool writeable)
        {
            DebugStub.Assert(MemoryManager.IsPageAligned(pageAddr));
            DebugStub.Assert(MemoryManager.IsPageAligned(physPage.Value));

            BootstrapEnsurePTs(pdpt, pageAddr, pageAddr + MemoryManager.PageSize);

            uint pdtIndex = (uint)(pageAddr / SpacePerPDT);
            UIntPtr offsetInPDT = MemoryManager.BytesNotAligned(pageAddr, SpacePerPDT);
            DebugStub.Assert(((*pdpt)[pdtIndex])->Valid); // ensured above
            PDT* pdt = (PDT*)((*pdpt)[pdtIndex])->Address;

            uint ptIndex = (uint)(offsetInPDT / SpacePerPT);
            UIntPtr offsetInPT = MemoryManager.BytesNotAligned(offsetInPDT, SpacePerPT);
            DebugStub.Assert(((*pdt)[ptIndex])->Valid); // ensured above;
            DebugStub.Assert(((*pdt)[ptIndex])->User); // Ring3
            PT* pt = (PT*)(((*pdt)[ptIndex])->Address);

            uint pageIndex = (uint)MemoryManager.PagesFromBytes(offsetInPT);
            DebugStub.Assert(!((*pt)[pageIndex])->Valid);

            ((*pt)[pageIndex])->Address = physPage.Value;
            ((*pt)[pageIndex])->User = true; // Ring3
            ((*pt)[pageIndex])->Writeable = writeable;
            ((*pt)[pageIndex])->Global = pageAddr < BootInfo.KERNEL_BOUNDARY;
            ((*pt)[pageIndex])->Valid = true;
        }

        /////////////////////////////////////
        // PRIVATE METHODS, POST-BOOTSTRAP
        /////////////////////////////////////

        //
        // Create a new address space that just maps the (existing) kernel
        // range.
        //
        private static unsafe AddressSpace CreateNewKernelSpace()
        {
            // REVIEW it's regrettable that we have to lock
            // for access to the scratch area. Maybe we should try to
            // reserve two pages of kernel-space memory instead?
            bool iflag = ScratchLock();

            try {
                // Get a page to use as a PDPT and map it
                PhysicalAddress physPage = PhysicalPages.AllocPage();
                DebugStub.Assert(physPage != PhysicalAddress.Null);
                MapPageInternal(physPage, scratchAddress1);
                PDPT* pdpt = (PDPT*)scratchAddress1;

                pdpt->Init();

                ((*pdpt)[0])->Address = kernelPDPE1.Value;
                ((*pdpt)[0])->Valid = true;

                // This is klunky but we can't have static arrays...
                if (numKernelPDPEs >= 2) {
                    ((*pdpt)[1])->Address = kernelPDPE2.Value;
                    ((*pdpt)[1])->Valid = true;
                }

                if (numKernelPDPEs >= 3) {
                    ((*pdpt)[2])->Address = kernelPDPE3.Value;
                    ((*pdpt)[2])->Valid = true;
                }

                DebugStub.Assert(numKernelPDPEs < 4);

                PhysicalAddress sanity = UnmapPage(scratchAddress1);
                DebugStub.Assert(sanity == physPage);

                return new AddressSpace(physPage);
            }
            finally {
                ScratchUnlock(iflag);
            }
        }

        //
        // Set up a kernel-only address space so its user range can be used.
        //
        private static unsafe void InitializeUserRange(AddressSpace space)
        {
            // Our task here is to set up enough PTs to cover the
            // user range's page-table pages. This gets confusing because
            // we also want to write mappings for the PDT and PTs we
            // create.

            // Assume the user-space page-table range doesn't straddle a PDT.
            DebugStub.Assert((userPageTableBase / SpacePerPDT) ==
                             ((userPageTableLimit - 1) / SpacePerPDT));

            // REVIEW it's regrettable that we have to lock
            // for access to the scratch area. Maybe we should try to
            // reserve two pages of kernel-space memory instead?
            bool iflag = ScratchLock();

            try {
                PT* pt = null;

                // First create the PDT
                PhysicalAddress newPdt = PhysicalPages.AllocPage();
                DebugStub.Assert(newPdt != PhysicalAddress.Null);
                MapPageInternal(newPdt, scratchAddress1);
                PDT* pdt = (PDT*)scratchAddress1;
                pdt->Init();

                // We're going to need to make a mapping for the PDT
                // so it is addressable at userPageTableBase + userPDTOffset.
                // Figure out what PT and PTE that lands in.
                UIntPtr bytesIntoPDTRange = MemoryManager.BytesNotAligned(
                    userPageTableBase + userPDTOffset, SpacePerPDT);
                uint pdtPTIdx = (uint)(bytesIntoPDTRange / SpacePerPT);

                UIntPtr bytesIntoPTRange = MemoryManager.BytesNotAligned(
                    bytesIntoPDTRange, SpacePerPT);
                uint pdtPTEIdx = (uint)(bytesIntoPTRange / MemoryManager.PageSize);

                // Same story for the PDPT, which gets mapped to a user-space
                // address (pdptAddr).
                bytesIntoPDTRange = MemoryManager.BytesNotAligned((UIntPtr)pdptAddr, SpacePerPDT);
                uint pdptPTIdx = (uint)(bytesIntoPDTRange / SpacePerPT);

                bytesIntoPTRange = MemoryManager.BytesNotAligned(
                    bytesIntoPDTRange, SpacePerPT);
                uint pdptPTEIdx = (uint)(bytesIntoPTRange / MemoryManager.PageSize);

                // Now fill in as many PDT slots with new PTs as are
                // necessary to cover the pagetable range
                bytesIntoPDTRange = MemoryManager.BytesNotAligned(userPageTableBase, SpacePerPDT);
                uint startPTIdx = (uint)(bytesIntoPDTRange / SpacePerPT);
                bytesIntoPDTRange = MemoryManager.BytesNotAligned(userPageTableLimit, SpacePerPDT);
                bytesIntoPDTRange = MemoryManager.Pad(bytesIntoPDTRange, SpacePerPT);
                uint limitPTIdx = (uint)(bytesIntoPDTRange / SpacePerPT);
                uint numPTs = limitPTIdx - startPTIdx;

                for (uint i = startPTIdx; i < limitPTIdx; i++) {
                    PhysicalAddress newPT = PhysicalPages.AllocPage();
                    ((*pdt)[i])->Address = newPT.Value;
                    ((*pdt)[i])->User = true; // Ring3
                    ((*pdt)[i])->Valid = true;
                    ((*pdt)[i])->Writeable = true; // each page sets this individually

                    // Map in the new PT page
                    MapPageInternal(newPT, scratchAddress2, i > startPTIdx);
                    pt = (PT*)scratchAddress2;
                    pt->Init();

                    if (i == pdtPTIdx) {
                        // This is the PT that the PTE for our new PDT
                        // goes into (yes, that's confusing)
                        ((*pt)[pdtPTEIdx])->Address = newPdt.Value;
                        ((*pt)[pdtPTEIdx])->User = true; // Ring3
                        ((*pt)[pdtPTEIdx])->Valid = true;
                        ((*pt)[pdtPTEIdx])->Writeable = true;
                    }

                    if (i == pdptPTIdx) {
                        // Same drill for the PDPT
                        ((*pt)[pdptPTEIdx])->Address = space.PdptPage.Value;
                        ((*pt)[pdptPTEIdx])->User = true; // Ring3
                        ((*pt)[pdptPTEIdx])->Valid = true;
                        ((*pt)[pdptPTEIdx])->Writeable = true;
                    }
                }

                // Now, write mappings for all the new PT pages we just
                // created. Assume here that all the PT page mappings
                // fit into a single PT.
                DebugStub.Assert(startPTIdx + numPTs < 512);

                // Map the PT that will hold our PTEs
                MapPageInternal(new PhysicalAddress(((*pdt)[startPTIdx])->Address),
                                scratchAddress2, true);
                pt = (PT*)scratchAddress2;

                bytesIntoPTRange = MemoryManager.BytesNotAligned(userPageTableBase, SpacePerPT);
                uint startPTEIdx = (uint)(bytesIntoPTRange / MemoryManager.PageSize);

                for (uint i = 0; i < numPTs; i++) {
                    ((*pt)[startPTEIdx + i])->Address = ((*pdt)[startPTIdx + i])->Address;
                    DebugStub.Assert(((*pt)[startPTEIdx + i])->Address != 0);
                    ((*pt)[startPTEIdx + i])->User = true; // Ring3
                    ((*pt)[startPTEIdx + i])->Valid = true;
                    ((*pt)[startPTEIdx + i])->Writeable = true;
                }

                PhysicalAddress sanity = UnmapPage(scratchAddress2);
                DebugStub.Assert(sanity.Value == ((*pdt)[startPTIdx])->Address);

                // Now flip scratchAddress1 to the PDPT for this space
                MapPageInternal(space.PdptPage, scratchAddress1, true);
                PDPT* pdpt = (PDPT*)scratchAddress1;

                // Write our new PDT into the PDPT
                uint pdtIdx = (uint)(userPageTableBase / SpacePerPDT);
                DebugStub.Assert(pdtIdx >= numKernelPDPEs);

                ((*pdpt)[pdtIdx])->Address = newPdt.Value;
                ((*pdpt)[pdtIdx])->Valid = true;

                sanity = UnmapPage(scratchAddress1);
                DebugStub.Assert(sanity == space.PdptPage);
            }
            finally {
                ScratchUnlock(iflag);
            }
        }

        // Return a pointer to the PDT that describes the address
        // range beginning at forAddr, allocating and mapping a
        // new one if necessary.
        private static unsafe PDT* EnsurePDT(UIntPtr forAddr)
        {
            PDPE* pdpe = GetPDPE(forAddr); // must succeed
            PDT* retval = GetPDT(forAddr);

            if (! IsPageMapped((UIntPtr)retval)) {
                // Don't panic; grab a new page to use as a PDT
                PhysicalAddress newPdt = PhysicalPages.AllocPage();

                if (newPdt == PhysicalAddress.Null) {
                    Kernel.Panic("Out of physical pages while trying to allocate a PDT");
                    return null;
                }

                // Activate this page. If this faults, it means
                // our address space wasn't set up properly with
                // enough PTs to describe the page table pages.
                MapPageInternal(newPdt, (UIntPtr)retval);

                // Now we can address the new page
                retval->Init();

                // Point our parent PDPT here
                DebugStub.Assert(!pdpe->Valid);
                pdpe->Address = newPdt.Value;
                pdpe->Valid = true;
            } else {
                DebugStub.Assert(pdpe->Valid);
            }

            return retval;
        }

        // Return a pointer to the PDE for the PT describing the
        // memory range beginning at forAddr, allocating and
        // mapping descriptors as necessary.
        private static unsafe PDE* EnsurePDE(UIntPtr forAddr)
        {
            DebugStub.Assert(MemoryManager.BytesNotAligned(forAddr, SpacePerPT) == 0);
            UIntPtr bytesIntoPDTRange = MemoryManager.BytesNotAligned(forAddr, SpacePerPDT);
            EnsurePDT(forAddr - bytesIntoPDTRange);
            return GetPDE(forAddr);
        }

        // Return a pointer to the PT that describes the address
        // range beginning at forAddr, allocating and mapping a
        // new one if necessary.
        private static unsafe PT* EnsurePT(UIntPtr forAddr)
        {
            PDE* pde = EnsurePDE(forAddr);
            PT* retval = GetPT(forAddr);

            if (! IsPageMapped((UIntPtr)retval)) {
                // Don't panic; grab a new page to use as a PT
                PhysicalAddress newPt = PhysicalPages.AllocPage();

                if (newPt == PhysicalAddress.Null) {
                    Kernel.Panic("Out of physical pages while trying to allocate a PT");
                    return null;
                }

                // Activate this page. If this faults, it means
                // our address space wasn't set up properly with
                // enough PTs to describe the page table pages.
                MapPageInternal(newPt, (UIntPtr)retval);

                // Now we can address the new page
                retval->Init();

                // Add ourselves to our parent PDT
                DebugStub.Assert(!pde->Valid);
                pde->Address = newPt.Value;
                pde->User = true; // Ring3
                pde->Valid = true;
                pde->Writeable = true;
            } else {
                DebugStub.Assert(pde->Valid);
            }

            return retval;
        }

        // Return a pointer to the PTE for the given page address,
        // allocating and mapping descriptors as necessary.
        private static unsafe PTE* EnsurePTE(UIntPtr pageAddr)
        {
            DebugStub.Assert(MemoryManager.IsPageAligned(pageAddr));

            UIntPtr bytesIntoPTRange = MemoryManager.BytesNotAligned(pageAddr, SpacePerPT);
            EnsurePT(pageAddr - bytesIntoPTRange);
            return GetPTE(pageAddr);
        }

        // Return a pointer to the PDT that describes the address
        // range beginning at forAddr, without ensuring that it is
        // mapped.
        private static unsafe PDT* GetPDT(UIntPtr forAddr)
        {
            // Make sure forAddr specifies a range that
            // makes sense
            DebugStub.Assert(MemoryManager.BytesNotAligned(forAddr, SpacePerPDT) == 0);

            UIntPtr pdtBase = kernelPageTableBase + kernelPDTOffset;
            UIntPtr addr = forAddr;

            if (forAddr >= BootInfo.KERNEL_BOUNDARY) {
                // User-space address
                pdtBase = userPageTableBase + userPDTOffset;
                addr -= BootInfo.KERNEL_BOUNDARY;
            }

            // Index into the table of PDTs
            uint pdtIndex = (uint)(addr / SpacePerPDT);
            return (PDT*)(pdtBase + MemoryManager.BytesFromPages(pdtIndex));
        }

        // Return a pointer to the PT that describes the address
        // range beginning at forAddr, without ensuring that it is
        // mapped.
        private static unsafe PT* GetPT(UIntPtr forAddr)
        {
            DebugStub.Assert(MemoryManager.BytesNotAligned(forAddr, SpacePerPT) == 0);

            UIntPtr ptBase = kernelPageTableBase;
            UIntPtr addr = forAddr;

            if (forAddr >= BootInfo.KERNEL_BOUNDARY) {
                // User-space address
                ptBase = userPageTableBase;
                addr -= BootInfo.KERNEL_BOUNDARY;
            }

            // Index into the table of PTs
            uint ptIndex = (uint)(addr / SpacePerPT);
            return (PT*)(ptBase + MemoryManager.BytesFromPages(ptIndex));
        }

        // Return a pointer to the PDPE for the PDT that describes
        // the memory range beginning at the provided address.
        // We assume the PDPT is always mapped.
        private static unsafe PDPE* GetPDPE(UIntPtr forAddr)
        {
            DebugStub.Assert(MemoryManager.BytesNotAligned(forAddr, SpacePerPDT) == 0);
            uint PDTidx = (uint)(forAddr / SpacePerPDT);
            return (*pdptAddr)[(uint)PDTidx];
        }

        // Return a pointer to the PDE for the PT that describes
        // the memory range beginning at the provided address,
        // without ensuring that the PDT is mapped
        private static unsafe PDE* GetPDE(UIntPtr forAddr)
        {
            DebugStub.Assert(MemoryManager.BytesNotAligned(forAddr, SpacePerPT) == 0);
            UIntPtr bytesIntoPDTRange = MemoryManager.BytesNotAligned(forAddr, SpacePerPDT);
            uint ptWithinPDT = (uint)(bytesIntoPDTRange / SpacePerPT);
            PDT* pdt = GetPDT(forAddr - bytesIntoPDTRange);
            return (*pdt)[(uint)ptWithinPDT];
        }

        // Return a pointer to the PTE for the given page address,
        // without ensuring that the PT is mapped.
        private static unsafe PTE* GetPTE(UIntPtr pageAddr)
        {
            DebugStub.Assert(MemoryManager.IsPageAligned(pageAddr));
            UIntPtr bytesIntoPTRange = MemoryManager.BytesNotAligned(pageAddr, SpacePerPT);
            uint pageWithinPT = (uint)MemoryManager.PagesFromBytes(bytesIntoPTRange);
            PT* pt = GetPT(pageAddr - bytesIntoPTRange);
            return (*pt)[pageWithinPT];
        }

        // This function directly examines the PTE for the virtual
        // address of a page-table structure (this address should
        // fall between pageTableBaseAddr and
        // pageTableBaseAddr + PageTableSize), on the assumption
        // that sufficient PTs to describe all page-table structures
        // are always mapped.
        private static unsafe bool IsKernelDescriptorPageMapped(UIntPtr pageAddr)
        {
            // Make sure this address is for a page-table element
            DebugStub.Assert(MemoryManager.IsPageAligned(pageAddr));
            DebugStub.Assert(pageAddr >= kernelPageTableBase);
            DebugStub.Assert(pageAddr < kernelPageTableLimit);
            PTE* pte = GetPTE(pageAddr);

            // If this faults, our address space wasn't set up right
            return pte->Valid;
        }

        // Same for userland
        private static unsafe bool IsUserDescriptorPageMapped(UIntPtr pageAddr)
        {
            // Make sure this address is for a page-table element
            DebugStub.Assert(MemoryManager.IsPageAligned(pageAddr));
            DebugStub.Assert(pageAddr >= userPageTableBase);
            DebugStub.Assert(pageAddr < userPageTableLimit);
            PTE* pte = GetPTE(pageAddr);

            // If this faults, our address space wasn't set up right
            return pte->Valid;
        }

        //
        // Writes a new virtual -> physical address into the page table,
        // ASSUMING the right PT is already allocated and mapped.
        //
        private static unsafe void MapPageInternal(PhysicalAddress physAddr,
                                                   UIntPtr virtualAddr,
                                                   bool expectExistingMapping)
        {
            DebugStub.Assert(MemoryManager.IsPageAligned(virtualAddr));
            PTE* pte = GetPTE(virtualAddr);

            DebugStub.Assert(pte->Valid == expectExistingMapping,
                             "Unexpected mapping state");

            pte->Address = physAddr.Value;
            pte->User = true; // Ring3
            pte->Valid = true;
            pte->Writeable = true;
            pte->Global = virtualAddr < BootInfo.KERNEL_BOUNDARY;
            Processor.InvalidateTLBEntry(virtualAddr);
        }

        private static unsafe void MapPageInternal(PhysicalAddress physAddr,
                                                   UIntPtr virtualAddr)
        {
            // Expect to not overwrite an existing mapping
            MapPageInternal(physAddr, virtualAddr, false);
        }

        [Inline]
        [NoStackLinkCheck]
        private static bool ScratchLock()
        {
            bool enabled = Processor.DisableInterrupts();
#if SINGULARITY_MP
            scratchSpin.Acquire(Thread.CurrentThread);
#endif // SINGULARITY_MP
            return enabled;
        }

        [Inline]
        [NoStackLinkCheck]
        private static void ScratchUnlock(bool iflag)
        {
#if SINGULARITY_MP
            scratchSpin.Release(Thread.CurrentThread);
#endif // SINGULARITY_MP
            Processor.RestoreInterrupts(iflag);
        }
    }
}

#endif // PAGING
