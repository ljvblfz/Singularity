//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

namespace System.Runtime.CompilerServices {

  // phx whidbey

  // Indicates that the modified type is const (i.e. has a const modifier)
  public class IsConst
  {
  }
  public class IsImplicitlyDereferenced
  {
  }
  public class IsSignUnspecifiedByte
  {
  }
  public class IsLong
  {
  }
  public class IsBoxed
  {
  }
  public class UnsafeValueTypeAttribute : Attribute
  {
  }
  public class FixedAddressValueTypeAttribute : Attribute
  {
  }

  // end phx whidbey

  [AttributeUsage(AttributeTargets.Constructor|
                  AttributeTargets.Method)]
  internal sealed class InlineAttribute: Attribute {

  }

  [AttributeUsage(AttributeTargets.Constructor|
                  AttributeTargets.Method)]
  internal sealed class NoInlineAttribute: Attribute {

  }

  [AttributeUsage(AttributeTargets.Struct)]
  internal sealed class InlineCopyAttribute: Attribute {
  }

  [AttributeUsage(AttributeTargets.Constructor|
                  AttributeTargets.Method)]
  internal sealed class DisableBoundsChecksAttribute: Attribute {

  }

  [AttributeUsage(AttributeTargets.Constructor|
                  AttributeTargets.Method)]
  internal sealed class DisableNullChecksAttribute: Attribute {

  }

  [AttributeUsage(AttributeTargets.Constructor|
                  AttributeTargets.Method)]
  internal sealed class InlineIntoOnceAttribute: Attribute {
  }

  [AttributeUsage(AttributeTargets.Interface|
                  AttributeTargets.Class|
                  AttributeTargets.Struct,
                  Inherited=false)]
  public sealed class CCtorIsRunDuringStartupAttribute : Attribute {
  }

  [AttributeUsage(AttributeTargets.Interface|
                  AttributeTargets.Class|
                  AttributeTargets.Struct,
                  Inherited=false)]
  public sealed class NoCCtorAttribute : Attribute {
  }

  [AttributeUsage(AttributeTargets.Constructor|
                  AttributeTargets.Method)]
  public sealed class NoHeapAllocationAttribute : Attribute {
  }

  [AttributeUsage(AttributeTargets.Class|
                  AttributeTargets.Struct|
                  AttributeTargets.Interface|
                  AttributeTargets.Method|
                  AttributeTargets.Constructor|
                  AttributeTargets.Field,
                  Inherited=false)]
  [RequiredByBartok]
  internal sealed class AccessedByRuntimeAttribute: Attribute {
      public AccessedByRuntimeAttribute(string reason) {
          this.reason = reason;
      }

      public int Option {
          get { return option; }
          set { option = value; }
      }
      int option;
      string reason;
  }

  [AttributeUsage(AttributeTargets.Method|
                  AttributeTargets.Constructor|
                  AttributeTargets.Field,
                  Inherited=false)]
  public sealed class ProvidedByOverrideAttribute: Attribute {
  }

  [AttributeUsage(AttributeTargets.Field)]
  public sealed class ExternalStaticDataAttribute : Attribute {
  }

  [AttributeUsage(AttributeTargets.Struct)]
  public sealed class StructAlignAttribute : Attribute {
      public StructAlignAttribute(int align) {}
  }

  [AttributeUsage(AttributeTargets.Method,
                  Inherited=false)]
  public sealed class StackBoundAttribute: Attribute {
      public StackBoundAttribute(int bound) {}
  }

  [AttributeUsage(AttributeTargets.Method|
                  AttributeTargets.Constructor,
                  Inherited=false)]
  public sealed class StackLinkCheckAttribute: Attribute {
  }

  [AttributeUsage(AttributeTargets.Method|
                  AttributeTargets.Constructor,
                  Inherited=false)]
  public sealed class RequireStackLinkAttribute: Attribute {
  }

  [AttributeUsage(AttributeTargets.Method|
                  AttributeTargets.Constructor,
                  Inherited=false)]
  public sealed class NoStackLinkCheckAttribute: Attribute {
  }

  [AttributeUsage(AttributeTargets.Method|
                  AttributeTargets.Constructor,
                  Inherited=false)]
  public sealed class NoStackLinkCheckTransAttribute: Attribute {
  }

  [AttributeUsage(AttributeTargets.Method|
                  AttributeTargets.Constructor,
                  Inherited=false)]
  public sealed class NoStackOverflowCheckAttribute: Attribute {
  }

  [AttributeUsage(AttributeTargets.Field|
                  AttributeTargets.Method|
                  AttributeTargets.Constructor,
                  Inherited=false)]
  public sealed class IntrinsicAttribute: Attribute {
      // Intrinsics should never have bodies.  However, csc complains if we
      // mark a property on a struct extern, so we have to give those bodies.
      // Hence this flag.  IgnoreBody=true means discard the body.
      // IgnoreBody=false means it is an error to supply a body.
      public bool IgnoreBody {
          get { return ignoreBody; }
          set { ignoreBody = value; }
      }
      bool ignoreBody;
  }

  [AttributeUsage(AttributeTargets.Field)]
  internal sealed class InlineVectorAttribute : Attribute {
      public InlineVectorAttribute(int numElements) {}
  }

  // This attribute is used to mark method that needs pushStackMark
  // and popStackMark around calls to it.
  [AttributeUsage(AttributeTargets.Method,
                  Inherited=false)]
  [RequiredByBartok]
  public sealed class OutsideGCDomainAttribute: Attribute {
  }

