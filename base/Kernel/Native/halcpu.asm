;
; Copyright (c) Microsoft Corporation.  All rights reserved.
;

.686p
.model flat
.code

assume ds:flat
assume es:flat
assume ss:flat
assume fs:nothing
assume gs:nothing

include hal.inc

?GetCurrentProcessorNumber@@YIHXZ proc
    mov eax, fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._cpuId]
    ret
?GetCurrentProcessorNumber@@YIHXZ endp

end
