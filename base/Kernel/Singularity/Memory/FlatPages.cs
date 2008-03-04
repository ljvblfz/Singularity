////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Legacy.cs - Primitive memory manager
//
//  Note:
//

#if !PAGING

//#define TEST
//#define VERBOSE
//#define MP_VERBOSE
//#define COMPLEX_TEST

#if MARKSWEEPCOLLECTOR
#define ALLOW_BOOT_ARGLIST // Cannot be used during boot for GCs w/ write barrier
#endif
#define NO__TRACE_PAGES

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.GCs;

using Microsoft.Singularity;
using Microsoft.Singularity.Hal; // for IHal


namespace Microsoft.Singularity.Memory
{
    [NoCCtor]
    [CLSCompliant(false)]
    public class FlatPages {

        // WARNING: don't initialize any static fields in this class
        // without manually running the class constructor at startup!
        //private const uint    PageMask = MemoryManager.PageSize - 1;
        private const uint    SmallSize = MemoryManager.PageSize;

        private static UIntPtr addressLimit;
        private static UIntPtr pageCount;
        private static ulong allocatedBytes;
        private static ulong allocatedCount;
        private static ulong freedBytes;
        private static ulong freedCount;
        private static SpinLock pageLock;

        private static unsafe uint *pageTable;

        // We keep two lists of free pages:
        // The freeList has pages that can be used at any moment.
        // The saveList has pages that were reserved for growing a region.

        private static FreeNode freeListTail;
        private static FreeNode saveListTail;
        private static unsafe FreeNode *freeList;
        private static unsafe FreeNode *saveList;


        // This is a representation of MemoryAffinity.  IMPORTANT: The
        // rule of the thumb is, always check "isValid" flag before
        // using any of the member variables. The reason is not all
        // subMemoryMap can be created from the initial freeList. The
        // caveat is some top memory addresses have been allocated at
        // boot process (e.g. from experiences almost 8 MB of memory
        // has been taken).  SRAT table usually gives separate entry
        // for the first 640 KB region.  Since the first 8 MB has gone
        // from the free list, we could not create the first
        // SubMemoryMap that represents the 640 KB region (hence
        // isValid is false for this SubMemoryMap).
        private struct SubMemoryMap
        {
            public int memoryId;
            public UIntPtr baseAddr;
            public UIntPtr endAddr;
            public UIntPtr length;
            public uint domain;
            public bool isValid;
        }

        // This is a per-processor memory map/address space.  Each
        // processor will have each own free list. Future memory
        // allocation for MP must consult this structure, the free
        // list in particular.  Note: Be careful that, the memory area
        // from baseAddr to endAddr does not necessary belong to this
        // processor. A processor is not guaranteed to have contiguous
        // memory addresses in NUMA node.  The "length" field
        // specifies how big of memory this processor has.  Other
        // Notes:
        //   . Maybe in the future we need to create an array of
        //     sub memory maps within this processorMemoryMap
        //   . Code for accounting has not been written
        private struct ProcessorMemoryMap
        {
            public int processorId;
            public uint domain;
            public UIntPtr baseAddr;
            public UIntPtr endAddr;
            public UIntPtr length;
            public FreeNode procFreeListTail;
            public FreeNode procSaveListTail;
            public unsafe FreeNode *procFreeList;
            public unsafe FreeNode *procSaveList;
            public bool isInitialized;
        }

        // This is a domain mapping for dividing memory across
        // processors evenly. Using this domain, we ensure that each
        // processor gets memory from the domain where it belongs.  If
        // SRAT table is not at available, we just create 1 domain.
        // Notes:
        //   . Currently, the code does not support a domain that does not
        //     have any memory. In other words, currently, we do not allow
        //     "borrowing" memory from other domains. If this is the case,
        //     an error will generated and a following DebugStub.Break
        private struct DomainMap
        {
            public uint domain;
            public ProcessorMemoryMap [] processors;
            public SubMemoryMap [] subMemories;
            public UIntPtr totalMemSize;
            public FreeNode domFreeListTail;
            public FreeNode domSaveListTail;
            public unsafe FreeNode *domFreeList;
            public unsafe FreeNode *domSaveList;
            public bool isSubMemConnected;
        }

        private static SubMemoryMap [] subMemoryMap;
        private static ProcessorMemoryMap [] processorMemoryMap;
        private static DomainMap [] domainMap;
        private static bool isProcessorMemoryInitialized;


        //////////////////////////////////////////////////////////////////
        //
        // haryadi: MP FlatPages routines start here.

        private static unsafe void PrintSubMemoryMap()
        {
            DebugStub.WriteLine("\n\n        SUB MEMORY MAP");
            DebugStub.WriteLine("        --------------------------------------------");
            for (int i = 0; i < subMemoryMap.Length; i++) {
                DebugStub.WriteLine("        [m{0}] b.{1:x8} e.{2:x8} l.{3:x8} d.{4} i.{5}",
                                    __arglist(subMemoryMap[i].memoryId,
                                              subMemoryMap[i].baseAddr,
                                              subMemoryMap[i].endAddr,
                                              subMemoryMap[i].length,
                                              subMemoryMap[i].domain,
                                              subMemoryMap[i].isValid));
            }
            DebugStub.WriteLine();
        }

        private static unsafe void PrintProcessorMemoryMap()
        {
            DebugStub.WriteLine("\n\n        PROCESSOR MEMORY MAP");
            DebugStub.WriteLine("        --------------------------------------------");

            for (int i = 0; i < processorMemoryMap.Length; i++) {
                DebugStub.WriteLine("        [p{0}] b.{1:x8} e.{2:x8} l.{3:x8} d.{4} f.{5:x8} s.{6:x8} i.{7:x8} ",
                                    __arglist(processorMemoryMap[i].processorId,
                                              processorMemoryMap[i].baseAddr,
                                              processorMemoryMap[i].endAddr,
                                              processorMemoryMap[i].length,
                                              processorMemoryMap[i].domain,
                                              (UIntPtr)processorMemoryMap[i].procFreeList,
                                              (UIntPtr)processorMemoryMap[i].procSaveList,
                                              processorMemoryMap[i].isInitialized));
            }
            DebugStub.WriteLine();
        }


        private static unsafe void PrintDomainMap()
        {
            DebugStub.WriteLine("\n\n        DOMAIN MAP");
            DebugStub.WriteLine("        --------------------------------------------");
            for (int i = 0; i < domainMap.Length; i++) {
                DebugStub.Print("        [d{0}]  ts.{1:x8}  dl.{2:x8}",
                                __arglist(i, domainMap[i].totalMemSize, (UIntPtr)domainMap[i].domFreeList));

                for (int j = 0; j < domainMap[i].processors.Length; j++) {
                    DebugStub.Print(" (p{0},{1:x8}) ", __arglist(domainMap[i].processors[j].processorId,
                                                                domainMap[i].processors[j].baseAddr));
                }
                for (int j = 0; j < domainMap[i].subMemories.Length; j++) {
                    DebugStub.Print(" (m{0},{1:x8}) ", __arglist(domainMap[i].subMemories[j].memoryId,
                                                        domainMap[i].subMemories[j].baseAddr));
                }
                DebugStub.WriteLine();
            }
            DebugStub.WriteLine();
        }

        private static unsafe void PrintAllMaps()
        {
            DebugStub.WriteLine("\n\n        **** PRINT ALL MAPS ****");
            PrintSubMemoryMap();
            PrintProcessorMemoryMap();
            PrintDomainMap();
            DebugStub.WriteLine();
        }

        // Create manually simple SubMemoryMap
        private static unsafe void PrepareSubMemoryMapSimpleTest()
        {
            int memoryCount = 5;
            subMemoryMap = new SubMemoryMap [memoryCount];
            fixed (SubMemoryMap *s = &(subMemoryMap[0])) {
                s->memoryId = 0;
                s->baseAddr = 0x0;
                s->endAddr  = 0x000a0000;  // 640 KB
                s->length   = s->endAddr - s->baseAddr;
                s->domain   = 0;
                s->isValid = false;
            }
            fixed (SubMemoryMap *s = &(subMemoryMap[1])) {
                s->memoryId = 1;
                s->baseAddr = 0x01000000; // 16 MB
                s->endAddr  = 0x20000000; // 512 MB
                s->length   = s->endAddr - s->baseAddr;
                s->domain   = 0;
                s->isValid = false;
            }
            fixed (SubMemoryMap *s = &(subMemoryMap[2])) {
                s->memoryId = 2;
                s->baseAddr = 0x20000000; // 512 MB
                s->endAddr  = 0x40000000; //   1 GB
                s->length   = s->endAddr - s->baseAddr;
                s->domain   = 1;
                s->isValid = false;
            }
            fixed (SubMemoryMap *s = &(subMemoryMap[3])) {
                s->memoryId = 3;
                s->baseAddr = 0x40000000; //   1 GB
                s->endAddr  = 0x60000000; // 1.5 GB
                s->length   = s->endAddr - s->baseAddr;
                s->domain   = 0;
                s->isValid = false;
            }
            fixed (SubMemoryMap *s = &(subMemoryMap[4])) {
                s->memoryId = 4;
                s->baseAddr = 0x60000000; // 1.5 GB
                s->endAddr  = 0x80000000; //   2 GB
                s->length   = s->endAddr - s->baseAddr;
                s->domain   = 1;
                s->isValid = false;
            }
        }

        // Create manually complex SubMemoryMap.
        // Current complexTest: 3 domains, 12 processors, 9 memories
        // 1 G: 0x4000_0000
        // 2 G: 0x8000_0000
        private static unsafe void PrepareSubMemoryMapComplexTest()
        {
            int memoryCount = 9;
            subMemoryMap = new SubMemoryMap [memoryCount];
            uint domain = 0;
            UIntPtr cur = 0;
            UIntPtr length = 0x04000000;

            for (int i = 0; i < memoryCount; i++) {
                fixed (SubMemoryMap *s = &(subMemoryMap[i])) {
                    s->memoryId = i;
                    s->baseAddr = cur;
                    s->endAddr  = cur+length;
                    s->length   = s->endAddr - s->baseAddr;
                    s->domain   = domain;
                    s->isValid  = false;

                    // the last one eat up everything
                    /*
                    if (i == memoryCount - 1) {
                        s->baseAddr = cur;
                        s->endAddr  = 0x80000000;
                        s->length   = s->endAddr - s->baseAddr;
                        }*/

                }
                cur += length;
                // flip domain, so that we have non-contiguous memory
                if (domain == 0) {
                    domain = 1;
                }
                else if (domain == 1){
                    domain = 2;
                }
                else {
                    domain = 0;
                }
            }
        }

        private static unsafe void PrepareSubMemoryMap()
        {
            // get memory banks
            IHalMemory.MemoryAffinity[] memories =
                Processor.GetMemoryAffinity();
            int memoryCount = memories.Length;
            subMemoryMap = new SubMemoryMap [memoryCount];
            for (int i = 0; i < subMemoryMap.Length; i++) {
                subMemoryMap[i].memoryId = i;
                subMemoryMap[i].baseAddr = memories[i].baseAddress;
                subMemoryMap[i].endAddr = memories[i].endAddress;
                subMemoryMap[i].length = memories[i].memorySize;
                subMemoryMap[i].domain = memories[i].domain;
                subMemoryMap[i].isValid = false;
            }
        }

        // If we don't have SRAT table, then we treat the whole memory
        // as 1 subMemoryMap. Since, we don't break the memory, so
        // isValid is set to true
        private static unsafe void PrepareSubMemoryMapNoAffinity()
        {
            subMemoryMap = new SubMemoryMap[1];
            subMemoryMap[0].memoryId = 0;
            subMemoryMap[0].baseAddr = 0x0;
            subMemoryMap[0].endAddr = GetMaxMemory();
            subMemoryMap[0].length = GetMaxMemory();
            subMemoryMap[0].domain = 0;
            subMemoryMap[0].isValid = true;
        }

        // Based on the SRAT table, we try to break original free list
        // into multiple free nodes. The rule is we break at the start
        // address of every sub memory map
        private static unsafe void CreateSubMemoryMap()
        {
            for (int mNum = 0; mNum < subMemoryMap.Length; mNum++) {
                subMemoryMap[mNum].isValid =
                    CreateSubMemory(mNum,
                                    subMemoryMap[mNum].baseAddr,
                                    subMemoryMap[mNum].endAddr);
                if (!subMemoryMap[mNum].isValid) {
#if MP_VERBOSE
                    DebugStub.WriteLine
                        ("    WARNING: SubMap-{0} [{1:x8}..{2:x8}] cannot be initialized",
                         __arglist(mNum,
                                   subMemoryMap[mNum].baseAddr,
                                   subMemoryMap[mNum].endAddr));
#endif
                }
            }
        }

