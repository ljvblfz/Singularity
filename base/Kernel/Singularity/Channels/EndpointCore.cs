////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   EndpointCore.cs
//

//  HACK: Because we currently compile this file as part of the Kernel with C#, we can't
//        make EndpointCore a rep struct, which is necessary for inheriting from it.
//        The compiler and Bartok recognize EndpointCore as special and treat it as
//        unsealed.
//        This hack can be removed, once we compile it with Sing#.
//        DON'T change the name until then!

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Singularity.Channels;
using Microsoft.Singularity.Security;
using Microsoft.Singularity.Memory;

namespace Microsoft.Singularity.Channels
{
    using Microsoft.Singularity.V1.Threads;
    using Microsoft.Singularity.V1.Security;
    using Microsoft.Singularity.V1.Services;
    using Microsoft.Singularity.V1.Types;
    using Allocation = Microsoft.Singularity.Memory.SharedHeap.Allocation;
    using System.Threading;
    using SharedHeap = Microsoft.Singularity.Memory.SharedHeap;

    [CLSCompliant(false)]
    public enum EndpointCoreEvent : ushort
    {
        Connect = 4,
        TransferToProcess = 5,
    }

#if !PAGING
    [CLSCompliant(false)]
    [CCtorIsRunDuringStartup]
    unsafe public struct EndpointCore
#else
    // The portion of the endpoint that is only
    // accessible to the kernel or to trusted run-time code:
    [CLSCompliant(false)]
    unsafe struct EndpointTrusted
#endif
    {

        /// <summary>
        /// Flag indicating that the endpoint is closed.
        /// This gets set either by an explicit close, or when
        /// the kernel determines that the endpoint is not reachable.
        /// NOTE: A channel only goes away entirely, once both ends are closed!
        ///
        /// NOTE: The lifetime of the various involved data structures is
        /// as follows:
        ///
        /// Endpoint Allocations: as today. Owned by a process, goes away when
        ///                       the process decides to free it or the
        ///                       sharedHeapWalker finds it.
        ///
        /// Endpoint structures:  ref counted as today in a process domain.
        ///                       Alive as long as there is at least one allocation
        ///                       structure to it. Note that if the channel spans
        ///                       protection domains, then the endpoint structure is
        ///                       not kept alive by the peer.
        ///
        /// Endpoint trusted:     ref counted in the kernel. Get collected once both
        ///                       sides are closed.
        /// </summary>
        private volatile bool closed;

        /// <summary>
        /// The endpoint to which this endpoint is connected.
        /// </summary>
#if PAGING
        private EndpointTrusted* peer;

        static private SpinLock spinLock;
        private int refCount;

        /// <summary>
        /// The user-level allocation for the endpoint that owns this
        /// EndpointCoreData struct.
        ///   this == this->self->data->trusted
        /// </summary>
        private Allocation* /*EndpointCore* opt(ExHeap)*/ self;

        /// <summary>
        /// If the peer is in the same protection domain, then this
        /// points to an allocation owned by this side to the peer
        /// EndpointCoreData struct.
        ///
        /// We materialize this on demand as well as tear it down
        /// when an endpoint moves out of a protection domain.
        ///
        /// This allocation is used for the fast path to write directly
        /// into the peer data structure.
        /// </summary>
        internal Allocation* /*EndpointCore* opt(ExHeap)*/ directPeerAllocation;

        private EndpointUpdates endpointUpdates;

        /// <summary>
        /// Return a handle for the peer
        /// </summary>
        internal Allocation* DirectPeerAllocation(out bool marshall)
        {
            marshall = ChannelSpansDomains;

            return this.directPeerAllocation;
        }

        bool ChannelSpansDomains {
            get {
                Process us = Process.GetProcessByID(this.ownerProcessId);
                Process them = Process.GetProcessByID(this.peer->ownerProcessId);
                return (us.ProcessSharedHeap != them.ProcessSharedHeap);
            }
        }

#else
        private Allocation* /*EndpointCore* opt(ExHeap)*/ peer;
#endif

        /// <summary>
        /// Event on which sends are signaled to this endpoint.
        /// The handle is owned by the kernel, since the endpoint can move.
        /// The kernel deallocates the handle when the channel is deallocated.
        /// NOTE: stays valid until the entire channel gets collected.
        /// </summary>
        private AutoResetEventHandle messageEvent;

        /// <summary>
        /// Event handle in case this endpoint is part of a collection
        /// </summary>
        private AutoResetEventHandle collectionEvent;

        /// <summary>
        /// Contains the process id of the process currently owning this end of the
        /// channel.
        /// </summary>
        private int ownerProcessId;

        /// <summary>
        /// Contains the principal that currently controls this end of the
        /// channel.  For now, this will simply be the owning process ID.  But
        /// this will change once we implement delegation.
        /// </summary>
        private PrincipalHandle ownerPrincipalHandle;

        /// <summary>
        /// Contains the number of sends to this endpoint.
        /// </summary>
        private int receiveCount;

        /// <summary>
        /// Contains the channelId (positive on the EXP endpoint, negative on the imp endpoint)
        /// </summary>
        private int channelId;

        /// <summary>
        /// Contains the pinned status
        /// </summary>
        ///private bool pinned;

        private static int channelIdGenerator;

        private static int openChannelCount = 0;

#if SINGULARITY_KERNEL && CHANNEL_COUNT
        internal static void IncreaseBytesSentCount(long bytes)
        {
              PerfCounters.AddBytesSent(bytes);
        }

#endif // SINGULARITY_KERNEL

        public static int OpenChannelCount { get { return openChannelCount; } }

#if PAGING
        struct FreeElement
        {
            internal FreeElement* next;
        }
        private static FreeElement* freeAllocList;
        private static SpinLock allocLock;

        // Warning: this allocation resides in a user address space.
        // TODO: add sanity checks to ensure the correct address space is mapped.
        public Allocation* Self { get { return self; } }

        public EndpointTrusted* Peer
        {
            [NoHeapAllocation]
            get { return peer; }
        }

        public void FlushTo(Allocation* peer) {
            // only touch peer and kernel
            this.endpointUpdates.Fetch(peer);
        }

        public static EndpointTrusted* New(Allocation* /*EndpointCore* opt(ExHeap)!*/ self)
        {
            bool iflag = Processor.DisableInterrupts();
            allocLock.Acquire();
            try {
                //
                // Attempt to get a struct from our free list.
                //
                FreeElement* address = freeAllocList;
                if (address != null) {
                    freeAllocList = address->next;
                }
                else {
                    //
                    // Free list is empty.
                    // Allocate new page and break it up into structs.
                    // Use first struct to satisfy current
                    // request, put remainder on the free list.
                    //
                    UIntPtr newSpace = MemoryManager.KernelAllocate(
                        1, null, 0, System.GCs.PageType.Shared);

                    uint size = (uint)sizeof(EndpointTrusted);

                    address = (FreeElement*) newSpace;
                    newSpace += size;
                    uint spaceRemaining = MemoryManager.PageSize - size;
                    while (spaceRemaining > size) {
                        FreeElement* next = freeAllocList;
                        freeAllocList = (FreeElement*) newSpace;
                        freeAllocList->next = next;
                        newSpace += size;
                        spaceRemaining -= size;
                    }
                }

                //
                // Initialize the trusted endpoint (zero fields).
                //
                Buffer.ZeroMemory((byte *)address, sizeof(EndpointTrusted));
                EndpointTrusted* ep = (EndpointTrusted*) address;
                ep->refCount = 0;
                ep->self = self;
                return ep;
            }
            finally {
                allocLock.Release();
                Processor.RestoreInterrupts(iflag);
            }
        }

        public static void Delete(EndpointTrusted* ep)
        {
            bool iflag = Processor.DisableInterrupts();
            allocLock.Acquire();
            try {
                FreeElement* element = (FreeElement*) ep;
                element->next = freeAllocList;
                freeAllocList = element;
            }
            finally {
                allocLock.Release();
                Processor.RestoreInterrupts(iflag);
            }
        }
#endif


        ///
        /// Closes this end of the channel and frees associated resources, EXCEPT the block
        /// of memory for this endpoint. It must be released by the caller. Sing# does this
        /// for the programmer.
        ///
        /// This runs in the kernel to avoid a race condition with Process.Stop.
#if !PAGING
        public bool Dispose() {
            if (this.Closed()) {
                return false;
            }
            this.Close(); // mark our side closed before waking up peer
            // (Note: peer may be null if process stopped before Connect.)
            if (this.peer != null) {
                EndpointCore* peerData = (EndpointCore*)Allocation.GetDataUnchecked(this.peer);
                if (peerData == null) {
                    return false;
                }
                peerData->Notify();  // wake up peer thread if waiting to receive
            }
            return true;
        }
#else
        public static void Dispose(EndpointTrusted* endpoint) {
            if (endpoint->Closed()) {
                throw new ApplicationException("Endpoint already closed");
            }
            endpoint->Close(); // mark our side closed before waking up peer
            // (Note: peer may be null if process stopped before Connect.)
            if (endpoint->Peer != null) {
                endpoint->Peer->Notify();  // wake up peer thread if waiting to receive
            }
        }
#endif //PAGING

#if !PAGING
        /// <summary>
        /// Explicitly frees this end of the channel. If other end is also
        /// freed, the channel is deallocated, meaning we have to deallocate
        /// the kernel handles for the auto reset events.
        /// Since both threads on the channel could try to do this simultaneously,
        /// we use the ref counting on the underlying endpoints to let the last
        /// free operation (the one pulling the ref count to 0) to free the associated
        /// event.
        /// </summary>
        public static void Free(Allocation* /*EndpointCore* opt(ExHeap)!*/ endpoint) {
            // Use unchecked GetData here, since this may be called from the
            // cleanup threading running in the kernel.
            //
            EndpointCore* epData = (EndpointCore*)Allocation.GetDataUnchecked(endpoint);
            if (epData == null) throw new ApplicationException("SharedHeap.GetData return null");
            if (!epData->closed) {
                throw new ApplicationException("Endpoint must have been Disposed prior to Delete");
            }

            Tracing.Log(Tracing.Debug, "Freeing endpoint {0:x8}", (UIntPtr)endpoint);

            // If peer is also closed, try to free ALL auto-reset-event resources associated with the channel.
            // (Note: peer may be null if process stopped before Connect.)
            if (epData->peer != null) {
                // Use unchecked GetData here, since this may be called from the
                // cleanup threading running in the kernel.
                //
                TryFreeResources(epData->peer, SharedHeap.CurrentProcessSharedHeap.EndpointPeerOwnerId); // release our ref count on the peer
                }
            TryFreeResources(endpoint, SharedHeap.CurrentProcessSharedHeap.EndpointOwnerId); // release our endpoint
            }
#else
        /// <summary>
        /// This is initiated by one side's Allocation endpoint being deleted (either explicitly)
        /// or via the cleanup thread.
        /// The code here is responsible for
        /// 1) releasing the direct peer allocation
        /// 2) decrementing the trusted ref count on the channel
        /// 3) if the trusted ref count goes to 0 for one of the two trusted endpoints,
        ///    then delete that trusted endpoint and its associated structures.
        ///
        /// NOTE: the main allocation and endpoint structures in the exchange heap are
        ///       not deleted here!
        /// </summary>
        public static void Free(EndpointTrusted* endpoint) {
            if (!endpoint->closed) {
                throw new ApplicationException("Endpoint must have been Disposed prior to Delete");
            }

            Tracing.Log(Tracing.Debug, "Freeing endpoint {0:x8}", (UIntPtr)(endpoint->self));

#region Free the user data endpoint and peer
            // We assume the freeing is done by a thread that runs in the same
            // protection domain as the endpoints. Otherwise, how could we possibly
            // access the data

            // (Note: peer may be null if process stopped before Connect.)
            Allocation* directPeerAllocation = endpoint->directPeerAllocation;

            if (directPeerAllocation != null) {
                // This means the peer is in the same protection domain.
                // delete this allocation.
                //
                SharedHeap.CurrentProcessSharedHeap.Free(directPeerAllocation,
                                                         SharedHeap.CurrentProcessSharedHeap.EndpointPeerOwnerId);

                SharedHeap.CurrentProcessSharedHeap.Free(endpoint->Self,
                                                         SharedHeap.CurrentProcessSharedHeap.EndpointOwnerId);
            }
#endregion

            // If peer is also closed, try to free ALL auto-reset-event resources associated with the channel.
            // (Note: peer may be null if process stopped before Connect.)
            if (endpoint->Peer != null) {
                TryFreeResources(endpoint->Peer); // release our ref count on the peer
            }
            TryFreeResources(endpoint); // release our endpoint
        }
#endif //PAGING

        /// <summary>
        /// The peer thread might try this too, so we use the ref count of the underlying memory
        /// to allow only the last freeer to also free the associated auto-reset event handle.
        /// Make sure to grab the handle before freeing the endpoint.
        /// </summary>
#if !PAGING
        unsafe private static void TryFreeResources(Allocation* /*EndpointCore*/ endpoint,
                                                    SharedHeap.AllocationOwnerId ownerId) {

            // Use unchecked GetData here, since this may be called from the
            // cleanup threading running in the kernel.
            //
            EndpointCore* epData = (EndpointCore*)Allocation.GetDataUnchecked(endpoint);
            AutoResetEventHandle areHandle = epData->messageEvent;

            int channelId = epData->channelId;

            bool lastRefGone = SharedHeap.KernelSharedHeap.Free(endpoint, ownerId);

            if (lastRefGone && channelId > 0) {
                // the entire channel is closed which we attribute to the closing of the Exp side
                // (whose channelId is positive).
                //
                DebugStub.Assert(openChannelCount > 0,
                                 "EndpointCore.cs: attempt to take open " +
                                 "channel count negative");
                openChannelCount--;
                Tracing.Log(Tracing.Debug, "Entire channel is closed {0:x8}", (UIntPtr)endpoint);
            }

            if (lastRefGone && areHandle.id != UIntPtr.Zero) {
                // got the AutoResetEventHandle
                Process.kernelProcess.ReleaseHandle(areHandle.id);
            }
        }
#else
        unsafe private static void TryFreeResources(EndpointTrusted* endpoint) {
            AutoResetEventHandle areHandle = endpoint->messageEvent;
            int channelId = endpoint->channelId;

            bool iflag = Processor.DisableInterrupts();
            spinLock.Acquire();
            bool lastRefGone;
            try {
                endpoint->refCount--;
                lastRefGone = (endpoint->refCount == 0);
            }
            finally {
                spinLock.Release();
                Processor.RestoreInterrupts(iflag);
            }

            if (lastRefGone && channelId > 0) {
                // the entire channel is closed which we attribute to the closing of the Exp side
                // (whose channelId is positive).
                //
                DebugStub.Assert(openChannelCount > 0,
                                 "EndpointCore.cs: attempt to take open " +
                                 "channel count negative");
                openChannelCount--;
                Tracing.Log(Tracing.Debug, "Entire channel is closed {0:x8}", (UIntPtr)(endpoint->self));
            }

            if (lastRefGone) {
                endpoint->endpointUpdates.FreeResources();
                EndpointTrusted.Delete(endpoint);
            }

            if (lastRefGone && areHandle.id != UIntPtr.Zero) {
                // got the AutoResetEventHandle
                Process.kernelProcess.ReleaseHandle(areHandle.id);
            }
        }
#endif //PAGING

        /// <summary>
        /// Performs the initialization of the core part of each endpoint and cross links
        /// them to form a channel.
        /// </summary>
#if !PAGING
        public static void Connect(
            Allocation* /*EndpointCore* opt(ExHeap)!*/ imp,
            Allocation* /*EndpointCore* opt(ExHeap)!*/ exp)
        {
          if (imp == null || exp == null)
          {
              throw new ApplicationException("Connect called with null endpoints");
          }
          Tracing.Log(Tracing.Debug, "connect {0:x8} and {1:x8}", (UIntPtr)imp, (UIntPtr)exp);

          // keep track of how many channels are open
          openChannelCount++;
#if CHANNEL_COUNT
          PerfCounters.IncrementChannelsCreated();
#endif

          EndpointCore* impData = (EndpointCore*)Allocation.GetData(imp);
          EndpointCore* expData = (EndpointCore*)Allocation.GetData(exp);
          if (impData == null || expData == null)
          {
              throw new ApplicationException("SharedHeap.GetData return null");
          }
          impData->Initialize(exp);
          expData->Initialize(imp);
          expData->channelId = ++channelIdGenerator;
          impData->channelId = -1 * channelIdGenerator;
          Monitoring.Log(Monitoring.Provider.EndpointCore,
                         (ushort)EndpointCoreEvent.Connect, 0,
                         (uint)expData->channelId, 0, 0, 0, 0);
        }
#else
        public static void Connect(
            EndpointTrusted* imp,
            EndpointTrusted* exp,
            Allocation* /*EndpointCore* opt(ExHeap)!*/ userImp,
            Allocation* /*EndpointCore* opt(ExHeap)!*/ userExp)
        {
          if (imp == null || exp == null)
          {
              throw new ApplicationException("Connect called with null endpoints");
          }
          Tracing.Log(Tracing.Debug, "connect {0:x8} and {1:x8}", (UIntPtr)(imp->self), (UIntPtr)(exp->self));

          // keep track of how many channels are open
          openChannelCount++;

          imp->Initialize(exp, userExp);
          exp->Initialize(imp, userImp);
          exp->channelId = ++channelIdGenerator;
          imp->channelId = -1 * channelIdGenerator;
          Monitoring.Log(Monitoring.Provider.EndpointCore,
                         (ushort)EndpointCoreEvent.Connect, 0,
                         (uint)exp->channelId, 0, 0, 0, 0);
        }
#endif //PAGING

#if !PAGING
        private void Initialize(Allocation* /*EndpointCore* opt(ExHeap)*/ myPeer)
#else
        // Careful: it seems that the allocator for trusted endpoints does not null
        // memory!
        private void Initialize(EndpointTrusted* myPeer,
                                Allocation* /*EndpointCore* opt(ExHeap)*/ myUserPeer)
#endif
        {
            //
            // Create a new auto-reset event, and a handle in the kernel
            // process to hold it.
            //
            this.messageEvent = new AutoResetEventHandle(
                Process.kernelProcess.AllocateHandle(new AutoResetEvent(false)));
            this.collectionEvent = new AutoResetEventHandle();

            this.ownerProcessId = Thread.CurrentProcess.ProcessId;
            this.ownerPrincipalHandle = new PrincipalHandle(Thread.CurrentProcess.Principal.Val);

            this.closed = false;
            //this.pinned = false;
            this.receiveCount = 0;
            this.channelId = 0; // set in the Connect for Exp
            // IMPORTANT: Put peer allocations in separate list to avoid putting
            // endpoints twice in the same list:
#if !PAGING
            this.peer =
                SharedHeap.CurrentProcessSharedHeap.Share(myPeer,
                    SharedHeap.CurrentProcessSharedHeap.EndpointPeerOwnerId,
                    (UIntPtr)0,
                    Allocation.GetSize(myPeer));
#else
            this.directPeerAllocation =
                SharedHeap.CurrentProcessSharedHeap.Share(myUserPeer,
                    SharedHeap.CurrentProcessSharedHeap.EndpointPeerOwnerId,
                    (UIntPtr)0,
                    Allocation.GetSize(myUserPeer));
            // IMPORTANT: our update buffer must be as big as the peer endpoint,
            //            not self.
            endpointUpdates.Initialize(myUserPeer);
            this.refCount++;
            this.peer = myPeer;
            SpinLock peerLock = spinLock;

            // TODO: remove synchronization here; it looks useless
            bool iflag = Processor.DisableInterrupts();
            peerLock.Acquire();
            try {
                myPeer->refCount++;
            }
            finally {
                peerLock.Release();
                Processor.RestoreInterrupts(iflag);
            }
#endif
        }

        /// <summary>
        /// Set this end to closed
        /// </summary>
        public void Close() { this.closed = true; }

        public bool Closed() { return this.closed; }

#if PAGING
        /// <summary>
        /// The event to wait for messages on this endpoint. Used by Select.
        /// </summary>
        public SyncHandle GetWaitHandle() { return messageEvent; }
#endif //PAGING

        /// <summary>
        /// Notify the owner of this endpoint that a message is ready.
        /// Notifies the set owner if this endpoint is part of a set.
        /// </summary>
        private void Notify() {
            this.receiveCount++;
            Tracing.Log(Tracing.Debug, "Endpoint Notify");

            // NB maf / Cache the collection event to prevent
            // a race with the receiver.
            AutoResetEventHandle cached_collEvent = this.collectionEvent;
            if (cached_collEvent.id != UIntPtr.Zero) {
                AutoResetEventHandle.Set(cached_collEvent);
            }

            AutoResetEventHandle.Set(this.messageEvent);
        }

        internal void NotifyPeer()
        {
            // commit the update record if necessary
#if PAGING
            if (ChannelSpansDomains) {
                this.endpointUpdates.EndUpdate();
            }
            peer->Notify();
#else
            EndpointCore* peerData = (EndpointCore*)Allocation.GetData(this.peer);
            peerData->Notify();
#endif
#if CHANNEL_COUNT
            //Interlocked.Increment(ref messageCount);
            PerfCounters.IncrementMsgsSent();
#endif
        }

#if !PAGING
        /// <summary>
        /// Used internally by the kernel to transfer an endpoint to a new owner
        /// Called from TRef's within the Kernel.
        /// </summary>
        /// The argument ep must be an endpoint.
        public static Allocation* MoveEndpoint(SharedHeap fromHeap,
                                               SharedHeap toHeap,
                                               Process newOwner,
                                               Allocation* ep)
        {
            // Only one heap on non-PAGING builds!
            DebugStub.Assert(fromHeap == toHeap);

            // Careful about the order.
            // Since we don't know if this is a release (current process owns it)
            // or an acquire (current process does not necessarily own it), we
            // have to bypass the owner check here.
            int processId = newOwner.ProcessId;
            EndpointCore* epData = (EndpointCore*)Allocation.GetDataUnchecked(ep);
            epData->ownerProcessId = processId;
            epData->ownerPrincipalHandle = new PrincipalHandle(newOwner.Principal.Val);
            Allocation.SetOwnerProcessId(ep, processId);
            Allocation.SetOwnerProcessId(epData->peer, processId);
            Monitoring.Log(Monitoring.Provider.EndpointCore,
                           (ushort)EndpointCoreEvent.TransferToProcess, 0,
                           (uint)epData->channelId, (uint)processId, 0, 0, 0);
            return ep;
        }
#else
        ///
        /// Only performs the adjustments of the ownership of blocks,
        /// No copies.
        ///
        private static void TransferToProcess(EndpointTrusted* ep,
                                              Process p)
        {
            int processId = p.ProcessId;
            ep->ownerProcessId = processId;
            ep->ownerPrincipalHandle = new PrincipalHandle(p.Principal.Val);

            // TODO: allocations should be process-local:
            Allocation.SetOwnerProcessId(ep->self, processId);
            Allocation.SetOwnerProcessId(ep->directPeerAllocation, processId);

            Monitoring.Log(Monitoring.Provider.EndpointCore,
                           (ushort)EndpointCoreEvent.TransferToProcess, 0,
                           (uint)ep->channelId, (uint)processId, 0, 0, 0);
        }

        /// <summary>
        /// Generic copy (either from kernel or to kernel)
        /// Determines if the thing we are moving is an endpoint and copies it accordingly.
        /// </summary>
        private static SystemType EndpointCoreSystemType =
            typeof(Microsoft.Singularity.V1.Services.EndpointCore).GetSystemType();

        public static Allocation* MoveData(SharedHeap fromHeap, SharedHeap toHeap,
                                           Process newOwner, Allocation* data)
        {
            if (data == null) return data;

            if (!fromHeap.Validate(data)) {
                throw new ArgumentException("Bad argument. Not visible");
            }

            // We can only transfer either into our out of the kernel's heap
            DebugStub.Assert(fromHeap == SharedHeap.KernelSharedHeap ||
                             toHeap == SharedHeap.KernelSharedHeap);

            if (SystemType.IsSubtype(data, EndpointCoreSystemType))
            {
                // we have an endpoint
                return MoveEndpoint(fromHeap, toHeap, newOwner, data);
            }
            else
            {
                // we have a NON-endpoint
                return MoveNonEndpoint(fromHeap, toHeap, newOwner, data);
            }
        }
        /// <summary>
        /// Used internally by the kernel to transfer data that is NOT and endpoint
        /// to a new owner
        /// </summary>
        private static Allocation* MoveNonEndpoint(SharedHeap fromHeap, SharedHeap toHeap,
                                                   Process newOwner, Allocation* data)
        {
            // We can only transfer either into our out of the kernel's heap
            DebugStub.Assert(fromHeap == SharedHeap.KernelSharedHeap ||
                             toHeap == SharedHeap.KernelSharedHeap);

            if (fromHeap != toHeap)
            {
                data = toHeap.Move(data, fromHeap, fromHeap.DataOwnerId, toHeap.DataOwnerId);
            }

            // update id's in kernel structure and who owns the blocks
            Allocation.SetOwnerProcessId(data, newOwner.ProcessId);
            return data;
        }


        /// <summary>
        /// Used internally by the kernel to transfer and endpoint data to a new owner
        ///
        /// Must update channel structures if we change co-location of peers.
        /// 1) if currently co-located and no longer co-located after move, must
        ///    allocate new target proxies etc.
        ///
        /// 2) if we were not colocated and we are after the move, must join the proxies
        ///    into a direct structure.
        ///
        /// </summary>
        private static Allocation* MoveEndpoint(SharedHeap fromHeap, SharedHeap toHeap,
                                                Process newOwner, Allocation* ep)
        {
            EndpointCore *epc = (EndpointCore*)Allocation.GetDataUnchecked(ep);
            EndpointTrusted* ept = epc->Trusted;

            if (ep != ept->self) {
                // passed in ep only pretends to be an endpoint
                DebugStub.Break();
            }

            // We can only transfer either into our out of the kernel's heap
            DebugStub.Assert(fromHeap == SharedHeap.KernelSharedHeap ||
                             toHeap == SharedHeap.KernelSharedHeap);

            if (fromHeap != toHeap)
            {
                // First Copy Self
                Allocation* epCopy = toHeap.ShallowCopy(ep,
                                                        toHeap.EndpointOwnerId);
                fromHeap.Free(ep, fromHeap.EndpointOwnerId);
                ept->self = epCopy;


                // Determine if we are going to be colocated or not:
                Process peerProcess = Process.GetProcessByID(ept->Peer->ownerProcessId);
                SharedHeap peerHeap = peerProcess.ProcessSharedHeap;

                // Now deal with Peer
                Allocation* peerAllocation = ept->directPeerAllocation;

                if (peerHeap != toHeap)
                {
                    // After the move, we are not colocated with peer.
                    // Copy peer proxy as well
                    Allocation* peerCopy = toHeap.ShallowCopy(peerAllocation,
                                                              toHeap.EndpointPeerOwnerId);

                    fromHeap.Free(peerAllocation, fromHeap.EndpointPeerOwnerId);

                    ept->directPeerAllocation = peerCopy;
                }
                else
                {
                    // assume peerHeap == toHeap

                    // After the move, we are colocated with peer.
                    // 1) Free peer proxy
                    // 2) Point peer structures at actual peers rather than proxy
                    fromHeap.Free(peerAllocation, fromHeap.EndpointPeerOwnerId);

                    // Now comes a tricky unsafe part. There's a race in that we are
                    // trying to share the peer structure, but that might be concurrently
                    // decrementing its ref count.

                    // TODO: look at shared heap implementation to guard against this.

                    Allocation *truePeer = ept->Peer->self; // CAREFUL: We may not own this guy

                    ept->directPeerAllocation =
                        toHeap.Share(truePeer,
                                     toHeap.EndpointPeerOwnerId,
                                     UIntPtr.Zero,
                                     Allocation.GetSize(truePeer));

                    // Now we must also update the peer's directPeer to point to us
                    // rather than the proxy!
                    peerHeap.Free(ept->Peer->directPeerAllocation, peerHeap.EndpointPeerOwnerId);
                    ept->Peer->directPeerAllocation =
                        peerHeap.Share(epCopy,
                                        peerHeap.EndpointPeerOwnerId,
                                        UIntPtr.Zero,
                                        Allocation.GetSize(epCopy));

                    // Make peer process own this shared allocation
                    Allocation.SetOwnerProcessId(ept->Peer->directPeerAllocation,
                                                 peerProcess.ProcessId);
                }
            }

            // update id's in kernel structure and who owns the blocks
            TransferToProcess(ept, newOwner);
            return ept->self;
        }


#endif //PAGING

        public int ChannelId {
            [NoHeapAllocation]
            get { return this.channelId; }
        }

        public int ProcessId {
            [NoHeapAllocation]
            get {
                return this.ownerProcessId;
            }
#if PAGING
            set {
                this.ownerProcessId = value;
            }
#endif
        }

        public int ReceiveCount
        {
            [NoHeapAllocation]
            get { return this.receiveCount; }
        }

        /// <summary>
        /// Used from ChannelDiagnostics module to access data it does not own
        /// </summary>
        public int PeerProcessId
        {
            [NoHeapAllocation]
            get
            {
#if !PAGING
                EndpointCore* peer = (EndpointCore*)Allocation.GetDataUnchecked(this.peer);
                return peer->ownerProcessId;
#else
                return Peer->ownerProcessId;
#endif
            }
        }

        public PrincipalHandle PrincipalHandle {
            [NoHeapAllocation]
            get {
                return ownerPrincipalHandle;
            }
#if PAGING
            set {
                this.ownerPrincipalHandle = value;
            }
#endif
        }

        public PrincipalHandle OwnerPrincipalHandle {
            [NoHeapAllocation]
            get {
                return ownerPrincipalHandle;
            }
        }

        public PrincipalHandle PeerPrincipalHandle
        {
            [NoHeapAllocation]
            get
            {
#if !PAGING
                EndpointCore* peer = (EndpointCore*)Allocation.GetDataUnchecked(this.peer);
                return peer->ownerPrincipalHandle;
#else
                return peer->ownerPrincipalHandle;
#endif
            }
        }

        /// <summary>
        /// Used from ChannelDiagnostics module to access data it does not own
        /// </summary>
        public int PeerReceiveCount
        {
            [NoHeapAllocation]
            get
            {
#if !PAGING
                EndpointCore* peer = (EndpointCore*)Allocation.GetDataUnchecked(this.peer);
                return peer->receiveCount;
#else
                return Peer->receiveCount;
#endif
            }
        }

#if PAGING
        public void LinkIntoCollection(AutoResetEventHandle ev) {
            this.collectionEvent = ev;
        }

        public void UnlinkFromCollection(AutoResetEventHandle ev) {
            this.collectionEvent = new AutoResetEventHandle();
        }

        internal void BeginUpdate(byte* basep, byte* source,
                                  int* tagAddress, int size) {
            endpointUpdates.BeginUpdate(basep, source, tagAddress, size);
        }

        unsafe internal void MarshallPointer(byte* basep, void** target, SystemType type)
        {
            endpointUpdates.MarshallPointer(basep, target, type,
                                            Process.GetProcessByID(peer->ownerProcessId));
        }

#endif

    }

#if PAGING
    [CLSCompliant(false)]
    unsafe public struct EndpointUpdate
    {
        // Each EndpointUpdate points to a message and tag in the
        // data area of the EndpointUpdates.  The tag is always
        // a 32-bit word.
        internal ushort msgOffset;
        internal ushort msgSize; // message size in bytes
        internal ushort tagOffset;
        internal ushort ptrToMarshallOffset0;
        internal ushort ptrToMarshallOffset1;
        internal ushort ptrToMarshallOffset2;

