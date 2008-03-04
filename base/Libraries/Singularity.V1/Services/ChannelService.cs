////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity - Singularity ABI Shim
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ChannelService.csi
//
//  Note:
//

using System;
using System.Runtime.CompilerServices;

using Microsoft.Singularity;
using Microsoft.Singularity.V1.Security;
using Microsoft.Singularity.V1.Types;
using Microsoft.Singularity.V1.Threads;

namespace Microsoft.Singularity.V1.Services
{
    using Allocation = SharedHeapService.Allocation;

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
        /// Contains the security principal that is the current owner of this end of the
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
        public readonly UIntPtr id; // handle id
#endif //PAGING

        /// <summary>
        /// Used to allocate a channel endpoint. The size must be correctly computed by
        /// the trusted caller (currently trusted code NewChannel)
        /// </summary>
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern Allocation* /*EndpointCore* opt(ExHeap)!*/
        Allocate(uint size, SystemType st);

        /// <summary>
        /// Closes this end of the channel and frees associated resources, EXCEPT the block
        /// of memory for this endpoint. It must be released by the caller. Sing# does this
        /// for the programmer.
        /// Returns true for success, false for failure.
        /// </summary>
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool Dispose(ref EndpointCore endpoint);

        /// <summary>
        /// Deallocates this end of the channel. If other end is also
        /// deallocated, the entire channel is deallocated.
        /// </summary>
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(960)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Free(Allocation* /* EndpointCore* opt(ExHeap) */ endpoint);

        /// <summary>
        /// Performs the initialization of the core part of each endpoint and cross links
        /// them to form a channel.
        /// </summary>
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Connect(
            Allocation* /*EndpointCore* opt(ExHeap)!*/ imp,
            Allocation* /*EndpointCore* opt(ExHeap)!*/ exp);

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
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool Closed(ref EndpointCore ep);

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern bool PeerClosed(ref EndpointCore ep);
#endif //PAGING

        /// <summary>
        /// Set this end to closed
        /// </summary>
#if !PAGING
        [NoHeapAllocation]
        public static void Close(ref EndpointCore ep)
        {
            ep.closed = true;
        }
#else
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Close(ref EndpointCore ep);
#endif //PAGING

        /// <summary>
        /// The endpoint to which this endpoint is connected.
        /// </summary>
#if !PAGING
        [NoHeapAllocation]
        public static Allocation* /*EndpointCore* opt(ExHeap) */ GetPeer(ref EndpointCore ep,
                                                                         out bool marshall)
        {
            marshall = false;
            return ep.peer;
        }
#else
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1174)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern Allocation* GetPeer(ref EndpointCore ep,
                                                 out bool marshall);
#endif //PAGING

        /// <summary>
        /// The event to wait for messages on this endpoint. Used by Select.
        /// </summary>
#if !PAGING
        [NoHeapAllocation]
        public static SyncHandle GetWaitHandle(ref EndpointCore ep)
        {
            return ep.messageEvent;
        }
#else
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern SyncHandle GetWaitHandle(ref EndpointCore ep);
#endif //PAGING

        /// <summary>
        /// Notify the owner of this endpoint that a message is ready.
        /// Notifies the set owner if this endpoint is part of a set.
        /// </summary>
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void NotifyPeer(ref EndpointCore ep);


        /// <summary>
        /// Wait for a message to arrive on this endpoint.
        /// </summary>
#if !PAGING
        [NoHeapAllocation]
        public static void Wait(ref EndpointCore ep) {
            SyncHandle.WaitOne(ep.messageEvent);
        }
#else
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Wait(ref EndpointCore ep);
#endif //PAGING

        /// <summary>
        /// Transfer the given Allocation block to the target endpoint
        /// </summary>
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
#if !PAGING
        public static extern void TransferBlockOwnership(Allocation* ptr, ref EndpointCore target);
#else
        // TODO: change "ref EndpointCore" to "EndpointCore"
        public static extern void TransferBlockOwnership(Allocation* ptr, ref EndpointCore target);
#endif //PAGING

        /// <summary>
        /// Transfer any contents that needs to be adjusted from the transferee to the target
        /// endpoint.
        /// </summary>
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(896)]
        [MethodImpl(MethodImplOptions.InternalCall)]
