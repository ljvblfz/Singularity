///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;

namespace NetStack.Runtime
{
    public class TcpSessionPool
    {
        static TcpModule tcpModule;
        static ArrayList tcpSessions = new ArrayList();

        static long allocations;
        static long getCalls;
        static long recycleCalls;

        public static void SetTcpModule(TcpModule m)
        {
            tcpModule = m;
        }

        public static TcpSession! Get()
        {
            lock (tcpSessions) {
                getCalls++;
                foreach (TcpSession! ts in tcpSessions) {
                    if (ts.IsClosed) {
                        Core.Instance().DeregisterSession(ts.Protocol, ts);
                        tcpSessions.Remove(ts);
                        return tcpModule.ReInitializeSession(ts);
                    }
                }
                allocations++;
            }

            return new TcpSession(tcpModule);
        }

        public static void Recycle(TcpSession! ts)
        {
            lock (tcpSessions) {
                recycleCalls++;
                tcpSessions.Add(ts);
            }
        }
    }
}