        public void Init(ushort msgOffset, ushort msgSize, ushort tagOffset)
        {
            this.msgOffset = msgOffset;
            this.msgSize = msgSize;
            this.tagOffset = tagOffset;
            this.ptrToMarshallOffset0 = 0;
            this.ptrToMarshallOffset1 = 0;
            this.ptrToMarshallOffset2 = 0;
        }

        public void AddMarshallOffset(ushort ptrOffset)
        {
            DebugStub.Assert(ptrOffset != 0);

            // There's only space for three pointers in out buffer
            if (ptrToMarshallOffset0 == 0) {
                ptrToMarshallOffset0 = ptrOffset;
                return;
            }
            if (ptrToMarshallOffset1 == 0) {
                ptrToMarshallOffset1 = ptrOffset;
                return;
            }
            if (ptrToMarshallOffset2 == 0) {
                ptrToMarshallOffset2 = ptrOffset;
                return;
            }
            // out of marshall pointer locations
            DebugStub.Assert(false);
        }

        public void CopyData(byte* source,
                             byte* dest)
        {
            Buffer.MoveMemory(dest + msgOffset,
                              source + msgOffset,
                              (int)(msgSize));

            *((int*)(dest + tagOffset)) = *((int*)(source + tagOffset));
        }

        private static void MarshallPointer(byte* source,
                                            byte* dest,
                                            ref ushort offsetRef)
        {
            ushort offset = offsetRef;

            if (offset == 0) return; // unused

            Allocation** srcptr = (Allocation**)(source + offset);

            // update the pointer in the kernel space
            *srcptr = EndpointTrusted.MoveData(SharedHeap.KernelSharedHeap,
                                               Thread.CurrentProcess.ProcessSharedHeap,
                                               Thread.CurrentProcess,
                                               *srcptr);
            offsetRef = 0; // mark unused
        }
        public void MarshallAndCopyData(byte* source,
                                        byte* dest)
        {
            // Marshall in kernel space
            MarshallPointer(source, dest, ref ptrToMarshallOffset0);
            MarshallPointer(source, dest, ref ptrToMarshallOffset1);
            MarshallPointer(source, dest, ref ptrToMarshallOffset2);

            CopyData(source, dest);
        }

    }

