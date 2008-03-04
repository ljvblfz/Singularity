// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
//
// Note: These routines assume that the processor supports
// 32-bit aligned accesses.  Migration to non-x86 platforms will
// need to examine and tune the implementations accordingly.
//

#define USE_EXTERNAL_MEMORY_OPERATIONS

namespace System {

    using System;
    using System.Runtime.CompilerServices;
    //| <include path='docs/doc[@for="Buffer"]/*' />

    [NoCCtor]
    [CLSCompliant(false)]
    public sealed class Buffer
    {
        private Buffer() {
        }

        // This is a replacement for the memmove intrinsic.
        // It performs better than the CRT one and the inline version
        // originally from Lightning\Src\VM\COMSystem.cpp
        [NoHeapAllocation]
        public static unsafe void MoveMemory(byte* dmem, byte* smem, UIntPtr size)
        {
            MoveMemory(dmem, smem, (int)size);
        }

#if USE_EXTERNAL_MEMORY_OPERATIONS
        [AccessedByRuntime("output to header: defined in c++")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(64)]
        [NoHeapAllocation]
        public static unsafe extern void MoveMemory(byte* dmem, byte* smem, int size);
#else // !USE_EXTERNAL_MEMORY_OPERATIONS
        [NoHeapAllocation]
        public static unsafe void MoveMemory(byte* dmem, byte* smem, int size)
        {
            if (dmem <= smem) {
                // make sure the destination is dword aligned
                while ((((int)dmem ) & 0x3) != 0 && size >= 3) {
                    *dmem++ = *smem++;
                    size -= 1;
                }

                // copy 16 bytes at a time
                if (size >= 16) {
                    size -= 16;
                    do {
                        ((int *)dmem)[0] = ((int *)smem)[0];
                        ((int *)dmem)[1] = ((int *)smem)[1];
                        ((int *)dmem)[2] = ((int *)smem)[2];
                        ((int *)dmem)[3] = ((int *)smem)[3];
                        dmem += 16;
                        smem += 16;
                    }
                    while ((size -= 16) >= 0);
                }

                // still 8 bytes or more left to copy?
                if ((size & 8) != 0) {
                    ((int *)dmem)[0] = ((int *)smem)[0];
                    ((int *)dmem)[1] = ((int *)smem)[1];
                    dmem += 8;
                    smem += 8;
                }

                // still 4 bytes or more left to copy?
                if ((size & 4) != 0) {
                    ((int *)dmem)[0] = ((int *)smem)[0];
                    dmem += 4;
                    smem += 4;
                }

                // still 2 bytes or more left to copy?
                if ((size & 2) != 0) {
                    ((short *)dmem)[0] = ((short *)smem)[0];
                    dmem += 2;
                    smem += 2;
                }

                // still 1 byte left to copy?
                if ((size & 1) != 0) {
                    dmem[0] = smem[0];
                    dmem += 1;
                    smem += 1;
                }
            } else {
                smem += size;
                dmem += size;

                // make sure the destination is dword aligned
                while ((((int)dmem) & 0x3) != 0 && size >= 3) {
                    *--dmem = *--smem;
                    size -= 1;
                }

                // copy 16 bytes at a time
                if (size >= 16) {
                    size -= 16;
                    do {
                        dmem -= 16;
                        smem -= 16;
                        ((int *)dmem)[3] = ((int *)smem)[3];
                        ((int *)dmem)[2] = ((int *)smem)[2];
                        ((int *)dmem)[1] = ((int *)smem)[1];
                        ((int *)dmem)[0] = ((int *)smem)[0];
                    }
                    while ((size -= 16) >= 0);
                }

                // still 8 bytes or more left to copy?
                if ((size & 8) != 0) {
                    dmem -= 8;
                    smem -= 8;
                    ((int *)dmem)[1] = ((int *)smem)[1];
                    ((int *)dmem)[0] = ((int *)smem)[0];
                }

                // still 4 bytes or more left to copy?
                if ((size & 4) != 0) {
                    dmem -= 4;
                    smem -= 4;
                    ((int *)dmem)[0] = ((int *)smem)[0];
                }

                // still 2 bytes or more left to copy?
                if ((size & 2) != 0) {
                    dmem -= 2;
                    smem -= 2;
                    ((short *)dmem)[0] = ((short *)smem)[0];
                }

                // still 1 byte left to copy?
                if ((size & 1) != 0) {
                    dmem -= 1;
                    smem -= 1;
                    dmem[0] = smem[0];
                }
            }
        }
#endif // !USE_EXTERNAL_MEMORY_OPERATIONS

        // Copies from one primitive array to another primitive array without
        // respecting types.  This calls memmove internally.
        //| <include path='docs/doc[@for="Buffer.BlockCopy"]/*' />
        public static void BlockCopy(Array src, int srcOffset,
                                     Array dst, int dstOffset, int count) {
            if (src == null) {
                throw new ArgumentNullException("src");
            }
            if (dst == null) {
                throw new ArgumentNullException("dst");
            }
            InternalBlockCopy(src, srcOffset, dst, dstOffset, count);
        }

