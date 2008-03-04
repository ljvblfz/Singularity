;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;
;;  Microsoft Research Singularity
;;
;;  Copyright (c) Microsoft Corporation.  All rights reserved.
;;
;;  File:   halidt.asm
;;
;;  Note:
;;

.686p
.mmx
.xmm
.model flat
.code

assume ds:flat
assume es:flat
assume ss:flat
assume fs:nothing
assume gs:nothing
        
include hal.inc

DEBUG_INTERRUPTS EQU 0
ifdef DEBUG
SPINLOCK_RELEASE_SANITY_CHECK EQU 1
else        
SPINLOCK_RELEASE_SANITY_CHECK EQU 0
endif

ifdef PAGING
public _SysEnter
endif        

externdef ?c_exceptionHandler@@3P6IXHPAUStruct_Microsoft_Singularity_X86_ThreadContext@@@ZA:uintPtr
; static void __fastcall Class.Microsoft.SingularityIoSystem.DispatchException(
;       int interrupt [ECX], ref ThreadContex [EDX]))
; __fastcall:   
;       Arg0 passed in ECX
;       Arg1 passed in EDX
;       Others passed right to left on stack.
;       EBX, ESI, EDI, and EBP are callee saved.
;       EAX receives return value if any.

public _EdtEnter0
public _EdtEnter1
public _EdtEnterBody
        
externdef ?c_interruptHandler@@3P6IXHPAUStruct_Microsoft_Singularity_X86_ThreadContext@@@ZA:uintPtr
; static void __fastcall Class.Microsoft.SingularityIoSystem.DispatchInterrupt(
;       int interrupt [ECX], ref ThreadContex [EDX]))
; __fastcall:   
;       Arg0 passed in ECX
;       Arg1 passed in EDX
;       Others passed right to left on stack.
;       EBX, ESI, EDI, and EBP are callee saved.
;       EAX receives return value if any.

public _IdtEnter20
public _IdtEnter21
public _IdtEnterBody
                
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;; 
;;; The IDT_ENTER building macros insure that each IDT target has
;;; an offset of form (_IdtEnter0 or _EdtEnter0) + 0x10 * interrupt_number.
;;;
IDT_ENTER_IN STRUCT 1
        _esi            UINT32          ?
        _num            UINT32          ?
        _err            UINT32          ?
        _eip            UINT32          ?
        _cs0            UINT32          ?
        _efl            UINT32          ?
IDT_ENTER_IN ENDS

ifdef PAGING
IDT_ENTER_IN_LARGE STRUCT 1
        _esi            UINT32          ?
        _num            UINT32          ?
        _err            UINT32          ?
        _eip            UINT32          ?
        _cs0            UINT32          ?
        _efl            UINT32          ?
        _esp            UINT32          ?
        _ss             UINT32          ?
IDT_ENTER_IN_LARGE ENDS
endif        

IDT_ENTER_OUT STRUCT 1
        _esi            UINT32          ?
        _eip            UINT32          ?
        _cs0            UINT32          ?
        _efl            UINT32          ?
IDT_ENTER_OUT ENDS

ifdef PAGING
IDT_ENTER_OUT_LARGE STRUCT 1
        _esi            UINT32          ?
        _eip            UINT32          ?
        _cs0            UINT32          ?
        _efl            UINT32          ?
        _esp            UINT32          ?
        _ss             UINT32          ?
IDT_ENTER_OUT_LARGE ENDS
endif        

EDT_ENTER_CLEAN MACRO num
        push    0                                       ; No error
        push    num
        jmp     _EdtEnterBody
        align   16
ENDM

EDT_ENTER_ERR   MACRO num
        push    num
        jmp     _EdtEnterBody
        align   16
ENDM    
        
IDT_ENTER_CLEAN MACRO num
        push    0                                       ; No error
        push    num
        jmp     _IdtEnterBody
        align   16
ENDM
       
IDT_SAVE_CONTEXT MACRO fxregs, error, dregs
        ;; Save the processor's thread context.
        ;; Mark that the context contains caller-saved registers as well.
        ;; Input:
        ;;      ESI = address of ThreadContext structure.
        ;;      ESP = bottom of IDT_ENTER_IN context:
        ;;              ESP[ 0] = esi
        ;;              ESP[ 4] = num
        ;;              ESP[ 8] = err
        ;;              ESP[12] = eip
        ;;              ESP[16] = cs
        ;;              ESP[20] = efl

        ;; XXX - if we came from ring 3 then stack also has SS and ESP
        ;;              ESP[24] = esp
        ;;              ESP[28] = ss 
        ;; This code needs to check cs&3 and if non-zero save ss:esp to thread context
        
        ;; Output:      
        ;;      ESI = address of ThreadContext structure.
        ;;      ESP = stack w/o IDT_ENTER_IN context
        ;;      ECX = num
        ;;
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._eax, eax
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._ebx, ebx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._ecx, ecx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._edx, edx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._ebp, ebp
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._edi, edi
        mov     eax, cr3
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._cr3, eax
        
        mov     eax, [esp].IDT_ENTER_IN._esi
        mov     ebx, [esp].IDT_ENTER_IN._eip
        mov     ecx, [esp].IDT_ENTER_IN._cs0
        mov     edx, [esp].IDT_ENTER_IN._efl

        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._esi, eax
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._eip, ebx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._cs0, ecx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._efl, edx

