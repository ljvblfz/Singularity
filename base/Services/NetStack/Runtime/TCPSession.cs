///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

/**
 * Microsoft Research, Cambridge
 * author: Yaron Weinsberg, Richard Black
 */

// #define DEBUG_TCP

using NetStack.Common;
using System;
using System.Threading;
using System.Collections;
using System.Diagnostics;

#if !SINGULARITY
using System.Net;
#endif

using System.Net.IP;
using Drivers.Net;
using NetStack.Protocols;
using NetStack.Contracts;
using Microsoft.Contracts;
using Microsoft.SingSharp;
using Microsoft.SingSharp.Runtime;
using Microsoft.Singularity;
using Microsoft.Singularity.Channels;

namespace NetStack.Runtime
{
    /**
     * This the TCP session object
     */
    public class TcpSession : Session
    {
        // the session's transmit q size (num of packets)
        public const int TxQSize=100;

        // the session's receive q size (num of packets)
        public const int RcvQSize=100;

        // the maximum number of retries to send before giving up
        private const int MaxRetries = 5;

        // the number of seconds to wait for an active connect to succeed
        private const int ConnectTimeout = 15;

        // the number of seconds to wait for a passive connect to succeed
        private const int PassiveConnectTimeout = 5;

        // the number of seconds between probes of a remote host that has
        // closed its receive window
        private const int PersistTimeout = 2;

        // the number of seconds to wait for a graceful shutdown to complete
        private const int PoliteShutdownTimeout = 10;

        // TCP retransmission state
        private RJBlack.Timer retransTimer;
        private const uint InitialRetransInterval = 3000; // 3s in ms, Per RFC 2988

        // 200ms, more aggressive than RFC "SHOULD" of 1s
        private const uint MinimumRetransInterval = 200;

        // Per-session retransmission state
        private uint retransInterval = InitialRetransInterval, srtt = InitialRetransInterval, rttvar = 0; // All in ms

        internal TcpState stateContext;  // our FSM state
        internal TcpState oldState;      // our previous state
        internal TcpError connectError; // the result of the Connect request

        // accepted session list (TcpSessions)
        private ArrayList acceptedSessions;
        private int maxAcceptedSessions;

        // a monitor for the accepted session list
        private object acceptSessionMonitor;

        // the passive session (owner) of this session
        // (applied to passive sessions)
        internal TcpSession passiveSession;

        // the retransmit queue
        private const int RetransmitQSize = 100;
        private ArrayList retransmitQ;

        // Setup / teardown timers
        RJBlack.Timer setupTimer;
        RJBlack.Timer shutdownTimer;

        // Information on the "persist" state (for when the remote host
        // has closed its receive window)
        RJBlack.Timer persistTimer;

        // An event that gets set when initial establishment either
        // succeeds or times out
        internal System.Threading.ManualResetEvent setupCompleteEvent;

        // An event that gets set when we're done shutting down this session
        internal System.Threading.ManualResetEvent closedEvent;

        // is this session is valid for USERS to read/write data
        protected bool isValidForRead, isValidForWrite;

        // Whether or not BindLocalEndPoint() has been called
        bool haveBound = false;

        public bool ValidForRead { get { return isValidForRead;} set {isValidForRead=value;}}
        public bool ValidForWrite { get { return isValidForWrite;} set {isValidForWrite=value;}}

        // TCB
        public struct TCB
        {
            public SNDValues SND;
            public RCVValues RCV;

            // send parameters
            public struct SNDValues
            {
                public uint UNA;     // send unacknowledges
                public uint NXT;     // send seqnum that will be transmitted
                public uint WND;     // send window
                public uint UP;      // send urgent pointer
                public uint WL1;     // segment seq number used for last window update
                public uint WL2;     // segment ack number used for last window update
                public uint ISS;     // initial send sequence number
                public uint NextSeq; // next sequence number to use when packetizing
                                     // (not the same as NXT)
            }

            // receive parameters
            public struct RCVValues
            {
                public uint NXT;     // receive next
                public uint WND;     // receive window
                public uint UP;      // receive urgent pointer
                public uint IRS;     // initial receive sequence number
            }
        }

        // the session's TCB
        internal TCB sessionTCB;