        // First, given the base address, we find the free node
        // (curNode) that will be cut by the base address. In other
        // words, the curNode is the node that will be broken to 2
        // parts. If we could not find it, then curNode is null.
        // Second, we need to check if the memory area from
        // memBaseAddr to memEndAddr is intersecting with any free
        // node.  (See more detailed comment in IsPartialIntersect()
        // function). IsPartialIntersect will give the new breakAddr.
        // The corresponding subMemoryMap's base address also must be
        // updated with the new breakAddr.  If the two conditions
        // above fail. Then this subMemory cannot be initialized.
        private static unsafe bool CreateSubMemory(int memoryNumber,
                                                   UIntPtr memBaseAddr,
                                                   UIntPtr memEndAddr)
        {
            // always break at the memBaseAddr
            UIntPtr breakAddr = memBaseAddr;

#if MP_VERBOSE
            DebugStub.WriteLine("\n    SubMap[{0}]: Creating at {1:x8}",
                                __arglist(memoryNumber,breakAddr));
#endif
            FreeNode* curNode = FreeNode.GetFreeNodeAtBreakAddr(freeList, breakAddr);
            if (curNode == null) {
                // now check just in case the bottom part of this
                // subMem is intersect with one of the free list node
                breakAddr = FreeNode.IsPartialIntersect(freeList, memBaseAddr, memEndAddr);
                curNode = FreeNode.GetFreeNodeAtBreakAddr(freeList, breakAddr);

                if (curNode == null) {
                    return false;
                }

                // update base address
                if (breakAddr != 0) {
                    subMemoryMap[memoryNumber].baseAddr = breakAddr;
                }
            }
#if MP_VERBOSE
            DebugStub.WriteLine("    SubMap[{0}]: braking list at {1.x8}",
                                __arglist(memoryNumber, breakAddr));
#endif
            FreeNode.BreakListAt(freeList, curNode, breakAddr);
            return true;
        }

        private static unsafe void PrepareProcessorMemoryMapNoAffinity()
        {
            int processorCount = Processor.GetProcessorCount();
            processorMemoryMap = new ProcessorMemoryMap [processorCount];
            PrepareProcessorMemoryMapCommonFields();
        }

        private static unsafe void PrepareProcessorMemoryMap()
        {
            IHalMemory.ProcessorAffinity [] halProcessors =
                Processor.GetProcessorAffinity();
            int processorCount = halProcessors.Length;
            processorMemoryMap = new ProcessorMemoryMap [processorCount];
            PrepareProcessorMemoryMapCommonFields();

            // update domain
            for (int i = 0; i < processorCount; i++) {
                processorMemoryMap[i].domain = halProcessors[i].domain;
            }
        }

        private static unsafe void PrepareProcessorMemoryMapCommonFields()
        {
            for (int i=0; i < processorMemoryMap.Length; i++) {
                processorMemoryMap[i].domain = 0;
                processorMemoryMap[i].processorId = i;
                processorMemoryMap[i].baseAddr = 0x0;
                processorMemoryMap[i].endAddr = 0x0;
                processorMemoryMap[i].length = 0x0;

                // Initialize the free and save lists.
                fixed (FreeNode *tail = &(processorMemoryMap[i].procFreeListTail)) {
                    processorMemoryMap[i].procFreeList = tail;
                    FreeNode.Init(processorMemoryMap[i].procFreeList, false);
                }
                fixed (FreeNode *tail = &(processorMemoryMap[i].procSaveListTail)) {
                    processorMemoryMap[i].procSaveList = tail;
                    FreeNode.Init(processorMemoryMap[i].procSaveList, true);
                }
            }
        }

        private static unsafe void PrepareProcessorMemoryMapComplexTest()
        {
            int processorCount = 12;
            uint domain = 0;
            processorMemoryMap = new ProcessorMemoryMap [processorCount];
            for (int i=0; i < processorCount; i++) {
                if (i == 4) {
                    domain++;
                }
                if (i == 8) {
                    domain++;
                }
                processorMemoryMap[i].domain = domain;
                processorMemoryMap[i].processorId = i;
                processorMemoryMap[i].baseAddr = 0x0;
                processorMemoryMap[i].endAddr = 0x0;
                processorMemoryMap[i].length = 0x0;

                // Initialize the free and save lists.
                fixed (FreeNode *tail = &(processorMemoryMap[i].procFreeListTail)) {
                    processorMemoryMap[i].procFreeList = tail;
                    FreeNode.Init(processorMemoryMap[i].procFreeList, false);
                }
                fixed (FreeNode *tail = &(processorMemoryMap[i].procSaveListTail)) {
                    processorMemoryMap[i].procSaveList = tail;
                    FreeNode.Init(processorMemoryMap[i].procSaveList, true);
                }
            }
        }

        private static unsafe void PrepareDomainMapCommonFields()
        {
            for (int i = 0; i < domainMap.Length; i ++) {
                domainMap[i].domain = (uint)i;
                domainMap[i].isSubMemConnected = false;
                // Initialize the free and save lists.
                fixed (FreeNode *tail = &(domainMap[i].domFreeListTail)) {
                    domainMap[i].domFreeList = tail;
                    FreeNode.Init(domainMap[i].domFreeList, false);
                }
                fixed (FreeNode *tail = &(domainMap[i].domSaveListTail)) {
                    domainMap[i].domSaveList = tail;
                    FreeNode.Init(domainMap[i].domSaveList, true);
                }
            }
        }

        // Just attach processors and memories if we don't have SRAT table
        private static unsafe void PrepareDomainMapNoAffinity()
        {
            domainMap = new DomainMap [1];
            PrepareDomainMapCommonFields();
            domainMap[0].processors = processorMemoryMap;
            domainMap[0].subMemories = subMemoryMap;
            domainMap[0].totalMemSize = 0;
            for (int i = 0; i < subMemoryMap.Length; i++) {
                domainMap[0].totalMemSize += subMemoryMap[i].length;
            }
        }

        // Per Domain: Traverse the processor and memory maps, and put
        // then in domainMap according to their domain numbers
        private static unsafe void PrepareDomainMap(int domainCount)
        {
            int count;
            domainMap = new DomainMap [domainCount];
            PrepareDomainMapCommonFields();

            for (int i = 0; i < domainCount; i ++) {
                domainMap[i].totalMemSize = 0;
                // processor, 1st pass, count
                count = 0;
                for (int j = 0; j < processorMemoryMap.Length; j++) {
                    if (processorMemoryMap[j].domain == domainMap[i].domain) {
                        count++;
                    }
                }

                domainMap[i].processors = new ProcessorMemoryMap[count];

                // processor, 2nd pass, count
                count = 0;
                for (int j = 0; j < processorMemoryMap.Length; j++) {
                    if (processorMemoryMap[j].domain == domainMap[i].domain) {
                        domainMap[i].processors[count] = processorMemoryMap[j];
                        count++;
                    }
                }

                // sub, 1st pass, count
                count = 0;
                for (int j = 0; j < subMemoryMap.Length; j++) {
                    if (subMemoryMap[j].domain == domainMap[i].domain) {
                        count++;
                    }
                }

                domainMap[i].subMemories = new SubMemoryMap[count];

                // sub, 2nd pass, count
                count = 0;
                for (int j = 0; j < subMemoryMap.Length; j++) {
                    if (subMemoryMap[j].domain == domainMap[i].domain) {
                        domainMap[i].subMemories[count] = subMemoryMap[j];
                        domainMap[i].totalMemSize += subMemoryMap[j].length;
                        count++;
                    }
                }
            }
        }

        // Basically, this function grab the original free list and
        // and attach it to the domain tail free list. After this
        // function is called, we should no longer use the original
        // free list
        private static unsafe void ConnectSubMemoriesPerDomainNoAffinity()
        {
            FreeNode *dom = domainMap[0].domFreeList;
            FreeNode *first = freeList->next;
            FreeNode *last = freeList->prev;

            dom->next = first;
            dom->prev = last;
            first->prev = dom;
            first->next = dom;

            domainMap[0].isSubMemConnected = true;;

#if MP_VERBOSE
            DebugStub.WriteLine("\n\n    Connect memory no affinity: ");
            DebugStub.WriteLine("    dl.{0:x8}  dn.{1:x8}  dp.{2:x8}",
                                __arglist((UIntPtr)dom,
                                          (UIntPtr)dom->next,
                                          (UIntPtr)dom->prev));
            DebugStub.WriteLine("    ff.{0:x8}  fn.{1:x8}  fp.{2:x8}",
                                __arglist((UIntPtr)first,
                                          (UIntPtr)first->next,
                                          (UIntPtr)first->prev));
            DebugStub.WriteLine("    ll.{0:x8}  ln.{1:x8}  lp.{2:x8}",
                                __arglist((UIntPtr)last,
                                          (UIntPtr)last->next,
                                          (UIntPtr)last->prev));
#endif
        }

        private static unsafe void ConnectSubMemoriesPerDomain()
        {
            for (int i = 0; i < domainMap.Length; i++) {
#if MP_VERBOSE
                DebugStub.WriteLine("\n    Domain [{0}]:", __arglist(i));
#endif
                ConnectSubMemoriesInDomain(domainMap[i]);
                domainMap[i].isSubMemConnected = true;
            }
        }

        // At this point, the original free list should have been
        // partitioned according to the subMemoryMap. Now, we attach
        // the sub memory maps to their corresponding domain free
        // list.  After this function is called, we should no longer
        // use the original free list
        private static unsafe void ConnectSubMemoriesInDomain(DomainMap dMap)
        {
            if (dMap.subMemories.Length == 0) {
                DebugStub.WriteLine("\n\n    **** ERROR, one of the domain does not have memory ****");
                DebugStub.WriteLine("\n\n    ****        this is not currently supported ****");
                DebugStub.Break();
            }

#if MP_VERBOSE
            DebugStub.WriteLine("\n    Connection SubMemories in Domain {0}:",
                                __arglist(dMap.domain));
            DebugStub.WriteLine("    -----------------------------------------------");
#endif

            FreeNode *domTailNode = dMap.domFreeList;
            FreeNode *curNode;
            FreeNode *prevNode = null;
            int validMem = 0;
            int validMemCount = 0;

#if MP_VERBOSE
            DebugStub.WriteLine("    Checking valid memory map:");
#endif
            for (int i = 0; i < dMap.subMemories.Length; i++) {
                if (dMap.subMemories[i].isValid) {

#if MP_VERBOSE
                    DebugStub.WriteLine("        Valid: sm{0}  smb.{1:x8}  sme.{2:x8}",
                                        __arglist(i,
                                                  dMap.subMemories[i].baseAddr,
                                                  dMap.subMemories[i].endAddr));
#endif
                    validMemCount++;
                }
            }

#if MP_VERBOSE
            DebugStub.WriteLine("\n    Connecting sub memories:");
#endif
            for (int i = 0; i < dMap.subMemories.Length; i++) {

                // if not valid continue
                if (!dMap.subMemories[i].isValid) {
                    continue;
                }

                // this is wrong
                curNode = (FreeNode*) dMap.subMemories[i].baseAddr;

#if MP_VERBOSE
                DebugStub.WriteLine("\n        [{0}].  curNode is at [base.{1:x8}]",
                                    __arglist(validMem, (UIntPtr)curNode));
#endif

                // if this is the first valid memory, update the head
                // of the linked list
                if (validMem == 0) {

                    domTailNode->next = curNode;
                    curNode->prev = domTailNode;

#if MP_VERBOSE
                    DebugStub.WriteLine("        [{0}].  [d.{1:x8}] dn.{2:x8} = c.{3:x8}",
                                        __arglist(validMem,
                                                  (UIntPtr)domTailNode,
                                                  (UIntPtr)domTailNode->next,
                                                  (UIntPtr)curNode));
                    DebugStub.WriteLine("        [{0}].  [c.{1:x8}] cp.{2:x8} = d.{3:x8}",
                                        __arglist(validMem,
                                                  (UIntPtr)curNode,
                                                  (UIntPtr)curNode->prev,
                                                  (UIntPtr)domTailNode));
#endif
                }

                // this is the last valid memory, update the tail of
                // the linked list
                if (validMem == validMemCount - 1) {

                    if (prevNode != null) {
                        prevNode->next = curNode;
                        curNode->prev = prevNode;

#if MP_VERBOSE
                        DebugStub.WriteLine("        [{0}].  [p.{1:x8}] pn.{2:x8} = c.{3:x8}",
                                            __arglist(validMem,
                                                      (UIntPtr)prevNode,
                                                      (UIntPtr)prevNode->next,
                                                      (UIntPtr)curNode));

                        DebugStub.WriteLine("        [{0}].  [c.{1:x8}] cp.{2:x8} = p.{3:x8}",
                                            __arglist(validMem,
                                                      (UIntPtr)curNode,
                                                      (UIntPtr)curNode->prev,
                                                      (UIntPtr)prevNode));
#endif
                    }

                    domTailNode->prev = curNode;
                    curNode->next = domTailNode;

#if MP_VERBOSE
                    DebugStub.WriteLine("        [{0}].  [d.{1:x8}] dp.{2:x8} = c.{3:x8}",
                                        __arglist(validMem,
                                                  (UIntPtr)domTailNode,
                                                  (UIntPtr)domTailNode->prev,
                                                  (UIntPtr)curNode));
                    DebugStub.WriteLine("        [{0}].  [c.{1:x8}] cn.{2:x8} = d.{3:x8}",
                                        __arglist(validMem,
                                                  (UIntPtr)curNode,
                                                  (UIntPtr)curNode->next,
                                                  (UIntPtr)domTailNode));
#endif
                }

                // else this is the middle
                if (validMem > 0 && validMem < validMemCount - 1) {

                    prevNode->next = curNode;
                    curNode->prev = prevNode;

#if MP_VERBOSE
                    DebugStub.WriteLine("        [{0}].  [p.{1:x8}] pn.{2:x8} = c.{3:x8}",
                                        __arglist(validMem,
                                                  (UIntPtr)prevNode,
                                                  (UIntPtr)prevNode->next,
                                                  (UIntPtr)curNode));

                    DebugStub.WriteLine("        [{0}].  [c.{1:x8}] cp.{2:x8} = p.{3:x8}",
                                        __arglist(validMem,
                                                  (UIntPtr)curNode,
                                                  (UIntPtr)curNode->prev,
                                                  (UIntPtr)prevNode));
#endif

                }
                prevNode = curNode;
                validMem++;
            }
        }