    [CLSCompliant(false)]
    unsafe public struct EndpointUpdates
    {
        // The buffer holds two areas:
        //   - Endpoint data (same layout as Endpoint)
        //   - EndpointUpdate values (one for each word in the data)
        private Allocation* buffer;
        private int pending; // number of currently pending updates
        private EndpointUpdate* updates; // interior pointer into buffer.data
        private EndpointUpdate current; // currently being constructed

        internal void BeginUpdate(byte* basep, byte* source,
                                  int* tagAddress, int size)
        {
            DebugStub.Assert(source >= basep);

            DebugStub.Assert(source - basep < 0x10000);
            DebugStub.Assert(0 <= size && size < 0x10000);
            DebugStub.Assert((byte*)tagAddress >= basep);
            DebugStub.Assert((byte*)tagAddress - basep < 0x10000);

            ushort msgOffset = (ushort)(source-basep);
            ushort msgSize = (ushort)size;
            ushort tagOffset = (ushort)((byte*)tagAddress-basep);

            this.current.Init(msgOffset, msgSize, tagOffset);

            this.current.CopyData(basep, GetBufferData());
        }

        internal void EndUpdate() {
            this.Add(this.current);
        }

        unsafe internal void MarshallPointer(byte* basep, void** target, SystemType type,
                                             Process newOwner)
        {
            long offset = (byte*)target-basep;

            DebugStub.Assert(offset < 0x10000);

            byte* kernelBuffer = GetBufferData();
            Allocation** dstPtr = ((Allocation**)(kernelBuffer+offset));
            Allocation* userPtr = *dstPtr;
            SharedHeap.CurrentProcessSharedHeap.Validate(userPtr);

            // Validate that pointer passed is of the required type
            if (!SystemType.IsSubtype(userPtr, type)) {
                // Trusted marshaller thinks we are supposed to pass something other
                // than the user passed.
                DebugStub.Break();
            }

            // Copy it into the kernel space and update the pointer
            // in the kernel space buffer
            *dstPtr = EndpointTrusted.MoveData(
                Thread.CurrentProcess.ProcessSharedHeap,
                SharedHeap.KernelSharedHeap,
                newOwner,
                userPtr);

            this.current.AddMarshallOffset( (ushort)offset );
        }

