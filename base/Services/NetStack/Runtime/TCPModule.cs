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

using NetStack.Common;
using System;
using System.Collections;
using System.Diagnostics;

#if !SINGULARITY
using System.Net;
using System.Text;
#endif

using System.Net.IP;
using Drivers.Net;
using NetStack.Protocols;

namespace NetStack.Runtime
{
    /**
     * This module implements the TCP protocol
     * Notice: It is a simplified implementation (the very basic)
     */
    public class TcpModule : IProtocol
    {
        // the ip handler
        protected IProtocol ip;

        // the TCP retransmit timeout
        protected int retransTimeout;

        // INetModule interfaces
        // ------------------------
        string INetModule.ModuleName    { get { return "TCP"; } }
        ushort INetModule.ModuleVersion { get { return 0x01; } }  // 0.1

        public bool StartModule()
        {
#if DEBUG_TCP
            Core.Log("Starting TCP module...\n");
#endif
            ip = Core.Instance().GetProtocolByName("IP");
            Debug.Assert(ip != null);
            return true;
        }

        public bool StopModule()
        {
            return true;
        }

        public bool DestroyModule()
        {
            return true;
        }

        // IProtocol interfaces
        // ------------------------
        public bool Initialize(ProtocolParams parameters)
        {
            Debug.Assert(parameters == null || parameters["name"]=="TCP");
            Core.Instance().RegisterProtocol(this);
            TcpSessionPool.SetTcpModule(this);
            return true;
        }

        [ Conditional("DEBUG_TCP") ]
        private static void DebugPrint(string format, params object [] args)
        {
            Core.Log(format, args);
        }

        public ushort GetProtocolID()
        {
            return ((ushort)IPFormat.Protocol.TCP);
        }

        Session IProtocol.CreateSession()
        {
            TcpSession tcp = (TcpSession) TcpSessionPool.Get();
            Core.Instance().RegisterSession(this,tcp);
            return tcp;
        }

        public TcpSession! ReInitializeSession(TcpSession! tcp)
        {
            tcp.ReInitialize(this);
            Core.Instance().RegisterSession(this, tcp);
            return tcp;
        }

        // handle incoming TCP packets
        public NetStatus OnProtocolReceive(NetPacket! packet)
        {
            DebugPrint("TcpModule.OnPacketReceive\n");

            // IP should have passed us a context which
            // is the IPHeader... lets see...
            IPFormat.IPHeader! ipHeader = (IPFormat.IPHeader!) packet.OverlapContext;

            // read the TCP header
            TcpFormat.TcpHeader tcpHeader;
            if (!TcpFormat.ReadTcpHeader(packet, out tcpHeader))
            {
                DebugPrint("Bad TCP Header");
                return NetStatus.Code.PROTOCOL_DROP_ERROR;
            }

            // check validity
            if (tcpHeader.checksum != 0 &&
                TcpFormat.IsChecksumValid(ipHeader,tcpHeader, packet) == false)
            {
                DebugPrint("TCP checksum failed. No cigar and no packet!");
                return NetStatus.Code.PROTOCOL_DROP_CHKSUM;
            }

            // where is our data?
            // skip over options (that we currently ignore)
            int tcpHeaderSize = 4 * TcpFormat.GetOffset(ref tcpHeader);
            int startIndex = EthernetFormat.Size + IPFormat.Size + tcpHeaderSize;
            int segmentSize = ipHeader.totalLength - IPFormat.Size - tcpHeaderSize;
            packet.Clip(startIndex,segmentSize - 1);

            // find the relevant session...
            Session s = FindSession(ipHeader, ref tcpHeader);
            if (s != null) {
                DebugPrint("TCP Session found.\n");
                // deliver the packet the the session
                // we pass the tcpHeader as a context
                // and the ipHeader as a packet.OverlapContext
                // since we will not use it anymore (and TCP may need it)
                packet.OverlapContext = ipHeader;
                return s.OnReceive(this,packet,tcpHeader);
            }

            return NetStatus.Code.PROTOCOL_DROP_ERROR;
        }

        // find the relevant session for this connection
        protected Session FindSession(IPFormat.IPHeader! ipHeader,
                                      ref TcpFormat.TcpHeader tcpHeader)
        {
            // get the session table
            ArrayList sessions = Core.Instance().GetSessions(this);
            if (sessions == null)
            {
                return null;
            }

            TcpSession passiveSession = null;  // this is the passive session
            // we first look for full match, saving the passive session
            // if exist. if no full match, we return the passiveSession

            lock (sessions.SyncRoot)
            {
                // this is a TcpSession
                foreach(TcpSession! s in sessions)
                {
                    bool b1 = (s.LocalAddress  == ipHeader.Destination);
                    bool b2 = (s.RemoteAddress == ipHeader.Source);
                    bool b3 = (s.LocalPort  == tcpHeader.destPort);
                    bool b4 = (s.RemotePort == tcpHeader.sourcePort);

                    bool fullMatch = b1 & b2 & b3 & b4;
                    // if we have full match, we return
                    // otherwise, save the passive session
                    if (fullMatch)
                    {
                        return s;
                    }
                    bool b5 = (s.RemoteAddress == IPv4.Broadcast &&
                               s.RemotePort == 0);
                    bool passiveMatch = b1 & b3 & b5;
                    if (passiveMatch)
                    {
                        passiveSession = s;
                    }
                }
            }
            // if we can't find exact match...
            return passiveSession;
        }
        // this method send a ready made TCP packet,
        // it uses the IP layer.
        public NetStatus OnProtocolSend(NetPacket! pkt)
        {
            // if the ARP hasn't resolved the address yet, the
            // runtime will try this again (on the next handle
            // of outgoing sessions' queues)
            return ip.OnProtocolSend(pkt);
        }

        public NetStatus SetProtocolSpecific(ushort opcode, byte[]! data)
        {
            return NetStatus.Code.PROTOCOL_OK;
        }

        public NetStatus GetProtocolSpecific(ushort opcode,out byte[] data)
        {
            data=null;
            return NetStatus.Code.PROTOCOL_OK;
        }

        public TcpModule()
        {
        }
    }
}