        // Since, a processor might not have a contiguous memory, this
        // function performs the conversion from the relativeAddr to th
        // realAddr. For example if a processor has 20 bytes memory at
        // mem[0..10] and mem[20..30], a relative address of mem[15] will
        // be converted to the real address mem[25]
        private static unsafe UIntPtr GetRealBaseAddrInDomainMap(DomainMap dMap, UIntPtr relativeAddr)
        {
            for (int i = 0; i < dMap.subMemories.Length; i++) {

                // We should not take into account subMemories
                // that are not valid. Remember that the first
                // subMemory usually can not be created because that
                // part of the memory is not in the original free list
                if (!dMap.subMemories[i].isValid) {
                    continue;
                }

                if (relativeAddr < dMap.subMemories[i].length) {
                    return (dMap.subMemories[i].baseAddr + relativeAddr);
                }
                relativeAddr = relativeAddr - dMap.subMemories[i].length;
            }

            DebugStub.WriteLine("\n\n    **** ERROR relativeAddr.{0:x8} is to big??, overflow ****",
                                __arglist(relativeAddr));
            DebugStub.Break();
            return 0;
        }

        // Convert relative end addr
        private static unsafe UIntPtr GetRealEndAddrInDomainMap (DomainMap dMap, UIntPtr relativeAddr)
        {
            for (int i = 0; i < dMap.subMemories.Length; i++) {

                if (!dMap.subMemories[i].isValid) {
                    continue;
                }

                if (relativeAddr <= dMap.subMemories[i].length) {
                    return (dMap.subMemories[i].baseAddr + relativeAddr);
                }
                relativeAddr = relativeAddr - dMap.subMemories[i].length;
            }
            DebugStub.WriteLine("\n\n    **** ERROR relativeAddr.{0:x8} is to big??, overflow ****",
                                __arglist(relativeAddr));
            DebugStub.Break();
            return 0;
        }

        // This is for rounding. Consider 1 memory, and 3 processors.
        // If first two processor get 0.33 of the whole memories, the
        // last one gets 0.34.
        private static unsafe UIntPtr GetLastAddrInDomainMap (DomainMap dMap)
        {
            return dMap.subMemories[dMap.subMemories.Length - 1].endAddr;
        }

        // For each Domain dMap, we will partitioned the memories in this
        // domain across the processors in the same domain.
        private static unsafe void CreatePerProcessorMemoryInDomain(DomainMap dMap)
        {
            int processorCount = dMap.processors.Length;
            UIntPtr pageCount = dMap.totalMemSize >> MemoryManager.PageBits;
            UIntPtr pagePerProcessor =
                (UIntPtr)((ulong)pageCount / (ulong)processorCount);
            UIntPtr curPage = 0;
            FreeNode *curNode;
            UIntPtr breakAddr;

#if MP_VERBOSE
            DebugStub.WriteLine("\n\n    Creating Domain [{0}]",
                                __arglist(dMap.domain));
            DebugStub.WriteLine("    ---------------------------------------");
            DebugStub.WriteLine("    Total MemSize : {0:x8}",
                                __arglist(dMap.totalMemSize));
            DebugStub.WriteLine("    Page Count    : {0}",
                                __arglist(pageCount));
            DebugStub.WriteLine("    Processor Cnt : {0}",
                                __arglist(processorCount));
            DebugStub.WriteLine("    Page/Proc     : {0}",
                                __arglist(pagePerProcessor));
            DebugStub.WriteLine("    DomFreeList   : {0:x8}",
                                __arglist((UIntPtr)dMap.domFreeList));
#endif

            for (int i = 0; i < processorCount; i++) {

#if MP_VERBOSE
                DebugStub.WriteLine("\n\n        PROCESSOR-{0}", __arglist(i));
                DebugStub.WriteLine("        -------------------------------");
#endif

                dMap.processors[i].baseAddr =
                    GetRealBaseAddrInDomainMap(dMap,
                                               curPage <<
                                               MemoryManager.PageBits);

#if MP_VERBOSE
                DebugStub.WriteLine("        GetRealAddr: curPage,{0} --> baseAddr.{1:x8}",
                                    __arglist(curPage,
                                              dMap.processors[i].baseAddr));
#endif

                // if last processor, take all what is left
                if (i == processorCount - 1) {
                    dMap.processors[i].endAddr = GetLastAddrInDomainMap(dMap);

                    // is not necessary contiguous
                    dMap.processors[i].length =
                        (pageCount - curPage) << MemoryManager.PageBits;
#if MP_VERBOSE
                    DebugStub.WriteLine("        LastProcessor in Domain gets all");
#endif
                }
                else {
                    dMap.processors[i].endAddr =
                        GetRealEndAddrInDomainMap(dMap,
                                                  (curPage + pagePerProcessor)
                                                  << MemoryManager.PageBits);

                    // is not necessary contiguous
                    dMap.processors[i].length =
                        (pagePerProcessor << MemoryManager.PageBits);
                }

#if MP_VERBOSE
                DebugStub.WriteLine("        GetEndAddr : curPage.{0} --> endAddr.{1:x8}, length.{2:x8}",
                                    __arglist(curPage,
                                              dMap.processors[i].endAddr,
                                              dMap.processors[i].length));
#endif

                // now, let's break at the start addr
                breakAddr = dMap.processors[i].baseAddr;
                curNode = FreeNode.GetFreeNodeAtBreakAddr(dMap.domFreeList, breakAddr);

                if (curNode != null) {
#if MP_VERBOSE
                    DebugStub.WriteLine("        Breaking at StartAddr.{0:x8}",
                                        __arglist(breakAddr));
                    DebugStub.WriteLine("        curNode found around StartAddr: node.{0:x8}  prev.{1:x8}  next.{2:x8}",
                                        __arglist((UIntPtr)curNode,
                                                  (UIntPtr)curNode->prev,
                                                  (UIntPtr)curNode->next));
#endif
                    FreeNode.BreakListAt(dMap.domFreeList, curNode, breakAddr);
                }
                else {
#if MP_VERBOSE
                    DebugStub.WriteLine("        Breaking at StartAddr.{0:x8} -- cancelled, can't find freeNode",
                                        __arglist(breakAddr));
#endif
                }

                // don't forget to add current page
                curPage += pagePerProcessor;

#if MP_VERBOSE
                DebugStub.WriteLine("        Processor[{0}] initialized at base.{0:x8}  end.{1:x8}  length.{2:x8}",
                                    __arglist(dMap.processors[i].baseAddr,
                                              dMap.processors[i].endAddr,
                                              dMap.processors[i].length));
#endif

            }
        }

        // Note that this function only performs computation. I.e.
        // it calculates the memory ranges that a process will be given.
        // It does not steal the free list yet.
        private static unsafe void CreatePerProcessorMemory()
        {
            for (int i = 0; i < domainMap.Length; i++) {
                CreatePerProcessorMemoryInDomain(domainMap[i]);
            }
        }

        // just copy back some fields, because domainMap.processors and
        // processorMemoryMap do not point to the same object?????
        private static unsafe void CopyDomainProcessorsToProcessors()
        {
            for (int i = 0; i < processorMemoryMap.Length; i++) {
                for (int j = 0; j < domainMap.Length; j++) {
                    for (int k = 0; k < domainMap[j].processors.Length; k++) {
                        // same, copy
                        if (processorMemoryMap[i].processorId ==
                            domainMap[j].processors[k].processorId) {

                            processorMemoryMap[i].domain =
                                domainMap[j].processors[k].domain;
                            processorMemoryMap[i].baseAddr =
                                domainMap[j].processors[k].baseAddr;
                            processorMemoryMap[i].endAddr =
                                domainMap[j].processors[k].endAddr;
                            processorMemoryMap[i].length =
                                domainMap[j].processors[k].length;
                        }
                    }
                }
            }
        }

        // Get lowest free node from all domains
        private static unsafe FreeNode* GetLowestNodeFromDomains()
        {
            // how about in 64 bits architecture??
            UIntPtr lowest = (UIntPtr) 0xffffffff;

            FreeNode *tail = null;
            FreeNode *first = null;
            for (int i = 0; i < domainMap.Length; i++) {
                tail = domainMap[i].domFreeList;
                first = tail->next;

                // need to check if tail == first, then we have problem:
                // there is no free list!!
                if (tail == first) {
                    DebugStub.WriteLine("\n\n****** ERROR ******");
                    DebugStub.WriteLine("GetLow: Domain [{0}] has no free list at tail {1:x8}",
                                        __arglist(i,(UIntPtr)tail));
                    DebugStub.Break();
                }


                if ((UIntPtr)first < lowest) {
                    lowest = (UIntPtr)first;
                }
            }
            return (FreeNode*)lowest;
        }

        // Get highest node from all domains
        private static unsafe FreeNode* GetHighestNodeFromDomains()
        {
            UIntPtr highest = (UIntPtr) 0x0;
            FreeNode *tail = null;
            FreeNode *last = null;
            for (int i = 0; i < domainMap.Length; i++) {
                tail = domainMap[i].domFreeList;
                last = tail->prev;
                if (tail == last) {
                    DebugStub.WriteLine("\n\n****** ERROR ******");
                    DebugStub.WriteLine("GetHigh: Domain [{0}] has no free list at tail {1:x8}",
                                        __arglist(i,(UIntPtr)tail));
                    DebugStub.Break();
                }
                if ((UIntPtr)last > highest) {
                    highest = (UIntPtr)last;
                }
            }
            return (FreeNode*)highest;
        }