        [NotDelayed]
        public TcpSession(IProtocol! p)
            : base(p,TxQSize,RcvQSize)
        {
            sessionTCB           = new TCB();
            sessionTCB.SND.WND   = TcpFormat.TCP_MSS;
            sessionTCB.RCV.WND   = TcpFormat.TCP_MSS;
            retransmitQ          = new ArrayList(RetransmitQSize);
            acceptedSessions     = new ArrayList();
            maxAcceptedSessions  = 0;                   // Changed by Listen()
            acceptSessionMonitor = new object();

            setupCompleteEvent = new System.Threading.ManualResetEvent(false);
            closedEvent        = new System.Threading.ManualResetEvent(false);
            passiveSession     = null;

            // at first the session is valid (user can interact with it)
            isValidForRead  = true;
            isValidForWrite = true;

            // create and initialize the init state
            this.oldState = null;
            ChangeState(TCPFSM.CLOSED);
        }

        public new void ReInitialize(IProtocol! p)
        {
            base.ReInitialize(p);
            sessionTCB          = new TCB();
            sessionTCB.SND.WND  = TcpFormat.TCP_MSS;
            sessionTCB.RCV.WND  = TcpFormat.TCP_MSS;
            maxAcceptedSessions = 0;
            passiveSession      = null;
            isValidForRead      = true;
            isValidForWrite     = true;

            DrainQueue(outQueue);
            DrainQueue(inQueue);
            retransmitQ.Clear();
            acceptedSessions.Clear();
            setupCompleteEvent.Reset();
            closedEvent.Reset();

            // create and initialize the init state
            this.oldState = null;
            if (!IsClosed) {
                ChangeState(TCPFSM.CLOSED);
            }

            DestroyTimer(ref setupTimer);
            DestroyTimer(ref shutdownTimer);
            DestroyTimer(ref persistTimer);

            retransInterval = InitialRetransInterval;
        }

        public bool IsClosed
        {
            get { return this.stateContext == TCPFSM.CLOSED; }
        }

        private void DestroyTimer(ref RJBlack.Timer t)
        {
            if (t != null) {
                Core.Instance().TheDispatcher.RemoveTimeoutCallback(t);
            }
            t = null;
        }

        [ Conditional("DEBUG_TCP") ]
        private static void DebugPrint(string format, params object [] args)
        {
            Core.Log("TCP: ");
            Core.Log(format, args);
        }

        internal void StartConnectTimer()
        {
            ulong timeout;

            if (passiveSession != null)
            {
                timeout = PassiveConnectTimeout;
            }
            else
            {
                timeout = ConnectTimeout;
            }

            Dispatcher.Callback fun = new Dispatcher.Callback(OnConnectTimeout);
            ulong expiryTime = (ulong)DateTime.UtcNow.Ticks + (timeout * DateTime.TicksPerSecond);
            setupTimer = Core.Instance().TheDispatcher.AddCallback(fun, null, expiryTime);
        }

        internal NetStatus OnConnectTimeout(Dispatcher.CallbackArgs args)
        {
            // We failed to become established in time. Bail out.
            Terminate(null, TcpError.Timeout);
            return NetStatus.Code.PROTOCOL_OK;
        }

        // change the state of this session
        internal void ChangeState(TcpState! newState)
        {
            if (stateContext != null)
            {
                oldState = stateContext;
                stateContext.OnStateLeave(this, newState);
            }

            // If we've become Established, stop the connect timer if there
            // is one ticking
            if (newState == TCPFSM.ESTABLISHED)
            {
                DestroyTimer(ref setupTimer);
            }

            if (newState == TCPFSM.CLOSED)
            {
                // Stop retransmitting
                DestroyTimer(ref retransTimer);
            }

            stateContext = newState;
            newState.OnStateEnter(this);
        }

        // the message is dispatched to the sessions. the sender
        // is the protocol and the context is session specific
        // (i.e., TCP can pass the TCP header to avoid
        // processing it again at the session instance)
        public delegate NetStatus OnPacketReceive(object     sender,
                                                  NetPacket! packet,
                                                  object     context);