if dregs     
        mov     eax, dr0
        mov     ebx, dr1
        mov     ecx, dr2
        mov     edx, dr3
        
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr0, eax
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr1, ebx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr2, ecx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr3, edx

        mov     eax, dr6
        mov     ebx, dr7
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr6, eax
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr7, ebx
        
        xor     eax, eax
        mov     dr6, eax
endif        
        
        mov     ecx, [esp].IDT_ENTER_IN._num            ;  this flows through
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._num, cx

if error        
        mov     ebx, [esp].IDT_ENTER_IN._err
        mov     eax, cr2
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._err, ebx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._cr2, eax
endif
                
if fxregs
if dregs        
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._regs, 3
else        
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._regs, 1
endif        
        fxsave  [esi].Struct_Microsoft_Singularity_X86_ThreadContext._mmx
        fninit
        mov     eax, 37eh
        push    eax
        fldcw   [esp]
        pop     eax
else
if dregs        
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._regs, 2
else
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._regs, 0
endif                
endif

ifdef PAGING
        ;; Is it a small or large frame?
        mov     eax, [esp].IDT_ENTER_IN._cs0
        and     eax, 3
        jz      s_case_0

        ;; Case ring-3: IDT_ENTER_IN_LARGE
        mov     eax, [esp].IDT_ENTER_IN_LARGE._esp
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._esp, eax
        add     esp, SIZEOF IDT_ENTER_IN_LARGE
        jmp s_case_0_3

s_case_0:
endif        
        ;; Case ring-0: IDT_ENTER_IN
        add     esp, SIZEOF IDT_ENTER_IN
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._esp, esp

ifdef PAGING
s_case_0_3:
endif
                
ENDM        
                 
IDT_LOAD_CONTEXT MACRO dregs
        ;; Create the outgoing stack frame.
ifdef PAGING        
        ;; Is it a small or large frame?
        mov     eax, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._cs0
        and     eax, 3
        jz      l_case_0

        ;; Case ring-3: push an IDT_ENTER_OUT_LARGE
        sub     esp, SIZEOF IDT_ENTER_OUT_LARGE
        mov     eax, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._esp
        mov     [esp].IDT_ENTER_OUT_LARGE._esp, eax
        mov     eax, Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtUD - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull + 3
        mov     [esp].IDT_ENTER_OUT_LARGE._ss, eax
        mov     es, ax
        ;; For the moment, share UF and PF:
        mov     eax, Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPF - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull + 3
        mov     fs, ax
        jmp     l_cases_0_3

l_case_0:
endif ;; PAGING        
        ;; Case ring-0: push an IDT_ENTER_OUT
        mov     esp, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._esp
        sub     esp, SIZEOF IDT_ENTER_OUT

ifdef PAGING        
l_cases_0_3:
        ;; Code common to ring-0 and ring-3
endif ;; PAGING        

        ;; Check if we need to restore the floating-point registers
        test    [esi].Struct_Microsoft_Singularity_X86_ThreadContext._regs, 1
        jz      skip_fxrstor
        fxrstor [esi].Struct_Microsoft_Singularity_X86_ThreadContext._mmx
skip_fxrstor:

if dregs        
        ;; Check if we need to restore the debug registers
        test    [esi].Struct_Microsoft_Singularity_X86_ThreadContext._regs, 2
        jz      skip_drstor
        
        mov     eax, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr0
        mov     ebx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr1
        mov     ecx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr2
        mov     edx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr3
        
        mov     dr0, eax
        mov     dr1, ebx
        mov     dr2, ecx
        mov     dr3, edx
        
        mov     eax, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr6
        mov     ebx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr7
        
        mov     dr6, eax
        mov     dr7, ebx
skip_drstor:
endif        

ifdef PAGING
        ;; Zero for cr3 in the ThreadContext means "don't care" (no paging)
        mov     eax, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._cr3
        cmp     eax, 0
        je      skip_cr3

        ;; Avoid TLB flushes if possible
        mov     ebx, CR3
        cmp     eax, ebx
        je      skip_cr3

        mov     CR3, eax
        
skip_cr3:       
endif ;; PAGING
                        
        ;; Restore the registers
        mov     eax, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._esi
        mov     ebx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._eip
        mov     ecx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._cs0
        mov     edx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._efl
        
        mov     [esp].IDT_ENTER_OUT._esi, eax
        mov     [esp].IDT_ENTER_OUT._eip, ebx
        mov     [esp].IDT_ENTER_OUT._cs0, ecx
        mov     [esp].IDT_ENTER_OUT._efl, edx
        
        mov     eax, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._eax
        mov     ebx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._ebx
        mov     ecx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._ecx
        mov     edx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._edx
        mov     ebp, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._ebp
        mov     edi, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._edi
        pop     esi
ifdef PAGING
        push    es
        pop     ds
endif        
ENDM        

DBG_EDX_FROM_EAX MACRO edxval, eaxval
if DEBUG_INTERRUPTS
        mov     edx, edxval
        mov     eax, eaxval
        mov     [edx], eax
