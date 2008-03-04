// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
/*=============================================================================
**
** Class: DivideByZeroException
**
**
** Purpose: Exception class for bad arithmetic conditions!
**
** Date: March 17, 1998
**
=============================================================================*/

namespace System {

    using System;
    using System.Runtime.CompilerServices;

    [RequiredByBartok]
    //| <include path='docs/doc[@for="DivideByZeroException"]/*' />
    public class DivideByZeroException : ArithmeticException {
        //| <include path='docs/doc[@for="DivideByZeroException.DivideByZeroException"]/*' />
        [AccessedByRuntime("referenced from halasm.asm")]
        public DivideByZeroException()
            : base("Arg_DivideByZero") {
        }

        //| <include path='docs/doc[@for="DivideByZeroException.DivideByZeroException1"]/*' />
        public DivideByZeroException(String message)
            : base(message) {
        }

        //| <include path='docs/doc[@for="DivideByZeroException.DivideByZeroException2"]/*' />
        public DivideByZeroException(String message, Exception innerException)
            : base(message, innerException) {
        }
    }
}