        // this is the state's delegate for handling the
        // protocol triggered event
        // the object parameter will be set to IProtocol interface
        internal override NetStatus OnReceive(object     sender,
                                              NetPacket! pkt,
                                              object     ctx)
        {
            DebugPrint("Packet received.");
            if (stateContext != null) {
                // process it in the current state's context
                DebugPrint("Packet received: State->{0}",
                           stateContext.GetStateName());
                return (stateContext.OnPacketReceive(this, pkt, ctx));
            }
            return NetStatus.Code.PROTOCOL_DROP_ERROR;
        }

        private void StartPersistTimer()
        {
            Dispatcher.Callback fun = new Dispatcher.Callback(OnPersistTimeout);
            ulong expiryTime = (ulong)DateTime.UtcNow.Ticks + (PersistTimeout * DateTime.TicksPerSecond);
            persistTimer = Core.Instance().TheDispatcher.AddCallback(fun, null, expiryTime);
        }

        internal void StopPersistTimer()
        {
            DestroyTimer(ref persistTimer);
        }

        internal bool InPersistState()
        {
            return persistTimer != null;
        }

        internal uint FreeRemoteWindowBytes()
        {
            // The portion of the remote window that is available is the most recently
            // advertised window size minus any outstanding unacknowledged data
            return sessionTCB.SND.WND - (sessionTCB.SND.NXT - sessionTCB.SND.UNA);
        }

        // Called when SND.WND is updated
        internal void HandleWindowUpdate()
        {
            uint newWindow = sessionTCB.SND.WND;

            if (InPersistState() && (newWindow > 0)) {
                // The remote receive window just reopened
                StopPersistTimer();
            }

            // The window update may have made it newly possible to transmit
            // queued data.
            if ((FreeRemoteWindowBytes() > 0) &&
                (outQueue.Count > 0)) {
                Core.Instance().SignalOutboundPackets();
            }
        }

        private NetStatus OnPersistTimeout(Dispatcher.CallbackArgs timeoutArg)
        {
            //
            // NOTE This is a hack. A proper TCP stack is supposed to
            // transmit a packet consisting of just one byte when probing the
            // remote host to see if it has reopened its receive window.
            // However, we prepacketize data, so we don't have that option.
            // Instead, we probe using full packets.
            //
            TcpSegment seg = null;

            if (retransmitQ.Count > 0) {
                // Use the oldest unacknowledged packet to probe
                seg = (TcpSegment)retransmitQ[0];

            } else {
                // Nothing in the retransmit queue; probe using the next
                // normal packet. This will transition the packet to the
                // retransmission queue.
                seg = GetNextPacket(true /*ignore window*/ );
            }

            if (seg != null) {
                seg.Mux = BoundMux;
                NetStatus err = Protocol.OnProtocolSend(seg);
                assert err == NetStatus.Code.PROTOCOL_OK;
            }

            if (stateContext != TCPFSM.CLOSED) {
                // rearm
                StartPersistTimer();
            }

            return NetStatus.Code.PROTOCOL_OK;
        }

        private TcpSegment GetNextPacket()
        {
            return GetNextPacket(false);
        }

        private TcpSegment GetNextPacket(bool ignoreReceiverWindow)
        {
            if (outQueue.Count == 0) {
                return null;
            }

            lock (outQueue.SyncRoot) {
                // recheck after lock
                if (outQueue.Count == 0) {
                    return null;
                }

                if (((TcpSegment!)outQueue[0]).retries > 0) {
                    // Special case: the head packet is a retransmission. No special work.
                    return (TcpSegment)base.GetPacket(outQueue, false, 0); // non blocking

                } else {
                    // The head packet is *not* a retransmission. Make sure we
                    // have room to to move it to the retransmission queue.
                    if (retransmitQ.Count < retransmitQ.Capacity)
                    {
                        TcpSegment nextSegment = (TcpSegment)outQueue[0];
                        assert nextSegment != null;
                        uint segSize = nextSegment.GetSegmentLength();

                        if ((!ignoreReceiverWindow) && (segSize > FreeRemoteWindowBytes())) {
                            return null; // Don't overrun the receiver
                        }

                        // Call the base class to dequeue the packet in an orderly way
                        TcpSegment! seg = (TcpSegment!)base.GetPacket(outQueue, false, 0); // non blocking
                        assert seg == nextSegment;

                        if (!seg.isAck) {
                            // save it for RTT adjustments
                            seg.sendTime = (ulong)DateTime.UtcNow.Ticks;
                            retransmitQ.Add(seg);

                            // Make sure the retransmitQ stays sorted
                            if (retransmitQ.Count > 1) {
                                TcpSegment! previousTail = (TcpSegment!)retransmitQ[retransmitQ.Count - 2];
                                assert TCPFSM.TCPSeqGreater(seg.seq, previousTail.seq);
                            }

                            // Kick off the retransmit timer if we are first
                            if (retransTimer == null) {
                                RestartRetransTimer();
                            }

                        } else if (segSize == 0) {
                            segSize = 1; // ACKs take up one segment number
                        }

                        // Advance the NXT counter since we're about to put this
                        // segment on the wire
                        sessionTCB.SND.NXT = seg.seq + segSize;
                        return seg;
                    }
                }
            }

            return null;
        }

