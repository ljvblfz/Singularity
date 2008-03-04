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
;;      routines suitable for use by ring-3 application code. This code is
;;      used to satisfy ring-3 applications' import of the LinkStack and
;;      UnlinkStack symbols.
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

;;; Public symbols
public ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
public ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

;;; Ring-3-specific stack ABIs
externdef ?g_LinkNewStackSegment@Struct_Microsoft_Singularity_V1_Services_StackService@@SIPAUuintPtr@@PAU2@PAII000@Z:NEAR
externdef ?g_ReturnStackSegmentRaw@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXPAUuintPtr@@0@Z:NEAR

        align 16

;;; Stack:
;;;   arg2.. (args 0 & 1 = ECX & EDX) + 40
;;;   caller (addr)     + 36
;;;   caller ebp        + 32    <= old EBP
;;;   callee (addr)     + 28    <= old ESP
;;;   argsize           + 24
;;;   unlinkN           + 20
;;;   eax               + 16
;;;   ecx               + 12
;;;   edx               + 8
;;;   stackLimit        + 4
;;;   stackBegin        + 0

?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ::

        push    eax                                     ; +16
        push    ecx                                     ; +12
        push    edx                                     ; +8
        
        mov     edx,fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]
        push    [edx].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit ; +4
        push    [edx].Struct_Microsoft_Singularity_X86_ThreadContext._stackBegin ; +0
        mov     eax,esp
        
        ;; Save the old ESP (and create a temporary frame head).
        lea     ecx,[eax+28]
        push    ecx
        
        ;; Call LinkNewStackSegment(ecx=stack needed, edx=*args, args, 
        ;;                          esp, begin, limit)
        push    [eax+4]                      ; stackLimit on old stack
        push    [eax+0]                      ; stackBegin on old stack
        push    ecx                          ; old esp
        push    [eax+24]                     ; #args
        lea     edx,[eax+40]                 ; &arg2
        mov     ecx,[eax+16]                 ; stack size needed
        
        call    ?g_LinkNewStackSegment@Struct_Microsoft_Singularity_V1_Services_StackService@@SIPAUuintPtr@@PAU2@PAII000@Z

        ;; Get back to old ESP, then adjust to pop off EDX and ECX
        pop     ecx
        sub     ecx,28
        mov     esp,eax

        push    [ecx+20]                                ; unlinkN
        mov     eax,[ecx+28]                            ; callee
        mov     edx,[ecx+8]                             ; edx
        mov     ecx,[ecx+12]                            ; ecx
        
        push    ebp                      ; Create new ebp frame.
        mov     ebp,esp
        jmp     eax                       ; jump to callee code.

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;; NB: See halstack.asm for a stack diagram
        
?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ::

        mov     esp,ebp                  ; Find adjusted ebp
        add     esp,ecx
        mov     ecx,[ebp+4]              ; move eip link up
        mov     [esp+4],ecx
        mov     ecx,[ebp+0]              ; move ebp link up
        mov     [esp+0],ecx
        mov     ebp,esp

        push    eax                      ; Save eax to old stack
        push    edx                      ; save edx to old stack

;;; Stack:
;;;   caller (addr)     + 12
;;;   caller ebp        + 8
;;;   eax               + 4
;;;   edx               + 0

        ;; Call ReturnStackSegmentRaw(ecx=begin, edx=limit)
        mov     eax,fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]
        mov     edx,[eax].Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit
        mov     ecx,[eax].Struct_Microsoft_Singularity_X86_ThreadContext._stackBegin
        call    ?g_ReturnStackSegmentRaw@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXPAUuintPtr@@0@Z

        pop     edx                      ; restore eax and edx
        pop     eax
        pop     ebp                      ; pop ebp chain
        ret

end