endif
ENDM        

DBG_SCREEN_AS_VALUE MACRO edxval, eaxval
if DEBUG_INTERRUPTS
        push    eax
        push    edx
        DBG_EDX_FROM_EAX edxval, eaxval
        pop     edx
        pop     eax
endif
ENDM        
        
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;; 
ifdef DEBUG
?g_TestSave@Class_Microsoft_Singularity_Processor@@SIXPAUStruct_Microsoft_Singularity_X86_ThreadContext@@@Z proc
        pop eax
        push eax
        pushad

        mov ebx, 0fffffeb0h
        mov edx, 0fffffed0h
        mov ebp, 0fffffeb1h
        mov edi, 0fffffed1h
        mov esi, 0fffffef1h
        
        pushfd          ; _efl
        push cs         ; _cs0
        push eax        ; _eip
        push 0eeeh      ; _err
        push 0fffh      ; _num
        push esi        ; _esi
        
        mov esi, ecx
        IDT_SAVE_CONTEXT 1,1,1

        popad
        ret
?g_TestSave@Class_Microsoft_Singularity_Processor@@SIXPAUStruct_Microsoft_Singularity_X86_ThreadContext@@@Z endp

?g_TestSaveLoad@Class_Microsoft_Singularity_Processor@@SIXPAUStruct_Microsoft_Singularity_X86_ThreadContext@@@Z proc
        pop eax
        
        pushfd          ; _efl
        push cs         ; _cs0
        push eax        ; _eip
        push 0eeeh      ; _err
        push 0fffh      ; _num
        push esi        ; _esi
        
        mov esi, ecx
        IDT_SAVE_CONTEXT 1,1,1
        IDT_LOAD_CONTEXT 0
        iretd
        
?g_TestSaveLoad@Class_Microsoft_Singularity_Processor@@SIXPAUStruct_Microsoft_Singularity_X86_ThreadContext@@@Z endp
endif
                 
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;; Exceptions.
;;; 
        align 16
_EdtEnter proc
_EdtEnter0::             
        EDT_ENTER_CLEAN       000h                            ; #DE Divide-by-Zero
_EdtEnter1::
        EDT_ENTER_CLEAN       001h                            ; #DB Debug Exception
        EDT_ENTER_CLEAN       002h                            ; NMI Non-Maskable-Interrupt
        EDT_ENTER_CLEAN       003h                            ; #BP Breakpoint
        EDT_ENTER_CLEAN       004h                            ; #OF OVerflow
        EDT_ENTER_CLEAN       005h                            ; #BR Bound-Range
        EDT_ENTER_CLEAN       006h                            ; #UD Invalid Opcode
        EDT_ENTER_CLEAN       007h                            ; #NM Device Not Available
        EDT_ENTER_ERR         008h                            ; #DF Double Fault
        EDT_ENTER_CLEAN       009h                            ; Unused (was x87 segment except)
        EDT_ENTER_ERR         00ah                            ; #TS Invalid TSS
        EDT_ENTER_ERR         00bh                            ; #NP Sgement Not Present
        EDT_ENTER_ERR         00ch                            ; #SS Stack Exception
        EDT_ENTER_ERR         00dh                            ; #GP General Protection
        EDT_ENTER_ERR         00eh                            ; #PF Page Fault
        EDT_ENTER_CLEAN       00fh                            ; Reserved
        EDT_ENTER_CLEAN       010h                            ; #MF x87 Math Error
        EDT_ENTER_ERR         011h                            ; #AC Alignment Check
        EDT_ENTER_CLEAN       012h                            ; #MC Machine Check
        EDT_ENTER_CLEAN       013h                            ; #XF SIMD Exception
        EDT_ENTER_CLEAN       014h                            ; 014h exception
        EDT_ENTER_CLEAN       015h                            ; 015h exception
        EDT_ENTER_CLEAN       016h                            ; 016h exception
        EDT_ENTER_CLEAN       017h                            ; 017h exception
        EDT_ENTER_CLEAN       018h                            ; 018h exception
        EDT_ENTER_CLEAN       019h                            ; 019h exception
        EDT_ENTER_CLEAN       01ah                            ; 01ah exception
        EDT_ENTER_CLEAN       01bh                            ; 01bh exception
        EDT_ENTER_CLEAN       01ch                            ; 01ch exception
        EDT_ENTER_CLEAN       01dh                            ; 01dh exception
        EDT_ENTER_CLEAN       01eh                            ; 01eh exception
        EDT_ENTER_CLEAN       01fh                            ; 01fh exception

_EdtEnterBody::
        push    esi

ifdef PAGING
        push    ss
        pop     ds    ; Copy stack segment selector to DS so we can access memory!
        push    ss
        pop     es    ; Copy stack segment selector to ES so we can access memory!
endif        

        DBG_SCREEN_AS_VALUE 0b8020h, 01f301f40h ; @0

ifdef PAGING
        ; XXX if we've arrived from ring 3 FS will be invalid
        push    Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPF - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
        pop     fs