        // NB: call *after* removing or adding items to the retransmitQ
        internal void RestartRetransTimer()
        {
            DestroyTimer(ref retransTimer);

            if (retransmitQ.Count > 0) {
                ulong nowTime = (ulong)DateTime.UtcNow.Ticks;
                // TODO: We should use a dynamically-calculated timeout interval
                ulong t = nowTime + (ulong)TimeSpan.FromMilliseconds(retransInterval).Ticks;

                retransTimer = Core.Instance().TheDispatcher.AddCallback(
                    new Dispatcher.Callback(OnRetransmitTimeout), null, t);
            } // else all data has been acknowledged
        }

        internal void FlushRetransmissions()
        {
            DestroyTimer(ref retransTimer);
            retransmitQ.Clear();
        }

        private void UpdateRTT(uint measurement)
        {
            const uint MaxCredibleMeasurement = 10000; // 10s in ms
            uint newInterval;

            if (measurement > MaxCredibleMeasurement) {
                // Garbage
                return;
            }

            if (retransInterval == InitialRetransInterval) {
                // We have never set the session RTT.
                srtt = measurement;
                rttvar = srtt / 2;
                newInterval = measurement * 2;
            } else {
                // Second or subsequent measurement. Per RFC 2988
                uint abs_srtt_meas = srtt > measurement ? srtt - measurement : measurement - srtt;
                rttvar = ((rttvar * 3) / 4) + (abs_srtt_meas / 4);
                srtt = ((7 * srtt) / 8) + (measurement / 8);
                newInterval = srtt + (4 * rttvar);
            }

            this.retransInterval = newInterval < MinimumRetransInterval ? MinimumRetransInterval : newInterval;
        }

        // Process a remote acknowledgement of data up to the given sequence number
        internal void ACKThrough(uint seqNum)
        {
            ulong nowTicks = (ulong)DateTime.UtcNow.Ticks;
            int removed = 0;

            // Pop packets off the retransmitQ through the acked seqnum
            while (retransmitQ.Count > 0) {
                TcpSegment! headSeg = (TcpSegment!)retransmitQ[0];

                if (retransmitQ.Count > 1) {
                    TcpSegment! nextSeg = (TcpSegment!)retransmitQ[1];
                    // Make sure the queue is in order
                    assert TCPFSM.TCPSeqLess(headSeg.seq, nextSeg.seq);
                }

                // If the head segment is fully acknowledged, pop it
                if (TCPFSM.TCPSeqLEQ(headSeg.seq + headSeg.GetSegmentLength(), seqNum)) {
                    retransmitQ.RemoveAt(0);
                    removed++;

                    // Use this ACK for RTT calculations.
                    // Ignore ACKs for retransmitted data.
                    if (headSeg.retries == 0) {
                        UpdateRTT(headSeg.GetRTT(nowTicks));
                    }
                } else {
                    // Out of packets to pop
                    break;
                }
            }

            if (removed > 0) {
                RestartRetransTimer();
            }
            // else this ACK didn't acknowledge any new data

            // INVARIANT: the head of the retransmit queue must contain
            // the first unacked seqnum.
            if (retransmitQ.Count > 0) {
                TcpSegment! headSeg = (TcpSegment!)retransmitQ[0];

                bool hasFirstUnacked =
                    TCPFSM.TCPSeqLEQ(headSeg.seq, sessionTCB.SND.UNA) &&
                    TCPFSM.TCPSeqGEQ(headSeg.seq + headSeg.GetSegmentLength(), sessionTCB.SND.UNA);

                assert hasFirstUnacked;
            }

            // We may have paused transmission so as to not overrun the receiver.
            // Poke the netstack core to be sure we get serviced if we have data
            // to send.
            if ((FreeRemoteWindowBytes() > 0) &&
                (outQueue.Count > 0)) {
                Core.Instance().SignalOutboundPackets();
            }
        }

