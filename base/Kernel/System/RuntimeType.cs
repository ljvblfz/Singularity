// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////
//
// __RuntimeType is the basic Type object representing classes as found in the
//      system.  This type is never creatable by users, only by the system itself.
//      The internal structure is known about by the runtime. __RuntimeXXX classes
//      are created only once per object in the system and support == comparisons.
//
// Date: March 98
//
namespace System {

    using Microsoft.Bartok.Runtime;
    using System;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Runtime.CompilerServices;
    using System.Text;
    using Thread = System.Threading.Thread;
    using SystemType = Microsoft.Singularity.V1.Types.SystemType;

    [CCtorIsRunDuringStartup]
    [StructLayout(LayoutKind.Sequential)]
    internal sealed class RuntimeType : Type, ICloneable
    {
        // expand as needed, but also see/use TypeAttributes
        // matches convert\ContainerInfo.cs::Enum_Kind
        internal enum Enum_Kind {
            NotSpecial     = 0,
            Vector         = 1,
            RectangleArray = 2,
            Primitive      = 3,
            Enum           = 4,
            OtherValueType = 5
        };

        private readonly Assembly assembly;
        [RequiredByBartok]
        private readonly RuntimeType enclosingType;
        [AccessedByRuntime("Referenced from C++")]
        [RequiredByBartok]
        private readonly String name;
        [RequiredByBartok]
        private readonly String nameSpace;

        [RequiredByBartok]
        internal readonly RuntimeType baseType;
        [RequiredByBartok]
        internal readonly System.RuntimeType[] interfaces;
        [RequiredByBartok]
        internal readonly Enum_Kind kind;
        [RequiredByBartok]
        internal readonly int rank;
        [RequiredByBartok]
        internal readonly TypeAttributes attributes;
        [AccessedByRuntime("Referenced from C++")]
        [RequiredByBartok]
        internal readonly VTable classVtable;

        // Putting these here for now because we need them for classes
        // and interfaces, and I don't think we build VTable objects for
        // interfaces:

        [RequiredByBartok]
        internal readonly System.UIntPtr cctor; // HACK: function pointer
        [RequiredByBartok]
        internal readonly System.UIntPtr ctor;  // HACK: function pointer
        [RequiredByBartok]
        internal Exception cctorException;
        [RequiredByBartok]
        internal TypeInitState cctorState;
        [RequiredByBartok]
        internal System.Threading.Thread cctorThread;

        // Prevent from begin created
        internal RuntimeType() {
            throw new Exception("RuntimeType constructor not supported");
        }

        // Given a class handle, this will return the class for that handle.
        [NoHeapAllocation]
        public unsafe static Type GetTypeFromHandleImpl(RuntimeTypeHandle handle) {
            IntPtr handleAddress = handle.Value;
            Object obj = Magic.fromAddress((UIntPtr) handleAddress);
            return Magic.toType(obj);
        }

        // Return the name of the class.  The name does not contain the namespace.
        public override String Name {
            [NoHeapAllocation]
            get { return name; }
        }

        public override int GetHashCode() {
            return ((int)this.classVtable.arrayOf << 8 + rank)
                + (int)this.classVtable.structuralView;
        }

        // Return the name of the class.  The name does not contain the namespace.
        public override String ToString(){
            return InternalGetProperlyQualifiedName();
        }

        private String InternalGetProperlyQualifiedName() {
            // markples: see also Lightning\Src\VM\COMClass.cpp::GetProperName
            return FullName;
        }

        private void AddFullName(StringBuilder sb) {
            RuntimeType enclosing = this.enclosingType;
            while(enclosing != null) {
                enclosing.AddFullName(sb);
                sb.Append('+');
                enclosing = enclosing.enclosingType;
            }
            if(nameSpace != null) {
                sb.Append(nameSpace);
                sb.Append('.');
            }
            sb.Append(name);
        }

        // Return the fully qualified name.  The name does contain the
        // namespace.
        public override String FullName {
            get {
                StringBuilder sb = new StringBuilder();
                AddFullName(sb);
                return sb.ToString();
            }
        }

        // Return the name of the type including the assembly from which it came.
        // This name can be persisted and used to reload the type at a later
        // time.
        public override String AssemblyQualifiedName {
            get {
                StringBuilder sb = new StringBuilder();
                AddFullName(sb);
                sb.Append(", ");
                sb.Append(this.assembly.FullName);
                return sb.ToString();
            }
        }

        public override Assembly Assembly {
            [NoHeapAllocation]
            get { return assembly; }
        }

        // Return the name space.
        public override String Namespace
        {
            [NoHeapAllocation]
            get { return nameSpace; }
        }

        // Returns the base class for a class.  If this is an interface or has
        // no base class null is returned.  Object is the only Type that does not
        // have a base class.
        public override Type BaseType {
            [NoHeapAllocation]
            get { return baseType; }
        }


        public override int GetArrayRank() {
            return rank;
        }

        // GetInterfaces
        // This method will return all of the interfaces implemented by a
        //  class
        public override Type[] GetInterfaces()
        {
            return interfaces;
        }

        ////////////////////////////////////////////////////////////////////////////////
        //
        // Attributes
        //
        //   The attributes are all treated as read-only properties on a class.  Most of
        //  these boolean properties have flag values defined in this class and act like
        //  a bit mask of attributes.  There are also a set of boolean properties that
        //  relate to the classes relationship to other classes and to the state of the
        //  class inside the runtime.
        //
        ////////////////////////////////////////////////////////////////////////////////