        // At this point, each processor should know the baseAddr of
        // the memory ranges that it should have. Now, we need to
        // partitioned the domain's free lists for the processors in
        // the domain.  This is a similar operation that we did when
        // we partition the original free list to sub-memories. The
        // way we break it here, is we will break the free list at
        // each processor's baseAddr. After we break the domain's free
        // list, we steal the free list and attach it to the
        // corresponding processors' free list.
        private static unsafe void AttachPerProcessorMemoryToFreeListTail()
        {
            FreeNode *procTailNode;
            FreeNode *firstNode;
            LastNode *last;
            FreeNode *lastNode;
            FreeNode *prevNode = null;

            for (int i = 0; i < processorMemoryMap.Length; i++) {

                procTailNode = processorMemoryMap[i].procFreeList;

                // Special case: The edges of the memory. i.e.
                // memories of processor[0] and processor[lastProc]
                if (i == 0) {
                    firstNode = GetLowestNodeFromDomains();
                    last = (LastNode*) (processorMemoryMap[i].endAddr - MemoryManager.PageSize);
                    lastNode = last->node;
                }
                else if (i == processorMemoryMap.Length - 1) {
                    firstNode = (FreeNode*) processorMemoryMap[i].baseAddr;
                    last = null;
                    lastNode = GetHighestNodeFromDomains();
                }
                else {
                    firstNode = (FreeNode*) processorMemoryMap[i].baseAddr;
                    last = (LastNode*) (processorMemoryMap[i].endAddr - MemoryManager.PageSize);
                    lastNode = last->node;
                }



                // if processor is the lowest
#if MP_VERBOSE
                DebugStub.WriteLine();
                DebugStub.WriteLine("\n    Attaching Processor[{0}]", __arglist(i));
                DebugStub.WriteLine("    -------------------------------------------");

                DebugStub.WriteLine("    firstNode    = {0:x8}", __arglist((UIntPtr)firstNode));
                DebugStub.WriteLine("    last         = {0:x8}", __arglist((UIntPtr)last));
                DebugStub.WriteLine("    lastNode     = {0:x8}", __arglist((UIntPtr)lastNode));
                DebugStub.WriteLine("    procTailNode = {0:x8}", __arglist((UIntPtr)procTailNode));
                DebugStub.WriteLine("    procTailNode = {0:x8}", __arglist((UIntPtr)procTailNode));

                DebugStub.WriteLine("\n    Before Attaching: \n");

                if (last != null) {
                    DebugStub.WriteLine("        last       a.{0:x8}  n.{1:x8} ",
                                        __arglist((UIntPtr)last, (UIntPtr)last->node));
                }
                DebugStub.WriteLine("        procTail   a.{0:x8}  p.{1:x8}  n.{2:x8}  l.{3:x8} ",
                                    __arglist((UIntPtr)procTailNode, (UIntPtr)procTailNode->prev,
                                              (UIntPtr)procTailNode->next, (UIntPtr)procTailNode->last));

                DebugStub.WriteLine("        firstNode  a.{0:x8}  p.{1:x8}  n.{2:x8}  l.{3:x8} ",
                                    __arglist((UIntPtr)firstNode, (UIntPtr)firstNode->prev,
                                              (UIntPtr)firstNode->next, (UIntPtr)firstNode->last));

                DebugStub.WriteLine("        lastNode   a.{0:x8}  p.{1:x8}  n.{2:x8}  l.{3:x8} ",
                                    __arglist((UIntPtr)lastNode, (UIntPtr)lastNode->prev,
                                              (UIntPtr)lastNode->next, (UIntPtr)lastNode->last));
#endif

                // set heads
                procTailNode->next = firstNode;
                firstNode->prev = procTailNode;

                // set tails
                procTailNode->prev = lastNode;
                lastNode->next = procTailNode;

                processorMemoryMap[i].isInitialized = true;


#if MP_VERBOSE
                DebugStub.WriteLine("\n    After Attaching: \n");

                if (last != null) {
                    DebugStub.WriteLine("        last       a.{0:x8}  n.{1:x8} ",
                                        __arglist((UIntPtr)last, (UIntPtr)last->node));
                }
                DebugStub.WriteLine("        procTail   a.{0:x8}  p.{1:x8}  n.{2:x8}  l.{3:x8} ",
                                    __arglist((UIntPtr)procTailNode, (UIntPtr)procTailNode->prev,
                                              (UIntPtr)procTailNode->next, (UIntPtr)procTailNode->last));

                DebugStub.WriteLine("        firstNode  a.{0:x8}  p.{1:x8}  n.{2:x8}  l.{3:x8} ",
                                    __arglist((UIntPtr)firstNode, (UIntPtr)firstNode->prev,
                                              (UIntPtr)firstNode->next, (UIntPtr)firstNode->last));

                DebugStub.WriteLine("        lastNode  a.{0:x8}  p.{1:x8}  n.{2:x8}  l.{3:x8} ",
                                    __arglist((UIntPtr)lastNode, (UIntPtr)lastNode->prev,
                                              (UIntPtr)lastNode->next, (UIntPtr)lastNode->last));
#endif
            }
        }


        private static unsafe void DebugMpPhase(int phase)
        {
#if MP_VERBOSE
            DebugStub.WriteLine("\n\n");
            DebugStub.Print("PHASE {0}: ", __arglist(phase));
            switch (phase) {
              case 0:
                DebugStub.WriteLine("MP FLAT-PAGES START");
                break;
              case 1:
                FreeNode.PrintFreeList(freeList);
                DebugStub.WriteLine("PREPARE SUB MEMORY MAP");
                break;
              case 2:
                DebugStub.WriteLine("CREATE SUB MEMORY MAP");
                break;
              case 3:
                PrintSubMemoryMap();
                FreeNode.PrintFreeList(freeList);
                DebugStub.WriteLine("PREPARE PROCESSOR MEM MAP");
                break;
              case 4:
                PrintProcessorMemoryMap();
                DebugStub.WriteLine("PREPARE DOMAIN MAPPING");
                break;
              case 5:
                PrintDomainMap();
                PrintSubMemoryMap();
                DebugStub.WriteLine("CREATE SUB MEM PER DOMAIN:");
                break;
              case 6:
                FreeNode.PrintDomainFreeLists();
                DebugStub.WriteLine("CREATE PER PROC MEMORY"); break;
              case 7:
                FreeNode.PrintDomainFreeLists();
                DebugStub.WriteLine("COPY DOMAIN TO PMAP");
                break;
              case 8:
                PrintAllMaps();
                DebugStub.WriteLine("ATTACH PROC FREE LIST");
                break;
              case 9:
                PrintAllMaps();
                FreeNode.PrintProcessorFreeLists();
                DebugStub.WriteLine("MP FLAT-PAGES DONE"); break;
              default: DebugStub.WriteLine(); break;
            }
            DebugStub.WriteLine("*************************************");
#endif
        }


        // At this point, all subMemories can be considered
        // independent. Even though, they are all still
        // full-linked under freeList we are going to break the
        // links.
        internal static unsafe void InitializeProcessorAddressSpace()
        {
            bool hasAffinityInfo = Processor.HasAffinityInfo();

            DebugMpPhase(0);

#if COMPLEX_TEST
            DebugMpPhase(1);
            PrepareSubMemoryMapComplexTest();

            DebugMpPhase(2);
            CreateSubMemoryMap();

            DebugMpPhase(3);
            PrepareProcessorMemoryMapComplexTest();

            DebugMpPhase(4);
            PrepareDomainMap(3);

            DebugMpPhase(5);
            ConnectSubMemoriesPerDomain();
#else
            if (!hasAffinityInfo) {

                DebugMpPhase(1);
                PrepareSubMemoryMapNoAffinity();

                // skip Phase 2, since only has 1 sub memory

                DebugMpPhase(3);
                PrepareProcessorMemoryMapNoAffinity();

                DebugMpPhase(4);
                PrepareDomainMapNoAffinity();

                DebugMpPhase(5);
                ConnectSubMemoriesPerDomainNoAffinity();
            }
            else {
                DebugMpPhase(1);
                PrepareSubMemoryMap();

                DebugMpPhase(2);
                CreateSubMemoryMap();

                DebugMpPhase(3);
                PrepareProcessorMemoryMap();

                DebugMpPhase(4);
                PrepareDomainMap(Processor.GetDomainCount());

                DebugMpPhase(5);
                ConnectSubMemoriesPerDomain();
            }
#endif

            // At this point, domain is ready, then we can break the
            // each domain's sub memories across the processors in the
            // domain

            DebugMpPhase(6);
            CreatePerProcessorMemory();

            DebugMpPhase(7);
            CopyDomainProcessorsToProcessors();

            DebugMpPhase(8);
            AttachPerProcessorMemoryToFreeListTail();

            // Reset back processor[0].baseAddr = 0. This is a hack for
            // now, the top part of the memory is already gone during
            // MP FlatPage initialization.
            processorMemoryMap[0].length += processorMemoryMap[0].baseAddr;
            processorMemoryMap[0].baseAddr = 0;

            // Each Processor's memory is ready
            isProcessorMemoryInitialized = true;

            DebugMpPhase(9);

            // Final check, dump to debugger
            FreeNode.PrintProcessorsAddressSpaces();
        }