        // we need to override GetPacket. We transmit the packet
        // and put it in the retransmit queue until we get an ack.
        // (we only do it for data segments including SYN which counts for one)
        // if a timer expired before ack, we retransmit it until we give up.
        override internal NetPacket GetPacket(ArrayList! q, bool toBlock, int timeout)
        {
            // We only concern ourselves with the remote host's receive window in states
            // where we are transmitting
            bool shouldRespectRemoteWindow =
                (stateContext != TCPFSM.CLOSED) &&
                (stateContext != TCPFSM.LISTEN) &&
                (stateContext != TCPFSM.SYN_SENT) &&
                (stateContext != TCPFSM.SYN_RECVD);

            // There needs to be at least one packet-worth of space in the send-window for us
            // to be sure we won't overrun the remote host.
            if (shouldRespectRemoteWindow && (sessionTCB.SND.WND == 0)) {
                // Make sure the persist timer is ticking

                if (!InPersistState()) {
                    StartPersistTimer();
                } // else already in the persist state

            } else {
                StopPersistTimer();
                return GetNextPacket();
            }

            return null;
        }

        private void PriorityEnqueuePacket(ArrayList! queue, NetPacket! packet)
        {
            lock (queue.SyncRoot) {
                // This may increase the capacity of the queue.  We probably want
                // watermark limit for user additions to the queue and not worry about
                // internal additions to the queue.
                queue.Insert(0, packet);
            }

            // Poke the core to service our queue
            Core.Instance().SignalOutboundPackets();
        }

        // Handler for TCP timeouts
        internal NetStatus OnRetransmitTimeout(Dispatcher.CallbackArgs timeoutArg)
        {
            if (!InPersistState()) {
                // Retransmit the oldest unacknowledged packet
                assert retransmitQ.Count > 0;
                TcpSegment! oldest = (TcpSegment!)retransmitQ[0];
                ++oldest.retries;

                if (oldest.retries >= MaxRetries) {
                    // Give up
                    Abort(TcpError.Timeout);
                    return NetStatus.Code.PROTOCOL_OK;
                }

                // INVARIANT: the head of the retransmit queue must contain
                // the first unacked seqnum
                if (retransmitQ.Count > 0) {
                    // TODO make this an assert
                    TcpSegment! headSeg = (TcpSegment!)retransmitQ[0];

                    bool hasFirstUnacked =
                        TCPFSM.TCPSeqLEQ(headSeg.seq, sessionTCB.SND.UNA) &&
                        TCPFSM.TCPSeqGreater(headSeg.seq + headSeg.GetSegmentLength(), sessionTCB.SND.UNA);

                    assert hasFirstUnacked;
                }

                PriorityEnqueuePacket(outQueue, oldest);
            } else {
                // we're in the persist state and retransmissions are suspended.
            }

            // Back off!
            retransInterval = retransInterval * 2;

            RestartRetransTimer();
            return NetStatus.Code.PROTOCOL_OK;
        }

        internal override bool IsSessionValidForUserRead()
        {
            return isValidForRead;
        }

        internal override bool IsSessionValidForUserWrite()
        {
            return isValidForWrite;
        }

        // data can be still available on a non-valid session
        public bool IsDataAvailable()
        {
            return (inQueue.Count>0);
        }

        // Callback type for packetizing data
        private delegate void CopyDataDelegate(byte[]! intoArray, int sourceOffset,
                                               int destOffset, int length);

        // Helper delegate for dealing with GC data
        private class GCDataCopier
        {
            private byte[] gcData;

            public GCDataCopier(byte[] gcData)
            {
                this.gcData = gcData;
            }

            public void CopyData(byte[]! intoArray, int sourceOffset,
                                 int destOffset, int length)
            {
                if (sourceOffset + length > gcData.Length) {
                    throw new Exception("Overrun of GC data helper");
                }

                Array.Copy(gcData, sourceOffset, intoArray,
                           destOffset, length);
            }
        }

