;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
; Copyright (c) Microsoft Corporation.  All rights reserved.
;
; Include file for assembly files.
;

;ASSUME  DS:FLAT, ES:FLAT, SS:FLAT, FS:NOTHING, GS:NOTHING

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;

SINGLE_THREADED          equ 0
EXCLUDED                 equ 0

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
;  Typedefs & Constants
;
UINT8  TYPEDEF BYTE
UINT16 TYPEDEF WORD
UINT32 TYPEDEF DWORD
UINT64 TYPEDEF QWORD
UINT128 STRUCT 16
        _lo             UINT64          ?
        _hi             UINT64          ?
UINT128 ENDS

;
X86_EFLAG_IF            equ     0200h

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
;  Bartok Types
;

uint8 TYPEDEF BYTE
uint16 TYPEDEF WORD
uint32 TYPEDEF DWORD
uint64 TYPEDEF QWORD

int8 TYPEDEF BYTE
int16 TYPEDEF WORD
int32 TYPEDEF DWORD
int64 TYPEDEF QWORD

bool TYPEDEF BYTE
bartok_char TYPEDEF WORD

intptr TYPEDEF DWORD
uintptr TYPEDEF DWORD

uintPtr STRUCT 4
        value  uintptr ?
uintPtr ENDS

intPtr STRUCT 4
        value  intptr ?
intPtr ENDS

PTR_uintptr TYPEDEF PTR uintptr

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;

include halclass.inc

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
; Macros
;

; define CurrentThread() function as a macro.
CurrentThread MACRO reg
        mov  reg,fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]
        mov  reg,[reg].Struct_Microsoft_Singularity_X86_ThreadContext.__thread
        ENDM

CurrentThreadContext MACRO reg
        mov  reg,fs:[Struct_Microsoft_Singularity_X86_ProcessorContext._threadContext]
        ENDM

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
; Big/Little Endian Definitions for Long Integers
;

ifdef   bigend                  ; Big Endian (hi word at low address)
LOWORD  equ     [4]
HIWORD  equ     [0]
else                            ; Little Endian (low word at low address)
LOWORD  equ     [0]
HIWORD  equ     [4]
endif

;;
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;; End of File.