        // Internal routine to get the attributes.
        [NoHeapAllocation]
        protected override TypeAttributes GetAttributeFlagsImpl() {
            return attributes;
        }

        // Internal routine to determine if this class represents an Array
        [NoHeapAllocation]
        protected override bool IsArrayImpl() {
            return this.classVtable.arrayOf != StructuralType.None;
        }

        // Internal routine to determine if this class represents a primitive type
        [NoHeapAllocation]
        protected override bool IsPrimitiveImpl()
        {
            return kind == Enum_Kind.Primitive;
        }

        internal bool IsVector {
            [NoHeapAllocation]
            get {
                return kind == Enum_Kind.Vector;
            }
        }

        internal bool IsRectangleArray {
            [NoHeapAllocation]
            get {
                return kind == Enum_Kind.RectangleArray;
            }
        }

        [NoHeapAllocation]
        public override Type GetElementType()
        {
            VTable element = this.classVtable.arrayElementClass;
            return (element != null) ? element.vtableType : null;
        }

        [NoHeapAllocation]
        protected override bool HasElementTypeImpl()
        {
            return (IsArray);
        }

        // Return the underlying Type that represents the IReflect Object.  For expando object,
        // this is the (Object) IReflectInstance.GetType().  For Type object it is this.
        public override Type UnderlyingSystemType {
            [NoHeapAllocation]
            get {return this;}
        }

        //
        // ICloneable Implementation
        //

        // RuntimeType's are unique in the system, so the only thing that we should do to clone them is
        // return the current instance.
        public Object Clone() {
            return this;
        }

        [NoHeapAllocation]
        public override bool IsSubclassOf(Type c) {
            Type p = this;
            if (p == c) {
                return false;
            }
            while (p != null) {
                if (p == c) {
                    return true;
                }
                p = p.BaseType;
            }
            return false;
        }

        [NoHeapAllocation]
        internal override TypeCode GetTypeCodeInternal()
        {
            switch (classVtable.structuralView) {
                case StructuralType.Bool:
                    return TypeCode.Boolean;
                case StructuralType.Char:
                    return TypeCode.Object;
                case StructuralType.Int8:
                    return TypeCode.SByte;
                case StructuralType.Int16:
                    return TypeCode.Int16;
                case StructuralType.Int32:
                    return TypeCode.Int32;
                case StructuralType.Int64:
                    return TypeCode.Int64;
                case StructuralType.UInt8:
                    return TypeCode.Byte;
                case StructuralType.UInt16:
                    return TypeCode.UInt16;
                case StructuralType.UInt32:
                    return TypeCode.UInt32;
                case StructuralType.UInt64:
                    return TypeCode.UInt64;
                case StructuralType.Float32:
                    return TypeCode.Single;
                case StructuralType.Float64:
                    return TypeCode.Double;
                default:
                    return TypeCode.Object;
            }
        }

        // This is a cache for the corresponding system-wide type
        private SystemType systemType;

        public override SystemType GetSystemType() {
            if (SystemType.IsNull(this.systemType)) {
                // initialize it
                if (this.baseType == null) {
                    this.systemType = SystemType.RootSystemType();
                }
                else {
                    SystemType baseSt = this.baseType.GetSystemType();
                    long lower, upper;
                    string fullname = this.FullName;
                    // for now compute an MD5 over the full name

#if SINGULARITY_PROCESS                    
                    unsafe {
                        byte[] nameArray =
                            RuntimeTypeHash.ComputeHashAndReturnName(fullname,
                                                                     out lower,
                                                                     out upper);
                        fixed(byte* dataptr = &nameArray[0]) {
                            char* name = (char*)dataptr;
                            this.systemType = SystemType.Register(name,
                                                                  fullname.Length,
                                                                  lower, upper, baseSt);
                        }
                    }
#else
                    RuntimeTypeHash.ComputeHash(fullname, out lower, out upper);
                    this.systemType = SystemType.Register(fullname,
                                                          lower, 
                                                          upper,
                                                          baseSt);
#endif
                }
            }
            return this.systemType;
        }

    }


    // Pulled these methods out of the main class in order to give
    // the IoSystem access to the ComputeHash function when prebinding endpoints
    public class RuntimeTypeHash
    {
        public static void ComputeHash(string fullname,
                                       out long lower,
                                       out long upper)
        {
            ComputeHashAndReturnName(fullname, out lower, out upper);
        }

        unsafe internal static byte[] ComputeHashAndReturnName(string fullname,
                                                               out long lower,
                                                               out long upper)
        {
            byte[] data = new byte[fullname.Length*sizeof(char)];
            String.InternalCopy(fullname, data, fullname.Length);
            ComputeHash(data, out lower, out upper);
            return data;
        }
        ///
        ///  Needs to be replaced with real hash computed over the
        ///  signature of the type
        ///
        unsafe internal static void ComputeHash(byte[] fullname,
                                              out long lower,
                                              out long upper)
        {
            byte[] md5 = new Microsoft.Singularity.Crypto.MD5().Hash(fullname);

            lower = ConvertToLong(md5, 0);
            upper = ConvertToLong(md5, 8);
        }

        private static long ConvertToLong(byte[] data, int start) {
            long result = data[start];
            for (int i=1; i<8; i++) {
                result = result << 8;
                result |= data[start+i];
            }
            return result;
        }

    }
}