        internal void Initialize(Allocation* endpoint)
        {
            UIntPtr size = Allocation.GetSize(endpoint);
            DebugStub.Assert(size < 0x10000); // must fit in ushort

            // TODO: it would be better not to allocate an allocation record here:
            buffer = SharedHeap.KernelSharedHeap.Allocate(
                size + 10*sizeof(EndpointUpdate), // allocate endpoint plus 10 update records
                typeof(EndpointUpdate).GetSystemType().TypeId,
                0,
                SharedHeap.KernelSharedHeap.DataOwnerId);
            Allocation.SetOwnerProcessId(buffer, Process.kernelProcess.ProcessId);
            pending = 0;
            updates = (EndpointUpdate*) (GetBufferData() + (int)size);
        }

        internal void FreeResources()
        {
            SharedHeap.KernelSharedHeap.Free(buffer, SharedHeap.KernelSharedHeap.DataOwnerId);
        }


        public Allocation* GetBuffer()
        {
            return buffer;
        }

        public byte* GetBufferData()
        {
            return (byte*) Allocation.GetDataUnchecked(buffer);
        }

        // Add an update to the buffer.
        // The data pointed to by the update should already be in "buffer".
        // Called in the sender's thread.
        public void Add(EndpointUpdate update)
        {
            // MULTIPROCESSOR NOTE: tricky concurrency between sender and receiver
            int n = pending;
            DebugStub.Assert(updates + (n + 1) <=
                GetBufferData() + (int)(Allocation.GetSize(buffer)));

            while (true) {
                *(updates + n) = update;
                int old = Interlocked.CompareExchange(ref pending, n + 1, n);
                if (old == n) {
                    break;
                }
                // Receiver just flushed the updates and didn't see ours, so
                // put the update at index 0.
                DebugStub.Assert(old == 0);
                n = 0;
            }
        }

