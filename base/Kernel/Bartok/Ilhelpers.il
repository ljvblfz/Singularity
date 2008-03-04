///////////////////////////////////////////////////////////////////////////////
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

/*******************************************************************/
/*                           WARNING                               */
/* This file should be identical in the Bartok and Singularity     */
/* depots. Master copy resides in Bartok Depot. Changes should be  */
/* made to Bartok Depot and propagated to Singularity Depot.       */
/*******************************************************************/

.assembly extern mscorlib
{
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89 )    // .z\V.4..
  .ver 1:0:5000:0
}

.assembly ILHelpers
{
}
.module ILHelpers.dll
.custom instance void [mscorlib]System.Security.UnverifiableCodeAttribute::.ctor() = ( 01 00 00 00 ) 
.imagebase 0x00400000
.subsystem 0x00000003
.file alignment 4096
.corflags 0x00000001
// Image base: 0x02c00000

// ============== CLASS STRUCTURE DECLARATION ==================

.namespace System
{
  .class public auto ansi serializable beforefieldinit sealed ILHelpers
         extends [mscorlib]System.Object
  {
  } // end of class ILHelpers

} // end of namespace System

// =============== CLASS MEMBERS DECLARATION ===================
//   note that class flags, 'extends' and 'implements' clauses
//          are provided here for information only

.namespace System
{
  .class public auto ansi serializable beforefieldinit sealed ILHelpers
         extends [mscorlib]System.Object
  {
    .method public hidebysig specialname static 
            void SetAt(object obj,
                       native unsigned int offset,
                       native unsigned int data) cil managed
    {
      //.maxstack  8
      .locals ([0] native unsigned int& dataPtr)

      // dataPtr = (native unsigned int&) obj
      ldarg.0
      stloc.0

      // dataPtr += offset
      ldloc.0
      ldarg.1
      add
      stloc.0

      // *dataPtr = data
      ldloc.0
      ldarg.2
      stind.i

      ret
    } // end of method ILHelpers::SetAt

    .method public hidebysig specialname static 
            void SetAt(object obj,
                       native unsigned int offset,
                       object data) cil managed
    {
      //.maxstack  8
      .locals ([0] object& dataPtr)

      // dataPtr = (object&) obj
      ldarg.0
      stloc.0

      // dataPtr += offset
      ldloc.0
      ldarg.1
      add
      stloc.0

      // *dataPtr = data
      ldloc.0
      ldarg.2
      stind.ref

      ret
    } // end of method ILHelpers::SetAt
  }
}
