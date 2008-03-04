;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;
;;  singldr0.asm - X86 Portion of Singularity Boot Loader
;;
;;  Copyright (c) Microsoft Corporation.  All rights reserved.
;;
;;  Notes:
;;    This file has three main purposes.
;;      1) Initialize context for the 16-bit C++ boot loader
;;         (BootPhase0 -> BootPhase1).
;;      2) Manage the transition from 16-bit real to 32-bit protected mode
;;         (BootPhase2 -> BootPhase3 -> undump).
;;      3) Manage the transition from 32-bit protected to 16-bit real mode
;;         (StopPhase0 -> StopPhase1 -> StopPhase2 -> StopPhase3).
;;
;;    In addition, this file provides a few helper functions used by the
;;    16-bit loader code in singldr.cpp.
;;
;;    Since there is only one .asm function (BiosDiskRead) specific to the
;;    disk boot process, it is in here too
;;
;;    The three-function-in-one PModeTransfer routine is also located here
;;    in order to ensure it links correctly, since it has mixed 16-bit and
;;    32-bit portions
;;

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;
    .model tiny, c
    .686p
    .mmx
    .xmm

    .xlist
    .list

OPTION SCOPED

DEBUG_BOOT EQU 0
DEBUG_VESA_MODES EQU 0

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;
;;  Bartok Types
;;

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
;;
;;  Other Types
;;

GDTDesc STRUCT
        GDT_limit   dw      0
        GDT_base1   dw      0
        GDT_base2   db      0
        GDT_access  db      0
        GDT_limacc  db      0
        GDT_base3   db      0
GDTDesc ENDS

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;

_TEXT32 segment para public use32 'CODE'    ; this segment contains all 32-bit
include halclass.inc

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;
;;  Macros
;;

CONSOLE_WRITE macro loc,value
    push    edx
    mov     edx, loc
    mov     ah, 01fh
    mov     al, value
    mov     [edx], ax
    pop	    edx
endm
	

LOAD_EAX_FLAT MACRO reg
    xor     eax,eax
    mov     ax,reg
    shl     eax,4
ENDM

OPERAND_SIZE_OVERLOAD MACRO
    uint8   066h
ENDM

MOV_EAX_CR4 MACRO
    uint8   00fh
    uint8   020h
    uint8   0e0h
ENDM

MOV_CR4_EAX MACRO
    uint8   00fh
    uint8   022h
    uint8   0e0h
ENDM

LEA_EAX_REALATIVE MACRO target
    call    @pusheip
@pusheip:
    pop     eax
    add     eax, target - @pusheip
ENDM

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;
;; 16-bit code segment
;;

_TEXT segment public use16 'CODE'    ; this segment contains all 16-bit

EXTRN BootPhase1:NEAR
EXTRN StopPhase3:NEAR
EXTRN MpBootPhase1:NEAR
    .data

PUBLIC MpStartupLock
MpStartupLock dw 1

    .code

    ORG     100h
BootPhase0 PROC NEAR

    ; if we are booting from PXE, the stack holds the return address and the
    ; !PXE address, and es:bx is the PXENV+ address.  CS:IP=0000:7c00
    ;
    ; if we are booting from HD/CD, the stack holds a null return address, the
    ; boot signature ("CD" or "HD"), and the boot drive.  CS:IP=5000:7c00

    ; First, we will move the IP to 100h, and CS to X7b0, where x=0 or x=5
    ; this lets us build this as a DOS .com file

    mov     ax, cs
    add     ax, 07b0h
    push    ax
    push    OFFSET begin
    retf                        ; really returns to next statement.

begin:

    ; if we booted off of the Hard Disk or CD, we're now at 57b0:0100
    ; relocate this segment to 07b0:1000

    cmp     ax, 07b0h           ; ax holds cs
    je      ConfigNetboot       ; if cs is already 07b0, this was a net boot

    ; relocate from 57b0:0100 to 07b0:0100
    mov     ax, 07c0h
    mov     es, ax
    xor     di, di              ; es:di points to 07c0:0000
    mov     ax, cs
    add     ax, 010h
    mov     ds, ax
    xor     si, si              ; ds:si points to 57c0:0000
    mov     ecx, 04000h
    rep     movsd               ; transfer 4000h DWORDS (64KB)

    ; since we are here, this wasn't a PXE boot.  set the stack appropriately:
    pop     eax                 ; pop the return address for a disk boot
    pop     ebx                 ; pop the boot device signature
    pop     edx                 ; pop the boot drive
    push    edx                 ; on a disk boot, we set the PXE! address to diskid
    push    eax                 ; put the return address back on the stack

    pushw   07b0h
    push    OFFSET relocated
    retf                        ; jump to the 'relocated' block

ConfigNetboot:
    push    es                  ; ebx holds the PXENV+ address
    push    bx
    pop     ebx
    xor     edx, edx            ; on a PXE boot, the bootdriveid=0

relocated:
    mov     ax, cs
    mov     ds, ax              ; set FS=GS=DS=CS for TINY model.
    mov     fs, ax
    mov     gs, ax

    pop     eax                 ; pop the return address.
    pop     ecx                 ; pop the !PXE address.
    push    ecx                 ; return !PXE address. (arg0)
    push    eax                 ; push the return address. (ret)

    push    ss                  ;
    push    sp                  ;
    pop     ebp                 ; get the stack pointer.

    mov     ax, ds              ; move the stack to cs:fff0h
    mov     ss, ax
    mov     ax, 0fff0h
    mov     sp, ax

    push    ebp                 ; push the old stack pointer.
    push    edx                 ; push the disk id (arg2)
    push    ebx                 ; push the PXENV+ address (arg1)
    push    ecx                 ; push the !PXE address (arg0)
    call    EnableA20
    call    BootPhase1

    pop     ecx                 ; pop the !PXE address
    pop     ecx                 ; pop the PXENV+ address
    pop     sp                  ; pop the old stack pointer.
    pop     ss

    retf
