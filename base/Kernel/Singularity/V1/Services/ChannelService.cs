////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity - Singularity ABI Implementation
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   EndpointCore.cs
//
//  Note:
//

using System;
using System.Threading;
using System.Runtime.CompilerServices;

using Microsoft.Singularity;
using Microsoft.Singularity.Channels;
using Microsoft.Singularity.Security;

using Microsoft.Singularity.Memory;
using Microsoft.Singularity.V1.Security;
using Microsoft.Singularity.V1.Threads;
using Microsoft.Singularity.V1.Types;

namespace Microsoft.Singularity.V1.Services
{
    using Allocation = SharedHeapService.Allocation;
    using EndpointCoreImplementation = Microsoft.Singularity.Channels.EndpointCore;

    [CLSCompliant(false)]
    public enum ChannelServiceEvent : ushort
    {
        TransferBlockOwnership = 1,
        TransferContentOwnership = 2,
    }

    [CLSCompliant(false)]
    unsafe public struct EndpointCore
    {
#if !PAGING
        /// <summary>
        /// Flag indicating that the endpoint is closed.
        /// This gets set either by an explicit close, or when
        /// the kernel determines that the endpoint is not reachable.
        /// NOTE: A channel only goes away entirely, once both ends are closed!
        /// </summary>
        private volatile bool closed;

        /// <summary>
        /// The endpoint to which this endpoint is connected.
        /// </summary>
        private Allocation* /*EndpointCore* opt(ExHeap)*/ peer;

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
        /// Contains the principal handle of the process currently owning this end of the
        /// channel.
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
#else
        internal EndpointTrusted* id;
#endif //PAGING

        /// <summary>
        /// Used to allocate a channel endpoint. The size must be correctly computed by
        /// the trusted caller (currently trusted code NewChannel)
        /// </summary>
        [ExternalEntryPoint]
        public static Allocation* /*EndpointCore* opt(ExHeap)!*/
        Allocate(uint size, SystemType st)
        {
            Allocation* ep = (Allocation*) SharedHeap.CurrentProcessSharedHeap.Allocate(
                size, st.id, 0, SharedHeap.CurrentProcessSharedHeap.EndpointOwnerId);
            if (ep == null) {
                throw new ApplicationException("SharedHeap.Allocate returned null");
            }
            return ep;
        }

        /// <summary>
        /// Closes this end of the channel and frees associated resources, EXCEPT the block
        /// of memory for this endpoint. It must be released by the caller. Sing# does this
        /// for the programmer.
        /// Returns true for success, false for failure.
        /// </summary>
        [ExternalEntryPoint]
        public static bool Dispose(ref EndpointCore endpoint)
        {
#if !PAGING
            fixed (EndpointCore* ep = &endpoint) {
                EndpointCoreImplementation* epimp = (EndpointCoreImplementation*)ep;
                return epimp->Dispose();
            }
#else
            try {
                EndpointCoreImplementation ep = new EndpointCoreImplementation(endpoint.id);
                EndpointCoreImplementation.Dispose(ref ep);
                return true;
            } catch(ApplicationException) {
                return false;
            }
#endif //PAGING
        }

        /// <summary>
        /// Deallocates this end of the channel. If other end is also
        /// deallocated, the entire channel is deallocated.
        /// </summary>
        [ExternalEntryPoint]
        public static void Free(Allocation* /* EndpointCore* opt(ExHeap) */ endpoint)
        {
            EndpointCoreImplementation.Free((SharedHeap.Allocation*)endpoint);
        }

        /// <summary>
        /// Performs the initialization of the core part of each endpoint and cross links
        /// them to form a channel.
        /// </summary>
        [ExternalEntryPoint]
        public static void Connect(
            Allocation* /*EndpointCore* opt(ExHeap)!*/ imp,
            Allocation* /*EndpointCore* opt(ExHeap)!*/ exp)
        {
            EndpointCoreImplementation.Connect((SharedHeap.Allocation*)imp,
                                               (SharedHeap.Allocation*)exp);
        }

        /// <summary>
        /// Indicates if this endpoint is closed
        /// </summary>
#if !PAGING
        [NoHeapAllocation]
        public static bool Closed(ref EndpointCore ep)
        {
            return ep.closed;
        }

        [NoHeapAllocation]
        public static bool PeerClosed(ref EndpointCore ep)
        {
            EndpointCore * peer = (EndpointCore*)SharedHeapService.GetData(ep.peer);
            return peer->closed;
        }
#else
        [ExternalEntryPoint]
        public static bool Closed(ref EndpointCore ep) {
            return new EndpointCoreImplementation(ep.id).Closed();
        }
        [ExternalEntryPoint]
        public static bool PeerClosed(ref EndpointCore ep) {
            return new EndpointCoreImplementation(ep.id).PeerClosed();
        }
#endif //PAGING

        /// <summary>
        /// Set this end to closed
        /// </summary>
#if !PAGING
        public static void Close(ref EndpointCore ep) {
            ep.closed = true;
        }
#else
        [ExternalEntryPoint]
        public static void Close(ref EndpointCore ep) {
            new EndpointCoreImplementation(ep.id).Close();
        }
#endif //PAGING