endif        

        ;; Exceptions spill to the per-processor exceptionContext.
        mov     esi, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._processorContext]
        lea     esi, [esi].Struct_Microsoft_Singularity_X86_ProcessorContext._exceptionContext

        IDT_SAVE_CONTEXT 1,1,1 ; Save fxregs, error codes, and dregs.


        ;; Link the per-processor exception to the faulting thread
        mov     edi, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]
        mov     eax, [edi].Struct_Microsoft_Singularity_X86_ThreadContext.__thread
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext.__thread, eax

        ;; Link the per-processor thread context to the original thread context.
        mov     [edi].Struct_Microsoft_Singularity_X86_ThreadContext._next, esi
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._prev, edi

        DBG_EDX_FROM_EAX 0b8024h, 01f311f40h ; @1
        
        ;; Switch to the exception stack and adjust the stack limit values.
        mov     esp, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._exceptionStackBegin]
        mov     eax, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._exceptionStackLimit]
        mov     edx, [edi].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit
        mov     [edi].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit, eax
        mov     fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._exceptionStackPreLimit], edx

        DBG_EDX_FROM_EAX 0b8028h, 01f321f40h ; @2
        
        ;; Call the exception handler.
        mov     edx, esi
        mov     eax, [?c_exceptionHandler@@3P6IXHPAUStruct_Microsoft_Singularity_X86_ThreadContext@@@ZA]
        call    eax

        DBG_EDX_FROM_EAX 0b802ch, 01f331f40h ; @3

        ;; mov     esi, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._processorContext]
        ;; lea     esi, [esi].Struct_Microsoft_Singularity_X86_ProcessorContext._exceptionContext

        ;; Restore the stack limit (edi should still be good)
        mov     eax, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._exceptionStackPreLimit]
        mov     [edi].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit, eax
        
        ;; Unlink the per-processor context.
        mov     edi, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._prev
        mov     [edi].Struct_Microsoft_Singularity_X86_ThreadContext._next, 0
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._prev, 0

        IDT_LOAD_CONTEXT 1
        
        iretd
        
_EdtEnter endp

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;; Interrupts.
;;; 

        align 16
_IdtEnter proc
_IdtEnter20::             
        IDT_ENTER_CLEAN       020h                            ; 021h: first interrupt
_IdtEnter21::   
        _num = 021h                                           ; 021h to 0ffh
        WHILE _num LE 0ffh
                IDT_ENTER_CLEAN       _num
                _num = _num + 1
        ENDM

_IdtEnterBody::
        push    esi

ifdef PAGING
        push    ss
        pop     ds    ; Copy stack segment selector to DS so we can access memory!
        push    ss
        pop     es    ; Copy stack segment selector to ES so we can access memory!
endif
        
        DBG_SCREEN_AS_VALUE 0b8000h, 01f301f40h ; @0

ifdef PAGING
        ; XXX if we've arrived from ring 3 FS will be invalid
        push    Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPF - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
        pop     fs
endif        
    
        ;; Interrupts spill to the thread's context.
        mov     esi, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]

        IDT_SAVE_CONTEXT 1,0,0 ; Save fxregs, but not error codes or dregs.


        DBG_EDX_FROM_EAX 0b8004h, 01f311f40h ; @1

        ;; Switch to the interrupt stack and adjust the stack limit values.
        mov     edi, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]
        mov     esp, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._interruptStackBegin]
        mov     eax, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._interruptStackLimit]
        mov     edx, [edi].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit
        mov     [edi].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit, eax
        mov     fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._interruptStackPreLimit], edx

        DBG_EDX_FROM_EAX 0b8008h, 01f321f40h ; @2

        ;; Call the interrupt handler.
        mov     edx, esi
        mov     eax, [?c_interruptHandler@@3P6IXHPAUStruct_Microsoft_Singularity_X86_ThreadContext@@@ZA]
        call    eax

        DBG_EDX_FROM_EAX 0b800ch, 01f331f40h ; @3

        ;; Restore the stack limit (edi should still be good)
        mov     eax, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._interruptStackPreLimit]
        mov     [edi].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit, eax
        
        ;; Select the processor's thread context (may have been changed by scheduler).
        mov     esi, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]

        IDT_LOAD_CONTEXT 0
        
        iretd
        
_IdtEnter endp

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;; Syscall via SYSENTER
;;; 
ifdef PAGING

SYSENTER_IN STRUCT 1
        _esi            UINT32          ?
        _num            UINT32          ?
SYSENTER_IN ENDS

SYSENTER_SAVE_CONTEXT MACRO fxregs, dregs
        ;; Save the processor's thread context.
        ;; Input:
        ;;      ESI = address of ThreadContext structure.
        ;;      EDX = orig ESP
        ;;      ECX = orig EIP
        ;; Output:      
        ;;      ESI = address of ThreadContext structure.
        ;;      ESP = stack w/o IDT_ENTER_IN context
        ;;      ECX = num
        ;;

        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._eip, ecx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._esp, edx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._cs0, Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtUD - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull + 3
        ;mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._ss,  05bh  ; XXX no room at the inn

        pushfd
        pop     edx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._efl, edx

        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._eax, eax
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._ebx, ebx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._ecx, ecx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._edx, edx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._ebp, ebp
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._edi, edi
        
        mov     eax, [esp].SYSENTER_IN._esi
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._esi, eax

        mov     ecx, [esp].SYSENTER_IN._num            ;  this flows through
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._num, cx
        
        ;; XXX Need to save segment registers!!

