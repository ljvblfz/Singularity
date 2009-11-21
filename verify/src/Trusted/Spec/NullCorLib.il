////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation
//
//  The null runtime library (NullCorLib).    This is the minimal set of class
//  definitions actually needed to compile a Bartok program and produce
//  an executable that links.

// TODO: there are instructions in some of the method bodies below -- we should move these out of trusted.

.assembly mscorlib
{
}
.module mscorlib.dll
.imagebase 0x00400000
.subsystem 0x00000003
.file alignment 4096
.corflags 0x00000001
// Image base: 0x02c00000

//
//  TODO: add definitons or remove use of attribute classes.    Otherwise
//  they cause Bartok's meta-data reader to vector into the meta-data for
//  mincorlib, causing asserts about duplicate methods
//
// ============== CLASS STRUCTURE DECLARATION ==================

.namespace System
{

   .class public abstract auto ansi beforefieldinit Enum
        extends System.ValueType
   {
   } 

  .class public abstract auto ansi beforefieldinit ValueType
       extends System.Object
  {
   .method family hidebysig specialname rtspecialname 
         instance void  .ctor() cil managed
  {
    .maxstack  8
    ldarg.0
    call       instance void System.Object::.ctor()
    ret
  } 
  } 

  .class public auto ansi beforefieldinit Object
  {
     .custom instance void [mscorlib]System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
  }

  .class public abstract sequential ansi beforefieldinit Array
  {
     .custom instance void [mscorlib]System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
  } 

  .class public auto ansi beforefieldinit Exception
  {
     .custom instance void [mscorlib]System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
  } // 

   .class public sequential ansi sealed beforefieldinit Byte
        extends System.ValueType
    {
    }
 
   .class public sequential ansi sealed beforefieldinit Int32
        extends System.ValueType
   {
   }

} // end of namespace System

// =============== CLASS MEMBERS DECLARATION ===================
//   note that class flags, 'extends' and 'implements' clauses
//          are provided here for information only

.namespace System
{
  .class public auto ansi beforefieldinit Exception
        extends System.Object
  {
      .field assembly string _message
      .custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
  }

  .class public auto ansi beforefieldinit Object
  {
//    .field assembly class System.VTable vtable
//    .custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 

    .field assembly valuetype Microsoft.Bartok.Runtime.PreHeader preHeader
    .field assembly valuetype Microsoft.Bartok.Runtime.PostHeader postHeader
    .custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 

    .method public hidebysig specialname rtspecialname 
        instance void  .ctor() cil managed
    {
	.custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
     .maxstack  8
     ret
    } // end of method Object::.ctor


    //
    // public virtual bool Equals();
    //
    .method public hidebysig newslot virtual 
        instance bool  Equals(object obj) cil managed
    {
        .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
        ldarg.0
        ldarg.1
        ceq
        ret
    }

    //
    // public virtual void Finalize()
    // 

    .method family hidebysig virtual instance void 
    Finalize() cil managed
    {
      .maxstack  8
      ret
    } 

    //
    // public virtual int GetHashCode()
    //
    .method public hidebysig newslot virtual 
        instance int32  GetHashCode() cil managed
    {
	ldc.i4.0
	ret
    }

   .method assembly hidebysig static object 
        GetObject(int32 syncBlockIndex) cil managed
    {
      .custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
      .maxstack  1
      ldnull
      ret
    }


    //	
    // public virtual System.TypeCode GetTypeCode	
    //

    .method public hidebysig newslot virtual 
        instance valuetype System.TypeCode 
        GetTypeCode() cil managed
    {
      .maxstack  1
      .locals (valuetype System.TypeCode V_0)
      ldc.i4.1
      stloc.0
      ldloc.0
      ret
    }



    //
    // public virtual String ToString()
    //

    .method public hidebysig newslot virtual 
        instance string  ToString() cil managed
    {
     .maxstack  1
     .locals (string V_0)
     ldnull
     ret
    }
  }

    .class public sequential ansi sealed beforefieldinit Byte
        extends System.ValueType
    {
     .custom instance void [mscorlib]System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
       .field private unsigned int8 m_value
       .custom instance void [mscorlib]System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
   }

  .class public sequential ansi sealed beforefieldinit Int32
       extends  System.ValueType
  {
     .custom instance void [mscorlib]System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
     .field private unsigned int32 m_value
     .custom instance void [mscorlib]System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
  }

  .class public auto ansi beforefieldinit Array
	extends System.Object
  {
	.field private int32 field1
      .custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
  }

  .class public auto ansi sealed beforefieldinit String
       extends System.Object
{
  .custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
  .custom instance void System.Runtime.CompilerServices.CCtorIsRunDuringStartupAttribute::.ctor() = ( 01 00 00 00 ) 

  .field private int32 m_arrayLength
  .custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 


  .field private char m_firstChar
  .custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 


.field private int32 m_stringLength
.custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
} 
}