        // Helper class for dealing with ExHeap data
        private class ExHeapDataCopier
        {
            VContainer<byte> exHeapData;

            public ExHeapDataCopier([Claims] byte[]! in ExHeap exHeapData)
            {
                this.exHeapData = new VContainer<byte>(exHeapData);
            }

            public void CopyData(byte[]! intoArray, int sourceOffset,
                                 int destOffset, int length)
            {
                if (this.exHeapData == null) {
                    throw new Exception("ExHeapDataCopier used after Destroy()");
                }

                byte[]! in ExHeap exHeapData = this.exHeapData.Acquire();

                try {
                    if (sourceOffset + length > exHeapData.Length) {
                        throw new Exception("Overrun of ExHeap data helper");
                    }

                    Bitter.ToByteArray(exHeapData, sourceOffset, length,
                                       intoArray, destOffset);
                }
                finally {
                    this.exHeapData.Release(exHeapData);
                }
            }

            public void Destroy()
            {
                // Explicitly discard our ExHeap object
                byte[]! in ExHeap data = this.exHeapData.Acquire();
                delete data;
                this.exHeapData = null;
            }
        }

        public int WriteData([Claims] byte[]! in ExHeap data)
        {
            int dataLength = data.Length;
            ExHeapDataCopier helper = new ExHeapDataCopier(data);
            int retval = InternalWrite(new CopyDataDelegate(helper.CopyData), dataLength);

            // Make sure the ExHeap block gets thrown away immediately to
            // reduce pressure on the finalizer thread
            helper.Destroy();

            return retval;
        }

        override public int WriteData(byte[]! data)
        {
            GCDataCopier helper = new GCDataCopier(data);
            return InternalWrite(new CopyDataDelegate(helper.CopyData), data.Length);
        }

        // here we create the segments from the data
        // The user is blocked until we have more room.
        // we return -1 if we can't write (session is not established)
        // TBC: according to the spec, when TCP is about to send it, if there is not
        // enough space at the peer receive buffer it can split it
        // to several smaller segments.
        private int InternalWrite(CopyDataDelegate! dataCopier, int dataSize)
        {
            if (!ValidForWrite)
            {
                return -1;
            }

            // This is the number of full packets to send
            uint mssCount = (uint)(dataSize / TcpFormat.TCP_MSS);

            // This is the size of the last (non-full) packet.
            uint mssResidue = (uint)(dataSize % TcpFormat.TCP_MSS);

            int readIndex = 0;
            uint segSequence = sessionTCB.SND.NextSeq;

            const int baseFrameSize = EthernetFormat.Size + IPFormat.Size + TcpFormat.Size;

            while (mssCount!=0)
            {
                // create a TCP segment without options

                // handle the data first
                byte[] pktData = new byte[baseFrameSize + TcpFormat.TCP_MSS];
                dataCopier(pktData, readIndex, baseFrameSize, TcpFormat.TCP_MSS);

                TcpFormat.WriteTcpSegment(pktData,this.LocalPort,
                                          this.RemotePort, sessionTCB.RCV.NXT,
                                          segSequence, TcpFormat.TCP_MSS,
                                          this.LocalAddress,
                                          this.RemoteAddress,
                                          TcpFormat.TCP_MSS,
                                          true,false,false,false,false);

                TcpSegment seg = new TcpSegment(pktData, this,
                                                segSequence, false);
                // the next segment sequence
                segSequence += TcpFormat.TCP_MSS;
                readIndex += TcpFormat.TCP_MSS;
                base.PutPacket(outQueue, seg, true);
                mssCount--;
            }

            if (mssResidue!=0)
            {
                byte[] pktData = new byte[baseFrameSize + mssResidue];
                dataCopier(pktData, readIndex, baseFrameSize, (int)mssResidue);

                TcpFormat.WriteTcpSegment(pktData,
                                          this.LocalPort,
                                          this.RemotePort,
                                          sessionTCB.RCV.NXT,
                                          segSequence, TcpFormat.TCP_MSS,
                                          this.LocalAddress,
                                          this.RemoteAddress,
                                          (ushort)mssResidue,
                                          true,false,false,false,false);

                TcpSegment seg = new TcpSegment(pktData, this,
                                                segSequence, false);
                segSequence += mssResidue;
                base.PutPacket(outQueue,seg,true);
            }

            sessionTCB.SND.NextSeq = segSequence;
            // since we always send it all...
            return dataSize;
        }