        internal static unsafe void Initialize()
        {
            Tracing.Log(Tracing.Debug, "FlatPages.Initialize() called");

            InitializeLock();

            BootInfo * bi = BootInfo.HalGetBootInfo();

            isProcessorMemoryInitialized = false;

            // First pass over SMAP, find the highest RAM address
            SMAPINFO *smap = (SMAPINFO*)bi->SmapData32;
            addressLimit = UIntPtr.Zero;
            for (uint i = 0; i < bi->SmapCount; i++) {
                if (smap[i].type == (ulong)SMAPINFO.AddressType.Free &&
                    smap[i].addr + smap[i].size > addressLimit) {
                    addressLimit = smap[i].addr + smap[i].size;
                }

                unchecked {
                    Tracing.Log(Tracing.Debug,
                                "   [{0,8:x8}..{1,8:x8}] = {2,8:x8}",
                                (UIntPtr)(uint)smap[i].addr,
                                (UIntPtr)(uint)(smap[i].addr + smap[i].size),
                                (UIntPtr)(uint)smap[i].type);
                }
            }
            pageCount = Pad((addressLimit >> MemoryManager.PageBits) + 1, MemoryManager.PageSize / sizeof(uint));
            UIntPtr limit = Pad(bi->DumpLimit, 0x200000);

            Tracing.Log(Tracing.Debug,
                        "Limit of RAM={0,8:x}, entries={1:x}, table={2:x}",
                        addressLimit, pageCount, limit);

            // Create the page descriptor table.
            pageTable = (uint *)limit;

            // Initialize all page descriptors to Unknown.
            SetPages(0, pageCount, MemoryManager.PageUnknown);

            // Second pass over SMAP, mark known RAM.
            for (uint i = 0; i < bi->SmapCount; i++) {
                if (smap[i].type == (ulong)SMAPINFO.AddressType.Free) {
                    SetRange(smap[i].addr, smap[i].size, MemoryManager.PageFree);
                }
            }

            // Record the page table memory.
            SetRange(limit, pageCount * sizeof(uint), MemoryManager.KernelPageNonGC);

            // Record the kernel memory.
            SetRange(0x0, BootInfo.KERNEL_STACK_BEGIN, MemoryManager.KernelPageImage);
            SetRange(bi->DumpBase, limit - bi->DumpBase, MemoryManager.KernelPageNonGC);
            SetRange(BootInfo.KERNEL_STACK_BEGIN,
                     BootInfo.KERNEL_STACK_LIMIT - BootInfo.KERNEL_STACK_BEGIN,
                     MemoryManager.KernelPageStack);

            // Note, normally filtered out by boot loader.
            // SetRange(bi->DumpAddr32, bi->DumpAddr32 + bi->DumpSize32, MemoryManager.PageUnknown);

            // Third pass over SMAP, mark hardware reserved memory as Unknown.
            for (uint i = 0; i < bi->SmapCount; i++) {
                if (smap[i].type != (ulong)SMAPINFO.AddressType.Free &&
                    smap[i].addr < addressLimit) {
                    SetRange(smap[i].addr, smap[i].size, MemoryManager.PageUnknown);
                }
            }

            // Initialize the free and save lists.
            fixed (FreeNode *tail = &freeListTail) {
                freeList = tail;
                FreeNode.Init(freeList, false);
            }
            fixed (FreeNode *tail = &saveListTail) {
                saveList = tail;
                FreeNode.Init(saveList, true);
            }

            uint *desc = pageTable;
            uint last = *desc;
            UIntPtr begin = UIntPtr.Zero;

            for (UIntPtr i = UIntPtr.Zero; i < pageCount; i++) {
                uint val = *desc++ & MemoryManager.SystemPageMask;

                if (val != last) {
                    if (last == MemoryManager.PageFree) {
                        FreeNode.CreateAndInsert(freeList,
                                                 AddrFromPage(begin),
                                                 AddrFromPage(i - begin));
                    }
                    begin = i;
                    last = val;
                }
            }

            Dump("Initialized");

#if TEST
            UIntPtr l1 = RawAllocateBelow(0x1000000, 0x20000, 0x20000, 0x88810000u);
            UIntPtr l2 = RawAllocateBelow(0x1000000, 0x10000, 0x20000, 0x88820000u);
            UIntPtr l3 = RawAllocateBelow(0x1000000, 0x20000, 0x20000, 0x88830000u);
            UIntPtr l4 = RawAllocateBelow(0x1000000, 0x10000, 0x20000, 0x88840000u);

            UIntPtr a1 = RawAllocate(  0x1000, 0x100000, 0x4000, 0x99910000u);
            UIntPtr a2 = RawAllocate( 0x10000, 0x100000, 0x4000, 0x99920000u);
            UIntPtr a3 = RawAllocate(0x100000, 0x100000, 0x4000, 0x99930000u);
            UIntPtr a4 = RawAllocate(  0x1000,  0x10000, 0x4000, 0x99940000u);
            UIntPtr a5 = RawAllocate(  0x1000,  0x10000, 0x4000, 0x99950000u);

            Dump("Base Allocations");

            UIntPtr a1a = a1 != UIntPtr.Zero
                ? RawAllocateExtend(a1 +  0x1000,  0xf000, 0x99910001u) : UIntPtr.Zero;
            UIntPtr a2a = a2 != UIntPtr.Zero
                ? RawAllocateExtend(a2 + 0x10000, 0x10000, 0x99920001u) : UIntPtr.Zero;
            UIntPtr a4a = a4 != UIntPtr.Zero
                ? RawAllocateExtend(a4 +  0x1000,  0xf000, 0x99940001u) : UIntPtr.Zero;

            Dump("Extend Allocations");

            Tracing.Log(Tracing.Debug, "Query Tests:");
            DumpQuery(0);
            DumpQuery(0x100000);
            DumpQuery(0x200000);
            DumpQuery(0x300000);
            DumpQuery(bi->DumpBase + 0x1000);
            DumpQuery(BootInfo.KERNEL_STACK_BEGIN + 0x1000);
            DumpQuery(l1);
            DumpQuery(l1 + 0x20000);
            DumpQuery(l2);
            DumpQuery(l2 + 0x20000);
            DumpQuery(l3);
            DumpQuery(l3 + 0x20000);
            DumpQuery(l4);
            DumpQuery(l4 + 0x20000);
            DumpQuery(a1);
            DumpQuery(a1 + 0x20000);
            DumpQuery(a2);
            DumpQuery(a2 + 0x20000);
            DumpQuery(a3);
            DumpQuery(a3 + 0x20000);
            DumpQuery(a4);
            DumpQuery(a4 + 0x20000);
            DumpQuery(a5);
            DumpQuery(a5 + 0x20000);

            if (l1 != UIntPtr.Zero) {
                RawFree(l1,  0x20000, 0x88810000u);
            }
            if (l3 != UIntPtr.Zero) {
                RawFree(l3,  0x20000, 0x88830000u);
            }
            if (a1 != UIntPtr.Zero) {
                RawFree(a1,  0x10000, 0x99910000u);
            }
            if (a3 != UIntPtr.Zero) {
                RawFree(a3, 0x100000, 0x99930000u);
            }
            if (a5 != UIntPtr.Zero) {
                RawFree(a5,   0x1000, 0x99950000u);
            }

            Dump("First Free");

            if (l2 != UIntPtr.Zero) {
                RawFree(l2, 0x10000, 0x88820000u);
            }
            if (l4 != UIntPtr.Zero) {
                RawFree(l4, 0x10000, 0x88840000u);
            }
            if (a2 != UIntPtr.Zero) {
                RawFree(a2, 0x20000, 0x99920000u);
            }
            if (a4 != UIntPtr.Zero) {
                RawFree(a4, 0x10000, 0x99940000u);
            }

            Dump("Final Free");
            DebugStub.Break();
            DebugStub.Break();
            DebugStub.Break();
#endif
        }

        internal static void Finalize()
        {
            // Doesn't actually do anything.
        }

        private static void InitializeLock()
        {
#if SINGULARITY_MP
            pageLock = new SpinLock();
#endif // SINGULARITY_MP
        }

        [NoStackLinkCheck]
        private static bool Lock()
        {
            bool enabled = Processor.DisableInterrupts();
#if SINGULARITY_MP
            pageLock.Acquire(Thread.CurrentThread);
#endif // SINGULARITY_MP
            return enabled;
        }

        [NoStackLinkCheck]
        private static void Unlock(bool iflag)
        {
#if SINGULARITY_MP
            pageLock.Release(Thread.CurrentThread);
#endif // SINGULARITY_MP
            Processor.RestoreInterrupts(iflag);
        }

        // Currently, we just return the BSP free list.  In the
        // future, this should consult the ProcessorMemoryMap
        private static unsafe FreeNode* GetFreeList()
        {
            if (isProcessorMemoryInitialized) {
                return processorMemoryMap[0].procFreeList;
            }
            else {
                return freeList;
            }
        }

        private static unsafe FreeNode* GetSaveList()
        {
            if (isProcessorMemoryInitialized) {
                return processorMemoryMap[0].procSaveList;
            }
            else {
                return saveList;
            }
        }



        //////////////////////////////////////////////////////////////////////
        //
        internal static UIntPtr PageCount
        {
            get { return pageCount; }
        }

        internal static unsafe uint * PageTable
        {
            get { return pageTable; }
        }

        [NoStackLinkCheck]
        internal static UIntPtr Allocate(UIntPtr bytes,
                                         UIntPtr reserve,
                                         UIntPtr alignment,
                                         Process process,
                                         uint extra,
                                         PageType type)
        {
#if NO__TRACE_PAGES
#else
            Kernel.Waypoint(960);
#endif
            UIntPtr got = new UIntPtr();

            bool iflag = Lock();
            try {
                got = RawAllocate(bytes, reserve, alignment,
                                  (process != null ? process.ProcessTag : MemoryManager.KernelPage)
                                  | (extra & MemoryManager.ExtraMask)
                                  | (uint)type);
#if VERBOSE
                Tracing.Log(Tracing.Debug, "{0:x8} Allocate({1:x},{2:x},{3:x}",
                            Kernel.AddressOf(process), bytes, reserve,
                            alignment);
#endif
                if (process != null) {
                    process.Allocated(bytes);
                }
            }
            finally {
                Unlock(iflag);
            }
#if NO__TRACE_PAGES
#else
            Kernel.Waypoint(961);
#endif
            return got;
        }

        internal static UIntPtr AllocateBelow(UIntPtr limit,
                                              UIntPtr bytes,
                                              UIntPtr alignment,
                                              Process process,
                                              uint extra,
                                              PageType type)
        {
            UIntPtr got = new UIntPtr();

            bool iflag = Lock();
            try {
                got = RawAllocateBelow(limit, bytes, alignment,
                                       (process != null ? process.ProcessTag : MemoryManager.KernelPage)
                                       | (extra & MemoryManager.ExtraMask)
                                       | (uint)type);
                if (process != null) {
                    process.Allocated(bytes);
                }
            }
            finally {
                Unlock(iflag);
            }
            return got;
        }

        internal static UIntPtr AllocateExtend(UIntPtr addr,
                                               UIntPtr bytes,
                                               Process process,
                                               uint extra,
                                               PageType type)
        {
            UIntPtr got = new UIntPtr();

            bool iflag = Lock();
            try {
                uint tag =
                    (process != null ?
                     process.ProcessTag :
                     MemoryManager.KernelPage)
                    | (extra & MemoryManager.ExtraMask)
                    | (uint)type;
                got = RawAllocateExtend(addr, bytes, tag);
                if (got != UIntPtr.Zero && process != null) {
                    process.Allocated(bytes);
                }
            }
            finally {
                Unlock(iflag);
            }
            return got;
        }

        [NoStackLinkCheck]
        internal static void Free(UIntPtr addr,
                                  UIntPtr bytes,
                                  Process process)
        {
            bool iflag = Lock();
            try {
                RawFree(addr, bytes, process != null ? process.ProcessTag : MemoryManager.KernelPage);
                if (process != null) {
                    process.Freed(bytes);
                }
            }
            finally {
                Unlock(iflag);
            }
        }

        internal static unsafe UIntPtr FreeAll(Process process)
        {
            DebugStub.Assert(process != null,
                             "FlatPages.FreeAll null process");
            DebugStub.Assert(process.ProcessTag != MemoryManager.KernelPage,
                             "FlatPages.FreeAll ProcessTag={0}",
                             __arglist(process.ProcessTag));

            uint tag = process.ProcessTag & MemoryManager.ProcessPageMask;
            uint *pageLimit = pageTable + pageCount;
            UIntPtr bytes = 0;

            Tracing.Log(Tracing.Debug, "FreeAll({0,8:x})", tag);

            for (uint *begin = pageTable; begin < pageLimit;) {
                uint *limit = begin;
                uint val = (*limit++) & MemoryManager.ProcessPageMask;
#if VERBOSE
                unchecked {
                    Tracing.Log(Tracing.Debug, "  {0,8:x}: {1,8:x}",
                                AddrFromPage((UIntPtr)(begin - pageTable)),
                                val);
                }
#endif

                if (val == tag) {
                    while ((((*limit) & MemoryManager.ProcessPageMask) == tag) && (limit < pageLimit)) {
                        limit++;
                    }

                    UIntPtr page = (UIntPtr)(begin - pageTable);
                    UIntPtr size = (UIntPtr)(limit - begin);

                    Tracing.Log(Tracing.Debug,
                                "  {0,8:x}..{1,8:x} : {2,8:x} [will free]",
                                page << MemoryManager.PageBits, (page + size) << MemoryManager.PageBits,
                                *begin);

                    bool iflag = Lock();
                    try {
                        RawFree(AddrFromPage(page), AddrFromPage(size), tag);
                    }
                    finally {
                        Unlock(iflag);
                    }

                    bytes += size;
                }
                else {
                    while ((((*limit) & MemoryManager.ProcessPageMask) != tag) && (limit < pageLimit)) {
                        limit++;
                    }

                    UIntPtr page = (UIntPtr)(begin - pageTable);
                    UIntPtr size = (UIntPtr)(limit - begin);

                    Tracing.Log(Tracing.Debug,
                                "- {0,8:x}..{1,8:x} : {2,8:x} [will free]",
                                page << MemoryManager.PageBits, (page + size) << MemoryManager.PageBits,
                                *begin);
                }
                begin = limit;
            }
            if (process != null) {
                process.Freed(bytes * MemoryManager.PageSize);
            }
            return bytes * MemoryManager.PageSize;
        }

        internal static PageType Query(UIntPtr queryAddr,
                                       Process process,
                                       out UIntPtr regionAddr,
                                       out UIntPtr regionSize)
        {
            PageType type = new PageType();

            bool iflag = Lock();
            try {
                type = RawQuery(queryAddr,
                                process != null ? process.ProcessTag : 0,
                                out regionAddr, out regionSize);
            }
            finally {
                Unlock(iflag);
            }
            return type;
        }