.class public abstract auto ansi beforefieldinit System.Attribute
       extends System.Object
{
}

.class public auto ansi sealed beforefieldinit System.Runtime.CompilerServices.AccessedByRuntimeAttribute
       extends [mscorlib]System.Attribute
{
}

.class public auto ansi sealed beforefieldinit System.Runtime.CompilerServices.CCtorIsRunDuringStartupAttribute
       extends [mscorlib]System.Attribute
{
}

.class public auto ansi sealed beforefieldinit System.Runtime.CompilerServices.RequiredByBartokAttribute
       extends System.Attribute
{
}

.class public auto ansi serializable sealed System.TypeCode
       extends System.Enum
{
  .field public specialname rtspecialname int32 value__
}

.class public sequential ansi sealed beforefieldinit System.Boolean extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.SByte extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.Char extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.Int16 extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.Int64 extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.IntPtr extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.UInt16 extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.UInt32 extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.UInt64 extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.UIntPtr extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.Single extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.Double extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.RuntimeTypeHandle extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.TypedReference extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.Void extends System.ValueType {}
.class public sequential ansi sealed beforefieldinit System.RuntimeArgumentHandle extends System.ValueType {}
.class public auto ansi sealed beforefieldinit System.Text.StringBuilder {}

.class private auto ansi sealed beforefieldinit System.RuntimeType extends System.Type
{
  .custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor(string) = ( 01 00 13 52 65 66 65 72 65 6E 63 65 64 20 66 72   // ...Referenced fr
                                                                                                      6F 6D 20 43 2B 2B 00 00 )                         // om C++..
}

.class public abstract auto ansi serializable beforefieldinit System.Delegate {}
.class public abstract auto ansi serializable beforefieldinit System.MulticastDelegate extends System.Delegate {}
.class public abstract auto ansi serializable System.Type {}

.class public auto ansi sealed beforefieldinit System.Threading.Thread {}

.class private sequential ansi sealed beforefieldinit System.InterfaceInfo extends System.ValueType {}

.class private sequential ansi sealed beforefieldinit Microsoft.Bartok.Runtime.PreHeader
         extends System.ValueType
{
  .pack 0
  .size 1
  .field assembly int32 muw
} // end of class Microsoft.Bartok.Runtime.PreHeader

.class private sequential ansi sealed beforefieldinit Microsoft.Bartok.Runtime.PostHeader
         extends System.ValueType
{
  .custom instance void [mscorlib]System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 
  .pack 0
  .size 1
  .field assembly class System.VTable vtableObject
  .custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor(string) = ( 01 00 18 61 63 63 65 73 73 65 64 20 66 72 6F 6D   // ...accessed from
                                                                                                      20 68 61 6C 65 78 6E 2E 63 70 70 00 00 )          //  halexn.cpp..
} // end of class Microsoft.Bartok.Runtime.PostHeader