        // A very simple and efficient array copy that assumes all of the
        // parameter validation has already been done.  All counts here are
        // in bytes.
        internal static unsafe void InternalBlockCopy(Array src, int srcOffset,
                                                      Array dst, int dstOffset,
                                                      int count) {
            VTable.Assert(src != null);
            VTable.Assert(dst != null);

            // Unfortunately, we must do a check to make sure we're writing
            // within the bounds of the array.  This will ensure that we don't
            // overwrite memory elsewhere in the system nor do we write out junk.
            // This can happen if multiple threads screw with our IO classes
            // simultaneously without being threadsafe.  Throw here.  -- Brian
            // Grunkemeyer, 5/9/2001
            int srcLen = src.Length * src.vtable.arrayElementSize;
            if (srcOffset < 0 || dstOffset < 0 || count < 0 ||
                srcOffset > srcLen - count)
                throw new IndexOutOfRangeException
                    ("IndexOutOfRange_IORaceCondition");
            if (src == dst) {
                if (dstOffset > srcLen - count)
                    throw new IndexOutOfRangeException
                        ("IndexOutOfRange_IORaceCondition");
            } else {
                int dstLen = dst.Length * dst.vtable.arrayElementSize;
                if (dstOffset > dstLen - count)
                    throw new IndexOutOfRangeException
                        ("IndexOutOfRange_IORaceCondition");
            }

            // Copy the data.
            // Call our faster version of memmove, not the CRT one.
            fixed (int *srcFieldPtr = &src.field1) {
                fixed (int *dstFieldPtr = &dst.field1) {
                    byte *srcPtr = (byte *)
                        src.GetFirstElementAddress(srcFieldPtr);
                    byte *dstPtr = (byte *)
                        dst.GetFirstElementAddress(dstFieldPtr);
                    MoveMemory(dstPtr + dstOffset, srcPtr + srcOffset, count);
                }
            }
        }

        [NoHeapAllocation]
        internal static unsafe void ZeroMemory(byte* dst, UIntPtr len)
        {
            ZeroMemory(dst, (int)len);
        }

#if USE_EXTERNAL_MEMORY_OPERATIONS
        [AccessedByRuntime("output to header: defined in c++")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(64)]
        [NoHeapAllocation]
        public static unsafe extern void ZeroMemory(byte* dst, int len);
#else // !USE_EXTERNAL_MEMORY_OPERATIONS
        [NoHeapAllocation]
        public static unsafe void ZeroMemory(byte* dst, int len)
        {
            // This is based on Peter Sollich's faster memcpy implementation,
            // from COMString.cpp.
            while ( (((int)dst) & 0x03) != 0 && len >= 3 ) {
                *dst = 0;
                len -= 1;
            }

            if (len >= 16) {
                len -= 16;
                do {
                    ((int*)dst)[0] = 0;
                    ((int*)dst)[1] = 0;
                    ((int*)dst)[2] = 0;
                    ((int*)dst)[3] = 0;
                    dst += 16;
                } while ((len -= 16) >= 0);
            }
            if ((len & 8) > 0) {
                ((int*)dst)[0] = 0;
                ((int*)dst)[1] = 0;
                dst += 8;
            }
            if ((len & 4) > 0) {
                ((int*)dst)[0] = 0;
                dst += 4;
            }
            if ((len & 2) != 0) {
                ((short*)dst)[0] = 0;
                dst += 2;
            }
            if ((len & 1) != 0)
                *dst++ = 0;
        }
#endif // !USE_EXTERNAL_MEMORY_OPERATIONS

        [AccessedByRuntime("output to header: defined in asm")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static unsafe extern void ZeroPages(byte* dst, int len);

        [AccessedByRuntime("output to header: defined in asm")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static unsafe extern void CopyPages(byte* dst, byte *src, int len);

        // Gets a particular byte out of the array.  The array must be an
        // array of primitives.
        //
        // This essentially does the following:
        // return ((byte*)array) + index.
        //
        //| <include path='docs/doc[@for="Buffer.GetByte"]/*' />
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static extern byte GetByte(Array array, int index);

        // Sets a particular byte in an the array.  The array must be an
        // array of primitives.
        //
        // This essentially does the following:
        // *(((byte*)array) + index) = value.
        //
        //| <include path='docs/doc[@for="Buffer.SetByte"]/*' />
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static extern void SetByte(Array array, int index, byte value);

        // Gets a particular byte out of the array.  The array must be an
        // array of primitives.
        //
        // This essentially does the following:
        // return array.length * sizeof(array.UnderlyingElementType).
        //
        //| <include path='docs/doc[@for="Buffer.ByteLength"]/*' />
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static extern int ByteLength(Array array);
    }
}
