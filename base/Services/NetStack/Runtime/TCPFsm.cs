///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

// #define DEBUG_TCP

using NetStack.Common;
using Microsoft.Contracts;
using System;
using System.Threading;
using System.Collections;
using System.Diagnostics;
using NetStack.Contracts;
using TcpError = NetStack.Contracts.TcpError;


#if !SINGULARITY
using System.Net;
#endif

using System.Net.IP;
using Drivers.Net;

namespace NetStack.Runtime
{
    using Protocols;

    internal class TcpState
    {
        // ctor
        internal TcpState()
        {
        }

        // make a state transition
        protected void ChangeState(TcpSession! owner, TcpState newState)
        {
            owner.ChangeState((!)newState);
        }

        // enter the new state
        virtual internal void OnStateEnter(TcpSession! owner)
        {
        }

        // a new packet(=event) received, should handle it
        // according to the current state.
        virtual internal NetStatus OnPacketReceive(TcpSession! owner,
                                                   NetPacket! pkt,
                                                   object     ctx)
        {
            return NetStatus.Code.PROTOCOL_OK;
        }

        // leave the state to the new one
        virtual internal void OnStateLeave(TcpSession! owner, TcpState newState)
        {
        }

        // handle timeout events
        // args is a session argument which is session specific
        virtual internal NetStatus OnTimeout(Dispatcher.CallbackArgs args)
        {
            return NetStatus.Code.PROTOCOL_OK;
        }

        // get the state name (debug)
        virtual internal string GetStateName()
        {
            return "UNDEFINED";
        }

        [ Conditional("DEBUG_TCP") ]
        internal static void DebugPrint(string format, params object [] args)
        {
            Core.Log("TCP: ");
            Core.Log(format, args);
        }
    }

    /**
     * This the TCP Finite State Machine Implementation
     * NOTICE: This is just a quick implementation...
     * should handle FIN,RST, window management, slow start, double acks...
     */
    internal class TCPFSM
    {

        // Definition of the TCP states
        internal static TcpState! CLOSED { get {return TCPStateClosed.Instance();}}

        internal static TcpState! LISTEN { get {return TCPStateListen.Instance();}}
        internal static TcpState! SYN_RECVD { get {return TCPStateSynRcv.Instance();}}
        internal static TcpState! SYN_SENT  { get {return TCPStateSynSent.Instance();}}
        internal static TcpState! ESTABLISHED  { get {return TCPStateEstab.Instance();}}
        internal static TcpState! CLOSE_WAIT  { get {return TCPCloseWait.Instance();}}
        internal static TcpState! LAST_ACK  { get {return TCPLastAck.Instance();}}

        internal static TcpState! FIN_WAIT1 { get {return TCPFINWait1.Instance();}}
        internal static TcpState! FIN_WAIT2 { get {return TCPFINWait2.Instance();}}
        internal static TcpState! CLOSING { get {return TCPClosing.Instance();}}

        //----------------------------------------------------------------------------

        // Compare two TCP sequence numbers: -1 means A < B, 0 means A == B and
        // 1 means A > B
        [Pure]
        internal static int TCPSeqCmp(uint seqA, uint seqB)
        {
            // Exploit integer underflow to compare correctly even in the case
            // of sequence number wraparound. This assumes the two numbers
            // are always in the same half of the numberspace.
            uint diff = unchecked(seqA - seqB);
            int signedDiff = unchecked((int)diff);

            if (signedDiff < 0) {
                return -1; // A < B
            } else if (signedDiff > 0) {
                return 1; // A > B
            } else {
                return 0; // A == B
            }
        }

        [Pure]
        internal static bool TCPSeqLEQ(uint seqA, uint seqB)
        {
            int cmp = TCPSeqCmp(seqA, seqB);
            return (cmp == 0) || (cmp == -1);
        }

        [Pure]
        internal static bool TCPSeqLess(uint seqA, uint seqB)
        {
            int cmp = TCPSeqCmp(seqA, seqB);
            return (cmp == -1);
        }

        [Pure]
        internal static bool TCPSeqGEQ(uint seqA, uint seqB)
        {
            int cmp = TCPSeqCmp(seqA, seqB);
            return (cmp == 0) || (cmp == 1);
        }

        [Pure]
        internal static bool TCPSeqGreater(uint seqA, uint seqB)
        {
            int cmp = TCPSeqCmp(seqA, seqB);
            return (cmp == 1);
        }

        //----------------------------------------------------------------------------
        internal sealed class TCPStateClosed : TcpState
        {
            static TCPStateClosed! instance = new TCPStateClosed();
            static TCPStateClosed() {}

            internal static TCPStateClosed! Instance()
            {
                return instance;
            }

            // get the state name (debug)
            override internal string GetStateName()
            {
                return "CLOSED";
            }

            override internal void OnStateEnter(TcpSession! owner)
            {
                DebugPrint("TCP::{0}, OnStateEnter\n",GetStateName());
            }

            // leave the state
            override internal void OnStateLeave(TcpSession! owner, TcpState newState)
            {
                assert newState != null;
                DebugPrint("State Transit: {0}->{1}\n", GetStateName(),
                           newState.GetStateName());
                if (newState==LISTEN)
                {
                    // passive open
                    DebugPrint("---> waiting for connections...\n");
                }
                else if (newState==SYN_SENT)
                {
                    // active open
                    DebugPrint("---> sending SYN...\n");
                    if (!TCPFSM.SendSYN(owner,TcpFormat.TCP_MSS))
                    {
                        // This should never happen; our outbound
                        // packet queue should never be empty
                        // while we're setting up a session
                        Debug.Assert(false);
                    }
                }
                else
                {
                    Core.Panic("TCP state machine error!!!");
                }

            }

            override internal NetStatus OnTimeout(Dispatcher.CallbackArgs args)
            {

                return NetStatus.Code.PROTOCOL_OK;
            }

            override internal NetStatus OnPacketReceive(TcpSession! owner, NetPacket! pkt,object ctx)
            {
                ///assert ctx != null;
                //TcpFormat.TcpHeader tcpHeader = (TcpFormat.TcpHeader)ctx;
                //TcpSession s = (TcpSession)owner; // not used
                return NetStatus.Code.PROTOCOL_DROP_ERROR;
            }
        }