        public bool BindLocalEndPoint(IPv4 address, ushort port)
        {
            haveBound = true;
            SetRemoteEndPoint(IPv4.Broadcast, 0);
            return SetLocalEndPoint(address, port);
        }

        // the method is used to make a session active (i.e., active open)
        // TBC: manage local ports automatically
        // we currently more restrictive regarding user
        // interaction (can't change passive to active etc)
        public bool Connect(IPv4 dstIP, ushort dstPort, out TcpError error)
        {

            DebugPrint("Connect: {0:x8}/{1}", dstIP, dstPort);

            if (stateContext!=TCPFSM.CLOSED) {
                DebugPrint("Connect: Failed FSM Closed", dstIP, dstPort);
                error = TcpError.AlreadyConnected;
                return false;
            }

            // init the session's parameters
            SetRemoteEndPoint(dstIP, dstPort);

            // Set the local endpoint to "don't care" if the user
            // hasn't called BindLocalEndPoint() previously
            if (!haveBound)
            {
                SetLocalEndPoint(IPv4.Any, 0);
            }

            sessionTCB.RCV = new TcpSession.TCB.RCVValues();
            sessionTCB.SND = new TcpSession.TCB.SNDValues();

            DrainQueue(outQueue);
            DrainQueue(inQueue);
            retransmitQ.Clear();

            // change this session state to SYNSENT.
            // a SYN message will be sent to the destination
            ChangeState(TCPFSM.SYN_SENT);
            // provide a default error
            this.connectError = TcpError.Unknown;


            // block the user until the session is ready
            setupCompleteEvent.WaitOne();

            DebugPrint("Connect: SetupCompleteEvent signalled");

            if (stateContext != TCPFSM.ESTABLISHED)
            {
                // The connect failed.
                error = this.connectError;
                DebugPrint("Connect: failed {0}", error);
                return false;
            }
            else
            {
                // The connection is up and running properly.
                error = TcpError.Unknown;
                DebugPrint("Connect: success");
                return true;
            }
        }

        private NetStatus OnShutdownTimedout(Dispatcher.CallbackArgs args)
        {
            // No more Mr. Nice Guy
            Abort(TcpError.Timeout);
            return NetStatus.Code.PROTOCOL_OK;
        }

        // close the session
        override public bool Close()
        {
            // Signal that we're done sending; this will start the polite
            // shutdown process.
            DoneSending();

            // Start a timer to make sure we don't wait for the shutdown
            // forever. TODO: we should use a value passed in by the
            // caller for the timeout rather than hard-coding it.
            Dispatcher.Callback fun = new Dispatcher.Callback(OnShutdownTimedout);
            ulong expiryTime = (ulong)DateTime.UtcNow.Ticks +
                (PoliteShutdownTimeout * DateTime.TicksPerSecond);
            shutdownTimer = Core.Instance().TheDispatcher.AddCallback(fun, null, expiryTime);

            // Wait while we complete the shutdown, drain the outbound
            // queue, etc.
            closedEvent.WaitOne();

            // Quash the timer in case it's still ticking
            if (Core.Instance().TheDispatcher.RemoveTimeoutCallback(shutdownTimer)) {
                Tracing.Log(Tracing.Debug, "Tcp quash timer successful.");
            }
            else {
                Tracing.Log(Tracing.Debug, "Tcp quash timer already expired.");
            }
            shutdownTimer = null;

            // After a Close() completes, pending data isn't available anymore!
            DrainQueue(inQueue);

            return true;
        }

        // hard-shutdown
        override public bool Abort()
        {
            return Abort(TcpError.Unknown);
        }

        public bool Abort(TcpError error)
        {
            // Abort our connection with a RST segment
            Terminate(TCPFSM.CreateResetSegment(this, true), error);
            return true;
        }

        private void Terminate(TcpSegment finalPacket, TcpError error)
        {
            StopPersistTimer();

            if (stateContext == TCPFSM.CLOSED)
            {
                // Signal anyone waiting, for good measure.
                setupCompleteEvent.Set();
            }
            else
            {
                // This will set the setup-complete event as a side effect
                TCPFSM.HandleTerminateSession(this, finalPacket, error);
            }
        }