        //////////////////////////////////////////////////////////////////////
        //
        [NoStackLinkCheck]
        private static unsafe UIntPtr RawAllocate(UIntPtr bytes,
                                                  UIntPtr reserve,
                                                  UIntPtr alignment,
                                                  uint tag)
        {
            VTable.Assert(Processor.InterruptsDisabled());
#if NO__TRACE_PAGES
#else
            Kernel.Waypoint(970);
#endif
            if (alignment < MemoryManager.PageSize)
            {
                alignment = MemoryManager.PageSize;
            }
            if (reserve < bytes) {
                reserve = bytes;
            }
#if VERBOSE
            Tracing.Log(Tracing.Debug,
                        " size={0:x}, res={1:x}, aln={2:x}, tag={3:x}",
                        bytes, reserve, alignment, tag);
#endif

            FreeNode * node = FreeNode.FindGoodFit(GetFreeList(), reserve, alignment);
            if (node == null) {
                node = FreeNode.FindGoodFit(GetFreeList(), bytes, alignment);
                if (node == null) {
                    node = FreeNode.FindGoodFit(GetSaveList(), reserve, alignment);
                    if (node == null) {
                        node = FreeNode.FindGoodFit(GetSaveList(), bytes, alignment);
                        if (node == null) {
                            // We should try to combine free and save pages...
                            // But for now, we just fail.
                            return UIntPtr.Zero;
                        }
                    }
                }
            }


            UIntPtr addr = (UIntPtr)node;
            UIntPtr adjust = SpaceNotAligned(addr + node->bytes, alignment);
            UIntPtr found = node->bytes;

#if VERBOSE
            Tracing.Log(Tracing.Debug, "    0. {0:x8}..{1:x8}: res={2:x}, adj={3:x}",
                        addr, addr + found, reserve, adjust);
#endif


            if (found > reserve + adjust) {
                // Put the extraneous pages in the free list.
                FreeNode.ReturnExtraBelow(GetFreeList(), ref addr, ref found, reserve + adjust);

#if VERBOSE
                Tracing.Log(Tracing.Debug, "    1. {0:x8}..{1:x8}",
                            addr, addr + found);
#endif
            }
#if ALLOW_BOOT_ARGLIST
            DebugStub.Assert
                (SpaceNotAligned(addr, alignment) == UIntPtr.Zero,
                 "FlatPages.RawAllocate not aligned addr={0} alignment={1}",
                 __arglist(addr, alignment));
#endif
            if (found > bytes) {
                // Put extra pages in the save list.
                FreeNode.ReturnExtraAbove(GetSaveList(), addr, ref found, bytes);

#if VERBOSE
                Tracing.Log(Tracing.Debug, "    2. {0:x8}..{1:x8}",
                            addr, addr + found);
#endif
            }
#if ALLOW_BOOT_ARGLIST
            DebugStub.Assert
                (found == bytes,
                 "FlatPages.RawAllocate wrong amount found={0} bytes={1}",
                 __arglist(found, bytes));
#endif
            SetRange(addr, found, tag);
#if NO__TRACE_PAGES
#else
            Kernel.Waypoint(971);
#endif

            allocatedCount++;
            allocatedBytes += (ulong)bytes;

            return addr;
        }

        private static unsafe UIntPtr RawAllocateBelow(UIntPtr limit,
                                                       UIntPtr bytes,
                                                       UIntPtr alignment,
                                                       uint tag)
        {
            VTable.Assert(Processor.InterruptsDisabled());
#if NO__TRACE_PAGES
#else
            Kernel.Waypoint(972);
#endif
            if (alignment < MemoryManager.PageSize)
            {
                alignment = MemoryManager.PageSize;
            }

#if VERBOSE
            Tracing.Log(Tracing.Debug,
                        "lim={0:x8}, size={1:x8}, align={2}, tag={3:x}",
                        limit, bytes, alignment, tag);
#endif
            FreeNode * node = FreeNode.FindBelow(limit, GetFreeList(), bytes, alignment);
            if (node == null) {
                node = FreeNode.FindBelow(limit, GetSaveList(), bytes, alignment);
                if (node == null) {
                    // We should try to combine free and save pages...
                    // But for now, we just fail.
                    return UIntPtr.Zero;
                }
            }

            UIntPtr addr = (UIntPtr)node;
            UIntPtr adjust = SpaceToAlign(addr, alignment);
            UIntPtr found = node->bytes;

            if (adjust != UIntPtr.Zero) {
                // Put the alignment pages in free list.
                FreeNode.ReturnExtraBelow(GetFreeList(), ref addr, ref found, found - adjust);
            }
            DebugStub.Assert
                (SpaceNotAligned(addr, alignment) == UIntPtr.Zero,
                 "FlatPages.RawAllocateBelow not aligned addr={0} alignment={1}",
                 __arglist(addr, alignment));

            if (found > bytes) {
                // Put the extra pages in free list.
#if VERBOSE
                Tracing.Log(Tracing.Debug,
                            "found {0:x8}..{1:x8}, found={3:x8}, keep={4:x8}",
                            addr, addr + found, found, bytes);
#endif

                FreeNode.ReturnExtraAbove(GetFreeList(), addr, ref found, bytes);
            }

            DebugStub.Assert
                (found == bytes,
                 "FlatPages.RawAllocateBelow wrong amount found={0} bytes={1}",
                 __arglist(found, bytes));

            SetRange(addr, found, tag);
#if NO__TRACE_PAGES
#else
            Kernel.Waypoint(973);
#endif

            allocatedCount++;
            allocatedBytes += (ulong)bytes;

            return addr;
        }

        private static unsafe UIntPtr RawAllocateExtend(UIntPtr addr,
                                                        UIntPtr bytes,
                                                        uint tag)
        {
            VTable.Assert(Processor.InterruptsDisabled());
#if NO__TRACE_PAGES
#else
            Kernel.Waypoint(974);
#endif
            UIntPtr page = MemoryManager.PageFromAddr(addr);
            if (*(pageTable + page) != MemoryManager.PageFreeFirst) {
                Tracing.Log(Tracing.Error,
                            "{0:x} is not first free page {1:x}.",
                            addr, *(pageTable + page));
                return UIntPtr.Zero;
            }

            FreeNode *node = (FreeNode *)addr;
            if (node->bytes < bytes) {
                Tracing.Log(Tracing.Error,
                            "Only {0:x} free bytes, not {1:x} as requested.",
                            node->bytes, bytes);
                return UIntPtr.Zero;
            }

#if VERBOSE
            Tracing.Log(Tracing.Debug, "addr={0:x8}, size={1:x8}, tag={2:x}",
                        addr, bytes, tag);
#endif

            // Remove the node from the list.
            FreeNode.Remove(node);

            UIntPtr found = node->bytes;

            if (found > bytes) {
                // Save the extra pages in the save list.
                FreeNode.ReturnExtraAbove(GetSaveList(), addr, ref found, bytes);
            }

            DebugStub.Assert
                (found == bytes,
                 "FlatPages.RawAllocateExtend wrong amount found={0} bytes{1}",
                 __arglist(found, bytes));

            SetRange(addr, found, tag);
#if NO__TRACE_PAGES
#else
            Kernel.Waypoint(975);
#endif

            allocatedCount++;
            allocatedBytes += (ulong)bytes;

            return addr;
        }

        [NoStackLinkCheck]
        private static unsafe void VerifyOwner(UIntPtr page, UIntPtr pages, uint tag)
        {
            tag &= MemoryManager.ProcessPageMask;
            for (UIntPtr i = UIntPtr.Zero; i < pages; i++) {
                DebugStub.Assert
                    (((*(pageTable + page + i)) & MemoryManager.ProcessPageMask) == tag,
                     "FlatPages.VerifyOwner page={0} i={1} tag={2}",
                     __arglist(page, i, tag));
            }
        }

        [NoStackLinkCheck]
        private static unsafe void RawFree(UIntPtr addr, UIntPtr bytes, uint tag)
        {
            VTable.Assert(Processor.InterruptsDisabled());
            UIntPtr bytesIn = bytes;
#if NO__TRACE_PAGES
#else
            Kernel.Waypoint(976);
#endif
#if VERBOSE
            Tracing.Log(Tracing.Debug, "adr={0:x}, size={1:x}, tag={2:x}",
                        addr, bytes, tag);
#endif

            VerifyOwner(MemoryManager.PageFromAddr(addr), MemoryManager.PagesFromBytes(bytes), tag);

            FreeNode *node = FreeNode.GetNodeAt(addr + bytes);
            FreeNode *prev = FreeNode.GetNodeFromLast(addr - MemoryManager.PageSize);

            SetRange(addr, bytes, MemoryManager.PageFree);
            // Try to combine with the previous region if it isn't a save region.
            if (prev != null && prev->isSave == false) {
                addr = (UIntPtr)prev;
                bytes += prev->bytes;

                FreeNode.Remove(prev);
            }
            // Try to combine with the next region even if it was a save region.
            if (node != null) {
                bytes += node->bytes;
                FreeNode.Remove(node);
                if (node->isSave) {
                    // If next was save, then try to combine with the follower.
                    node = FreeNode.GetNodeAt(addr + bytes);
                    if (node != null) {
                        bytes += node->bytes;
                        FreeNode.Remove(node);
                    }
                }
            }
            // Create the free node.
            FreeNode.CreateAndInsert(GetFreeList(), addr, bytes);
#if NO__TRACE_PAGES
#else
            Kernel.Waypoint(977);
#endif

            freedCount++;
            freedBytes += (ulong)bytesIn;
        }

        private static unsafe PageType RawQuery(UIntPtr queryAddr,
                                                uint tag,
                                                out UIntPtr regionAddr,
                                                out UIntPtr regionSize)
        {
            VTable.Assert(Processor.InterruptsDisabled());
            UIntPtr page = MemoryManager.PageFromAddr(queryAddr);
            UIntPtr startPage = page;
            UIntPtr limitPage = page + 1;

            PageType type;
            uint val = *(pageTable + startPage);
            bool used = ((val & MemoryManager.ProcessPageMask) != MemoryManager.SystemPage);

            if ((val & MemoryManager.ProcessPageMask) == MemoryManager.SystemPage) {
                // Found a system page.
                type = (tag == 0) ? (PageType)(val & MemoryManager.TypeMask) : PageType.Unknown;

                // Find the start of the SystemPage region.
                for (; startPage > UIntPtr.Zero; startPage--) {
                    val = *(pageTable + startPage - 1);
                    if ((val & MemoryManager.ProcessPageMask) != MemoryManager.SystemPage) {
                        break;
                    }
                }
                // Find the end of the SystemPage region
                for (; limitPage < pageCount; limitPage++) {
                    val = *(pageTable + limitPage);
                    if ((val & MemoryManager.ProcessPageMask) != MemoryManager.SystemPage) {
                        break;
                    }
                }
            }
            else {
                // Found a process page.
                uint ptag = val & MemoryManager.ProcessPageMask;
                type = (tag == 0 || ptag == tag)
                    ? (PageType)(val & MemoryManager.TypeMask) : PageType.Unknown;

                if ((val & MemoryManager.TypeMask) == (uint)PageType.System) {
                    // Find the start of the process code region.
                    for (; startPage > UIntPtr.Zero; startPage--) {
                        val = *(pageTable + startPage - 1);
                        if ((val & MemoryManager.ProcessPageMask) != ptag ||
                            (val & MemoryManager.TypeMask) != (uint)PageType.System) {
                            break;
                        }
                    }
                    // Find the end of the process code region
                    for (; limitPage < pageCount; limitPage++) {
                        val = *(pageTable + limitPage);
                        if ((val & MemoryManager.ProcessPageMask) != ptag ||
                            (val & MemoryManager.TypeMask) != (uint)PageType.System) {
                            break;
                        }
                    }
                }
                else {
                    // Find the start of the process region.
                    for (; startPage > UIntPtr.Zero; startPage--) {
                        val = *(pageTable + startPage - 1);
                        if ((val & MemoryManager.ProcessPageMask) != ptag ||
                            (val & MemoryManager.TypeMask) == (uint)PageType.System) {
                            break;
                        }
                    }
                    // Find the end of the process region
                    for (; limitPage < pageCount; limitPage++) {
                        val = *(pageTable + limitPage);
                        if ((val & MemoryManager.ProcessPageMask) != ptag ||
                            (val & MemoryManager.TypeMask) == (uint)PageType.System) {
                            break;
                        }
                    }
                }
            }
#if VERBOSE
            Tracing.Log(Tracing.Debug, "[{0:x8}..{1:x8}]",
                        AddrFromPage(startPage), AddrFromPage(limitPage));
#endif
            regionAddr = AddrFromPage(startPage);
            regionSize = AddrFromPage(limitPage - startPage);
            return type;
        }

        //////////////////////////////////////////////////////////////////////////
        //
        private static unsafe void DumpQuery(UIntPtr addr)
        {
            UIntPtr regionAddr;
            UIntPtr regionSize;
            PageType type = RawQuery(addr, 0, out regionAddr, out regionSize);

            Tracing.Log(Tracing.Debug, "  {0:x8} => {1:x8}..{2:x8} [{3:x}]",
                        addr, regionAddr, regionAddr + regionSize, (uint)type);
        }

        private static unsafe void DumpFreeNodes(FreeNode *list)
        {
            DumpFreeNodes(list, list->isSave);
        }