.class private sequential ansi sealed System.VTable
{
  .custom instance void [mscorlib]System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor() = ( 01 00 00 00 ) 

  .field public static literal bool enableLibraryOptions = bool(true)
  .field public initonly class System.VTable depth1
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public initonly class System.VTable depth2
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public initonly class System.VTable depth3
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public initonly class System.VTable depth4
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public initonly class System.VTable depth5
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public initonly class System.VTable depth6
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public class System.VTable posCache
  .field public int32 depth
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public initonly int32 arrayOf
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public initonly class System.VTable elementVTable
//  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public initonly int32 arrayElementSize
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public initonly valuetype System.InterfaceInfo[] interfaces
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public initonly uint32 baseLength
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public initonly uint32 baseAlignment
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public initonly native uint pointerTrackingMask
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public initonly int32 structuralView
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
//  .field public initonly class System.RuntimeType vtableType
//  .custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor(string) = ( 01 00 13 52 65 66 65 72 65 6E 63 65 64 20 66 72   // ...Referenced fr
//                                                                                                      6F 6D 20 43 2B 2B 00 00 )                         // om C++..
//  .field public initonly int32 marshalSize
//  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
//  .field public initonly class System.VTable vectorClass
//  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
//  .field public initonly bool isAcyclicRefType
//  .custom instance void System.Runtime.CompilerServices.AccessedByRuntimeAttribute::.ctor(string) = ( 01 00 26 54 79 70 65 20 69 6E 66 6F 72 6D 61 74   // ..&Type informat
//                                                                                                      69 6F 6E 20 61 76 61 69 6C 61 62 6C 65 20 74 6F   // ion available to
//                                                                                                      20 72 75 6E 74 69 6D 65 2E 00 00 )                //  runtime...

  .method assembly hidebysig static void 
          throwNewIndexOutOfRangeException() cil managed
  {
    .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
    .maxstack  8
    IL_0000:  nop
    IL_0001:  ldnull
//    IL_0001:  newobj     instance void System.Exception::.ctor()
    IL_0006:  throw
  } // end of method VTable::throwNewIndexOutOfRangeException

  .method assembly hidebysig static void 
          throwNewOverflowException() cil managed
  {
    .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
    .maxstack  8
    IL_0000:  nop
    IL_0001:  ldnull
//    IL_0001:  newobj     instance void System.Exception::.ctor()
    IL_0006:  throw
  } // end of method VTable::throwNewIndexOutOfRangeException

  .method assembly hidebysig static void 
          throwNewDivideByZeroException() cil managed
  {
    .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
    .maxstack  8
    IL_0000:  nop
    IL_0001:  ldnull
//    IL_0001:  newobj     instance void System.Exception::.ctor()
    IL_0006:  throw
  } // end of method VTable::throwNewIndexOutOfRangeException

  .method assembly hidebysig static void 
          throwNewArithmeticException() cil managed
  {
    .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
    .maxstack  8
    IL_0000:  nop
    IL_0001:  ldnull
//    IL_0001:  newobj     instance void System.Exception::.ctor()
    IL_0006:  throw
  } // end of method VTable::throwNewIndexOutOfRangeException

  .method assembly hidebysig static void 
          throwNewStringIndexOutOfRangeException() cil managed
  {
    .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
    .maxstack  8
    IL_0000:  nop
    IL_0001:  ldnull
//    IL_0001:  newobj     instance void System.Exception::.ctor()
    IL_0006:  throw
  } // end of method VTable::throwNewIndexOutOfRangeException


} 

.class private auto ansi beforefieldinit System.GCs.StaticData
       extends System.Object
{
  .field private static int32 sectionCount
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private static native uint** dataSectionEnd
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private static native uint** dataSectionBase
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private static native uint** roDataSectionEnd
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private static native uint** roDataSectionBase
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private static uint32** staticDataPointerBitMap
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
}

.class private auto ansi beforefieldinit System.GCs.CallStack
{
  .field private static int32 callSiteTableCount
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private static native uint* codeBaseStartTable
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private static native uint** returnAddressToCallSiteSetNumbers
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private static int32** callSiteSetCount
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private static uint16** callSetSiteNumberToIndex
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  .field private static valuetype System.GCs.CompressedFrameDescriptor** activationDescriptorTable
  .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
}

.class private sequential ansi sealed beforefieldinit System.GCs.CompressedFrameDescriptor
       extends System.ValueType
{
}

.class public auto ansi sealed System.GC extends System.Object
{
  .method assembly hidebysig static object AllocateObject(class System.VTable vtable) cil managed internalcall
  {
    .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  }

  .method assembly hidebysig static class System.Array AllocateVector(class System.VTable vtable, int32 numElements) cil managed internalcall
  {
    .custom instance void System.Runtime.CompilerServices.RequiredByBartokAttribute::.ctor() = ( 01 00 00 00 ) 
  }
}

.class public auto ansi sealed beforefieldinit System.Runtime.CompilerServices.IsVolatile
       extends System.Object
{
}
