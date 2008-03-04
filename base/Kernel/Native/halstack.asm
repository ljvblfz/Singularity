;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;
;;  Microsoft Research Singularity
;;
;;  Copyright (c) Microsoft Corporation.  All rights reserved.
;;
;;  File:   halstack.asm
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

externdef ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ:NEAR
externdef ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ:NEAR

;;; Public symbols
public ?c_LinkedStackBegin@Class_Microsoft_Singularity_Memory_Stacks@@2EA
public ?c_LinkStackBegin@Class_Microsoft_Singularity_Memory_Stacks@@2EA
public ?c_LinkStackLimit@Class_Microsoft_Singularity_Memory_Stacks@@2EA
public ?c_UnlinkStackBegin@Class_Microsoft_Singularity_Memory_Stacks@@2EA
public ?c_UnlinkStackLimit@Class_Microsoft_Singularity_Memory_Stacks@@2EA
public ?c_LinkedStackLimit@Class_Microsoft_Singularity_Memory_Stacks@@2EA

        align 16

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
;
;          1) Stack at entry of LinkStackN
;              +-----------------------+
;  old base -> |     previous esp      |   2) Stack on exit from LinkStackN
;              +-----------------------+       +------------------------+ <- base
;              |  previous stackBase   |       |         old esp        |
;              +-----------------------+       +------------------------+       
;              |  previous stackLimit  |       |        old base        |
;              +-----------------------+       +------------------------+
;              |          ..           |       |        old limit       |
;              +-----------------------+       +------------------------+
;              |          ..           |  ==>  |            ..          |
;              |         args          |   .   |           args         |
;              +-----------------------+   .   +------------------------+
;              | return addr in caller |   .   | return to UnlinkStackN |
;              +-----------------------+   .   +------------------------+    
;   old ebp -> |      caller ebp       |  ==>  |        old ebp         | <- esp &
;              +-----------------------+       +------------------------+    ebp
;   old esp -> | return addr in callee |       |           ..           |
;              +-----------------------+       |           ..           | <- limit
; * EAX = amount of stack needed.
;
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;
;; Implementations of LinkStackNn just push the appropriate number
;; and call the general-purpose LinkStack.

?c_LinkedStackBegin@Class_Microsoft_Singularity_Memory_Stacks@@2EA byte 0
?c_LinkStackBegin@Class_Microsoft_Singularity_Memory_Stacks@@2EA byte 0

?g_LinkStack0@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    0
        push    ?g_UnlinkStack0@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack4@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    1
        push    ?g_UnlinkStack4@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack8@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    2
        push    ?g_UnlinkStack8@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack12@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    3
        push    ?g_UnlinkStack12@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack16@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    4
        push    ?g_UnlinkStack16@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack20@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    5
        push    ?g_UnlinkStack20@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack24@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    6
        push    ?g_UnlinkStack24@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack28@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    7
        push    ?g_UnlinkStack28@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack32@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    8
        push    ?g_UnlinkStack32@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack36@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    9
        push    ?g_UnlinkStack36@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack40@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    10
        push    ?g_UnlinkStack40@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack44@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    11
        push    ?g_UnlinkStack44@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack48@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    12
        push    ?g_UnlinkStack48@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack52@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    13
        push    ?g_UnlinkStack52@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack56@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    14
        push    ?g_UnlinkStack56@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack60@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    15
        push    ?g_UnlinkStack60@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

?g_LinkStack64@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        push    16
        push    ?g_UnlinkStack64@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ
        jmp     ?g_LinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
;
?c_LinkStackLimit@Class_Microsoft_Singularity_Memory_Stacks@@2EA byte 0

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
; 1) Stacks on entry to UnlinkStackN:      
;    old base -> +-----------------------+Organization.tif
;                |     previous esp      |    
;                +-----------------------+       +------------------------+ <- base
;                |  previous stackBase   |       |         old esp        |
;                +-----------------------+       +------------------------+       
;                |  previous stackLimit  |       |        old base        |
;                +-----------------------+       +------------------------+
;                |          ..           |       |        old limit       | <- esp
;                +-----------------------+       +------------------------+
;                |          ..           |       |           ..           | * ebp = 
;                |         args          |       |           ..           |   old ebp
;                +-----------------------+       |           ..           |
;                | return addr in caller |       |           ..           |   ecx
;                +-----------------------+       |           ..           |   is free
;         ebp -> |      caller ebp       |       |           ..           | 
;                +-----------------------+       |           ..           |
;     old esp -> | return addr in callee |       |           ..           |
;                +-----------------------+       |           ..           | <- limit
; * EAX/EDX = return value from callee.
;                 
; 2) Stacks on exit from UnlinkStackN:      
;   stackBase -> +-----------------------+
;                |     previous esp      |    
;                +-----------------------+    
;                |  previous stackBase   |         
;                +-----------------------+                
;                |  previous stackLimit  |         
;                +-----------------------+         
;                |          ..           |         
;         ebp -> |          ..           |         
;                |          ..           |         
;         esp -> |          ..           |         
;                +-----------------------+    * eip = return addr in caller  
;
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;
;; Implementations of UnlinkStackNn just push the appropriate number
;; and call the general-purpose UnlinkStack.

?c_UnlinkStackBegin@Class_Microsoft_Singularity_Memory_Stacks@@2EA byte 0

?g_UnlinkStack0@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,0
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack4@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,4
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack8@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,8
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack12@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,12
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack16@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,16
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack20@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,20
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack24@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,24
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack28@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,28
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack32@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,32
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack36@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,36
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack40@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,40
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack44@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,44
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack48@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,48
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack52@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,52
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack56@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,56
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack60@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,60
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ
?g_UnlinkStack64@Class_Microsoft_Singularity_Memory_Stacks@@SIXXZ::
        mov     ecx,64
        jmp     ?g_UnlinkStack@Struct_Microsoft_Singularity_V1_Services_StackService@@SIXXZ

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;

?c_UnlinkStackLimit@Class_Microsoft_Singularity_Memory_Stacks@@2EA byte 0
?c_LinkedStackLimit@Class_Microsoft_Singularity_Memory_Stacks@@2EA byte 0

align 16
PUBLIC  __checkStackLimit
__checkStackLimit       PROC
        push    edx
        mov     edx, eax
        CurrentThreadContext eax
        mov     eax, [eax][Struct_Microsoft_Singularity_X86_ThreadContext._stackLimit]
        cmp     edx, eax
        jb debugHACK
        pop     edx
        ret
debugHACK:     
        pop     edx
        ret
__checkStackLimit       ENDP

end