        private static unsafe void DumpFreeNodes(FreeNode *list, bool isSave)
        {
            if (isSave) {
                Tracing.Log(Tracing.Debug, " SaveList:");
            }
            else {
                Tracing.Log(Tracing.Debug, " FreeList:");
            }

            for (FreeNode *node = list->next; node != list; node = node->next) {
                string fmt = "  {0:x8}..{1:x8} prev={2:x8}, next={3:x8}, last={4:x8} ";
                if (node->isSave != isSave) {
                    if (node->isSave) {
                        fmt = "  {0:x8}..{1:x8} prev={2:x8}, next={3:x8}, last={4:x8} [Save!]";
                    }
                    else {
                        fmt = "  {0:x8}..{1:x8} prev={2:x8}, next={3:x8}, last={4:x8} [Free!]";
                    }
                }
                unchecked {
                    Tracing.Log(Tracing.Debug, fmt,
                                (UIntPtr)node, (UIntPtr)node + node->bytes,
                                (UIntPtr)node->prev, (UIntPtr)node->next,
                                (UIntPtr)node->last);
                }
            }
        }

        internal static unsafe void Dump(string where)
        {
            Tracing.Log(Tracing.Debug, "FlatPages.Dump: {0}", where);

            uint *descriptors = pageTable;
            uint last = *descriptors++ & MemoryManager.SystemPageMask;
            UIntPtr begin = UIntPtr.Zero;

            UIntPtr freePages = UIntPtr.Zero;
            UIntPtr usedPages = UIntPtr.Zero;
            UIntPtr unknownPages = UIntPtr.Zero;
            UIntPtr sharedPages = UIntPtr.Zero;

            for (UIntPtr i = (UIntPtr)1; i < pageCount; i++) {
                uint dsc = *descriptors++;
                uint val = dsc & MemoryManager.SystemPageMask;

                switch (val) {
                    case MemoryManager.PageUnknown:
                        unknownPages++;
                        break;
                    case MemoryManager.PageShared:
                        sharedPages++;
                        break;
                    case MemoryManager.PageFree:
                        freePages++;
                        break;
                    default:
                        usedPages++;
                        break;
                }

                if (dsc != last) {
                    Tracing.Log(Tracing.Debug, "  {0:x8}..{1:x8} : {2:x8} : {3:x8}",
                                begin << MemoryManager.PageBits, i << MemoryManager.PageBits, last,
                                (i - begin) << MemoryManager.PageBits);
                    last = dsc;
                    begin = i;
                }
            }

            Tracing.Log(Tracing.Debug, "  {0:x8}..{1:x8} : {2:x8} : {3:x8}",
                        begin << MemoryManager.PageBits, pageCount << MemoryManager.PageBits, last,
                        (pageCount - begin) << MemoryManager.PageBits);

            DumpFreeNodes(GetFreeList(), false);
            DumpFreeNodes(GetSaveList(), true);

            Tracing.Log(Tracing.Audit,
                        "Totals: free={0:x8}, used={1:x8}, unknown={2:x8}, reserved={3:x8}",
                        freePages << MemoryManager.PageBits,
                        usedPages << MemoryManager.PageBits,
                        unknownPages << MemoryManager.PageBits,
                        sharedPages << MemoryManager.PageBits);
        }

        //////////////////////////////////////////////////////////////////////
        //
        [NoStackLinkCheck]
        private static unsafe void SetPages(UIntPtr startPage, UIntPtr pageCount, uint tag)
        {
            uint * descriptor = pageTable + startPage;

#if VERY_VERBOSE
            Tracing.Log(Tracing.Audit,
                        "SetPages(beg={0:x},num={1:x},val={2}",
                        startPage << MemoryManager.PageBits,
                        pageCount << MemoryManager.PageBits,
                        tag);
#endif

            while (pageCount > UIntPtr.Zero) {
                *descriptor++ = tag;
                pageCount--;
            }
        }

        [NoStackLinkCheck]
        private static void SetRange(UIntPtr start, UIntPtr bytes, uint tag)
        {
            if (start > addressLimit) {
                return;
            }
            if (start + bytes > addressLimit) {
                bytes = addressLimit - start;
            }
            SetPages(MemoryManager.PageFromAddr(start), MemoryManager.PagesFromBytes(bytes), tag);
        }
        //////////////////////////////////////////////////////////////////////////
        //

        public static UIntPtr GetMaxMemory()
        {
            return addressLimit;
        }

        public static unsafe UIntPtr GetFreeMemory()
        {
            uint *descriptors = pageTable;
            UIntPtr retval = 0;

            // Count free pages
            for (UIntPtr i = (UIntPtr)1; i < pageCount; i++) {
                uint dsc = *descriptors++;
                uint val = dsc & MemoryManager.SystemPageMask;

                if (val == MemoryManager.PageFree)
                {
                    retval++;
                }
            }

            return retval * MemoryManager.PageSize;
        }

        public static unsafe UIntPtr GetUsedMemory()
        {
            uint *descriptors = pageTable;
            UIntPtr retval = 0;

            // Count free pages
            for (UIntPtr i = (UIntPtr)1; i < pageCount; i++) {
                uint dsc = *descriptors++;
                uint val = dsc & MemoryManager.SystemPageMask;

                if (val != MemoryManager.PageFree)
                {
                    retval++;
                }
            }

            return retval * MemoryManager.PageSize;
        }

        public static void GetUsageStatistics(out ulong allocatedCount,
                                              out ulong allocatedBytes,
                                              out ulong freedCount,
                                              out ulong freedBytes)
        {
            allocatedCount = FlatPages.allocatedCount;
            allocatedBytes = FlatPages.allocatedBytes;
            freedCount = FlatPages.freedCount;
            freedBytes = FlatPages.freedBytes;
        }

        //////////////////////////////////////////////////////////////////////
        //

        [Inline]
        internal static UIntPtr AddrFromPage(UIntPtr page) {
            return (page << MemoryManager.PageBits);
        }

        [Inline]
        private static UIntPtr Align(UIntPtr data, UIntPtr size)
        {
            return ((data) & ~(size - 1));
        }

        [Inline]
        private static UIntPtr Pad(UIntPtr data, UIntPtr size)
        {
            return ((data + size - 1) & ~(size - 1));
        }

        [Inline]
        private static UIntPtr SpaceToAlign(UIntPtr data, UIntPtr size)
        {
            return Pad(data, size) - data;
        }

        [Inline]
        private static UIntPtr SpaceNotAligned(UIntPtr data, UIntPtr size)
        {
            return ((data) & (size - 1));
        }

        //////////////////////////////////////////////////////////////////////
        //
        [StructLayout(LayoutKind.Sequential)]
        private struct LastNode
        {
            internal const uint Signature   = 0xaa2222aa;
            internal const uint Removed     = 0xee1111ee;

            internal uint signature;
            internal unsafe FreeNode * node;

            [NoStackLinkCheck]
            internal static unsafe LastNode * Create(UIntPtr addr, FreeNode *node)
            {
                LastNode *last = (LastNode *)addr;
                last->signature = LastNode.Signature;
                last->node = node;
                node->last = last;
#if VERBOSE
                Tracing.Log(Tracing.Debug, "addr={0:x8}, node={1:x8}",
                            addr, (UIntPtr) last->node);
#endif
                return last;
            }

            [NoStackLinkCheck]
            internal static unsafe void Remove(LastNode *last)
            {
                last->signature = Removed;
                last->node = null;
            }

            [NoStackLinkCheck]
            internal static unsafe void PrintLastNode(UIntPtr addr)
            {
                LastNode *last = (LastNode *)addr;
                DebugStub.WriteLine("ln.{1:x8}  ", __arglist((UIntPtr)last->node));
            }

        }


        //////////////////////////////////////////////////////////////////////
        //
        [StructLayout(LayoutKind.Sequential)]
        private struct FreeNode
        {
            internal const uint Signature   = 0x22aaaa22;
            internal const uint Removed     = 0x11eeee11;

            internal uint signature;
            internal unsafe FreeNode * prev;
            internal unsafe FreeNode * next;
            internal unsafe LastNode * last;
            internal UIntPtr bytes;
            internal bool isSave;

            [NoStackLinkCheck]
            internal static unsafe void Init(FreeNode *list, bool isSave)
            {
                list->signature = Signature;
                list->prev = list;
                list->next = list;
                list->last = null;
                list->bytes = 0;
                list->isSave = isSave;
            }

            [NoStackLinkCheck]
            internal static unsafe bool Remove(FreeNode *node)
            {
                FreeNode * prev;
                FreeNode * next;

                UIntPtr page = MemoryManager.PageFromAddr((UIntPtr)node);
                *(pageTable + page) = MemoryManager.PageFree;

                next = node->next;
                prev = node->prev;
                prev->next = next;
                next->prev = prev;

                if (node->last != null) {
                    LastNode.Remove(node->last);
                }
                node->signature = Removed;

                return (next == prev);
            }

            [NoStackLinkCheck]
            private static unsafe void InsertAsPrev(FreeNode *list, FreeNode *node)
            {
                FreeNode * prev;

                prev = list->prev;
                node->next = list;
                node->prev = prev;
                prev->next = node;
                list->prev = node;
            }

            [NoStackLinkCheck]
            private static unsafe void InsertAsNext(FreeNode *list, FreeNode *node)
            {
                FreeNode * next;

                next = list->next;
                node->prev = list;
                node->next = next;
                next->prev = node;
                list->next = node;
            }

            [NoStackLinkCheck]
            private static unsafe void InsertBySize(FreeNode *list, FreeNode *node)
            {
#if ALLOW_BOOT_ARGLIST
                DebugStub.Assert(node->bytes > 0,
                                 "FlatPages.InsertBySize node->bytes={0}",
                                 __arglist(node->bytes));
#endif
                if (node->bytes <= SmallSize) {
                    // If the size is pretty small, we insert from the back of the list...
                    for (FreeNode *step = list->prev; step != list; step = step->prev) {
                        if (step->bytes >= node->bytes) {
                            InsertAsNext(step, node);
                            return;
                        }
                    }
                    InsertAsNext(list, node);
                }
                else {
                    // Insert a region into the list by size.
                    for (FreeNode *step = list; step->next != list; step = step->next) {
                        if (step->next->bytes <= node->bytes) {
                            InsertAsNext(step, node);
                            return;
                        }
                    }
                    InsertAsPrev(list, node);
                }
            }

            ///////////////////////////////////////////////////////////
            // haryadi FreeNode's new routines start here

            internal static unsafe void PrintFreeList(FreeNode *list)
            {
                int count = 0;
                DebugStub.WriteLine
                    ("        PRINT FREE LIST  (tail.{0:x8}  prev.{1:x8}  next.{2:x8})",
                     __arglist((UIntPtr)(list),
                               (UIntPtr)list->prev,
                               (UIntPtr)list->next));
                DebugStub.WriteLine("        ---------------------------------------------------");

                for (FreeNode *node = list->next;
                     node != list; node = node->next) {
                    DebugStub.Print
                        ("        [{0}] b.{1:x8} e.{2:x8} {3,8}KB p.{4:x8} n.{5:x8} l.{6:x8}  --  ",
                         __arglist(
                                   count,
                                   (UIntPtr)node, (UIntPtr)node + node->bytes,
                                   node->bytes/(1024),
                                   (UIntPtr)node->prev,
                                   (UIntPtr)node->next,
                                   (UIntPtr)node->last));
                    if (node->last != null) {
                        LastNode.PrintLastNode((UIntPtr)(node->last));
                    }
                    else {
                        DebugStub.WriteLine();
                    }
                    if (count++ > 20) {
                        DebugStub.WriteLine("\n        **** ERROR INFINITE LIST ****\n");
                        DebugStub.Break();
                    }
                }
            }

            internal static unsafe void PrintDomainFreeLists()
            {
                DebugStub.WriteLine("        DOMAIN FREE LIST");
                DebugStub.WriteLine("        ------------------------------------------");
                for (int i = 0; i < domainMap.Length; i++) {
                    if (domainMap[i].isSubMemConnected) {
                        DebugStub.WriteLine("\n\n        Domain [{0}]:", __arglist(i));
                        PrintFreeList(domainMap[i].domFreeList);
                    }

                }
            }

            internal static unsafe void PrintProcessorFreeLists()
            {
                DebugStub.WriteLine("\n");
                DebugStub.WriteLine("        ******************************************");
                DebugStub.WriteLine("        PROCESSOR FREE LIST");
                DebugStub.WriteLine("        ******************************************");
                for (int i = 0; i < processorMemoryMap.Length; i++) {
                    DebugStub.WriteLine("\n\n        Processor [{0}]:", __arglist(i));
                    if (processorMemoryMap[i].isInitialized) {
                        PrintFreeList(processorMemoryMap[i].procFreeList);
                    }

                }
                DebugStub.WriteLine();
            }