BootPhase0 ENDP

; void IoSpaceWrite8(uint16 port, uint8 value);
;   Called by 16-bit C++ code to access video registers.
PUBLIC IoSpaceWrite8
IoSpaceWrite8 PROC NEAR
    push    bp
    mov     bp, sp
    mov     dx, [bp+4]          ; dx = address segment
    mov     al, [bp+6]         ; eax = value
    out     dx, al
    pop     bp
    ret
IoSpaceWrite8 ENDP

; void IoSpaceWrite32(uint16 port, uint32 value);
;   Called by 16-bit C++ code to access PCI config space.
PUBLIC IoSpaceWrite32
IoSpaceWrite32 PROC NEAR
    push    bp
    mov     bp, sp
    mov     dx, [bp+4]          ; dx = address segment
    mov     eax, [bp+6]         ; eax = value
    out     dx, eax
    pop     bp
    ret
IoSpaceWrite32 ENDP

; uint8 IoSpaceRead8(uint16 port);
;   Called by 16-bit C++ code to access video registers.
PUBLIC IoSpaceRead8
IoSpaceRead8 PROC NEAR
    push    bp
    mov     bp, sp
    mov     dx, [bp+4]          ; dx = address segment
    in      al, dx
    pop     bp
    ret
IoSpaceRead8 ENDP

; uint32 IoSpaceRead32(uint16 port);
;   Called by 16-bit C++ code to access PCI config space.
PUBLIC IoSpaceRead32
IoSpaceRead32 PROC NEAR
    push    bp
    mov     bp, sp
    mov     dx, [bp+4]          ; dx = address segment
    in      eax, dx
    mov     edx, eax
    shr     edx, 16
    pop     bp
    ret
IoSpaceRead32 ENDP