        /*
        public void Add(byte* msgPtr, int msgSize, byte* tagPtr)
        {
            byte* data = GetBufferData();
            DebugStub.Assert(msgPtr >= data);
            DebugStub.Assert(msgPtr - data < 0x10000);
            DebugStub.Assert(0 <= msgSize && msgSize < 0x10000);
            DebugStub.Assert(tagPtr >= data);
            DebugStub.Assert(tagPtr - data < 0x10000);
            Add(new EndpointUpdate(
                (ushort)(msgPtr - data),
                (ushort)msgSize,
                (ushort)(tagPtr - data            DebugStub.Assert(msgPtr >= data);
            DebugStub.Assert(msgPtr - data < 0x10000);
            DebugStub.Assert(0 <= msgSize && msgSize < 0x10000);
            DebugStub.Assert(tagPtr >= data);
            DebugStub.Assert(tagPtr - data < 0x10000);
)));
        }
        */

        // Flush all pending updates into the destination Endpoint.
        // Called in the receiver's thread.
        public void Fetch(Allocation* destEp)
        {
            // MULTIPROCESSOR NOTE: tricky concurrency between sender and receiver
            int n = pending;
            int i = 0;
            while (i < n) {
                for (; i < n; i++) {
                    (updates + i)->MarshallAndCopyData(GetBufferData(),
                                                       (byte*) (Allocation.GetData(destEp)));
                }
                // We've delivered all the updates that we know of (n==i),
                // so try to change pending from n to 0.  If this succeeds,
                // n will stay the same (n==i).  Otherwise, n will be a more
                // recent value of pending (n>i).
                n = Interlocked.CompareExchange(ref pending, 0, n);
            }
        }
    }