if dregs     
        mov     eax, dr0
        mov     ebx, dr1
        mov     ecx, dr2
        mov     edx, dr3
        
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr0, eax
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr1, ebx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr2, ecx
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr3, edx

        mov     eax, dr6
        mov     ebx, dr7
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr6, eax
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr7, ebx
        
        xor     eax, eax
        mov     dr6, eax
endif        
                        
if fxregs
if dregs        
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._regs, 3
else        
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._regs, 1
endif        
        fxsave  [esi].Struct_Microsoft_Singularity_X86_ThreadContext._mmx
        fninit
        mov     eax, 37eh
        push    eax
        fldcw   [esp]
        pop     eax
else
if dregs        
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._regs, 2
else
        mov     [esi].Struct_Microsoft_Singularity_X86_ThreadContext._regs, 0
endif                
endif
        ;; XXX this is wrong for the ring 3 case...
        add     esp, SIZEOF SYSENTER_IN

ENDM        
                 
SYSENTER_LOAD_CONTEXT MACRO dregs
        ;; Check if we need to restore the floating-point registers
        test    [esi].Struct_Microsoft_Singularity_X86_ThreadContext._regs, 1
        jz      skip_fxrstor
        fxrstor [esi].Struct_Microsoft_Singularity_X86_ThreadContext._mmx
skip_fxrstor:

if dregs        
        ;; Check if we need to restore the debug registers
        test    [esi].Struct_Microsoft_Singularity_X86_ThreadContext._regs, 2
        jz      skip_drstor
        
        mov     eax, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr0
        mov     ebx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr1
        mov     ecx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr2
        mov     edx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr3
        
        mov     dr0, eax
        mov     dr1, ebx
        mov     dr2, ecx
        mov     dr3, edx
        
        mov     eax, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr6
        mov     ebx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._dr7
        
        mov     dr6, eax
        mov     dr7, ebx
skip_drstor:
endif        
        
        ;; Restore the registers
        mov     eax, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._eax
        mov     ebx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._ebx
        mov     ecx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._ecx  ; pointless
        mov     edx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._edx  ; pointless
        mov     ebp, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._ebp
        mov     edi, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._edi

        ;; We restore EIP into edx and ESP into ECX
        mov     ecx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._esp
        mov     edx, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._eip


        ;; Need to restore segment registers

        ; Restore flags - is this necessary?
        push    [esi].Struct_Microsoft_Singularity_X86_ThreadContext._efl
        mov     esi, [esi].Struct_Microsoft_Singularity_X86_ThreadContext._esi
        popfd
ENDM        

        align 16
_SysEnter proc
        ; SYSENTER doesn't put *anything* on the stack - the only way 
        ; we can know the return EIP/ESP is if they are passed in registers.
        ; On Windows there is only one SYSENTER instruction in ntdll, so sysexit
        ; knows where to return to, and the ring 3 stack pointer is passed in edx.
        ;
        ; Entry state:   ss:esp0, cs:eip loaded from processor MSRs
        ; 
        push    0
        push    02Fh 
        push    esi     ; This is to make the stack frame look like IDT_ENTER_ expects

        push    ss
        pop     ds      ; Copy stack segment selector to DS so we can access memory!

        DBG_SCREEN_AS_VALUE 0b8000h, 01f301f40h ; @0

        ; XXX if we've arrived from ring 3 FS will be invalid
        push    Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPF - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
        pop     fs
        push    ss
        pop     es      ; Copy stack segment selector to ES so we can access memory!
    
        ;; Interrupts spill to the thread's context.
        mov     esi, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]

        ;; XXX - Can save some time by not saving caller-save regs 
        SYSENTER_SAVE_CONTEXT 1,0 ; Save fxregs, but not error codes or dregs.

        DBG_EDX_FROM_EAX 0b8004h, 01f311f40h ; @1

        ;; Switch to the interrupt stack and adjust the stack limit values.
        mov     edi, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]
        mov     esp, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._interruptStackBegin]
        mov     eax, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._interruptStackLimit]
        mov     edx, [edi].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit
        mov     [edi].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit, eax
        mov     fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._interruptStackPreLimit], edx

        DBG_EDX_FROM_EAX 0b8008h, 01f321f40h ; @2

        ;; Call the interrupt handler.
        mov     edx, esi
        mov     eax, [?c_interruptHandler@@3P6IXHPAUStruct_Microsoft_Singularity_X86_ThreadContext@@@ZA]
        call    eax

        DBG_EDX_FROM_EAX 0b800ch, 01f331f40h ; @3

        ;; Restore the stack limit (edi should still be good)
        mov     eax, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._interruptStackPreLimit]
        mov     [edi].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit, eax
        
        ;; Select the processor's thread context (may have been changed by scheduler).
        mov     esi, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]

        ; 
        SYSENTER_LOAD_CONTEXT 0
    
        ; SYSEXIT does the following
        ; EIP <-- EDX
        ; ESP <-- ECX
        ; CS  <-- MSR[IA32_CS_SYSENTER] + 0x18
        sysexit
        
_SysEnter endp

