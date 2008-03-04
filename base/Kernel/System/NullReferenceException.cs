// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
/*=============================================================================
**
** Class: NullReferenceException
**
**
** Purpose: Exception class for dereferencing a null reference.
**
** Date: March 17, 1998
**
=============================================================================*/

namespace System {

    using System;
    using System.Runtime.CompilerServices;

    //| <include path='docs/doc[@for="NullReferenceException"]/*' />
    [RequiredByBartok]
    public class NullReferenceException : SystemException {
        //| <include path='docs/doc[@for="NullReferenceException.NullReferenceException"]/*' />
        [AccessedByRuntime("referenced from halasm.asm")]
        public NullReferenceException()
            : base("Arg_NullReferenceException") {
        }

        //| <include path='docs/doc[@for="NullReferenceException.NullReferenceException1"]/*' />
        public NullReferenceException(String message)
            : base(message) {
        }

        //| <include path='docs/doc[@for="NullReferenceException.NullReferenceException2"]/*' />
        public NullReferenceException(String message, Exception innerException)
            : base(message, innerException) {
        }
    }
}