    // The portion of the endpoint that is untrusted and may
    // contain random garbage.
    [CLSCompliant(false)]
    [CCtorIsRunDuringStartup]
    unsafe public struct EndpointCore
    {
        private EndpointTrusted* id;

        [NoHeapAllocation]
        internal EndpointCore(EndpointTrusted* trusted)
        {
            this.id = trusted;
        }

        internal EndpointTrusted* Trusted {
            [NoHeapAllocation]
            get {
                return id;
            }
        }

        private static EndpointTrusted* AllocationTrusted(
            Allocation* /*EndpointCore* opt(ExHeap)!*/ endpoint)
        {
            return ((EndpointCore*)Allocation.GetData(endpoint))->Trusted;
        }

        public static int OpenChannelCount { get { return EndpointTrusted.OpenChannelCount; } }

        ///
        /// Closes this end of the channel and frees associated resources, EXCEPT the block
        /// of memory for this endpoint. It must be released by the caller. Sing# does this
        /// for the programmer.
        ///
        /// This runs in the kernel to avoid a race condition with Process.Stop.
        public static void Dispose(ref EndpointCore endpoint) {
            EndpointTrusted.Dispose(endpoint.Trusted);
        }

        /// <summary>
        /// Explicitly frees this end of the channel. If other end is also
        /// freed, the channel is deallocated, meaning we have to deallocate
        /// the kernel handles for the auto reset events.
        /// Since both threads on the channel could try to do this simultaneously,
        /// we use the ref counting on the underlying endpoints to let the last
        /// free operation (the one pulling the ref count to 0) to free the associated
        /// event.
        /// </summary>
        public static void Free(Allocation* /*EndpointCore* opt(ExHeap)!*/ endpoint) {
            EndpointTrusted.Free(AllocationTrusted(endpoint));
        }

