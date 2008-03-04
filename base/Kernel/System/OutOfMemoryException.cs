// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
/*=============================================================================
**
** Class: OutOfMemoryException
**
**
** Purpose: The exception class for OOM.
**
** Date: March 17, 1998
**
=============================================================================*/

namespace System {

    using System;
    using System.Runtime.CompilerServices;

    //| <include path='docs/doc[@for="OutOfMemoryException"]/*' />
    public class OutOfMemoryException : SystemException {
        //| <include path='docs/doc[@for="OutOfMemoryException.OutOfMemoryException"]/*' />
        public OutOfMemoryException()
            : base("Arg_OutOfMemoryException") {
        }

        //| <include path='docs/doc[@for="OutOfMemoryException.OutOfMemoryException1"]/*' />
        public OutOfMemoryException(String message)
            : base(message) {
        }

        //| <include path='docs/doc[@for="OutOfMemoryException.OutOfMemoryException2"]/*' />
        public OutOfMemoryException(String message, Exception innerException)
            : base(message, innerException) {
        }
    }
}