            internal static unsafe UIntPtr GetFreeListTotalSize(FreeNode *list)
            {
                UIntPtr size = 0;
                for (FreeNode *node = list->next;
                     node != list; node = node->next) {
                    size += node->bytes;
                }
                return size;
            }

            internal static unsafe void PrintProcessorAddressSpace(FreeNode *list)
            {
                ulong MB = 1024*1024;
                for (FreeNode *node = list->next;
                     node != list; node = node->next) {
                    DebugStub.Print
                        ("[{0:x8}..{1:x8},{2,3}MB]  ",
                         __arglist((UIntPtr)node,
                                   (UIntPtr)node + node->bytes,
                                   (ulong)(node->bytes)/MB));
                }
            }

            internal static unsafe void PrintProcessorsAddressSpaces()
            {
                UIntPtr size = 0;
                ulong MB = 1024*1024;

                DebugStub.WriteLine("Processor Address Space (Current Free List):");
                for (int i = 0; i < processorMemoryMap.Length; i++) {
                    if (processorMemoryMap[i].isInitialized) {
                        size = GetFreeListTotalSize(processorMemoryMap[i].procFreeList);
                        DebugStub.Print("  p{0} ({1,3}MB) : ",
                                        __arglist(i, (ulong)size/MB));
                        PrintProcessorAddressSpace(processorMemoryMap[i].procFreeList);
                    }
                    DebugStub.WriteLine();
                }
            }

            [NoStackLinkCheck]
            internal static unsafe FreeNode* GetFreeNodeAtBreakAddr(FreeNode *list, UIntPtr breakAddr)
            {
                int count = 0;

                for (FreeNode *node = list->next;
                     node != list; node = node->next) {

                    if ((UIntPtr)node <= breakAddr
                        && breakAddr < ((UIntPtr)node + node->bytes)) {
                        return node;
                    }
                    if (count++ > 20) {
                        DebugStub.WriteLine("  WARNING: Can't GetFreeNode ListTail.{0:x8} at {1:x8} after 20 iterations",
                                            __arglist((UIntPtr)list, breakAddr));
                        DebugStub.Break();
                    }

                }
                return null;
            }

            // Imagine the case where the current free list contains
            // node from address 100 to 1000 Now, the SRAT table says
            // that a sub memory is from range 50 to 500.  In
            // CreateSubMemory, when we call GetFreeNodeBreakAddr(50)
            // it will fail, because there is no free node at address
            // 50. However this sub memory is actually intersects with
            // the free list node. So the correct thing to do is to
            // break it at address 100. This function will return the
            // correct address (i.e. 100) to the caller, so that the
            // caller can break the free list at 100 instead of 50.
            // return breakAddr
            [NoStackLinkCheck]
            internal static unsafe UIntPtr IsPartialIntersect(FreeNode *list, UIntPtr baseAddr, UIntPtr endAddr)
            {
                UIntPtr nodeBaseAddr;
                UIntPtr nodeEndAddr;

                for (FreeNode *node = list->next;
                     node != list; node = node->next) {

                    nodeBaseAddr = (UIntPtr)node;
                    nodeEndAddr = (UIntPtr)(node) + node->bytes;

                    if (nodeBaseAddr < endAddr && nodeBaseAddr >= baseAddr) {
#if MP_VERBOSE
                        DebugStub.WriteLine("  ** Return Nb.{0:x8}",
                                            __arglist(baseAddr));
#endif
                        return nodeBaseAddr;
                    }
                }
                return 0;
            }



            // This will break curNode into two nodes. For example
            // curNode is from address X to Y The two nodes will be
            // one from X to breakAddr and the other from breakAddr to
            // Y. Also prev and next pointers are updated
            [NoStackLinkCheck]
            internal static unsafe void BreakListAt(FreeNode *list, FreeNode *curNode, UIntPtr breakAddr)
            {
                // Before breaking, need to check if this breakAddr
                // has been broken before or not. If so, don't double
                // break.  One way to find out is to check the
                // signature of the lastnode and freenode before and
                // after the breakAddress respectively
                FreeNode *freeNode = (FreeNode*) breakAddr;
                LastNode *lastNode = (LastNode*) (breakAddr - MemoryManager.PageSize);
                if (lastNode->signature == LastNode.Signature &&
                    freeNode->signature == FreeNode.Signature) {
#if MP_VERBOSE
                    DebugStub.WriteLine("        {0:x8} Has been broken before. Cancel braking.",
                                        __arglist(breakAddr));
#endif
                    return;
                }

                // If this is the first node in the list, and the address of
                // the first node is the same as curNode. Then,
                // don't break this node.
                if ((UIntPtr) freeNode == breakAddr &&
                    freeNode->prev == list) {
#if MP_VERBOSE
                    DebugStub.WriteLine("        {0:x8} is the first node. Cancel braking.",
                                        __arglist(breakAddr));
#endif
                    return;
                }

#if MP_VERBOSE
                DebugStub.WriteLine("        {0:x8} is okay. Proceed Breaking", __arglist(breakAddr));
#endif

                // first remember originals
                LastNode *origLast = curNode->last;
                FreeNode *origNext = curNode->next;
                FreeNode *origPrev = curNode->prev;
                UIntPtr origBytes = curNode->bytes;
                bool origIsSave = curNode->isSave;
                uint origSignature = curNode->signature;

                // prepare the two nodes
                FreeNode *firstNode = curNode;
                FreeNode *secondNode = (FreeNode*)breakAddr;
                UIntPtr firstNodeBase = (UIntPtr) firstNode;
                UIntPtr firstNodeEnd = breakAddr;
                UIntPtr secondNodeBase = breakAddr;
                UIntPtr secondNodeEnd = (UIntPtr)curNode + curNode->bytes;

                // now fix the second node FIRST!! (before the first node)
                secondNode->next = origNext;
                secondNode->prev = firstNode;
                secondNode->bytes = secondNodeEnd - secondNodeBase;
                secondNode->isSave = origIsSave;
                secondNode->signature = origSignature;
                LastNode.Create(secondNodeEnd - MemoryManager.PageSize, secondNode);

                // now fix the first node
                firstNode->next = secondNode;
                firstNode->prev = origPrev;
                firstNode->bytes = firstNodeEnd - firstNodeBase;
                firstNode->isSave = origIsSave;
                firstNode->signature = origSignature;
                LastNode.Create(firstNodeEnd - MemoryManager.PageSize, firstNode);

                // now fix the original next's previous pointer
                origNext->prev = secondNode;
            }


            [NoStackLinkCheck]
            internal static unsafe FreeNode * FindGoodFit(FreeNode *list,
                                                          UIntPtr bytes, UIntPtr alignment)
            {
#if ALLOW_BOOT_ARGLIST
                DebugStub.Assert(bytes > 0,
                                 "FlatPages.FindGoodFit bytes={0}",
                                 __arglist(bytes));
#endif
                // If it is a small allocation, we try to accelerate the search.
                if (bytes <= SmallSize && alignment <= MemoryManager.PageSize) {
                    for (FreeNode *node = list->prev; node != list; node = node->prev) {
                        if (node->bytes >= bytes) {
                            Remove(node);
                            return node;
                        }
                    }
                    return null;
                }
                else {
                    // First try to find a region closest in size to bytes...
                    FreeNode *best = null;
                    for (FreeNode *node = list->next; node != list; node = node->next) {
                        if (bytes <= node->bytes) {
                            UIntPtr full = SpaceToAlign((UIntPtr)node, alignment) + bytes;
                            if (full <= node->bytes) {
                                // If we find a candidate, remember it.
                                best = node;
                                if (full == node->bytes) {
                                    // Stop if it is the ideal region.
                                    break;
                                }
                            }
                        }
                        else {
                            // Stop if we have a candidate and we've reach smaller regions.
                            if (best != null) {
                                break;
                            }
                        }
                    }
                    if (best != null) {
                        Remove(best);
                    }
                    return best;
                }
            }

            [NoStackLinkCheck]
            internal static unsafe FreeNode * FindBelow(UIntPtr limit, FreeNode *list,
                                                        UIntPtr bytes, UIntPtr alignment)
            {
                DebugStub.Assert(bytes > 0,
                                 "FlatPages.FindBelow bytes={0}",
                                 __arglist(bytes));

                // Try to find the first region below the limit address.
                for (FreeNode *node = list->next; node != list; node = node->next) {
                    if ((UIntPtr)node + bytes < limit && node->bytes >= bytes) {
                        UIntPtr full = SpaceToAlign((UIntPtr)node, alignment) + bytes;
                        if ((UIntPtr)node + full < limit && node->bytes >= full) {
                            Remove(node);
                            return node;
                        }
                    }
                }
                return null;
            }

            [NoStackLinkCheck]
            internal static unsafe FreeNode * GetNodeAt(UIntPtr addr)
            {
                UIntPtr page = MemoryManager.PageFromAddr(addr);

                if (*(pageTable + page) == MemoryManager.PageFreeFirst) {
                    return (FreeNode *)addr;
                }
                return null;
            }

            [NoStackLinkCheck]
            internal static unsafe FreeNode * GetNodeFromLast(UIntPtr addr)
            {
                UIntPtr page = MemoryManager.PageFromAddr(addr);

                if (*(pageTable + page) == MemoryManager.PageFree &&
                    *(pageTable + page + 1) != MemoryManager.PageFree) {

                    return ((LastNode *)addr)->node;
                }
                if (*(pageTable + page) == MemoryManager.PageFreeFirst) {
                    return (FreeNode *)addr;
                }
                return null;
            }

            [NoStackLinkCheck]
            internal static unsafe FreeNode * Create(UIntPtr addr, UIntPtr bytes, bool isSave)
            {
                // Mark a page as a node in the free list, initialize the node struct.
                FreeNode * node = (FreeNode *)addr;

#if VERY_VERBOSE
                Tracing.Log(Tracing.Debug,
                            isSave ?
                            "{0:x8}..{1:x8}, last={4:x8}" :
                            "{0:x8}..{1:x8}, last={4:x8}",
                            addr, addr+bytes, addr + bytes - MemoryManager.PageSize);
#endif

                UIntPtr page = MemoryManager.PageFromAddr(addr);
                *(pageTable + page) = MemoryManager.PageFreeFirst;

                node->signature = FreeNode.Signature;
                node->bytes = bytes;
                node->isSave = isSave;
                node->prev = null;
                node->next = null;
                node->last = null;

                if (bytes > MemoryManager.PageSize) {
                    LastNode.Create(addr + bytes - MemoryManager.PageSize, node);
                }
                return node;
            }

            [NoStackLinkCheck]
            internal static unsafe void CreateAndInsert(FreeNode *list,
                                                        UIntPtr addr,
                                                        UIntPtr bytes)
            {
                FreeNode * node = Create(addr, bytes, list->isSave);

#if VERBOSE
                Tracing.Log(Tracing.Debug,
                            list->isSave ?
                            "({0:x8}, {1:x8}, true), prev={3:x8}, next={4:x8}, last={5:x8}" :
                            "({0:x8}, {1:x8}, false), prev={3:x8}, next={4:x8}, last={5:x8}",
                            addr, bytes, (UIntPtr) node->prev,
                            (UIntPtr) node->next, (UIntPtr) node->last);
#endif


#if ALLOW_BOOT_ARGLIST
                DebugStub.Assert((bytes & MemoryManager.PageMask) == 0,
                                 "FlatPages.CreateAndInsert bytes={0}",
                                 __arglist(bytes));
                DebugStub.Assert((node->bytes & MemoryManager.PageMask) == 0,
                                 "FlatPages.CreateAndInsert node->bytes={0}",
                                 __arglist(node->bytes));
#endif
                InsertBySize(list, node);
            }

            [NoStackLinkCheck]
            internal static unsafe void ReturnExtraAbove(FreeNode *list,
                                                         UIntPtr addr,
                                                         ref UIntPtr found,
                                                         UIntPtr keep)
            {
                CreateAndInsert(list, addr + keep, found - keep);
                found = keep;
            }

            [NoStackLinkCheck]
            internal static unsafe void ReturnExtraBelow(FreeNode *list,
                                                         ref UIntPtr addr,
                                                         ref UIntPtr found,
                                                         UIntPtr keep)
            {
                CreateAndInsert(list, addr, found - keep);
                addr = addr + found - keep;
                found = keep;
            }
        }
    }
}

#endif // !PAGING