        // passively open a session
        public bool Listen(int backlog)
        {
            if (stateContext != TCPFSM.CLOSED)
                return false;

            // User must have previously bound
            if (!haveBound)
            {
                return false;
            }

            maxAcceptedSessions = backlog;
            ChangeState(TCPFSM.LISTEN);
            return true;
        }

        // start accepting new clients
        // will block until a new client connection is established
        public TcpSession Accept()
        {
            if (stateContext != TCPFSM.LISTEN)
                return null;

            TcpSession tcpSession=null;

            // block the user until a session is available
            lock (acceptSessionMonitor)
            {
                while (acceptedSessions.Count==0)
                    Monitor.Wait(acceptSessionMonitor);
                tcpSession = (TcpSession)acceptedSessions[0];
                acceptedSessions.RemoveAt(0);
            }

            return tcpSession;
        }

        // Returns false if our queue is full
        internal bool AddAcceptedSession(TcpSession newSession)
        {
            lock (acceptSessionMonitor)
            {
                if (AcceptQueueIsFull())
                {
                    return false;
                }
                else
                {
                    acceptedSessions.Add(newSession);
                    Monitor.PulseAll(acceptSessionMonitor);
                    return true;
                }
            }
        }

        // Indicate whether there are queued sessions waiting to be Accept()ed
        public int GetNumWaitingListenSessions()
        {
            lock (acceptSessionMonitor)
            {
                return acceptedSessions.Count;
            }
        }

        // Determine whether the accept queue is full or not
        internal bool AcceptQueueIsFull()
        {
            return GetNumWaitingListenSessions() >= maxAcceptedSessions;
        }

        // Indicate that we're done sending
        public void DoneSending()
        {
            if (!isValidForWrite)
            {
                // Nothing to do
                return;
            }

            isValidForWrite = false;

            if (stateContext == TCPFSM.ESTABLISHED)
            {
                // Transition to FIN_WAIT1 since we are the first
                // side to close
                TCPFSM.SendFin(this, true); // can block
                ChangeState(TCPFSM.FIN_WAIT1);
            }
            else if (stateContext == TCPFSM.CLOSE_WAIT)
            {
                // The other side closed first; transition to
                // LAST_ACK instead
                TCPFSM.SendFin(this, true); // blocks
                ChangeState(TCPFSM.LAST_ACK);
            }
            else
            {
                // We're in some transitory setup or teardown
                // state; just abort the connection.
                Abort(TcpError.Closed);
            }
        }

        // Indicate that we're done receiving
        public void DoneReceiving()
        {
            ValidForRead = false;
        }
    }

    // a TCP segment
    public class TcpSegment : NetPacket
    {
        // segment identifier
        internal uint seq;          // the segment sequence number
        internal uint retries;      // number of retransmit retries
        internal bool isAck;        // is it an ack segment (no retrans for ack!)
        internal ulong sendTime;    // used to dynamically adjust the RTT

        // create a TcpSegment, add room for lower level protocols
        public TcpSegment(byte[]! buffer) : base(buffer)
        {
            seq=0;
            retries=0;
            isAck=false;
            sendTime=0;
        }

        // the isAck indicate that this is an ack segment
        // (we never ack an ack segments without data)
        [NotDelayed]
        public TcpSegment(byte[]! buffer, TcpSession! owner, uint seqNum, bool isAck) : base(buffer)
        {
            seq=seqNum;
            retries=0;
            this.isAck=isAck;
            sendTime=0;
            this.SessionContext = owner;
        }

        public TcpSession owner
        {
            // owner was a field in TcpSegment, but it mirrors field
            // in NetPacket and one we now use for unblocking after
            // the ARP response comes back.
            get { return this.SessionContext as TcpSession; }
            set { this.SessionContext = value; }
        }

        // return the TCP data segment size
        public uint GetSegmentLength()
        {
            return ((uint)(base.Length - EthernetFormat.Size - IPFormat.Size - TcpFormat.Size));
        }

        public uint GetRTT(ulong receiveTime)
        {
            ulong deltaTime = (receiveTime>sendTime ? receiveTime-sendTime : 0 );
            return (uint)(TimeSpan.FromTicks((long)deltaTime)).Milliseconds;
        }
    }
}