        /// <summary>
        /// Performs the initialization of the core part of each endpoint and cross links
        /// them to form a channel.
        /// </summary>
        public static void Connect(
            Allocation* /*EndpointCore* opt(ExHeap)!*/ imp,
            Allocation* /*EndpointCore* opt(ExHeap)!*/ exp)
        {
            // TODO: allocate handle in local process handle table
            //Process process = Thread.CurrentProcess;
            Process process = Process.kernelProcess;
            EndpointTrusted *impEp = EndpointTrusted.New(imp);
            EndpointTrusted *expEp = EndpointTrusted.New(exp);
            EndpointCore *impCore = (EndpointCore*)Allocation.GetData(imp);
            EndpointCore *expCore = (EndpointCore*)Allocation.GetData(exp);
            impCore->id = impEp;
            expCore->id = expEp;
            EndpointTrusted.Connect(impEp, expEp, imp, exp);
        }

        /// <summary>
        /// Set this end to closed
        /// </summary>
        public void Close() { Trusted->Close(); }

        public bool Closed() { return Trusted->Closed(); }

        public bool PeerClosed() {
            // make sure to flush updates from peer into this endpoint
            Trusted->Peer->FlushTo(Trusted->Self);
            return Trusted->Peer->Closed();
        }

        /// <summary>
        /// Return a handle for the peer
        /// </summary>
        public Allocation* Peer(out bool marshall) {
            Allocation* peerAlloc = Trusted->DirectPeerAllocation(out marshall);
            return peerAlloc;
        }