        //----------------------------------------------------------------------------
        // a passive connection listen state
        internal sealed class TCPStateListen : TcpState
        {
            static TCPStateListen! instance = new TCPStateListen();
            static TCPStateListen() {}

            internal static TCPStateListen! Instance()
            {
                return instance;
            }

            // get the state name (debug)
            override internal string GetStateName()
            {
                return "LISTEN";
            }

            override internal void OnStateEnter(TcpSession! owner)
            {
                DebugPrint("TCP::{0}, OnStateEnter\n",GetStateName());
            }

            override internal NetStatus OnPacketReceive(TcpSession! owner, NetPacket! pkt,object ctx)
            {
                assert ctx != null;
                TcpFormat.TcpHeader tcpHeader = (TcpFormat.TcpHeader)ctx;
                TcpSession s = (TcpSession)owner;

                if (TcpFormat.IsReset(ref tcpHeader))
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;  // we ignore it

                if (TcpFormat.IsAck(ref tcpHeader))
                {
                    SendReset(s, false);
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                if (TcpFormat.IsSync(ref tcpHeader))
                {
                    // we received a syn!
                    // 1. create a new TCP session and initialize it
                    // (only if we have room...TBC: should use listen param for bound)
                    // 2. send syn-ack in the context of the new session

                    TcpSession client =
                        (TcpSession!)s.Protocol.CreateSession();
                    client.passiveSession     = s;  // this is our "owner"
                    client.SetLocalEndPoint(s.LocalAddress, s.LocalPort);
                    client.sessionTCB.RCV.NXT = tcpHeader.seq+1;
                    client.sessionTCB.RCV.IRS = tcpHeader.seq;
                    client.sessionTCB.SND.ISS = (uint)DateTime.UtcNow.Ticks;
                    client.sessionTCB.SND.NXT = client.sessionTCB.SND.ISS+1;
                    client.sessionTCB.SND.NextSeq = client.sessionTCB.SND.NXT;
                    client.sessionTCB.SND.UNA = client.sessionTCB.SND.ISS;
                    client.sessionTCB.SND.WND = tcpHeader.window;
                    client.sessionTCB.RCV.WND = TcpFormat.TCP_MSS;

                    // we have access to the IP header from the packet overlapped
                    // context (TCPModule put it there)
                    IPFormat.IPHeader ipHeader = pkt.OverlapContext as IPFormat.IPHeader;
                    assert ipHeader != null;

                    client.SetRemoteEndPoint(ipHeader.Source,
                                             tcpHeader.sourcePort);

                    // Save ourselves work if we should reject the session right away
                    if (s.AcceptQueueIsFull())
                    {
                        TcpSegment reset = CreateResetSegment(client, true);
                        s.Protocol.OnProtocolSend(reset);
                        return NetStatus.Code.PROTOCOL_DROP_ERROR;
                    }

                    // send syn-ack
                    if (!SendSYNAck(client))
                    {
                        // This should never happen; our outbound packet
                        // queues should never be full while we're setting
                        // up a session.
                        Debug.Assert(false);
                    }

                    // the new session starts at syn-rcv
                    client.stateContext=null;
                    client.oldState=null;
                    client.ChangeState(SYN_RECVD);

                    // Start the timer ticking on establishing the new session
                    client.StartConnectTimer();

                    // we are left at the SAME state!!!
                    return NetStatus.Code.PROTOCOL_OK;
                }
                else
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
            }
        }
        //----------------------------------------------------------------------------
        internal sealed class TCPStateSynRcv : TcpState
        {
            static TCPStateSynRcv! instance = new TCPStateSynRcv();
            static TCPStateSynRcv() {}

            internal static TCPStateSynRcv! Instance()
            {
                return instance;
            }

            // get the state name (debug)
            override internal string GetStateName()
            {
                return "SYNRECVD";
            }

            override internal void OnStateEnter(TcpSession! owner)
            {
                DebugPrint("TCP::{0}, OnStateEnter\n", GetStateName());
            }

