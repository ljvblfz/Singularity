    page    ,132
    title   ulrem - unsigned long remainder routine
;***
;ulrem.asm - unsigned long remainder routine
;
;   Copyright (c) Microsoft Corporation.  All rights reserved.
;
;Purpose:
;   defines the unsigned long remainder routine
;   the following routines are created:
;       __aFulrem   (large, medium models)
;       __aNulrem   (small, compact models)
;
;*******************************************************************************

.xlist
include ulhelp.inc
.list

sBegin  code
    assumes cs,code
    assumes ds,data

page
;***
;ulrem - unsigned long remainder
;
;Purpose:
;   Does a unsigned long remainder of the arguments.  Arguments are
;   not changed.
;
;Entry:
;   Arguments are passed on the stack:
;       1st pushed: divisor (DWORD)
;       2nd pushed: dividend (DWORD)
;
;Exit:
;   DX:AX contains the remainder (dividend%divisor)
;   NOTE: this routine removes the parameters from the stack.
;
;Uses:
;   CX
;
;Exceptions:
;
;*******************************************************************************

    ASGN    ulrem

    push    bx

; Set up the local stack and save the index registers.  When this is done
; the stack frame will look as follows (assuming that the expression a%b will
; generate a call to ulrem(a, b)):
;
;       -----------------
;       |       |
;       |---------------|
;       |       |
;       |--divisor (b)--|
;       |       |
;       |---------------|
;       |       |
;       |--dividend (a)-|
;       |       |
;       |---------------|
;       | return addr** |
;       |---------------|
;   BP----->|    old BP |
;       |---------------|
;   SP----->|      BX   |
;       -----------------
;
; ** - 2 bytes if small model; 4 bytes if medium/large model

DVND    equ BPARGBAS[bp]    ; stack address of dividend (a)
DVSR    equ BPARGBAS+4[bp]  ; stack address of divisor (b)

; Now do the divide.  First look to see if the divisor is less than 64K.
; If so, then we can use a simple algorithm with word divides, otherwise
; things get a little more complex.
;

    mov ax,HIWORD(DVSR) ; check to see if divisor < 64K
    or  ax,ax
    jnz L1      ; nope, gotta do this the hard way
    mov cx,LOWORD(DVSR) ; load divisor
    mov ax,HIWORD(DVND) ; load high word of dividend
    xor dx,dx
    div cx      ; dx <- remainder, ax <- quotient
    mov ax,LOWORD(DVND) ; dx:ax <- remainder:lo word of dividend
    div cx      ; dx <- final remainder
    mov ax,dx       ; dx:ax <- remainder
    xor dx,dx
    jmp short L2    ; restore stack and return

;
; Here we do it the hard way.  Remember, ax contains DVSRHI
;

L1:
    mov cx,ax       ; cx:bx <- divisor
    mov bx,LOWORD(DVSR)
    mov dx,HIWORD(DVND) ; dx:ax <- dividend
    mov ax,LOWORD(DVND)
L3:
    shr cx,1        ; shift divisor right one bit; hi bit <- 0
    rcr bx,1
    shr dx,1        ; shift dividend right one bit; hi bit <- 0
    rcr ax,1
    or  cx,cx
    jnz L3      ; loop until divisor < 64K
    div bx      ; now divide, ignore remainder

;
; We may be off by one, so to check, we will multiply the quotient
; by the divisor and check the result against the orignal dividend
; Note that we must also check for overflow, which can occur if the
; dividend is close to 2**32 and the quotient is off by 1.
;

    mov cx,ax       ; save a copy of quotient in CX
    mul word ptr HIWORD(DVSR)
    xchg    cx,ax       ; put partial product in CX, get quotient in AX
    mul word ptr LOWORD(DVSR)
    add dx,cx       ; DX:AX = QUOT * DVSR
    jc  L4      ; carry means Quotient is off by 1

;
; do long compare here between original dividend and the result of the
; multiply in dx:ax.  If original is larger or equal, we're ok, otherwise
; subtract the original divisor from the result.
;

    cmp dx,HIWORD(DVND) ; compare hi words of result and original
    ja  L4      ; if result > original, do subtract
    jb  L5      ; if result < original, we're ok
    cmp ax,LOWORD(DVND) ; hi words are equal, compare lo words
    jbe L5      ; if less or equal we're ok, else subtract
L4:
    sub ax,LOWORD(DVSR) ; subtract divisor from result
    sbb dx,HIWORD(DVSR)
L5:

;
; Calculate remainder by subtracting the result from the original dividend.
; Since the result is already in a register, we will perform the subtract in
; the opposite direction and negate the result to make it positive.
;

    sub ax,LOWORD(DVND) ; subtract original dividend from result
    sbb dx,HIWORD(DVND)
    neg dx      ; and negate it
    neg ax
    sbb dx,0

;
; Just the cleanup left to do.  dx:ax contains the remainder.
; Restore the saved registers and return.
;

L2:

    pop bx
cEnd    <nolocals>
return  8

sEnd

    end
