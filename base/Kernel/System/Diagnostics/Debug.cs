///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

namespace System.Diagnostics
{

    public sealed class Debug {

        [Conditional("DEBUG")]
        public static void Assert(bool truth)
        {
            VTable.Assert(truth);
        }

        [Conditional("DEBUG")]
        public static void Assert(bool truth, string statement)
        {
            VTable.Assert(truth, statement);
        }

        public static void AssertValidReference(Object obj) {
            VTable.Assert(obj == null || obj.vtable != null);
        }
    }
}
