// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
/*=============================================================================
**
** Class: InvalidCastException
**
**
** Purpose: Exception class for bad cast conditions!
**
** Date: March 17, 1998
**
=============================================================================*/

namespace System {

    using System;
    using System.Runtime.CompilerServices;

    //| <include path='docs/doc[@for="InvalidCastException"]/*' />
    public class InvalidCastException : SystemException {
        //| <include path='docs/doc[@for="InvalidCastException.InvalidCastException"]/*' />
        public InvalidCastException()
            : base("Arg_InvalidCastException") {
        }

        //| <include path='docs/doc[@for="InvalidCastException.InvalidCastException1"]/*' />
        public InvalidCastException(String message)
            : base(message) {
        }

        //| <include path='docs/doc[@for="InvalidCastException.InvalidCastException2"]/*' />
        public InvalidCastException(String message, Exception innerException)
            : base(message, innerException) {
        }
    }
}
