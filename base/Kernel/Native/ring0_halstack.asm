;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;
;;  Microsoft Research Singularity
;;
;;  Copyright (c) Microsoft Corporation.  All rights reserved.
;;
;;  File:   halstack.asm
;;
;;  Note:
;;      This file contains implementations of the LinkStack and UnlinkStack
;;      routines suitable for use by ring-0 code (kernel and ring-0 apps).
;;

.686p
.mmx
.xmm
.model flat
.code

ifdef SINGULARITY
assume ds:flat
assume es:flat
assume ss:flat
assume fs:nothing
assume gs:nothing
else  ;  Singularity
PAGE_BITS               EQU     12
endif  ;  Singularity

include hal.inc

        align 16

;; Public symbols
public ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
public ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

;;; Stack:
;;;   arg2.. (args 0 & 1 = ECX & EDX) + 44
;;;   caller (addr)     + 40
;;;   caller ebp        + 36    <= old EBP
;;;   callee (addr)     + 32    <= old ESP
;;;   argsize           + 28
;;;   unlinkN           + 24
;;;   efl               + 20
;;;   eax               + 16
;;;   ecx               + 12
;;;   edx               + 8
;;;   stackLimit        + 4
;;;   stackBegin        + 0
?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ ::
 
        ;; Disable interrupts
        pushfd                                          ; +20
        cli
        push    eax                                     ; +16
        push    ecx                                     ; +12
        push    edx                                     ; +8
        
        mov     edx,fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]
        push    [edx].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit ; +4
        push    [edx].Struct_Microsoft_Singularity_X86_ThreadContext._stackBegin ; +0

        mov     eax,esp
        
        ;; Change to schedulerStack
        mov     ecx,fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._schedulerStackLimit]
        mov     [edx].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit,ecx
        mov     ecx,fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._schedulerStackBegin]
        mov     [edx].Struct_Microsoft_Singularity_X86_ThreadContext._stackBegin,ecx
        
ifdef DEBUG
        ;; If we're already on the scheduler stack, we're in trouble,
        ;; so trap to the debugger:
        cmp eax, [edx].Struct_Microsoft_Singularity_X86_ThreadContext._stackBegin
        jg stackOk
        cmp eax, [edx].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit
        jl stackOk
        sti
        int 3
stackOk:
endif ;; DEBUG

        mov     esp,ecx

        ;; Save the old ESP (and create a temporary frame head).
        lea     ecx,[eax+32]
        push    ecx
        push    [eax+4]                ; stackLimit on old stack
        push    [eax+0]                ; stackBegin on old stack
        
        ;; Call GetStackSegmentAndCopy(ecx=stack needed, edx=threadContext, *args, args, 
        ;;                             esp, begin, limit)
        push    ecx                          ; old esp
        push    [eax+28]                     ; #args
        lea     ecx,[eax+44]                 ; &arg2
        push    ecx                          ;
        mov     ecx,[eax+16]                 ; stack size needed
        
        call    ?g_GetStackSegmentAndCopy@Class_Microsoft_Singularity_Memory_Stacks@@SIPAUuintPtr@@PAU2@PAUStruct_Microsoft_Singularity_X86_ThreadContext@@PAII000@Z

        ;; Get back to old ESP, then adjust to pop off EDX and ECX
        pop     ecx
        sub     ecx,32
        mov     esp,eax

        push    [ecx+24]                                ; unlinkN
        push    [ecx+20]                                ; efl
        mov     eax,[ecx+32]                            ; callee
        mov     edx,[ecx+8]                             ; edx
        mov     ecx,[ecx+12]                            ; ecx
        
        ;; Restore interrupts
        popfd
        
        push    ebp                      ; Create new ebp frame.
        mov     ebp,esp
        jmp     eax                       ; jump to callee code.

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;        
;; NB: See halstack.asm for a stack diagram

?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ::
        
        ;; Disable interrupts and remove dead args from old stack 
        pushfd
        cli
        
        mov     esp,ebp                      ; Find adjusted ebp
        add     esp,ecx
        mov     ecx,[ebp+4]                   ; move eip link up
        mov     [esp+4],ecx
        mov     ecx,[ebp+0]                   ; move ebp link up
        mov     [esp+0],ecx
        mov     ebp,esp

        push    eax                      ; save eax to old stack
        push    edx                      ; save edx to old stack
        
        mov     ecx,fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]
        mov     eax,[ecx].Struct_Microsoft_Singularity_X86_ThreadContext._stackBegin
        mov     eax,[eax-16]             ; save efl to old stack
        push    eax

;;; Stack:
;;;   caller (addr)     + 16
;;;   caller ebp        + 12
;;;   eax               + 8
;;;   edx               + 4
;;;   efl               + 0
        
        ;; Prepare scheduler stack segment.
        mov     eax,esp
        mov     esp,fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._schedulerStackBegin]
        push    eax                               ; save old esp
        push    [ecx].Struct_Microsoft_Singularity_X86_ThreadContext._stackBegin
        push    [ecx].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit
        
        push    [ecx].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit
        mov     edx,[ecx].Struct_Microsoft_Singularity_X86_ThreadContext._stackBegin
        
        ;; Then switch to scheduler stack.
        mov     eax,fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._schedulerStackLimit]
        mov     [ecx].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit,eax
        mov     eax,fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._schedulerStackBegin]
        mov     [ecx].Struct_Microsoft_Singularity_X86_ThreadContext._stackBegin,eax

        ;; Call ReturnStackSegmentRaw(ecx=threadContext, edx=begin, limit)
        call    ?g_ReturnStackSegmentRaw@Class_Microsoft_Singularity_Memory_Stacks@@SIXPAUStruct_Microsoft_Singularity_X86_ThreadContext@@PAUuintPtr@@1@Z

        pop     eax                              ; pop old limit
        pop     eax                              ; pop old begin
        pop     esp                                ; restore esp
        popfd                               ; restore interrupts
        pop     edx                        ; restore eax and edx
        pop     eax
        pop     ebp                              ; pop ebp chain
        ret

end
