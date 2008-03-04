	page	,132
	title	lmul - long multiply routine
;***
;lmul.asm - long multiply routine
;
;	Copyright (c) Microsoft Corporation.  All rights reserved.
;
;Purpose:
;	Defines long multiply routines
;	Both signed and unsigned routines are the same, since multiply's
;	work out the same in 2's complement
;	creates the following routines:
;	    __aFlmul, __aFulmul (large, medium model)
;	    __aNlmul, __aNulmul (small, compact model)
;
;*******************************************************************************

.xlist
include ulhelp.inc
.list

sBegin	code
	assumes cs,code
	assumes ds,data

page
;***
;lmul - long multiply routine
;
;Purpose:
;	Does a long multiply (same for signed/unsigned)
;	Parameters are not changed.
;
;Entry:
;	Parameters are passed on the stack:
;		1st pushed: multiplier (DWORD)
;		2nd pushed: multiplicand (DWORD)
;
;Exit:
;	DX:AX - product of multiplier and multiplicand
;	NOTE: parameters are removed from the stack
;
;Uses:
;	CX
;
;Exceptions:
;
;*******************************************************************************

if	sizeC
cProc	_aFulmul,<PUBLIC>,<>
cBegin	nogen
cEnd	nogen
else
cProc	_aNulmul,<PUBLIC>,<>
cBegin	nogen
cEnd	nogen
endif


if	sizeC
cProc	_aFlmul,<PUBLIC>,<>
else
cProc	_aNlmul,<PUBLIC>,<>
endif

cBegin
A	equ	BPARGBAS[bp]	; stack address of a
B	equ	BPARGBAS+4[bp]	; stack address of b

;
;	AHI, BHI : upper 16 bits of A and B
;	ALO, BLO : lower 16 bits of A and B
;
;	      ALO * BLO
;	ALO * BHI
; +	BLO * AHI
; ---------------------
;
	mov	ax,HIWORD(A)
	mov	cx,HIWORD(B)
	or	cx,ax		;test for both hiwords zero.
	mov	cx,LOWORD(B)
	jnz	hard		;both are zero, just mult ALO and BLO

	mov	ax,LOWORD(A)
	mul	cx

	pop	bp
	ret
	return	8		; callee restores the stack

hard:
	push	bx

	mul	cx		;ax has AHI, cx has BLO, so AHI * BLO
	mov	bx,ax		;save result

	mov	ax,LOWORD(A)
	mul	word ptr HIWORD(B) ;ALO * BHI
	add	bx,ax		;bx = ((ALO * BHI) + (AHI * BLO))

	mov	ax,LOWORD(A)	;cx = BLO
	mul	cx		;so dx:ax = ALO*BLO
	add	dx,bx		;now dx has all the LO*HI stuff

	pop	bx

cEnd	nolocals
	return	8		; callee restores the stack

sEnd

	end
