// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
/*=============================================================================
**
** Class: IndexOutOfRangeException
**
**
** Purpose: Exception class for invalid array indices.
**
** Date: March 24, 1998
**
=============================================================================*/

namespace System {

    using System;
    using System.Runtime.CompilerServices;

    //| <include path='docs/doc[@for="IndexOutOfRangeException"]/*' />
    [RequiredByBartok]
    public sealed class IndexOutOfRangeException : SystemException {
        //| <include path='docs/doc[@for="IndexOutOfRangeException.IndexOutOfRangeException"]/*' />
        public IndexOutOfRangeException()
            : base("Arg_IndexOutOfRangeException") {
        }

        //| <include path='docs/doc[@for="IndexOutOfRangeException.IndexOutOfRangeException1"]/*' />
        public IndexOutOfRangeException(String message)
            : base(message) {
        }

        //| <include path='docs/doc[@for="IndexOutOfRangeException.IndexOutOfRangeException2"]/*' />
        public IndexOutOfRangeException(String message, Exception innerException)
            : base(message, innerException) {
        }
    }
}