#if !PAGING
        public static extern void TransferContentOwnership(
           ref EndpointCore transferee,
           ref EndpointCore target);
#else
        // TODO: change "ref EndpointCore" to "EndpointCore"
        public static extern void TransferContentOwnership(
           ref EndpointCore transferee,
           ref EndpointCore target);
#endif //PAGING

#if !PAGING
        [NoHeapAllocation]
        public static int GetOwnerProcessID(ref EndpointCore ep)
        {
            return ep.ownerProcessId;
        }
#else
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetOwnerProcessID(ref EndpointCore ep);
#endif //PAGING

#if !PAGING
        [NoHeapAllocation]
        public static int GetChannelID(ref EndpointCore ep)
        {
            return ep.channelId;
        }
#else
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetChannelID(ref EndpointCore ep);
#endif //PAGING

#if !PAGING
        [NoHeapAllocation]
        public static int GetPeerProcessID(ref EndpointCore ep)
        {
            EndpointCore * peer = (EndpointCore*)SharedHeapService.GetData(ep.peer);
            return peer->ownerProcessId;
        }
#else
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetPeerProcessID(ref EndpointCore ep);
#endif //PAGING

#if !PAGING
        [NoHeapAllocation]
        public static PrincipalHandle GetOwnerPrincipalHandle(ref EndpointCore ep)
        {
            return ep.ownerPrincipalHandle;
        }
#else
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern PrincipalHandle GetOwnerPrincipalHandle(ref EndpointCore ep);
#endif //PAGING

#if !PAGING
        [NoHeapAllocation]
        public static PrincipalHandle GetPeerPrincipalHandle(ref EndpointCore ep)
        {
            EndpointCore * peer = (EndpointCore*)SharedHeapService.GetData(ep.peer);
            return peer->ownerPrincipalHandle;
        }
#else
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern PrincipalHandle GetPeerPrincipalHandle(ref EndpointCore ep);
#endif //PAGING

#if !PAGING
        /// <summary>
        /// Instruct the selectable object to signal events on the given AutoResetEvent
        /// rather than its normal event in order to aggregate signalling into a set.
        /// A selectable object need only support being part of a single collection at
        /// any point in time.
        /// </summary>
        public static void LinkIntoCollection(ref EndpointCore ep,
                                              AutoResetEventHandle ev) {
            //          Debug.Assert(this.collectionEvent.id == UIntPtr.Zero);
            ep.collectionEvent = ev;
        }

        /// <summary>
        /// Instruct the selectable object to stop signalling events on the given
        /// AutoResetEvent.
        /// </summary>
        public static void UnlinkFromCollection(ref EndpointCore ep,
                                                AutoResetEventHandle ev) {
            // Debug.Assert(this.collectionEvent.id != UIntPtr.Zero);
            ep.collectionEvent = new AutoResetEventHandle();
        }

#else
        /// <summary>
        /// Instruct the selectable object to signal events on the given AutoResetEvent
        /// rather than its normal event in order to aggregate signalling into a set.
        /// A selectable object need only support being part of a single collection at
        /// any point in time.
        /// </summary>
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void LinkIntoCollection(ref EndpointCore ep,
                                                     AutoResetEventHandle ev);

        /// <summary>
        /// Instruct the selectable object to stop signalling events on the given
        /// AutoResetEvent.
        /// </summary>
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void UnlinkFromCollection(ref EndpointCore ep,
                                                       AutoResetEventHandle ev);

        /// <summary>
        /// Called when sending a message across domains. Instructs the kernel
        /// to prepare an update record to push into the peer when the peer
        /// is running.
        ///
        /// This call starts a sequence of MarshallPointer calls that will
        /// end with a call to NotifyPeer.
        /// </summary>
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        unsafe public static extern void MarshallMessage(ref EndpointCore ep,
                                                         byte* basep, byte* source,
                                                         int* tagAddress, int size);


        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        unsafe public static extern void MarshallPointer(ref EndpointCore ep,
                                                         byte* basep, byte** target, SystemType type);

#endif //PAGING

    }
}