        /// <summary>
        /// The endpoint to which this endpoint is connected.
        /// </summary>
#if !PAGING
        public static Allocation* /*EndpointCore* opt(ExHeap) */ GetPeer(ref EndpointCore ep,
                                                                         out bool marshall)
        {
            marshall = false;
            return ep.peer;
        }
#else
        [ExternalEntryPoint]
        public static Allocation* /*EndpointCore* opt(ExHeap) */ GetPeer(ref EndpointCore ep,
                                                                         out bool marshall)
        {
            return (Allocation*)new EndpointCoreImplementation(ep.id).Peer(out marshall);
        }
#endif //PAGING

        /// <summary>
        /// The event to wait for messages on this endpoint. Used by Select.
        /// </summary>
#if !PAGING
        public static SyncHandle GetWaitHandle(ref EndpointCore ep)
        {
            return ep.messageEvent;
        }
#else
        [ExternalEntryPoint]
        public static SyncHandle GetWaitHandle(ref EndpointCore ep) {
            return new EndpointCoreImplementation(ep.id).GetWaitHandle();
        }
#endif //PAGING

        /// <summary>
        /// Notify the owner of this endpoint that a message is ready.
        /// Notifies the set owner if this endpoint is part of a set.
        /// </summary>
        [ExternalEntryPoint]
        public static void NotifyPeer(ref EndpointCore endpoint) {
#if !PAGING
            fixed (EndpointCore* ep = &endpoint) {
                EndpointCoreImplementation* epimp = (EndpointCoreImplementation*)ep;
                epimp->NotifyPeer();
            }
#else
            new EndpointCoreImplementation(endpoint.id).NotifyPeer();
#endif //PAGING
        }

        /// <summary>
        /// Wait for a message to arrive on this endpoint.
        /// </summary>
#if !PAGING
        public static void Wait(ref EndpointCore ep) {
          SyncHandle.WaitOne(ep.messageEvent);
        }
#else
        [ExternalEntryPoint]
        public static void Wait(ref EndpointCore ep) {
          SyncHandle.WaitOne(GetWaitHandle(ref ep));
        }
#endif //PAGING

        /// <summary>
        /// Transfer the given Allocation block to the target endpoint
        /// </summary>
#if !PAGING
        [ExternalEntryPoint]
        public static void TransferBlockOwnership(Allocation* ptr, ref EndpointCore target)
        {
            Monitoring.Log(Monitoring.Provider.ChannelService,
                           (ushort)ChannelServiceEvent.TransferBlockOwnership, 0,
                           (uint)target.channelId, (uint)target.ownerProcessId,
                           0, 0, 0);
            SharedHeapService.SetOwnerProcessId(ptr, target.ownerProcessId);
#if CHANNEL_COUNT
            EndpointCoreImplementation.IncreaseBytesSentCount((long)SharedHeapService.GetSize(ptr));
#endif
        }
#else
        [ExternalEntryPoint]
        // TODO: change "ref EndpointCore" to "EndpointCore"
        public static void TransferBlockOwnership(Allocation* ptr, ref EndpointCore target)
        {
            EndpointCoreImplementation ep = new EndpointCoreImplementation(target.id);
            EndpointCoreImplementation.TransferBlockOwnership((SharedHeap.Allocation*)ptr, ref ep);
        }
#endif //PAGING

        /// <summary>
        /// Transfer any contents that needs to be adjusted from the transferee to the target
        /// endpoint. Currently, this means setting the ownerProcessId of the
        /// transferee to that of the target.
        /// </summary>
#if !PAGING
        [ExternalEntryPoint]
        public static void TransferContentOwnership(
           ref EndpointCore transferee,
           ref EndpointCore target)
        {
            Monitoring.Log(Monitoring.Provider.ChannelService,
                           (ushort)ChannelServiceEvent.TransferContentOwnership, 0,
                           (uint)transferee.ownerProcessId,
                           (uint)target.ownerProcessId,
                           (uint)transferee.channelId,
                           (uint)target.channelId,
                           // Not allowed to look at target.peer. We don't own that!
                           // (uint)((EndpointCore*)(SharedHeapService.GetData(target.peer)))->channelId);
                           (uint)target.channelId);

            transferee.ownerProcessId = target.ownerProcessId;
            transferee.ownerPrincipalHandle = target.ownerPrincipalHandle;
            // also fix up ownership of peer allocation
            Allocation* transfereePeerAllocation = transferee.peer;
            TransferBlockOwnership(transfereePeerAllocation, ref target);
        }
#else
        [ExternalEntryPoint]
        // TODO: change "ref EndpointCore" to "EndpointCore"
        public static void TransferContentOwnership(
           ref EndpointCore transferee,
           ref EndpointCore target)
        {
            EndpointCoreImplementation ep1 = new EndpointCoreImplementation(transferee.id);
            EndpointCoreImplementation ep2 = new EndpointCoreImplementation(target.id);
            EndpointCoreImplementation.TransferContentOwnership(ref ep1, ref ep2);
        }
#endif //PAGING

#if !PAGING
        [NoHeapAllocation]
        public static int GetChannelID(ref EndpointCore ep)
        {
            return ep.channelId;
        }
#else
        [ExternalEntryPoint]
        [NoHeapAllocation]
        public static int GetChannelID(ref EndpointCore ep)
        {
            return new EndpointCoreImplementation(ep.id).ChannelId;
        }
#endif //PAGING

#if !PAGING
        [NoHeapAllocation]
        public static int GetOwnerProcessID(ref EndpointCore ep)
        {
            return ep.ownerProcessId;
        }
#else
        [ExternalEntryPoint]
        [NoHeapAllocation]
        public static int GetOwnerProcessID(ref EndpointCore ep)
        {
            return new EndpointCoreImplementation(ep.id).ProcessId;
        }
#endif //PAGING

#if !PAGING
        [NoHeapAllocation]
        public static int GetPeerProcessID(ref EndpointCore ep)
        {
            EndpointCore * peer = (EndpointCore*)SharedHeapService.GetData(ep.peer);
            return peer->ownerProcessId;
        }
#else
        [ExternalEntryPoint]
        [NoHeapAllocation]
        public static int GetPeerProcessID(ref EndpointCore ep)
        {
            return new EndpointCoreImplementation(ep.id).PeerProcessId;
        }
#endif //PAGING

