// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
/*=============================================================================
**
** Class: ThreadState
**
**
** Purpose: Enum to represent the different thread states
**
** Date: Feb 2, 2000
**
=============================================================================*/

namespace System.Threading {

    //| <include path='docs/doc[@for="ThreadState"]/*' />
    public enum ThreadState
    {
        /*=========================================================================
        ** Constants for thread states.
        =========================================================================*/
        //| <include path='docs/doc[@for="ThreadState.Running"]/*' />
        Running = 0,
        //| <include path='docs/doc[@for="ThreadState.Unstarted"]/*' />
        Unstarted = 1,
        //| <include path='docs/doc[@for="ThreadState.Stopping"]/*' />
        Stopping = 2,
        //| <include path='docs/doc[@for="ThreadState.Stopped"]/*' />
        Stopped = 3,
        Suspended = 4,
    }
}