            override internal NetStatus OnPacketReceive(TcpSession! owner, NetPacket! pkt,object ctx)
            {
                assert ctx != null;
                // all TCP states get the tcpHeader context
                TcpFormat.TcpHeader tcpHeader = (TcpFormat.TcpHeader)ctx;
                TcpSession s = (TcpSession)owner;
                uint segmentSize = (uint)pkt.Available;

                bool accept=IsSegmentAcceptable(s,ref tcpHeader,segmentSize);

                if (!accept)
                {
                    if (!TcpFormat.IsReset(ref tcpHeader))
                    {
                        // send an ACK; don't care about whether this
                        // works or not since we're discarding the packet
                        // anyhow.
                        TCPFSM.SendAck(owner);
                    }
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                // we accept the segment
                Debug.Assert(tcpHeader.seq==s.sessionTCB.RCV.NXT);

                // if we get a reset
                if (TcpFormat.IsReset(ref tcpHeader))
                {
                    if (s.oldState==LISTEN)
                    {
                        // if we came from the listen state (passive open)
                        // the reset will return us to the Listen state
                        s.FlushRetransmissions();
                        ChangeState(s,LISTEN);
                        return NetStatus.Code.PROTOCOL_DROP_ERROR;
                    }
                    else
                    {
                        // we came from SYN_SENT (active open)
                        // the connection was refused, close it!
                        HandleTerminateSession(s,null, TcpError.Refused);
                        return NetStatus.Code.PROTOCOL_DROP_ERROR;
                    }
                }


                if (TcpFormat.IsSync(ref tcpHeader))
                {
                    // reset
                    HandleTerminateSession(s,CreateResetSegment(s, false), TcpError.ProtocolViolation);
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                if (TcpFormat.IsAck(ref tcpHeader))
                {
                    if (TCPSeqLEQ(s.sessionTCB.SND.UNA, tcpHeader.ackSeq) &&
                        TCPSeqGEQ(s.sessionTCB.SND.NXT, tcpHeader.ackSeq))
                    {
                        // enter established state

                        // we get an Ack for our syn-ack
                        // remove the SYN-ACK from the retransmit Q
                        TCPFSM.HandleTCPAck(s,ref tcpHeader);
                        AttemptToEnterESTABLISHED(s, ref tcpHeader);
                        return NetStatus.Code.PROTOCOL_OK;
                    }
                    else
                    {
                        // reset
                        HandleTerminateSession(s,CreateResetSegment(s, false), TcpError.Reset);
                        return NetStatus.Code.PROTOCOL_DROP_ERROR;
                    }
                }

                if (TcpFormat.IsFIN(ref tcpHeader))
                {
                    DebugPrint("TCP::TCPStateSynRcv: Received FIN!!!\n");

                    // RCV.NXT should reflect the data that we processed out of
                    // this packet, but not the FIN. Advance over the FIN.
                    s.sessionTCB.RCV.NXT += 1;

                    // ack the FIN. Don't worry if this doesn't actually work;
                    // the remote side will think the ACK got lost and retransmit,
                    // which we will hopefully be able to successfully ACK later.
                    SendAck(s);
                    ChangeState(s,CLOSE_WAIT);
                }

                return NetStatus.Code.PROTOCOL_DROP_ERROR;
            }
        }
        //----------------------------------------------------------------------------
        internal sealed class TCPStateSynSent : TcpState
        {
            static TCPStateSynSent! instance = new TCPStateSynSent();
            static TCPStateSynSent() {}

            internal static TCPStateSynSent! Instance()
            {
                return instance;
            }

            // get the state name (debug)
            override internal string GetStateName()
            {
                return "SYNSENT";
            }

            override internal void OnStateEnter(TcpSession! owner)
            {
                DebugPrint("TCP::{0}, OnStateEnter\n",GetStateName());
            }

            // we sent a syn we wait for syn-ack
            override internal NetStatus OnPacketReceive(TcpSession! owner,NetPacket! pkt,object ctx)
            {
                assert ctx != null;
                // all TCP states get the tcpHeader context
                TcpFormat.TcpHeader tcpHeader = (TcpFormat.TcpHeader)ctx;
                TcpSession s = (TcpSession)owner;
                bool ackAcceptable=false;

                if (TcpFormat.IsAck(ref tcpHeader))
                {
                    if (TCPSeqLEQ(tcpHeader.ackSeq, s.sessionTCB.SND.ISS) ||
                        TCPSeqGreater(tcpHeader.ackSeq, s.sessionTCB.SND.NXT))
                    {
                        if (TcpFormat.IsReset(ref tcpHeader))
                            return NetStatus.Code.PROTOCOL_DROP_ERROR;

                        SendReset(s, false);
                        return NetStatus.Code.PROTOCOL_DROP_ERROR;
                    }

                    if (TCPSeqLEQ(s.sessionTCB.SND.UNA, tcpHeader.ackSeq) &&
                        TCPSeqLEQ(tcpHeader.ackSeq, s.sessionTCB.SND.NXT))
                    {
                        ackAcceptable=true;
                    }
                }

                if (TcpFormat.IsReset(ref tcpHeader))
                {
                    if (ackAcceptable)
                    {
                        // connection was reset, drop the segment
                        // and close the connection.
                        HandleTerminateSession(s,null, TcpError.Reset);
                        return NetStatus.Code.PROTOCOL_DROP_ERROR;
                    }
                    else
                        return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }


                // check syn
                // ack is is ok or there is no ack and no RST
                if (TcpFormat.IsSync(ref tcpHeader))
                {
                    if (ackAcceptable)
                    {
                        DebugPrint("TCP::SYNSENT, Received SYN-ACK\n");

                        // grab the ack parameters and
                        // complete the session's data
                        s.sessionTCB.RCV.NXT = tcpHeader.seq+1;
                        s.sessionTCB.RCV.IRS = tcpHeader.seq;
                        s.sessionTCB.SND.UNA = tcpHeader.ackSeq;
                        s.sessionTCB.SND.WND = tcpHeader.window;
                        s.sessionTCB.RCV.WND = TcpFormat.TCP_MSS;

                        // great! now remove the SYN from
                        // the retransmit Q (so we don't
                        // retransmit it again)
                        TCPFSM.HandleTCPAck(s,ref tcpHeader);

                        if (s.sessionTCB.SND.UNA>s.sessionTCB.SND.ISS)
                        {
                            // our syn has been acked.
                            TCPFSM.SendAck(owner);

                            // change the state to established!
                            AttemptToEnterESTABLISHED(owner, ref tcpHeader);
                            return NetStatus.Code.PROTOCOL_OK;
                        }
                    }
                    else
                    {
                        // received a new SYN (overlap connection)
                        // (see SYN_RECVD for more details)

                        // Create a new object to track the new session
                        TcpSession client = (TcpSession!)s.Protocol.CreateSession();
                        client.passiveSession     = s; // this is our "owner"
                        client.SetLocalEndPoint(s.LocalAddress,
                                                s.LocalPort);
                        client.sessionTCB.RCV.NXT = tcpHeader.seq+1;
                        client.sessionTCB.RCV.IRS = tcpHeader.seq;
                        client.sessionTCB.SND.ISS = (uint)DateTime.UtcNow.Ticks;
                        client.sessionTCB.SND.NXT = client.sessionTCB.SND.ISS+1;
                        client.sessionTCB.SND.NextSeq = client.sessionTCB.SND.NXT;
                        client.sessionTCB.SND.UNA = client.sessionTCB.SND.ISS;
                        client.sessionTCB.SND.WND = tcpHeader.window;
                        client.sessionTCB.RCV.WND = TcpFormat.TCP_MSS;

                        // we have access to the IP header from the packet overlapped
                        // context (TCPModule put it there)
                        IPFormat.IPHeader ipHeader = pkt.OverlapContext as IPFormat.IPHeader;
                        assert ipHeader!=null;
                        client.SetRemoteEndPoint(ipHeader.Source,
                                                 tcpHeader.sourcePort);

                        // Save ourselves work if we should reject the session right away
                        if (s.AcceptQueueIsFull())
                        {
                            TcpSegment reset = CreateResetSegment(client, true);
                            s.Protocol.OnProtocolSend(reset);
                            return NetStatus.Code.PROTOCOL_DROP_ERROR;
                        }

                        // send syn-ack
                        if (!SendSYNAck(client))
                        {
                            // This should never happen; our outbound packet
                            // queues should never be empty when we're setting
                            // up a session
                            Debug.Assert(false);
                        }

                        // the new session starts at syn-rcv
                        client.stateContext=null;
                        client.oldState=null;
                        client.ChangeState(SYN_RECVD);

                        // start the establishment countdown
                        client.StartConnectTimer();

                        return NetStatus.Code.PROTOCOL_OK;
                    }
                }
                return NetStatus.Code.PROTOCOL_DROP_ERROR;
            }
        } /* TCPStateSynSent */

        //----------------------------------------------------------------------------
        // This is the working mode state
        internal sealed class TCPStateEstab : TcpState
        {
            static TCPStateEstab! instance = new TCPStateEstab();
            static TCPStateEstab() {}

            internal static TCPStateEstab! Instance()
            {
                return instance;
            }

            // get the state name (debug)
            override internal string GetStateName()
            {
                return "ESTABLISHED";
            }

            override internal void OnStateEnter(TcpSession! owner)
            {
#if DEBUG_TCP
                Core.Log("TCP::{0}, OnStateEnter\n",GetStateName());
#endif
                // Wake up anyone waiting for the connection to complete
                ((TcpSession)owner).setupCompleteEvent.Set();
            }

            // leave the state
            override internal void OnStateLeave(TcpSession! owner, TcpState newState)
            {
#if DEBUG_TCP
                assert newState != null;
                Core.Log("State Transit: {0}->{1}\n", GetStateName(),
                         newState.GetStateName());
#endif
            }

            override internal NetStatus OnPacketReceive(TcpSession! owner, NetPacket! pkt,object ctx)
            {
                assert ctx != null;
                TcpFormat.TcpHeader tcpHeader = (TcpFormat.TcpHeader)ctx;
                TcpSession s = (TcpSession)owner;
                uint segmentSize = (uint)pkt.Available;  // TCPModule already shrinked it for us
                NetStatus res=NetStatus.Code.PROTOCOL_DROP_ERROR;

                bool accept=IsSegmentAcceptable(s,ref tcpHeader,segmentSize);

                if (!accept)
                {
                    if (!TcpFormat.IsReset(ref tcpHeader))
                    {
                        // send an ACK and return
                        TCPFSM.SendAck(owner);
                    }
                    return res;
                }

                if (TcpFormat.IsReset(ref tcpHeader))
                {
                    // for simplicity, just close the connection.
                    HandleTerminateSession(s,null, TcpError.Reset);
                    return res;
                }

                // check syn
                if (TcpFormat.IsSync(ref tcpHeader))
                {
                    // send a reset
                    HandleTerminateSession(s,CreateResetSegment(s, false), TcpError.Reset);
                    return res;
                }

                res = HandleTCPData(ref tcpHeader, pkt, s);

                // our peer wants to end the relationship...
                if (TcpFormat.IsFIN(ref tcpHeader))
                {
#if DEBUG_TCP
                    Core.Log("TCP::ESTABLISHED: Received FIN!!!\n");
#endif
                    // RCV.NXT should reflect any data in this packet, but not the FIN.
                    // Advance over the FIN.
                    s.sessionTCB.RCV.NXT += 1;

                    // ack the FIN
                    SendAck(s);
                    ChangeState(s,CLOSE_WAIT);
                }

                return res;
            }
        }
        //----------------------------------------------------------------------------
        // CLOSE_WAIT is entered when we receive (and ACK) a FIN from the
        // remote side. There is no further data to receive, but we can
        // continue sending if we like.
        internal sealed class TCPCloseWait : TcpState
        {
            static TCPCloseWait! instance = new TCPCloseWait();
            static TCPCloseWait() {}

            internal static TCPCloseWait! Instance()
            {
                return instance;
            }

            // get the state name (debug)
            override internal string GetStateName()
            {
                return "CLOSE_WAIT";
            }

            override internal void OnStateEnter(TcpSession! owner)
            {
#if DEBUG_TCP
                Core.Log("TCP::{0}, OnStateEnter\n",GetStateName());
#endif
                // Signal that there is nothing more to read on this
                // connection
                owner.ValidForRead = false;
            }

            // leave the state
            override internal void OnStateLeave(TcpSession! owner, TcpState newState)
            {
#if DEBUG_TCP
                assert newState != null;
                Core.Log("State Transit: {0}->{1}\n", GetStateName(),
                         newState.GetStateName());
#endif
            }

            override internal NetStatus OnPacketReceive(TcpSession! owner, NetPacket! pkt,object ctx)
            {
                assert ctx != null;
                TcpFormat.TcpHeader tcpHeader = (TcpFormat.TcpHeader)ctx;
                TcpSession s = (TcpSession)owner;
                uint segmentSize = (uint)pkt.Available;

                if (!IsSegmentAcceptable(s,ref tcpHeader,segmentSize))
                {
                    if (!TcpFormat.IsReset(ref tcpHeader))
                    {
                        // send an ACK and return
                        TCPFSM.SendAck(owner);
                    }

                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                if (TcpFormat.IsReset(ref tcpHeader) ||
                    TcpFormat.IsSync(ref tcpHeader) ||
                    (!TcpFormat.IsAck(ref tcpHeader)))
                {
                    // Abort
                    HandleTerminateSession(s,null, TcpError.Reset);
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                TCPFSM.HandleTCPAck(s,ref tcpHeader);
                return NetStatus.Code.PROTOCOL_OK;
            }
        }
        //----------------------------------------------------------------------------
        // LAST_ACK is entered when we close our side of the duplex
        // connection and the remote site had sent us its FIN
        // previously. We send out our own FIN and just hang around
        // to make sure it was received properly before shutting down.
        //
        // CALLER SHOULD ARRANGE TO TRANSMIT THE FIN PACKET BEFORE
        // ENTERING THIS STATE!
        internal sealed class TCPLastAck : TcpState
        {
            static TCPLastAck! instance = new TCPLastAck();
            static TCPLastAck() {}

            internal static TCPLastAck! Instance()
            {
                return instance;
            }

            // get the state name (debug)
            override internal string GetStateName()
            {
                return "LAST_ACK";
            }

            override internal void OnStateEnter(TcpSession! owner)
            {
#if DEBUG_TCP
                Core.Log("TCP::{0}, OnStateEnter\n",GetStateName());
#endif

                // Flag that writing is now disallowed on this session
                ((TcpSession)owner).ValidForWrite = false;
            }

            // leave the state
            override internal void OnStateLeave(TcpSession! owner, TcpState newState)
            {
#if DEBUG_TCP
                assert newState != null;
                Core.Log("State Transit: {0}->{1}\n",GetStateName(),
                         newState.GetStateName());
#endif
            }

            override internal NetStatus OnPacketReceive(TcpSession! owner,NetPacket! pkt,object ctx)
            {
                assert ctx != null;
                TcpFormat.TcpHeader tcpHeader = (TcpFormat.TcpHeader)ctx;
                TcpSession s = (TcpSession)owner;
                uint segmentSize = (uint)pkt.Available;  // TCPModule already shrinked it for us

                // RST is illegal at this point
                if (TcpFormat.IsReset(ref tcpHeader))
                {
                    // for simplicity, just close the connection.
                    HandleTerminateSession(s,null, TcpError.Reset);
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                // SYNC causes us to issue a RST and abort
                if (TcpFormat.IsSync(ref tcpHeader))
                {
                    // send a reset
                    HandleTerminateSession(s,CreateResetSegment(s, false), TcpError.Reset);
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                if (!IsSegmentAcceptable(s,ref tcpHeader,segmentSize))
                {
                    // Just drop it
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                // Do ACK housekeeping
                if (TCPSeqLess(s.sessionTCB.SND.UNA, tcpHeader.ackSeq) &&
                    TCPSeqLEQ(tcpHeader.ackSeq, s.sessionTCB.SND.NXT))
                {
                    // This appears to be ACKing something we sent.
                    s.sessionTCB.SND.UNA = tcpHeader.ackSeq;

                    // remove the packet(s) from the retransmit queue
                    TCPFSM.HandleTCPAck(s,ref tcpHeader);
                }

                // Note the weirdness here; because we prepacketize
                // data, we don't want to compare to SND.NXT
                if (TCPSeqGEQ(s.sessionTCB.SND.UNA, s.sessionTCB.SND.NextSeq))
                {
                    // The remote site has acknowledged everything
                    // we sent. We're done.
                    HandleTerminateSession(s, null, TcpError.Reset);
                }

                return NetStatus.Code.PROTOCOL_DROP_ERROR;
            }
        }
        //----------------------------------------------------------------------------
        // FIN_WAIT1 is entered when we close our side of the duplex
        // connection. We don't send any more data, but we continue to
        // receive data from the remote side.
        //
        // CALLER SHOULD ARRANGE TO SEND THE FIN PACKET BEFORE ENTERING
        // THIS STATE!
        internal sealed class TCPFINWait1 : TcpState
        {
            static TCPFINWait1! instance = new TCPFINWait1();
            static TCPFINWait1() {}

            internal static TCPFINWait1! Instance()
            {
                return instance;
            }

            // get the state name (debug)
            override internal string GetStateName()
            {
                return "FIN_WAIT1";
            }

            override internal void OnStateEnter(TcpSession! owner)
            {
#if DEBUG_TCP
                Core.Log("TCP::{0}, OnStateEnter\n",GetStateName());
#endif

                // Flag that writing is now disallowed on this session
                ((TcpSession)owner).ValidForWrite = false;
            }

            // leave the state
            override internal void OnStateLeave(TcpSession! owner, TcpState newState)
            {
#if DEBUG_TCP
                assert newState != null;
                Core.Log("State Transit: {0}->{1}\n",GetStateName(),
                         newState.GetStateName());
#endif
            }

            override internal NetStatus OnPacketReceive(TcpSession! owner, NetPacket! pkt,object ctx)
            {
                assert ctx != null;
                TcpFormat.TcpHeader tcpHeader = (TcpFormat.TcpHeader)ctx;
                TcpSession s = (TcpSession)owner;
                uint segmentSize = (uint)pkt.Available;  // TCPModule already shrinked it for us

                // RST is illegal at this point
                if (TcpFormat.IsReset(ref tcpHeader))
                {
                    // for simplicity, just close the connection.
                    HandleTerminateSession(s,null, TcpError.ProtocolViolation);
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                // SYNC causes us to issue a RST and abort
                if (TcpFormat.IsSync(ref tcpHeader))
                {
                    // send a reset
                    HandleTerminateSession(s,CreateResetSegment(s, false), TcpError.ProtocolViolation);
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                if (!IsSegmentAcceptable(s,ref tcpHeader,segmentSize))
                {
                    if (!TcpFormat.IsReset(ref tcpHeader))
                    {
                        // send an ACK and return
                        TCPFSM.SendAck(owner);
                    }

                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                // Chew on the payload...
                NetStatus retval = HandleTCPData(ref tcpHeader, pkt, s);

                if (TCPSeqLEQ(tcpHeader.seq, s.sessionTCB.RCV.NXT) &&
                    (TcpFormat.IsFIN(ref tcpHeader)))
                {
                    // This is the remote side's FIN, and we have
                    // received all data preceding it, too!
                    // advance RCV.NXT over the FIN sequence
                    s.sessionTCB.RCV.NXT += 1;

                    // note the weirdness here; because we
                    // prepacketize data, we don't want to test
                    // against SND.NXT
                    if (TCPSeqLess(s.sessionTCB.SND.UNA, s.sessionTCB.SND.NextSeq))
                    {
                        // ...but the remote side hasn't received
                        // everything we have said, yet.

                        // ack the FIN
                        SendAck(s);
                        ChangeState(s, CLOSING);
                    }
                    else
                    {
                        // The remote side has heard everything we
                        // have to say. We're done.
                        // ACK the FIN and shut down
                        TcpSegment ackSeg = CreateAckSegment(s);
                        HandleTerminateSession(s, ackSeg, TcpError.Closed);
                    }
                }
                else
                {
                    // Note the weirdness here; because we
                    // prepacketize data, we do not want to test
                    // against SND.NXT, but rather SND.NextSeq.
                    if (TCPSeqGEQ(s.sessionTCB.SND.UNA, s.sessionTCB.SND.NextSeq))
                    {
                        // The remote side has ACKed everything we have
                        // said, but they haven't FINed themselves.
                        ChangeState(s, FIN_WAIT2);
                    }
                }

                return retval;
            }
        }
        //----------------------------------------------------------------------------
        // FIN_WAIT2 is entered when the other side has received our FIN, but has
        // more data to send. We wait for them to signal FIN as well.
        internal sealed class TCPFINWait2 : TcpState
        {
            static TCPFINWait2! instance = new TCPFINWait2();
            static TCPFINWait2() {}

            internal static TCPFINWait2! Instance()
            {
                return instance;
            }

            // get the state name (debug)
            override internal string GetStateName()
            {
                return "FIN_WAIT2";
            }

            override internal void OnStateEnter(TcpSession! owner)
            {
#if DEBUG_TCP
                Core.Log("TCP::{0}, OnStateEnter\n",GetStateName());
#endif
            }

            // leave the state
            override internal void OnStateLeave(TcpSession! owner, TcpState newState)
            {
#if DEBUG_TCP
                assert newState != null;
                Core.Log("State Transit: {0}->{1}\n",GetStateName(),
                         newState.GetStateName());
#endif
            }

            override internal NetStatus OnPacketReceive(TcpSession! owner, NetPacket! pkt,object ctx)
            {
                assert ctx != null;
                TcpFormat.TcpHeader tcpHeader = (TcpFormat.TcpHeader)ctx;
                TcpSession s = (TcpSession)owner;
                uint segmentSize = (uint)pkt.Available;  // TCPModule already shrinked it for us

                // RST is illegal at this point
                if (TcpFormat.IsReset(ref tcpHeader))
                {
                    // for simplicity, just close the connection.
                    HandleTerminateSession(s,null, TcpError.ProtocolViolation);
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                // SYNC causes us to issue a RST and abort
                if (TcpFormat.IsSync(ref tcpHeader))
                {
                    // send a reset
                    HandleTerminateSession(s,CreateResetSegment(s, false), TcpError.ProtocolViolation);
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                if (!IsSegmentAcceptable(s,ref tcpHeader,segmentSize))
                {
                    if (!TcpFormat.IsReset(ref tcpHeader))
                    {
                        // send an ACK and return
                        TCPFSM.SendAck(owner);
                    }

                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                // Chew on the payload...
                NetStatus retval = HandleTCPData(ref tcpHeader, pkt, s);

                // Only check for FIN if we have previously received
                // all data.
                if (TCPSeqLEQ(tcpHeader.seq, s.sessionTCB.RCV.NXT) &&
                    (TcpFormat.IsFIN(ref tcpHeader)))
                {
#if DEBUG_TCP
                    Core.Log("FIN_WAIT2: Received FIN!!!\n");
#endif
                    // RCV.NXT should reflect the data in this packet, but not the
                    // FIN. Advance over the FIN...
                    s.sessionTCB.RCV.NXT += 1;

                    // ack the FIN and shut down
                    TcpSegment ackSeg = CreateAckSegment(s);
                    HandleTerminateSession(s, ackSeg, TcpError.Closed);
                }

                return retval;
            }
        }
        //----------------------------------------------------------------------------
        // CLOSING is entered when both sides have sent a FIN, but we're not sure
        // the other side has received all our data yet. We hang around processing
        // ACKs until we're sure the other side has heard everything we have to say.
        internal sealed class TCPClosing : TcpState
        {
            static TCPClosing! instance = new TCPClosing();
            static TCPClosing() {}

            internal static TCPClosing! Instance()
            {
                return instance;
            }

            // get the state name (debug)
            override internal string GetStateName()
            {
                return "CLOSING";
            }

            override internal void OnStateEnter(TcpSession! owner)
            {
#if DEBUG_TCP
                Core.Log("TCP::{0}, OnStateEnter\n",GetStateName());
#endif
                // Signal that there's nothing more to read on this session
                owner.ValidForRead = false;
            }

            // leave the state
            override internal void OnStateLeave(TcpSession! owner, TcpState newState)
            {
#if DEBUG_TCP
                assert newState != null;
                Core.Log("State Transit: {0}->{1}\n",GetStateName(),
                         newState.GetStateName());
#endif
            }

            override internal NetStatus OnPacketReceive(TcpSession! owner, NetPacket! pkt, object ctx)
            {
                assert ctx != null;
                TcpFormat.TcpHeader tcpHeader = (TcpFormat.TcpHeader)ctx;
                TcpSession s = (TcpSession)owner;
                uint segmentSize = (uint)pkt.Available;  // TCPModule already shrinked it for us

                // RST is illegal at this point
                if (TcpFormat.IsReset(ref tcpHeader))
                {
                    // for simplicity, just close the connection.
                    HandleTerminateSession(s,null, TcpError.ProtocolViolation);
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                // SYNC causes us to issue a RST and abort
                if (TcpFormat.IsSync(ref tcpHeader))
                {
                    // send a reset
                    HandleTerminateSession(s,CreateResetSegment(s, false), TcpError.ProtocolViolation);
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                if (!IsSegmentAcceptable(s,ref tcpHeader,segmentSize))
                {
                    // Just drop it
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }

                // Do ACK housekeeping
                if (TCPSeqLess(s.sessionTCB.SND.UNA, tcpHeader.ackSeq) &&
                    TCPSeqLEQ(tcpHeader.ackSeq, s.sessionTCB.SND.NXT))
                {
                    // This appears to be ACKing something we sent.
                    s.sessionTCB.SND.UNA = tcpHeader.ackSeq;

                    // remove the packet(s) from the retransmit queue
                    TCPFSM.HandleTCPAck(s,ref tcpHeader);
                }

                // Note the weirdness here; because we
                // prepacketize data, we don't want to compare against
                // SND.NXT
                if (TCPSeqGEQ(s.sessionTCB.SND.UNA, s.sessionTCB.SND.NextSeq))
                {
                    // The remote side has now heard everything we said.
                    HandleTerminateSession(s, null, TcpError.Closed);
                }

                return NetStatus.Code.PROTOCOL_DROP_ERROR;
            }
        }
        //----------------------------------------------------------------------------
        // Create an ACK for data we have received to date
        internal static TcpSegment! CreateAckSegment(TcpSession! s)
        {
            byte[] ackBuffer = new byte[EthernetFormat.Size+IPFormat.Size+TcpFormat.Size];

            TcpFormat.WriteTcpSegment(
                ackBuffer,s.LocalPort,s.RemotePort,s.sessionTCB.RCV.NXT,
                s.sessionTCB.SND.NextSeq,TcpFormat.TCP_MSS,s.LocalAddress,
                s.RemoteAddress,0,true,false,false,false,false);

            return new TcpSegment(ackBuffer,s,s.sessionTCB.SND.NextSeq,true);
        }

        // sends an ack
        internal static bool SendAck(TcpSession! owner)
        {
            TcpSession s = (TcpSession)owner;
            TcpSegment ackSeg = CreateAckSegment(s);

            // put it on this session outgoing queue
            return s.PutPacket(s.outQueue,ackSeg,false);
        }

        // Sends a FIN
        internal static bool SendFin(TcpSession! s, bool canBlock)
        {
            byte[] finBuffer = new byte[EthernetFormat.Size+IPFormat.Size+TcpFormat.Size];

            TcpFormat.WriteTcpSegment(
                finBuffer,s.LocalPort,s.RemotePort,s.sessionTCB.RCV.NXT,
                s.sessionTCB.SND.NextSeq,TcpFormat.TCP_MSS,s.LocalAddress,s.RemoteAddress,0,
                true,false,false,true,false);

            // ok, we have a ready segment.
            TcpSegment syn = new TcpSegment(finBuffer,s,s.sessionTCB.SND.NextSeq,true);

            bool retVal = s.PutPacket(s.outQueue,syn, canBlock);

            // Only advance the segment counter if we successfully queued the
            // outbound packet
            if (retVal)
            {
                s.sessionTCB.SND.NextSeq++;
            }

            return retVal;
        }

        // sends a syn ack packet
        internal static bool SendSYNAck(TcpSession! owner)
        {
            TcpSession s =  (TcpSession)owner;
            byte[] synackBuffer = new byte[EthernetFormat.Size+IPFormat.Size+TcpFormat.Size];

            TcpFormat.WriteTcpSegment(synackBuffer,s.LocalPort,s.RemotePort,s.sessionTCB.RCV.NXT,
                                      s.sessionTCB.SND.ISS,(ushort)s.sessionTCB.SND.WND,s.LocalAddress,
                                      s.RemoteAddress,0,true,true,false,false,false);

            // ok, we have a ready segment.
            // (SYN is regular segment)
            TcpSegment syn = new TcpSegment(synackBuffer, s, s.sessionTCB.SND.ISS, true);

            // put it on this session outgoing queue
            return s.PutPacket(s.outQueue,syn,false);
        }

        // sends a syn packet, with MSS options
        internal static bool SendSYN(TcpSession! owner,ushort MSS)
        {
            TcpSession s =  (TcpSession)owner;
            byte[] synBuffer = new byte[EthernetFormat.Size+IPFormat.Size+TcpFormat.Size+4];

            // setup the session
            s.sessionTCB.SND.ISS=(uint)DateTime.UtcNow.Ticks;
            s.sessionTCB.SND.UNA=s.sessionTCB.SND.ISS;
            s.sessionTCB.SND.NXT=s.sessionTCB.SND.ISS+1; // next packet sequence
            s.sessionTCB.SND.NextSeq = s.sessionTCB.SND.NXT;
            s.sessionTCB.SND.WND=TcpFormat.TCP_MSS;

            // we must first write the data...
            byte[] options=new byte[] {2,4, ((byte)(MSS>>8)),(byte)MSS};
            Array.Copy(options,0,synBuffer,EthernetFormat.Size+IPFormat.Size+TcpFormat.Size,4);

            // now create the segment+checksum
            TcpFormat.WriteTcpSegment(
                synBuffer,s.LocalPort,s.RemotePort,0,
                s.sessionTCB.SND.ISS,MSS,s.LocalAddress,s.RemoteAddress,4,
                false,true,false,false,true);

            // ok, we have a ready segment.
            // (SYN is regular segment)
            TcpSegment syn = new TcpSegment(synBuffer,s,s.sessionTCB.SND.ISS,false);

            // put it on this session outgoing queue
            return s.PutPacket(s.outQueue,syn,false);
        }

        // send a TCP reset
        internal static bool SendReset(TcpSession! owner, bool isAck)
        {
            // we won't retransmit it (it is like an Ack)
            TcpSegment syn = CreateResetSegment(owner, true);

            // put it on this session outgoing queue
            return owner.PutPacket(owner.outQueue,syn,false);
        }

        // send a TCP reset
        internal static TcpSegment! CreateResetSegment(TcpSession! owner, bool isAck)
        {
            TcpSession s =  (TcpSession)owner;
            byte[] rstBuffer = new byte[EthernetFormat.Size+IPFormat.Size+TcpFormat.Size];

            // note use SND.NXT as the sequence number here instead of nextSeq,
            // otherwise the sequence number may be far ahead (if there's lots of queued
            // data) and the receiving host may ignore it as outside its window.
            TcpFormat.WriteTcpSegment(
                rstBuffer,s.LocalPort,s.RemotePort,s.sessionTCB.RCV.NXT,
                s.sessionTCB.SND.NXT,TcpFormat.TCP_MSS,s.LocalAddress,
                s.RemoteAddress,0,isAck,false,true,false,false);

            // we won't retransmit it
            TcpSegment syn = new TcpSegment(rstBuffer,s,s.sessionTCB.SND.NXT,true);
            return syn;
        }

        // Handle received TCP data
        // Returns the appropriate NetStatus.Code
        internal static NetStatus HandleTCPData(ref TcpFormat.TcpHeader tcpHeader,
                                                NetPacket! pkt, TcpSession! s)
        {
            uint segmentSize = (uint)pkt.Available;  // TCPModule already shrinked it for us

            // TBC: We assume the segment start with
            // the RCV.NXT !!! (otherwise we can buffer them)
            if (TCPSeqGreater(tcpHeader.seq, s.sessionTCB.RCV.NXT))
            {
                // we missed one or few, send ack again
                TCPFSM.SendAck(s);
                return NetStatus.Code.PROTOCOL_DROP_ERROR;
            }

            if (!TcpFormat.IsAck(ref tcpHeader))
            {
                return NetStatus.Code.PROTOCOL_DROP_ERROR;
            }

            // First, deal with the window advertised in this packet.
            s.sessionTCB.SND.WND = tcpHeader.window;
            s.HandleWindowUpdate();

            // ack is set and it is relevant (ack something we sent)
            if (TCPSeqLess(s.sessionTCB.SND.UNA, tcpHeader.ackSeq) &&
                TCPSeqLEQ(tcpHeader.ackSeq, s.sessionTCB.SND.NXT))
            {
                s.sessionTCB.SND.UNA = tcpHeader.ackSeq;

                // remove the packet(s) from the retransmit queue
                TCPFSM.HandleTCPAck(s,ref tcpHeader);
            }
            else if (s.sessionTCB.SND.UNA>tcpHeader.ackSeq)
            {
                // a duplicate ack
                return NetStatus.Code.PROTOCOL_DROP_ERROR;
            }
            else if (TCPSeqGreater(tcpHeader.ackSeq, s.sessionTCB.SND.NXT))
            {
                // ack for future data..
                TCPFSM.SendAck(s);
                return NetStatus.Code.PROTOCOL_DROP_ERROR;
            }

            // check URG bit
            if (TcpFormat.IsUrg(ref tcpHeader))
            {
                // TBC
            }

            if (TcpFormat.IsPush(ref tcpHeader))
            {
#if DEBUG_TCP
                Core.Log("TCP: Received PUSH data, Len={0} !!!!!!!\n",segmentSize);
#endif
            }

            // at last, process the segment data!!!
            // at the end send an ACK (TBC: change to accumulate acks)
            if (segmentSize>0)
            {
                // put the data in the session's inQ
                if (s.PutPacket(s.inQueue,pkt,false))
                {
                    s.sessionTCB.RCV.NXT+=segmentSize; // we expect the next segment seq
                    s.sessionTCB.RCV.WND=TcpFormat.TCP_MSS; // TBC: change according to buffer
#if DEBUG_TCP
                    Core.Log("TCP:: PUSH data, Len={0} is on Queue\n",segmentSize);
#endif
                    // send our ack if we ack data
                    SendAck(s);
                    // don't release the packet
                    return NetStatus.Code.PROTOCOL_PROCESSING;
                }
                else
                {
                    // packet was dropped...
                    // send our ack if we ack data
                    SendAck(s);
                    return NetStatus.Code.PROTOCOL_DROP_ERROR;
                }
            }
            else
            {
                // Payload was empty...
                return NetStatus.Code.PROTOCOL_DROP_ERROR;
            }
        }

        // handle a TCP received ack
        // the ack is already checked for validity (by the actual state)
        internal static void HandleTCPAck(TcpSession! owner, ref TcpFormat.TcpHeader tcpHdr)
        {
            owner.ACKThrough(tcpHdr.ackSeq);
        }

        // check if we can accept the segment
        internal static bool IsSegmentAcceptable(TcpSession! s, ref TcpFormat.TcpHeader tcpHeader,uint segmentSize)
        {
            bool accept=false;
            // first check sequence number
            if ((segmentSize==0)&&(s.sessionTCB.RCV.WND==0))
            {
                accept = (tcpHeader.seq==s.sessionTCB.RCV.NXT);
            }
            else if ((segmentSize==0)&&(s.sessionTCB.RCV.WND>0))
            {
                accept=(TCPSeqGEQ(tcpHeader.seq, s.sessionTCB.RCV.NXT) &&
                        TCPSeqLess(tcpHeader.seq, s.sessionTCB.RCV.NXT+s.sessionTCB.RCV.WND));
            }
            else if ((segmentSize>0)&&(s.sessionTCB.RCV.WND>0))
            {
                accept=(TCPSeqGEQ(tcpHeader.seq, s.sessionTCB.RCV.NXT) &&
                        TCPSeqLess(tcpHeader.seq, s.sessionTCB.RCV.NXT+s.sessionTCB.RCV.WND));

                accept = accept || (TCPSeqLEQ(s.sessionTCB.RCV.NXT, tcpHeader.seq+segmentSize-1) &&
                                    TCPSeqLess(tcpHeader.seq +segmentSize - 1,
                                               s.sessionTCB.RCV.NXT + s.sessionTCB.RCV.WND));
            }
            return accept;
        }

        private static void AttemptToEnterESTABLISHED(TcpSession! s,
                                                      ref TcpFormat.TcpHeader tcpHeader)
        {
            TcpSession passiveOwner = s.passiveSession;

            if (passiveOwner != null)
            {
                bool success = passiveOwner.AddAcceptedSession(s);

                if (!success)
                {
                    // Oops; while this connection was being established
                    // we ran out of room in the accept queue. Abort
                    // the connection!
                    HandleTerminateSession(s,CreateResetSegment(s, false), TcpError.ResourcesExhausted);
                    return;
                }
            }

            s.ChangeState(ESTABLISHED);
        }

        // this method terminated the given session
        // if nextSegment is not null than it is sent before final
        // removal.
        internal static void HandleTerminateSession(TcpSession! s, TcpSegment nextSegment, TcpError connectError)
        {
            s.StopPersistTimer();

            // 1. first clear the retransmit queue
            // 2. make the session not valid for users!
            // 3. release any user waiting for read/write on the session
            // 4. clear the out/in queues
            // 5. remove the session from TCP if it is no longer needed
            //    (when it considered to be closed)
            s.FlushRetransmissions();

            // some user may wait on the q to write/read data
            // we will release them by trigger the monitors.
            // they will fail since Valid is false and they are users.
            // only TCP can still use this session.
            lock (s.outQueue.SyncRoot)
            {
                // from now on, every user thread will
                // fail to read/write data
                s.ValidForRead = false;
                s.ValidForWrite = false;

                s.DrainQueue(s.outQueue);
                if (nextSegment != null)
                    s.outQueue.Add(nextSegment);

                // Don't forget these!
                Monitor.PulseAll(s.outQueue.SyncRoot);
                Core.Instance().SignalOutboundPackets();
            }

            lock (s.inQueue.SyncRoot)
            {
                // Pulse anyone waiting to read data so they can notice
                // they are unlikely to succeed, but don't clear the
                // queue; if there is data on it, we may want to drain
                // it, still.
                Monitor.PulseAll(s.inQueue.SyncRoot);
            }

            if (nextSegment == null)
            {
                IProtocol tcpProtocol = s.Protocol;
                Core.Instance().DeregisterSession(tcpProtocol, s);
            }

            // change the state to close
            s.ChangeState(TCPFSM.CLOSED);

            // Signal anyone who was waiting for this session to begin or end
            s.connectError = connectError;
            s.setupCompleteEvent.Set();

            // NOTE  If we are transmitting a last-gasp packet,
            // don't signal the session as closed just yet; wait for the packet
            // to actually get transmitted.
            if (nextSegment == null)
            {
                s.closedEvent.Set();
            }
        }
    }
}