endif
        
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
; void Processor.SwitchToThreadContext(ref X86_ThreadContext newContext);
;   Saves the current context and load the new context from
;   newContext(ecx), effectively switching contexts between threads.
;   Always returns executing in the new context.
;   The code after _SwitchedInContextSwitch will only run if the context was
;   switch out using this routine.
;
; precondition: Scheduler.dispatchLock held
;
align 16
?g_SwitchToThreadContextNoGC@Class_Microsoft_Singularity_Processor@@SIXPAUStruct_Microsoft_Singularity_X86_ThreadContext@@@Z proc
        ;; Save the old processor context.

        pushfd
        
        ;; From here on we need interrupts disabled.
        cli
        
        mov     edx, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]
        
        ;; On clean switch, no need to save caller-saved registers (eax,ecx,edx,fxsave)
ifdef PAGING        
        mov     [edx].Struct_Microsoft_Singularity_X86_ThreadContext._cs0, Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPC - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
endif        
        mov     [edx].Struct_Microsoft_Singularity_X86_ThreadContext._ebx, ebx

        pop     eax                                     ; pop flags from stack.
        mov     [edx].Struct_Microsoft_Singularity_X86_ThreadContext._efl, eax
        
        mov     [edx].Struct_Microsoft_Singularity_X86_ThreadContext._esp, esp
        mov     [edx].Struct_Microsoft_Singularity_X86_ThreadContext._ebp, ebp
        mov     [edx].Struct_Microsoft_Singularity_X86_ThreadContext._esi, esi
        mov     [edx].Struct_Microsoft_Singularity_X86_ThreadContext._edi, edi
ifdef PAGING        
        mov     eax, CR3
        mov     [edx].Struct_Microsoft_Singularity_X86_ThreadContext._cr3, eax
endif        
        
        lea     eax, _SwitchedInContextSwitch
        mov     [edx].Struct_Microsoft_Singularity_X86_ThreadContext._eip, eax

ifdef THREAD_TIME_ACCOUNTING
        ;; thread execution time accouting
        ;; Get and save current timestamp
        mov     ebx, edx       ; save edx
        rdtsc                  ; -> edx:eax

        push    eax            ; save timestamp for later use
        push    edx

        ;; now - old.lastExecutionTimeUpdate
        sub     eax, dword ptr [ebx    ].Struct_Microsoft_Singularity_X86_ThreadContext._lastExecutionTimeUpdate
        sbb     edx, dword ptr [ebx + 4].Struct_Microsoft_Singularity_X86_ThreadContext._lastExecutionTimeUpdate

        ;; old.executionTime += now - old.lastExecutionTimeUpdate
        add     dword ptr [ebx    ].Struct_Microsoft_Singularity_X86_ThreadContext._executionTime, eax
        adc     dword ptr [ebx + 4].Struct_Microsoft_Singularity_X86_ThreadContext._executionTime, edx

        pop     edx            ; restore timestamp
        pop     eax

        ;; new.lastExecutionTimeUpdate = now
        mov     dword ptr [ecx    ].Struct_Microsoft_Singularity_X86_ThreadContext._lastExecutionTimeUpdate, eax
        mov     dword ptr [ecx + 4].Struct_Microsoft_Singularity_X86_ThreadContext._lastExecutionTimeUpdate, edx

        mov     edx, ebx       ; restore edx
        ;; End thread execution time accouting
endif
        ;; On clean switch, no need to save caller-saved registers
        ; fxsave  [edx].Struct_Microsoft_Singularity_X86_ThreadContext._mmx
        mov     [edx].Struct_Microsoft_Singularity_X86_ThreadContext._regs, 0

        ;; The old context has been saved, so we can now release the dispatch
        ;; lock and let other processors load the old context.
        ;; (We've released the old stack pointer and haven't yet
        ;; set up a new stack pointer, so the following code
        ;; does not touch the stack.)
        ;; This code duplicates code from SpinLock.Release.

if SPINLOCK_RELEASE_SANITY_CHECK
        ; if (this.lockWord != 1 || this.lockingThreadIndexPlusOne-1 != currentThreadId) throw ...
        cmp     ?c_dispatchLock@Class_Microsoft_Singularity_Scheduling_Scheduler@@2?AUStruct_System_Threading_SpinLock@@A.Struct_System_Threading_SpinLock._lockWord, 1
        je      spinLockOk1
        int     3
        spinLockOk1:
        mov     eax, ?c_dispatchLock@Class_Microsoft_Singularity_Scheduling_Scheduler@@2?AUStruct_System_Threading_SpinLock@@A.Struct_System_Threading_SpinLock._lockingThreadIndexPlusOne
        sub     eax, 1
        cmp     [edx].Struct_Microsoft_Singularity_X86_ThreadContext._threadIndex, ax
        je      spinLockOk2
        int     3
        spinLockOk2:
endif ; SPINLOCK_RELEASE_SANITY_CHECK
        ; this.lockingThreadIndexPlusOne = 0;
        ; this.lockWord = 0;
        mov     ?c_dispatchLock@Class_Microsoft_Singularity_Scheduling_Scheduler@@2?AUStruct_System_Threading_SpinLock@@A.Struct_System_Threading_SpinLock._lockingThreadIndexPlusOne, 0
        mov     ?c_dispatchLock@Class_Microsoft_Singularity_Scheduling_Scheduler@@2?AUStruct_System_Threading_SpinLock@@A.Struct_System_Threading_SpinLock._lockWord, 0

        ;; Load the new processor context.

        mov     fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext], ecx
        mov     esi, ecx

        IDT_LOAD_CONTEXT 0
        
        iretd
        
