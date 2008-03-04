/*******************************************************************/
/*                           WARNING                               */
/* This file should be identical in the Bartok and Singularity     */
/* depots. Master copy resides in Bartok Depot. Changes should be  */
/* made to Bartok Depot and propagated to Singularity Depot.       */
/*******************************************************************/

//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

namespace System.GCs
{

    using Microsoft.Bartok.Runtime;

    using System.Threading;
    using System.Runtime.CompilerServices;

    [NoCCtor]
    internal abstract class BaseCollector : Collector
    {

        internal override void NewThreadNotification(Thread newThread,
                                                     bool initial)
        {
            Transitions.NewThreadNotification(newThread.threadIndex, initial);
        }

        internal override void DeadThreadNotification(Thread deadThread)
        {
        }

        internal override void ThreadStartNotification(int currentThreadIndex)
        {
            Thread currentThread = Thread.threadTable[currentThreadIndex];
#if !SINGULARITY
            PageManager.MarkThreadStack(currentThread);
#endif
        }

        internal override void ThreadEndNotification(Thread currentThread)
        {
        }

        [ManualRefCounts]
        internal override Object AllocateObject(VTable vtable,
                                                Thread currentThread)
        {
            UIntPtr numBytes = ObjectLayout.ObjectSize(vtable);
            UIntPtr objectAddr =
                AllocateObjectMemory(numBytes, vtable.baseAlignment,
                                     currentThread);
            Object result = Magic.fromAddress(objectAddr);
            this.CreateObject(result, vtable, currentThread);
            if (GC.IsProfiling) {
                ProfileAllocation(result);
            }
            if (VTable.enableGCProfiling) {
                System.GC.bytesAllocated += (ulong)numBytes;
                System.GC.objectsAllocated++;
            }

            return result;
        }

        [ManualRefCounts]
        internal override Array AllocateVector(VTable vtable,
                                               int numElements,
                                               Thread currentThread)
        {
            UIntPtr numBytes =
                ObjectLayout.ArraySize(vtable, unchecked((uint)numElements));
            UIntPtr vectorAddr =
                AllocateObjectMemory(numBytes, vtable.baseAlignment,
                                     currentThread);
            Array result = Magic.toArray(Magic.fromAddress(vectorAddr));
            CreateObject(result, vtable, currentThread);
            result.InitializeVectorLength(numElements);
            if (VTable.enableGCProfiling) {
                System.GC.bytesAllocated += (ulong)numBytes;
                System.GC.objectsAllocated++;
            }
            return result;
        }

        [ManualRefCounts]
        internal override  Array AllocateArray(VTable vtable,
                                               int rank,
                                               int totalElements,
                                               Thread currentThread)
        {
            UIntPtr numBytes =
                ObjectLayout.ArraySize(vtable, unchecked((uint)totalElements));
            UIntPtr arrayAddr =
                AllocateObjectMemory(numBytes, vtable.baseAlignment,
                                     currentThread);
            Array result = Magic.toArray(Magic.fromAddress(arrayAddr));
            CreateObject(result, vtable, currentThread);
            result.InitializeArrayLength(rank, totalElements);
            if (VTable.enableGCProfiling) {
                System.GC.bytesAllocated += (ulong)numBytes;
                System.GC.objectsAllocated++;
            }
            return result;
        }

        [ManualRefCounts]
        internal override String AllocateString(int stringLength,
                                                Thread currentThread)
        {
            VTable vtable =
                Magic.toRuntimeType(typeof(System.String)).classVtable;
            UIntPtr numBytes =
                ObjectLayout.StringSize(vtable,
                                        unchecked((uint) (stringLength+1)));
            UIntPtr stringAddr =
                AllocateObjectMemory(numBytes, unchecked((uint) UIntPtr.Size),
                                     currentThread);
            String result = Magic.toString(Magic.fromAddress(stringAddr));
            CreateObject(result, vtable, currentThread);
            result.InitializeStringLength(stringLength);
            if (VTable.enableGCProfiling) {
                System.GC.bytesAllocated += (ulong)numBytes;
                System.GC.objectsAllocated++;
            }
            return result;
        }

        [ManualRefCounts]
        [Inline]
        protected virtual void CreateObject(Object obj, VTable vtable,
                                            Thread currentThread)
        {
            obj.vtable = vtable;
        }

        [Inline]
        protected bool IsValidGeneration(int generation)
        {
            return ((generation >= MinGeneration) && (generation <= MaxGeneration));
        }
    }

}