  // This attribute is used to mark method that needs enterGCSafteState
  // and leaveGCSafeState around its definition
  [AttributeUsage(AttributeTargets.Method,
                  Inherited=false)]
  public sealed class ExternalEntryPointAttribute: Attribute {
      public int Option {
          get { return option; }
          set { option = value; }
      }
      public int IgnoreCallerTransition {
          get { return ignoreCallerTransition; }
          set { option = value; }
      }
      int option, ignoreCallerTransition;
  }

  // This attribute is used to mark type/field/methods that are required
  // by Bartok compiler.
  [AttributeUsage(AttributeTargets.Class |
                  AttributeTargets.Struct |
                  AttributeTargets.Enum |
                  AttributeTargets.Interface |
                  AttributeTargets.Delegate |
                  AttributeTargets.Method |
                  AttributeTargets.Constructor |
                  AttributeTargets.Field,
                  Inherited=false)]
  public sealed class RequiredByBartokAttribute: Attribute {
      public RequiredByBartokAttribute() {}
      public RequiredByBartokAttribute(string reason) {
          this.reason = reason;
      }
      string reason;
  }

  /// <summary>
  /// This attribute must be placed on override types that override the class
  /// constructor.  It is a compile-time error if the attribute is missing
  /// during an override.  It is also a compile-time error if it exists and
  /// either the original or the override type does not have a class
  /// constructor.
  /// </summary>
  [AttributeUsage(AttributeTargets.Class|
                  AttributeTargets.Struct|
                  AttributeTargets.Interface)]
  internal sealed class OverrideCctorAttribute : Attribute {
  }

  /// <summary>
  /// This attribute must be placed on override types that mean to override the
  /// base class.  If a base class is overridden, then either this attribute or
  /// IgnoreOverrideExtendsAttribute must be present.  It is also a compile-time
  /// error if this attribute exists and the override base class is the same as
  /// the original base class.
  /// </summary>
  [AttributeUsage(AttributeTargets.Class)]
  internal sealed class OverrideExtendsAttribute : Attribute {
  }

  /// <summary>
  /// This attribute must be placed on override types that override the base
  /// class in the override assembly but do not mean to override the base class
  /// in the actual type.  If a base class is overridden, then either this
  /// attribute or OverrideExtendsAttribute must be present.  It is also a
  /// compile-time error if this attribute exists and the override base class is
  /// the same as the original base class.
  /// </summary>
  [AttributeUsage(AttributeTargets.Class)]
  internal sealed class IgnoreOverrideExtendsAttribute : Attribute {
  }

  /// <summary>
  /// This attribute is placed on override types to delete the built-in class
  /// constructor.  Using this is better than overriding with an empty method.
  /// </summary>
  [AttributeUsage(AttributeTargets.Class|
                  AttributeTargets.Struct|
                  AttributeTargets.Interface)]
  internal sealed class DeleteCctorAttribute : Attribute {
  }

  [AttributeUsage(AttributeTargets.Struct)]
  public sealed class OverrideLayoutAttribute : Attribute {
  }

  // There are at least three reasons why one would need to prevent
  // the automatic insertion of vanilla reference counting (RC) code
  // into the body of a method, property or constructor:
  //
  //     1. To suppress reference counting before a reference to
  //        the installed GC is set up.
  //
  //     2. Methods that directly manipulate reference counts such
  //        as allocation routines.
  //
  //     3. To suppress the insertion of RC code into code bodies
  //        that may be directly or indirectly invoked from the
  //        IncrementRefCount or DecrementRefCount methods of the
  //        reference counting collector.
  //
  // The IrRCUpdate compiler phase can be made to skip code bodies for
  // any of the above reasons by affixing one of two special attributes
  // to their declarations. Currently, the [PreInitRefCounts] attribute
  // is used to mark code that could be invoked before the GC gets set
  // up and that, in its absence, may cause the IrRCUpdate phase to
  // insert RC increment and decrement code. The [ManualRefCounts]
  // attribute models cases in which the code writer takes the onus of
  // maintaining consistent reference counts.
  //
  // The reason for separating the preinitialization case from the
  // other two is because special RC updates, which test for
  // initialization of the GC before incrementing or decrementing the
  // reference counts, could still have been inserted into code bodies
  // marked as [PreInitRefCounts]. However, if the same code body is
  // called after initialization, such updates may slow down the
  // common case. This provides an optimization opportunity for the
  // compiler in which a method f marked with [PreInitRefCounts] could
  // be cloned into a version f' that contains plain RC code and that
  // is actually called wherever a non-[PreInitRefCounts] method such
  // as g calls f.
  //
  // If a method h has the [ManualRefCounts] attribute and if reference
  // counts are directly read or written in h, then the code must either
  // be also marked as [NoInline] or must only be called from methods
  // that also have the [ManualRefCounts] attribute. This is because if
  // h were inlined into a method in which reference counting is on by
  // default, the injected RC code may cause the reference counts
  // to become inconsistent.
  [AttributeUsage(AttributeTargets.Method|
                  AttributeTargets.Constructor)]
  [RequiredByBartok]
  internal sealed class PreInitRefCountsAttribute: Attribute {
  }
  [AttributeUsage(AttributeTargets.Method|
                  AttributeTargets.Constructor)]
  [RequiredByBartok]
  internal sealed class ManualRefCountsAttribute: Attribute {
  }
  // This marks classes that are acyclic reference types.
  // (See IrAcyclicRefTypes.cs.)
  [AttributeUsage(AttributeTargets.Class)]
  [RequiredByBartok]
  internal sealed class AcyclicRefTypeAttribute: Attribute {
  }

  [AttributeUsage(AttributeTargets.Field)]
  internal sealed class TrustedNonNullAttribute: Attribute {
  }
}