_SwitchedInContextSwitch:
        ret
        
?g_SwitchToThreadContextNoGC@Class_Microsoft_Singularity_Processor@@SIXPAUStruct_Microsoft_Singularity_X86_ThreadContext@@@Z endp

ifdef DOUBLE_FAULT_HANDLER
align 16
?CallTaskGate@@YIXGI@Z proc
;
; FAR JMP to double-fault task gate.  This is hand-coded, because I can't figure out
; how to make MASM do this for me.
;
        db      09Ah                ; CALL FAR PTR
        dd      0
        dw      Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtDFG - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
        
        ret
?CallTaskGate@@YIXGI@Z endp

PRINTAL MACRO
        mov     ah, 0ah;0ah
        mov     [edx], ax
        add     edx, 2
ENDM
        
PRINTC MACRO num
        mov     al, num
        mov     ah, 02h;0ah
        mov     [edx], ax
        add     edx, 2        
ENDM
        
; DoubleFault
;   Entered in 32-bit protected mode.
PUBLIC ?DoubleFault@@YIXXZ
?DoubleFault@@YIXXZ PROC NEAR
    mov     edx, 0b8000h

    PRINTC  '*'
    PRINTC  '*'
    PRINTC  '2'
    PRINTC  '*'
    PRINTC  '*'
    PRINTC  ' '

    PRINTC  'e'
    PRINTC  'b'
    PRINTC  'p'
    mov     ecx, ebp
    call    printdw

    PRINTC  'e'
    PRINTC  'f'
    PRINTC  'l'
    pushfd
    pop     ecx
    call    printdw

    PRINTC  'e'
    PRINTC  'f'
    PRINTC  'l'
    pushfd
    pop     ecx
;     or      ecx, 04000h                                 
;     push    ecx
;     popfd
    call    printdw

    mov     edx, 0b8000h + 160
        
    PRINTC  'c'
    PRINTC  's'
    push    cs
    pop     cx
    call    printw

    PRINTC  's'
    PRINTC  's'
    mov     cx, ss
    call    printw

    PRINTC  'd'
    PRINTC  's'
    mov     cx, ds
    call    printw

    PRINTC  'e'
    PRINTC  's'
    mov     cx, es
    call    printw

    PRINTC  'f'
    PRINTC  's'
    mov     cx, fs
    call    printw

    PRINTC  'g'
    PRINTC  's'
    mov     cx, gs
    call    printw

    PRINTC  't'
    PRINTC  'r'
    str     cx
    call    printw

    mov     edx, 0b8000h + 160 * 2
        
    PRINTC  'e'
    PRINTC  's'
    PRINTC  'p'
    mov     ecx, esp
    call    printdw

    PRINTC  '+'
    PRINTC  '0'
    mov     ecx, [esp]
    call    printdw

    PRINTC  '+'
    PRINTC  '4'
    mov     ecx, [esp+4]
    call    printdw

    PRINTC  '+'
    PRINTC  '8'
    mov     ecx, [esp+8]
    call    printdw

    PRINTC  '+'
    PRINTC  '1'
    PRINTC  '2'
    mov     ecx, [esp+12]
    call    printdw

    PRINTC  '+'
    PRINTC  '1'
    PRINTC  '6'
    mov     ecx, [esp+16]
    call    printdw

    mov     edx, 0b8000h + 160 * 3
        
    PRINTC  'e'
    PRINTC  's'
    PRINTC  'i'
    mov     ecx, esi
    call    printdw

    PRINTC  'p'
    PRINTC  't'
    mov     cx, [esi].Struct_Microsoft_Singularity_X86_TSS._previous_tss
    call    printw

    PRINTC  'e'
    PRINTC  's'
    PRINTC  'p'
    mov     ecx, [esi].Struct_Microsoft_Singularity_X86_TSS._esp
    call    printdw

    PRINTC  'e'
    PRINTC  'i'
    PRINTC  'p'
    mov     ecx, [esi].Struct_Microsoft_Singularity_X86_TSS._eip
    call    printdw

    PRINTC  'e'
    PRINTC  'f'
    PRINTC  'l'
    mov     ecx, [esi].Struct_Microsoft_Singularity_X86_TSS._eflags
    call    printdw

    PRINTC  'c'
    PRINTC  'r'
    PRINTC  '3'
    mov     ecx, [esi].Struct_Microsoft_Singularity_X86_TSS._cr3
    call    printdw

    PRINTC  'u'
    mov     cx, [esi].Struct_Microsoft_Singularity_X86_TSS._trap_bit
    call    printw

    mov     edx, 0b8000h + 160 * 4
        
    PRINTC  'e'
    PRINTC  'd'
    PRINTC  'i'
    mov     ecx, edi
    call    printdw

    PRINTC  'p'
    PRINTC  't'
    mov     cx, [edi].Struct_Microsoft_Singularity_X86_TSS._previous_tss
    call    printw

    PRINTC  'e'
    PRINTC  's'
    PRINTC  'p'
    mov     ecx, [edi].Struct_Microsoft_Singularity_X86_TSS._esp
    call    printdw

    PRINTC  'e'
    PRINTC  'i'
    PRINTC  'p'
    mov     ecx, [edi].Struct_Microsoft_Singularity_X86_TSS._eip
    call    printdw

    PRINTC  'e'
    PRINTC  'f'
    PRINTC  'l'
    mov     ecx, [edi].Struct_Microsoft_Singularity_X86_TSS._eflags
    call    printdw

    PRINTC  'c'
    PRINTC  'r'
    PRINTC  '3'
    mov     ecx, [edi].Struct_Microsoft_Singularity_X86_TSS._cr3
    call    printdw

    PRINTC  'u'
    mov     cx, [edi].Struct_Microsoft_Singularity_X86_TSS._trap_bit
    call    printw

    mov     edx, 0b8000h + 160 * 5
    mov     ebp, [esi].Struct_Microsoft_Singularity_X86_TSS._esp
        
    PRINTC  'e'
    PRINTC  's'
    PRINTC  'p'
    mov     ecx, ebp
    call    printdw

    PRINTC  '+'
    PRINTC  '0'
    mov     ecx, [ebp]
    call    printdw

    PRINTC  '+'
    PRINTC  '4'
    mov     ecx, [ebp+4]
    call    printdw

    PRINTC  '+'
    PRINTC  '8'
    mov     ecx, [ebp+8]
    call    printdw

    PRINTC  '+'
    PRINTC  '1'
    PRINTC  '2'
    mov     ecx, [ebp+12]
    call    printdw

    PRINTC  '+'
    PRINTC  '1'
    PRINTC  '6'
    mov     ecx, [ebp+16]
    call    printdw

        
    iretd ;     // note, should clear CR0.TS                                          