        /// <summary>
        /// The event to wait for messages on this endpoint. Used by Select.
        /// </summary>
        public SyncHandle GetWaitHandle() { return Trusted->GetWaitHandle(); }

        /// <summary>
        /// Notify the peer of this endpoint that a message is ready.
        /// Notifies the set owner if this endpoint is part of a set.
        /// </summary>
        public void NotifyPeer() {
            Trusted->NotifyPeer();
        }

        /// <summary>
        /// Used internally by the kernel to transfer an endpoint to a new owner
        ///
        /// Can be used to transfer ANY kind of shared heap data, not just endpoints.
        /// </summary>
        /// The argument ep must be an endpoint.
        public static Allocation* MoveEndpoint(SharedHeap fromHeap, SharedHeap toHeap,
                                               Process newOwner, Allocation *ep)
        {
            return EndpointTrusted.MoveData(fromHeap, toHeap, newOwner, ep);
        }

        /// <summary>
        /// Transfer the given Allocation block to the target endpoint
        /// </summary>
        // TODO: change "ref EndpointCore" to "EndpointCore"
        // TODO: rethink this operation
        public static void TransferBlockOwnership(Allocation* ptr, ref EndpointCore target)
        {
            Monitoring.Log(Monitoring.Provider.ChannelService,
                           (ushort)ChannelServiceEvent.TransferBlockOwnership, 0,
                           (uint)target.Trusted->ChannelId, (uint)target.Trusted->ProcessId,
                           0, 0, 0);
            Allocation.SetOwnerProcessId(ptr, target.Trusted->ProcessId);
        }

        /// <summary>
        /// Transfer any contents that needs to be adjusted from the transferee to the target
        /// endpoint. Currently, this means setting the ownerProcessId of the
        /// transferee to that of the target.
        /// </summary>
        // TODO: change "ref EndpointCore" to "EndpointCore"
        public static void TransferContentOwnership(
           ref EndpointCore transferee,
           ref EndpointCore target)
        {
            Monitoring.Log(Monitoring.Provider.ChannelService,
                           (ushort)ChannelServiceEvent.TransferContentOwnership, 0,
                           (uint)transferee.Trusted->ProcessId,
                           (uint)target.Trusted->ProcessId,
                           (uint)transferee.Trusted->ChannelId,
                           (uint)target.Trusted->ChannelId,
                           (uint)target.Trusted->Peer->ChannelId);

            EndpointTrusted* ept = transferee.Trusted;
            int toProcessId = target.Trusted->ProcessId;
            PrincipalHandle toPrincipalHandle = target.Trusted->PrincipalHandle;
            ept->ProcessId = toProcessId;
            ept->PrincipalHandle = toPrincipalHandle;
            // also transfer the peer allocation
            Allocation.SetOwnerProcessId(ept->directPeerAllocation, toProcessId);
        }

        public int ChannelId
        {
            [NoHeapAllocation]
            get { return Trusted->ChannelId; }
        }
        public int ProcessId
        {
            [NoHeapAllocation]
            get { return Trusted->ProcessId; }
        }
        public int ReceiveCount
        {
            get { return Trusted->ReceiveCount; }
        }
        public int PeerProcessId
        {
            [NoHeapAllocation]
            get { return Trusted->PeerProcessId; }
        }
        public PrincipalHandle OwnerPrincipalHandle
        {
            [NoHeapAllocation]
            get { return Trusted->OwnerPrincipalHandle; }
        }
        public PrincipalHandle PeerPrincipalHandle
        {
            [NoHeapAllocation]
            get { return Trusted->PeerPrincipalHandle; }
        }
        public int PeerReceiveCount
        {
            [NoHeapAllocation]
            get { return Trusted->PeerReceiveCount; }
        }

        public void LinkIntoCollection(AutoResetEventHandle ev) {
            Trusted->LinkIntoCollection(ev);
        }

        public void UnlinkFromCollection(AutoResetEventHandle ev) {
            Trusted->UnlinkFromCollection(ev);
        }

        internal void BeginUpdate(byte* basep, byte* source,
                                  int* tagAddress, int size) {
            Trusted->BeginUpdate(basep, source, tagAddress, size);
        }

        unsafe public void MarshallPointer(byte* basep, void** target, SystemType type)
        {
            if (target == null) return;
            Trusted->MarshallPointer(basep, target, type);
        }
    }
#endif
}