        // It is unfortunate that PrincipalHandle and Principal are not
        // compatible types.  They are only distinct because ABI types are disfavored
        // for code that doesn't deal directly with the ABI.

#if !PAGING
        [NoHeapAllocation]
        public static PrincipalHandle GetPeerPrincipalHandle(ref EndpointCore ep)
        {
            EndpointCore * peer = (EndpointCore*)SharedHeapService.GetData(ep.peer);
            return peer->ownerPrincipalHandle;
        }
#else
        [ExternalEntryPoint]
        [NoHeapAllocation]
        public static PrincipalHandle GetPeerPrincipalHandle(ref EndpointCore ep)
        {
            return new EndpointCoreImplementation(ep.id).PeerPrincipalHandle;
        }
#endif


#if !PAGING
        [NoHeapAllocation]
        public static PrincipalHandle GetOwnerPrincipalHandle(ref EndpointCore ep)
        {
            return ep.ownerPrincipalHandle;
        }
#else
        [ExternalEntryPoint]
        [NoHeapAllocation]
        public static PrincipalHandle GetOwnerPrincipalHandle(ref EndpointCore ep)
        {
            return new EndpointCoreImplementation(ep.id).OwnerPrincipalHandle;
        }
#endif //PAGING

        /// <summary>
        /// Instruct the selectable object to signal events on the given AutoResetEvent
        /// rather than its normal event in order to aggregate signalling into a set.
        /// A selectable object need only support being part of a single collection at
        /// any point in time.
        /// </summary>
        [ExternalEntryPoint]
        public static void LinkIntoCollection(ref EndpointCore ep, AutoResetEventHandle ev) {
#if !PAGING
            //          Debug.Assert(this.collectionEvent.id == UIntPtr.Zero);
            ep.collectionEvent = ev;
#else
            new EndpointCoreImplementation(ep.id).LinkIntoCollection(ev);
#endif
            Tracing.Log(Tracing.Debug, "Ev:{0:x}", ev.id);
        }

        /// <summary>
        /// Instruct the selectable object to stop signalling events on the given
        /// AutoResetEvent.
        /// </summary>
        [ExternalEntryPoint]
        public static void UnlinkFromCollection(ref EndpointCore ep, AutoResetEventHandle ev) {
#if !PAGING
            // Debug.Assert(this.collectionEvent.id != UIntPtr.Zero);
            ep.collectionEvent = new AutoResetEventHandle();
#else
            new EndpointCoreImplementation(ep.id).UnlinkFromCollection(ev);
#endif
        }

#if PAGING
        [ExternalEntryPoint]
        unsafe public static void MarshallMessage(ref EndpointCore ep,
                                                  byte* basep, byte* source,
                                                  int* tagAddress, int size)
        {
            Tracing.Log(Tracing.Debug,
                        "source offset:{0:x} tagLoc offset:{1:x} size {2:x}",
                        (uint)source-(uint)basep, (uint)tagAddress-(uint)basep,
                        (uint)size);

            new EndpointCoreImplementation(ep.id).BeginUpdate(basep, source, tagAddress, size);
        }

        [ExternalEntryPoint]
        unsafe public static void MarshallPointer(ref EndpointCore ep,
                                                  byte* basep, byte** target, SystemType type)
        {

            Tracing.Log(Tracing.Debug,
                        "source offset:{0:x} type:{1}",
                        (uint)target-(uint)basep, type.id);

            new EndpointCoreImplementation(ep.id).MarshallPointer(basep, (void**)target, type);
        }
#endif

    }
}