; uint16 BiosDiskRead(uint8 __far * addr, uint32 diskblock, uint16 blocks, uint16 driveid);
;   Called by 16-bit C++ code to read from the disk (HD or CD) using extended int13h
PUBLIC BiosDiskRead
BiosDiskRead PROC NEAR
    push    bp
    mov     bp, sp
    pushad

    mov     dx, [bp+14]         ; dl = drive #
    mov     eax, [bp+8]         ; eax = LBA #
    mov     cx, [bp+12]         ; ecx = count (# of sectors)
    mov     bx, [bp+6]          ; bx = address segment
    mov     di, [bp+4]          ; di = address offset

    pushd   00
    push    eax                 ; push 64-bit sector address (top half always null)

    push    bx
    push    di                  ; push transfer address

    push    cx                  ; # sectors
    pushw   0010h               ; this request packet is 16 bytes

    mov     ah,42h              ; extended read
    mov     si,sp               ; set ds:si = address of params
    int     13h                 ; perform the read

    add     sp, 10h             ; clean the stack, discard the return code

    popad
    pop     bp
    ret
BiosDiskRead ENDP

; uint16 BiosDiskReadCHS(uint8 __far * addr, uint32 diskblock, uint16 driveid, uint16 sectors, uint16 secpertrack, uint16 numheads)
;   Called by 16-bit C++ code to read from a Usb disk using CHS-style int13h
PUBLIC BiosDiskReadCHS
BiosDiskReadCHS PROC NEAR
    push    bp
    mov     bp, sp
    pushad
    push    es

    mov     eax, [bp+8]         ; eax = 32-bit absolute sector number

    xor     edx,edx             ; clear edx before long divide
    xor     ecx,ecx
    mov     cx,[bp+16]          ; ecx = sectors per track
    div     ecx                 ; eax = track, edx = sector within track (0-62)
    inc     dl                  ; dl = sector within track (1-63)
    mov     cl,dl               ; cl = sector within track
    mov     edx,eax
    shr     edx,16              ; dx:ax = track
    xor     ebx, ebx
    mov     bx, [bp+18]
    div     bx                  ; ax = cylinder (0-1023), dx = head (0-255)
    xchg    dl,dh               ; dh = head

    mov     ch,al               ; ch = bits 0-7 of cylinder
    shl     ah,6
    or      cl,ah               ; bits 6-7 of cl = bits 8-9 of cylinder

    mov     ax, [bp+12]
    mov     dl, al              ; dl = int13 unit #

    mov     bx, [bp+6]
    mov     es, bx              ; es = address segment
    mov     bx, [bp+4]          ; bx = address offset

    mov     ax, [bp+14]         ; ax = number of sectors, let's hope it's less than 256!
    mov     ah, 02h             ; read 1 sector

    int 13h

    pop     es
    popad
    pop     bp
    ret
BiosDiskReadCHS ENDP

; void BootGetBiosInfo(Struct_Microsoft_Singularity_BootInfo __far * pInfo);
;   Called by 16-bit C++ code to fill in portions of BootInfo.
PUBLIC BootGetBiosInfo
BootGetBiosInfo PROC NEAR
    push    bp
    mov     bp, sp
    mov     es, [bp+6]
    mov     si, [bp+4]  ; pInfo

    push    di
    push    cx
    push    dx

    mov     eax, SIZEOF Struct_Microsoft_Singularity_BootInfo
    mov     es:[si].Struct_Microsoft_Singularity_BootInfo._RecSize, eax

    ; Set Bios-related Information.
    ;
    xor     edx,edx
    mov     ax,0b101h
    stc
    int     1ah
    jc      skip_1A_B101
    mov     es, [bp+6]
    mov     si, [bp+4]  ; pInfo
    and     eax, 0ffffh
    mov     es:[si].Struct_Microsoft_Singularity_BootInfo._PciBiosAX, eax
    and     ebx, 0ffffh
    mov     es:[si].Struct_Microsoft_Singularity_BootInfo._PciBiosBX, ebx
    and     ecx, 0ffh
    mov     es:[si].Struct_Microsoft_Singularity_BootInfo._PciBiosCX, ecx
    mov     es:[si].Struct_Microsoft_Singularity_BootInfo._PciBiosEDX, edx
skip_1A_B101:

    ; Save the IDT and PIC information.
    xor     ax, ax
    mov     dx, 0a0h
    in      al, dx
    mov     ah, al
    mov     dx, 020h
    in      al, dx

    mov     es:[si].Struct_Microsoft_Singularity_BootInfo._BiosPicMask, ax
    sidt    es:[si].Struct_Microsoft_Singularity_BootInfo._BiosIdtPtr._limit

    pop     dx
    pop     cx
    pop     di

    pop     bp
    ret
BootGetBiosInfo ENDP

; void BootPhase2(Struct_Microsoft_Singularity_BootInfo *)
;   Entered in 16-bit real mode.
;   Transitions to 32-bit protected mode (to BootPhase3).
;   Loads EDX = Struct_Microsoft_Singularity_BootInfo far *
PUBLIC BootPhase2
BootPhase2 PROC NEAR

    push    bp
    xor     ebp, ebp
    mov     bp, sp
    mov     ax, [bp+6]
    mov     bp, [bp+4]  ; pBootInfo
    mov     ss, ax

    ; disable interrupts until kernel turns them back on.
    cli

    ; prepare for jump to 32-bit protected mode.
    ;   edx = physical address of Struct_Microsoft_Singularity_BootInfo.
    ;
    mov     edx, DWORD PTR [bp].Struct_Microsoft_Singularity_BootInfo._Info32

    ; set segments before switch
    lgdt    FWORD PTR [bp].Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPtr._limit

    ; Turn prot mode on, flush prefetch
    mov     eax, cr0
    or      eax, Struct_Microsoft_Singularity_X86_CR0_PE + Struct_Microsoft_Singularity_X86_CR0_NE
    mov     cr0, eax

    jmp cont
cont:

    ; Load protected stack segment registers.
    mov     ax,Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtRS - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
    mov     ss,ax
    mov     esp,ebp

    ; push the 16:32 address onto the stack, then "return" to it.
    push    0
    push    Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPC - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
    mov     eax, 07b00h + OFFSET BootPhase3
    push    eax
    OPERAND_SIZE_OVERLOAD
    retf

BootPhase2 ENDP

; StopPhase1
;   Entered in 16-bit protected mode.
;   Transitions to 16-bit real mode (StopPhase2).
;   Passes through BootInfo __far * in ebx.
;
StopPhase1 PROC NEAR
    ; Turn off protected mode.
    mov     eax, cr0
    and     eax, NOT (Struct_Microsoft_Singularity_X86_CR0_PE + Struct_Microsoft_Singularity_X86_CR0_NE)
    mov     cr0, eax

    db      0EAh                        ; JMP FAR PTR
    dw      offset StopPhase2           ; 07b0:StopPhase2
    dw      07b0h
StopPhase1 ENDP

; StopPhase2
;   Entered in 16-bit real mode.  Shuts the machine down.
;   Assumes BootInfo __far * is in ebx.
;
PUBLIC StopPhase2
StopPhase2 PROC NEAR
    mov     ax, 07b0h           ; move the stack to 07b0:fff0h (17AF0)
    mov     ss, ax
    mov     ds, ax              ; set DS=CS for TINY model.
    mov     es, ax              ; set ES=CS for TINY model.
    mov     fs, ax              ; set FS=CS for TINY model.
    mov     gs, ax              ; set GS=CS for TINY model.
    mov     ax, 0fff0h
    mov     sp, ax

    ; Set ES:SI = BootInfo __far *.
    mov     si, bx
    shr     ebx, 16
    mov     es, bx

    ; Restore the PIC and BIOS IDT.
    mov     ax, es:[si].Struct_Microsoft_Singularity_BootInfo._BiosPicMask
    mov     dx, 0a0h
    mov     ah, al
    out     dx, al
    mov     ax, es:[si].Struct_Microsoft_Singularity_BootInfo._BiosPicMask
    mov     dx, 020h
    out     dx, al

    lidt    FWORD PTR es:[si].Struct_Microsoft_Singularity_BootInfo._BiosIdtPtr._limit

    call    StopPhase3

again:
    jmp again
StopPhase2 ENDP

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
; empty_8042 -- wait for 8042 input buffer to drain
;      al=0, z=0   => 8042 input buffer empty
; Uses:
;      ax, flags
;

_8042_STATUS    EQU     64h             ; 8042 com port
_8042_DATA      EQU     60h             ; 8042 data port
_8042_BUF_FULL  EQU     2               ; 8042 busy bit

empty_8042 PROC NEAR
    sub     cx,cx                   ; cx = 0, timeout loop counter
empty:
    in      al,_8042_STATUS         ; read 8042 status port
    jmp     $+2
    jmp     $+2
    jmp     $+2
    jmp     $+2
    and     al,_8042_BUF_FULL       ; test buffer full bit
    loopnz  empty
    ret
empty_8042 ENDP

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
;EnableA20:
;    Enables the A20 line for any machine.  If the MachineType global variable
;    is set to MCA, then it will call the EnableMcaA20 routine.  If not, it
;    will execute the ISA code for enabling the A20 line.
;
EnableA20 PROC NEAR
    stc
    mov     ax, 2401h
    int     15h                 ; Call BIOS Enable A20
    jnc     exit

    mov     ah,0dfh             ; (AH) = Code for enable

    ; Enable or disable the A20 line (disable=dd, enable=df)

    call    empty_8042          ; ensure 8042 input buffer empty
    jnz     exit                ; 8042 error return

    mov     al,0d1h             ; 8042 cmd to write output port
    out     _8042_STATUS,al     ; send cmd to 8042
    call    empty_8042          ; wait for 8042 to accept cmd
    jnz     exit                ; 8042 error return

    mov     al,ah               ; 8042 port data
    out     _8042_DATA,al       ; output port data to 8042
    call    empty_8042

    ; We must wait for the a20 line to settle down, which (on an AT)
    ; may not happen until up to 20 usec after the 8042 has accepted
    ; the command.  We make use of the fact that the 8042 will not
    ; accept another command until it is finished with the last one.
    ; The 0FFh command does a NULL 'Pulse Output Port'.  Total execution
    ; time is on the order of 30 usec, easily satisfying the IBM 8042
    ; settling requirement.
    ;
    mov     al,0FFh             ; Pulse Output Port (pulse no lines)
    out     _8042_STATUS,al     ; send cmd to 8042
    call    empty_8042          ; wait for 8042 to accept cmd
exit:
    ret
EnableA20 ENDP

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
; void Reset()
;
_CMOS_CTRL      EQU     70h             ; CMOS ctrl port
_CMOS_DATA      EQU     71h             ; CMOS data port

PUBLIC Reset
Reset PROC NEAR
    mov     al, 0bh             ; Set up for control reg B
    out     _CMOS_CTRL, al
    call    empty_8042          ; stall
    in      al, _CMOS_DATA
    and     al, 0bfh            ; Clear periodic interrupt enable.
    out     _CMOS_DATA, al
    call    empty_8042          ; stall

    mov     al, 0ah             ; Set up for control reg A
    out     _CMOS_CTRL, al
    call    empty_8042          ; stall
    in      al, _CMOS_DATA
    and     al, 0f0h            ; clear rate setting
    or      al, 006h
    out     _CMOS_DATA, al

    mov     al, 015h            ; Set a "neutral" cmos address.
    out     _CMOS_CTRL, al

    call    empty_8042          ; ensure 8042 input buffer empty
    mov     al,0feh             ; 8042 cmd to write output port
    out     _8042_STATUS,al     ; send cmd to 8042
    ret
Reset ENDP

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
; int GetSystemMap(SMAP_INFO __far *pDesc, uint32 __far *pNext)
;
PUBLIC BootGetSMAP
BootGetSMAP PROC NEAR
    push    bp
    mov     bp, sp

    push    es
    push    esi
    push    edi
    push    ecx
    push    edx
    push    ebx

    mov     es, [bp+10]
    mov     di, [bp+8]  ; pNext
    mov     ebx, es:[di]

    mov     es, [bp+6]
    mov     di, [bp+4]  ; pDesc

    mov     eax, 0e820h
    mov     ecx, 20
    mov     edx, 534D4150h                              ; 'SMAP'
    int     15h

    mov     es, [bp+10]
    mov     di, [bp+8]  ; pNext
    mov     es:[di], ebx

    pop     ebx
    pop     edx
    pop     ecx
    pop     edi
    pop     esi
    pop     es

    pop     bp
    ret
BootGetSMAP ENDP

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
; int BootCallVesa(uint16 ax, uint16 bx, uint16 cx, uint8 __far *pBuffer)
;       ax = [bp+4]
;       bx = [bp+6]
;       cx = [bp+8]
;       es:di = [bp+12/10]
;
if DEBUG_VESA_MODES
PUBLIC BootCallVesa
BootCallVesa PROC NEAR
    push    bp
    mov     bp, sp

    push    es
    push    esi
    push    edi
    push    ecx
    push    edx
    push    ebx

    mov     ax, [bp+4]
    mov     bx, [bp+6]
    mov     cx, [bp+8]
    mov     di, [bp+10]  ; pNext
    mov     es, [bp+12]

    int     10h

    pop     ebx
    pop     edx
    pop     ecx
    pop     edi
    pop     esi
    pop     es

    pop     bp
    ret
BootCallVesa ENDP
endif

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
; void BootHalt(void)
;
PUBLIC BootHalt
BootHalt PROC NEAR
    hlt
again:  jmp again
    ret
BootHalt ENDP

; void PModeTransfer(uint32 StartAddress, uint32 DestinationAddress, uint32 bytes);
;   Entered in 16-bit real mode.
;   Transitions to 32-bit protected mode and jumps to CopySector
;   This is one of 3 functions that together comprise a single function call that moves from real to protected and back
;
; It appears that this can be done through the use of int15 AH=87h instead
;   refer to http://www.ctyme.com/intr/rb-1527.htm for more information
;

PUBLIC PModeTransfer
PModeTransfer PROC NEAR
    cli                         ; shut off interrupts

    push    bp
    mov     bp, sp
    pushad
    push    ds
    push    es
    push    fs
    push    gs

    mov     ecx, [bp+12]        ; # bytes to copy
    mov     edx, [bp+8]         ; destination address
    mov     ebx, [bp+4]         ; origin address

    push    dword ptr 0
    popfd                       ; clear flags

    xor     ax,ax               ; set the segments to null
    mov     gs,ax
    mov     es,ax
    mov     fs,ax
    mov     ss,ax

    lgdt    FWORD PTR [gdtptr1] ; load the GDT register

    xor     ax, ax
    mov     ds, ax              ; set ds to null

    mov     eax, cr0            ; turn on protected mode
    or      al, Struct_Microsoft_Singularity_X86_CR0_PE
    mov     cr0, eax

    ; emit a 32-bit address intersegment jump
    db      66h
    db      0eah
    dd      offset _TEXT32:CopySector + 07b00h  ; this is the 32-bit address of CopySector
    dw      offset codeSeg - offset nullSeg     ; protected mode code segment = 08h
PModeTransfer ENDP

; this is the GDT we use for the PModeTransfer function
nullSeg     GDTDesc     <0, 0, 0, 0, 0, 0>
codeSeg     GDTDesc     <0FFFFh, 0h, 0h, 9ah, 0cfh, 0>              ; flat 4GB code segment
dataSeg     GDTDesc     <0FFFFh, 0h, 0h, 92h, 0cfh, 0>              ; flat 4GB data segment
codeSegR    GDTDesc     <0FFFFh, 07b00h, 000h, 09Ah, 000h, 000h>    ; real mode code segment
dataSegR    GDTDesc     <0FFFFh, 07b00h, 000h, 092h, 000h, 000h>    ; real mode data segment
gdtptr1     dw          offset gdtptr1 - offset nullSeg
gdtptr2     dd          offset nullSeg + 7b00h

; CopyReturn
; This is the 3rd piece of the real-protected-real jump (the 2nd piece is in the 32bit code seg)
CopyReturn PROC NEAR

    ; Turn off protected mode.
    mov     eax, cr0
    and     eax, NOT Struct_Microsoft_Singularity_X86_CR0_PE
    mov     cr0, eax

    db      0EAh                ; JMP FAR PTR to the next instruction
    dw      offset RealMode
    dw      07b0h

RealMode:
    ; reset all segment registers:
    mov     ax, 07b0h
    mov     ss, ax
    pop     gs
    pop     fs
    pop     es
    pop     ds

    sti                         ; reset interrupts and return to calling procedure
    popad
    pop     bp
    ret
CopyReturn ENDP



;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
; void MpEnter(void)
;
; This function must be aligned on a 4KB boundary to be usable by IPI STARTUP.
; It should be the last 16-bit assembly function to avoid wasting space.
; NB: the entry point is 7c00 - 100 + ORG.
;
    ORG     500h        ; Base at 08000h.  Must be 4KB aligned for IPI START.
PUBLIC MpEnter
MpEnter PROC NEAR
    cli
    mov     ax, 07b0h   ; Select data segment
    mov     ds, ax

spinlock:
    cmp     MpStartupLock, 0
    je      get_lock
    pause
    jmp     spinlock

get_lock:
    mov     ax, 1
    xchg    ax, MpStartupLock
    cmp     ax, 0
    jne     spinlock

    ; Switch to tiny mode with same descriptors as uniprocessor and reuse
    ; real-mode stack.
    mov     ax, 07b0h
    mov     ds, ax              ; set GS=FS=ES=DS=CS for TINY model.
    mov     es, ax
    mov     fs, ax
    mov     gs, ax
    mov     ss, ax
    mov     sp, 0fff0h
    push    ax
    push    OFFSET init_cpu
    retf

init_cpu:
    call    MpBootPhase1        ;  Point of no return

MpEnter ENDP

; void MpBootPhase2(Struct_Microsoft_Singularity_BootInfo __far *, Struct_Microsoft_Singularity_CpuInfo*)
; Entered in 16-bit real-mode and transitions to 32-bit Protected mode.
PUBLIC MpBootPhase2
MpBootPhase2 PROC NEAR
    push    bp
    xor     ebp, ebp
    mov     bp, sp
    mov     bx, [bp + 8]                            ; CpuInfo
    mov     ax, [bp + 6]                            ; segment(BootInfo)
    mov     bp, [bp + 4]                            ; bp + 4 = BootInfo
    mov     ss, ax
    mov     ds, ax

    ; disable interrupts until kernel turns them back on.
    cli

    ; prepare for jump to 32-bit protected mode.
    ;   edx = physical address of Struct_Microsoft_Singularity_BootInfo.
    ;
    mov     edx, DWORD PTR [bp].Struct_Microsoft_Singularity_BootInfo._Info32

    ; prepare segments
    lgdt    FWORD PTR [bx].Struct_Microsoft_Singularity_CpuInfo._GdtPtr._limit

    ; Turn protected mode on and jump to flush prefetch
    mov     eax, cr0
    or      eax, Struct_Microsoft_Singularity_X86_CR0_PE + Struct_Microsoft_Singularity_X86_CR0_NE
    mov     cr0, eax
    jmp cont
cont:
    ; Load protected stack segment registers.
    mov     ax,Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtRS - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
    mov     ss,ax
    mov     esp,ebp

    ; push the 16:32 address onto the stack, then "return" to it.
    push    0
    push    Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPC - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
    mov     eax, 07b00h + OFFSET MpBootPhase3
    push    eax
    OPERAND_SIZE_OVERLOAD
    retf
MpBootPhase2 ENDP

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
; 32-bit code segment
;

.686p
_TEXT32 segment para public use32 'CODE'    ; this segment contains all 32-bit
assume cs:_TEXT32, ds:_TEXT32

; BootPhase3:
;   Entered in 32-bit protected mode with paging disabled.
;   Configures the segment registers and stack.  Does not turn on paging.
;   Transitions to the 32-bit loader (undump.cpp)
PUBLIC BootPhase3
BootPhase3 PROC NEAR
    ;   edx = physical address of Struct_Microsoft_Singularity_BootInfo structure.

    ; load the 32-bit segment selectors.
    mov     bx, Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPD - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
    mov     ds, bx
    mov     es, bx
    mov     ss, bx
    mov     bx, Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPF - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
    mov     fs, bx
    mov     bx, Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPG - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
    mov     gs, bx

    ; set the initial stack.
    mov     esp, Struct_Microsoft_Singularity_BootInfo_REAL_STACK
    mov     ebp, Struct_Microsoft_Singularity_BootInfo_REAL_PBOOTINFO
    mov     [ebp], edx
    mov     ebp, esp

    ; copy the undumper into place.
    mov     esi, DWORD PTR [edx].Struct_Microsoft_Singularity_BootInfo._Undump
    mov     edi, undump_dst
    mov     ecx, undump_siz
    call    memcopy

    ; Select the stack
    mov     esp, Struct_Microsoft_Singularity_BootInfo_REAL_STACK

    ; Enable super pages. note that MASM doesn't support the opcode
    ; for these instructions. so we are going to emit them manually

    MOV_EAX_CR4
    or      eax, (Struct_Microsoft_Singularity_X86_CR4_PAE + Struct_Microsoft_Singularity_X86_CR4_PSE + Struct_Microsoft_Singularity_X86_CR4_PCE + Struct_Microsoft_Singularity_X86_CR4_OSFXSR)
    MOV_CR4_EAX

;;; ifdef DOUBLE_FAULT_HANDLER
    ; Set the PDBR
    mov     eax, DWORD PTR [edx].Struct_Microsoft_Singularity_BootInfo._Pdpt32
    mov     cr3, eax

    ; Turn on paging.
    mov     eax, cr0
    or      eax, Struct_Microsoft_Singularity_X86_CR0_PG + Struct_Microsoft_Singularity_X86_CR0_WP
    mov     cr0, eax

    jmp     @reload_tlb
    ALIGN 010h
@reload_tlb:
;;; endif

    ; Set the TSS selector
    mov     ax, Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtTSS - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
    ltr     ax

    ; Set the IO privilege level to 3, so that ring-3 apps can disable
    ; interrupts.  (TODO: eliminate CLI from ring-3 apps.)
    pushfd
    pop     eax
    or      eax, Struct_Microsoft_Singularity_X86_EFlags_IOPL
    push    eax
    popfd

    ; Set up the initial stack.
    mov     esp, Struct_Microsoft_Singularity_BootInfo_REAL_STACK
    xor     ecx, ecx
    push    ecx
    push    ecx
    push    ecx
    push    ecx
    push    ecx                                         ; cpu = 0 (bsp)
    push    edx                                         ; bootinfo

    mov     eax, undump_ent
    call    eax

BootPhase3 ENDP

; MpBootPhase3:
;   Entered in 32-bit protected mode with paging disabled.
;   Configures the segment registers and stack.  Does not turn on paging.
;   On entry edx contains the physical address of the BootInfo structure.
PUBLIC MpBootPhase3
MpBootPhase3 PROC NEAR
    ; load the 32-bit segment selectors.
    mov     bx, Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPD - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
    mov     ds, bx
    mov     es, bx
    mov     ss, bx
    mov     bx, Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPF - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
    mov     fs, bx
    mov     bx, Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtPG - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
    mov     gs, bx

    ; locate pre-allocated kernel stack
    imul    eax, [edx].Struct_Microsoft_Singularity_BootInfo._MpCpuCount, SIZEOF Struct_Microsoft_Singularity_CpuInfo
    add     eax, Struct_Microsoft_Singularity_BootInfo._Cpu0
    mov     eax, DWORD PTR [edx + eax].Struct_Microsoft_Singularity_CpuInfo._KernelStack

    ; switch to kernel stack
    mov     esp, eax
    mov     ebp, eax

    ; Save cpu number as other starting CPU's will bump value
    mov     ecx, [edx].Struct_Microsoft_Singularity_BootInfo._MpCpuCount

    mov     [edx].Struct_Microsoft_Singularity_BootInfo._MpStatus32, Struct_Microsoft_Singularity_MpBootStatus_Phase3Entry

    ; Enable super pages. note that MASM doesn't support the opcode
    ; for these instructions. so we are going to emit them manually
    MOV_EAX_CR4
    or      eax, (Struct_Microsoft_Singularity_X86_CR4_PAE + Struct_Microsoft_Singularity_X86_CR4_PSE + Struct_Microsoft_Singularity_X86_CR4_PCE + Struct_Microsoft_Singularity_X86_CR4_OSFXSR)
    MOV_CR4_EAX

    mov     eax, DWORD PTR [edx].Struct_Microsoft_Singularity_BootInfo._Pdpt32
    mov     cr3, eax

    ; Turn on paging.
    mov     eax, cr0
    or      eax, Struct_Microsoft_Singularity_X86_CR0_PG + Struct_Microsoft_Singularity_X86_CR0_WP
    mov     cr0, eax

    jmp     @reload_tlb_mp
    ALIGN 010h
@reload_tlb_mp:

    ;  stack already has cpu number on it
    push    ecx                                   ; cpu number
    push    edx                                   ; bootinfo
    mov     eax, undump_ent
    call    eax

deadloop:
    cli
    hlt
    jmp deadloop

MpBootPhase3 ENDP

; StopPhase0
;   Entered in 32-bit protected mode with paging enabled..
;   Turns off paging.
;   Transitions to the 16-bit protected mode (StopPhase1)
;   Loads BootInfo __far * into ebx.
PUBLIC StopPhase0
StopPhase0 PROC NEAR
    ; no intrs for a while
    ; Configures EBX = Struct_Microsoft_Singularity_BootInfo
    cli

    mov     esp, Struct_Microsoft_Singularity_BootInfo_REAL_STACK
    mov     ebp, Struct_Microsoft_Singularity_BootInfo_REAL_PBOOTINFO
    mov     ebx, [ebp]

ifdef DEBUG_BOOT
    mov     edx, 0b8000h
    mov     ah, 01fh
    mov     al, '['
    mov     [edx], ax

    mov     edx, 0b8002h
    mov     ah, 01fh
    mov     al, '1'
    mov     [edx], ax
endif

    ; Set the IO privilege level to 0.
    pushfd
    pop     eax
    and     eax, NOT Struct_Microsoft_Singularity_X86_EFlags_IOPL
    push    eax
    popfd

    ; Turn off paging.
    mov     eax, cr0
    and     eax, NOT (Struct_Microsoft_Singularity_X86_CR0_PG + Struct_Microsoft_Singularity_X86_CR0_WP)
    mov     cr0, eax

    ; Flush and reset the TLB.
    mov     eax,0
    mov     cr3,eax

    jmp     reload_tlb
    ALIGN 010h
reload_tlb:

    ; restore the segments to the 16-bit defaults from the Gdt
    mov     ebx, [ebx].Struct_Microsoft_Singularity_BootInfo._Info16
    mov     ax, Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtRS - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull
    mov     ds, ax
    mov     es, ax
    mov     fs, ax
    mov     gs, ax
    mov     ss, ax

    db      0EAh                        ; JMP FAR PTR
    dd      offset _TEXT:StopPhase1     ; RC:StopPhase1
    dw      Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtRC - Struct_Microsoft_Singularity_BootInfo._Cpu0._GdtNull

again:
    jmp again

StopPhase0 ENDP

; Copy a memory region.
;   [in] ds:si = pointer to source buffer.
;   [in] es:di = pointer to destination buffer.
;   [in] ecx = number of bytes to copy
;
memcopy PROC NEAR
    jecxz   done
    cld
    add     ecx,3               ; Pad to DWORD
    shr     ecx,2               ; Shift ECX for count of dwords
    rep     movsd               ; Copy the data.
done:
    ret
memcopy ENDP

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;;
;;; The IDT_ENTER building macros insure that each IDT target has
;;; an offset of form _IdtEnter + 0x10 * interrupt_number.
;;;
public IdtTarget
public IdtEnter0
public IdtEnter1
public IdtEnterN

IDT_TARGET_ARGS STRUCT 1
        _ret            uint32          ?
        _edi            uint32          ?
        _esi            uint32          ?
        _ebp            uint32          ?
        _esp            uint32          ?
        _ebx            uint32          ?
        _edx            uint32          ?
        _ecx            uint32          ?
        _eax            uint32          ?
        _num            uint32          ?
        _err            uint32          ?
        _eip            uint32          ?
        _cs0            uint32          ?
        _efl            uint32          ?
IDT_TARGET_ARGS ENDS

IDT_ENTER_CLEAN MACRO num
        push    0 ; No error
        push    num
        jmp     _IdtEnterBody
        align   16
ENDM

IDT_ENTER_ERR   MACRO num
        push    num
        jmp     _IdtEnterBody
        align   16
ENDM

        align 16
IdtEnter PROC NEAR
IdtEnter0 LABEL NEAR
        IDT_ENTER_CLEAN       000h                            ; #DE Divide-by-Zero
IdtEnter1::
        IDT_ENTER_CLEAN       001h                            ; #DB Debug Exception
        IDT_ENTER_CLEAN       002h                            ; NMI Non-Maskable-Interrupt
        IDT_ENTER_CLEAN       003h                            ; #BP Breakpoint
        IDT_ENTER_CLEAN       004h                            ; #OF OVerflow
        IDT_ENTER_CLEAN       005h                            ; #BR Bound-Range
        IDT_ENTER_CLEAN       006h                            ; #UD Invalid Opcode
        IDT_ENTER_CLEAN       007h                            ; #NM Device Not Available
        IDT_ENTER_ERR         008h                            ; #DF Double Fault
        IDT_ENTER_CLEAN       009h                            ; Unused (was x87 segment except)
        IDT_ENTER_ERR         00ah                            ; #TS Invalid TSS
        IDT_ENTER_ERR         00bh                            ; #NP Sgement Not Present
        IDT_ENTER_ERR         00ch                            ; #SS Stack Exception
        IDT_ENTER_ERR         00dh                            ; #GP General Protection
        IDT_ENTER_ERR         00eh                            ; #PF Page Fault
        IDT_ENTER_CLEAN       00fh                            ; Reserved
        IDT_ENTER_CLEAN       010h                            ; #MF x87 Math Error
        IDT_ENTER_ERR         011h                            ; #AC Alignment Check
        IDT_ENTER_CLEAN       012h                            ; #MC Machine Check
        IDT_ENTER_CLEAN       013h                            ; #XF SIMD Exception

        _num = 014h                                     ; 014h to 020h
        WHILE _num LE 020h
                IDT_ENTER_CLEAN       _num
                _num = _num + 1
        ENDM
IdtEnterN::

_IdtEnterBody::
        pushad
        mov eax, cr2
        push eax

        LEA_EAX_REALATIVE IdtTarget
        mov eax, [eax]
        call eax
again:  jmp again

        align 4
IdtTarget::
        dd 0

IdtEnter endp

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;

if 1
; Print a DWORD to the screen
;   [in] ebx = address of screen
;   [in] eax = dword to print
;   [in] ecx = trashed for temporary
;
printdw PROC NEAR
    mov     ecx, eax
    shr     ecx, 28
    and     ecx, 0fh
    add     ecx, 00f30h
    cmp     ecx, 00f3ah
    jl      print28
    add     ecx, 7h
print28:
    mov     [ebx+0], ecx
    add     ebx, 2

    mov     ecx, eax
    shr     ecx, 24
    and     ecx, 0fh
    add     ecx, 00f30h
    cmp     ecx, 00f3ah
    jl      print24
    add     ecx, 7h
print24:
    mov     [ebx+0], ecx
    add     ebx, 2

    mov     ecx, eax
    shr     ecx, 20
    and     ecx, 0fh
    add     ecx, 00f30h
    cmp     ecx, 00f3ah
    jl      print20
    add     ecx, 7h
print20:
    mov     [ebx+0], ecx
    add     ebx, 2

    mov     ecx, eax
    shr     ecx, 16
    and     ecx, 0fh
    add     ecx, 00f30h
    cmp     ecx, 00f3ah
    jl      print16
    add     ecx, 7h
print16:
    mov     [ebx+0], ecx
    add     ebx, 2

    mov     ecx, eax
    shr     ecx, 12
    and     ecx, 0fh
    add     ecx, 00f30h
    cmp     ecx, 00f3ah
    jl      print12
    add     ecx, 7h
print12:
    mov     [ebx+0], ecx
    add     ebx, 2

    mov     ecx, eax
    shr     ecx, 08
    and     ecx, 0fh
    add     ecx, 00f30h
    cmp     ecx, 00f3ah
    jl      print08
    add     ecx, 7h
print08:
    mov     [ebx+0], ecx
    add     ebx, 2

    mov     ecx, eax
    shr     ecx, 04
    and     ecx, 0fh
    add     ecx, 00f30h
    cmp     ecx, 00f3ah
    jl      print04
    add     ecx, 7h
print04:
    mov     [ebx+0], ecx
    add     ebx, 2

    mov     ecx, eax
    shr     ecx, 00
    and     ecx, 0fh
    add     ecx, 00f30h
    cmp     ecx, 00f3ah
    jl      print00
    add     ecx, 7h
print00:
    mov     [ebx+0], ecx
    add     ebx, 2

    mov     ecx, 00f20h
    mov     [ebx+0], ecx
    add     ebx, 2
    mov     [ebx+0], ecx
    add     ebx, 2

    ret
printdw ENDP
endif


; CopySector:
;   Entered in 32-bit protected mode with paging disabled.
;   this routine moves back into 16-bit code by going to CopyReturn
;   on the way, we'll have it move ecx bytes from [ebx] to [edx]
CopySector PROC NEAR

    ; load the 32-bit segment selectors.
    mov     ax, dataSeg - nullSeg
    mov     ds, ax
    mov     es, ax
    mov     fs, ax
    mov     gs, ax
    mov     ss, ax

    jecxz   copydone

    cld
    add     ecx,3               ; Pad ecx to DWORD
    shr     ecx,2               ; Shift ecx for count of dwords

    mov     esi, ebx
    mov     edi, edx
    rep     movsd

copydone:
    ; set the segment registers back to real mode selectors
    mov     ax, dataSegR - nullSeg
    mov     ds, ax
    mov     es, ax
    mov     fs, ax
    mov     gs, ax
    mov     ss, ax

    ; now return to 16-bit code...
    db      0EAh                        ; emit a JMP FAR PTR
    dd      offset _TEXT:CopyReturn
    dw      codeSegR - nullSeg

CopySector ENDP

;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;
; 32-bit Code Generated by C Compiler
;
; Contains:
;   undump_ent  EQU     001112d5h
;   undump_dst  EQU     00111000h
;   undump_siz  EQU     00000480h
;   undump_dat  uint8   000h,080h,00bh,000h,030h,031h,032h,033h,...
;
PUBLIC undump_dat
include undump.asb

_TEXT32 ends

end BootPhase0
;
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;; End of File.