again:
    jmp again

?DoubleFault@@YIXXZ ENDP

; Print a DWORD to the screen
;   [in]  ecx = dword to print
;   [in]  edx = address of screen
;   [use] eax = trashed for temporary
;
printdw PROC NEAR
    PRINTC  ':'
        
    mov     eax, ecx
    shr     eax, 28
    and     eax, 0fh
    add     eax, 030h
    cmp     eax, 03ah
    jl      print28
    add     eax, 7h
print28:
    PRINTAL        

    mov     eax, ecx
    shr     eax, 24
    and     eax, 0fh
    add     eax, 030h
    cmp     eax, 03ah
    jl      print24
    add     eax, 7h
print24:
    PRINTAL        

    mov     eax, ecx
    shr     eax, 20
    and     eax, 0fh
    add     eax, 030h
    cmp     eax, 03ah
    jl      print20
    add     eax, 7h
print20:
    PRINTAL        

    mov     eax, ecx
    shr     eax, 16
    and     eax, 0fh
    add     eax, 030h
    cmp     eax, 03ah
    jl      print16
    add     eax, 7h
print16:
    PRINTAL        

    mov     eax, ecx
    shr     eax, 12
    and     eax, 0fh
    add     eax, 030h
    cmp     eax, 03ah
    jl      print12
    add     eax, 7h
print12:
    PRINTAL        

    mov     eax, ecx
    shr     eax, 08
    and     eax, 0fh
    add     eax, 030h
    cmp     eax, 03ah
    jl      print08
    add     eax, 7h
print08:
    PRINTAL        

    mov     eax, ecx
    shr     eax, 04
    and     eax, 0fh
    add     eax, 030h
    cmp     eax, 03ah
    jl      print04
    add     eax, 7h
print04:
    PRINTAL        

    mov     eax, ecx
    shr     eax, 00
    and     eax, 0fh
    add     eax, 030h
    cmp     eax, 03ah
    jl      print00
    add     eax, 7h
print00:
    PRINTAL        

    PRINTC  ' '

    ret
printdw ENDP
                
; Print a WORD to the screen
;   [in]  ecx = word to print (in low 16-bits, high 16-bits ignored)
;   [in]  edx = address of screen
;   [use] eax = trashed for temporary
;
printw PROC NEAR
    PRINTC  ':'
        
    mov     eax, ecx
    shr     eax, 12
    and     eax, 0fh
    add     eax, 030h
    cmp     eax, 03ah
    jl      print12
    add     eax, 7h
print12:
    PRINTAL        

    mov     eax, ecx
    shr     eax, 08
    and     eax, 0fh
    add     eax, 030h
    cmp     eax, 03ah
    jl      print08
    add     eax, 7h
print08:
    PRINTAL        

    mov     eax, ecx
    shr     eax, 04
    and     eax, 0fh
    add     eax, 030h
    cmp     eax, 03ah
    jl      print04
    add     eax, 7h
print04:
    PRINTAL        

    mov     eax, ecx
    shr     eax, 00
    and     eax, 0fh
    add     eax, 030h
    cmp     eax, 03ah
    jl      print00
    add     eax, 7h
print00:
    PRINTAL        

    PRINTC  ' '

    ret
printw ENDP
endif ;; DOUBLE_FAULT_HANDLER
                        
END
