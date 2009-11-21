//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

// We model input and output as unbounded streams of input events or output events.

// $VgaEvents is an unbounded stream of events sent to the VGA screen.
// $VgaEvents[0...$VgaNextEvent-1] have already been sent to the screen.
type VgaEvent;
function VgaTextStore($ptr:int, $val:int) returns(VgaEvent);
var $VgaEvents:[int]VgaEvent;
var $VgaNextEvent:int;

const ?VgaTextLo: int; axiom ?VgaTextLo == /*0xb8000*/753664;
const ?VgaTextHi: int; axiom ?VgaTextHi ==            761664;
function vgaAddr2(i:int) returns (bool) {?VgaTextLo <= i && i <= ?VgaTextHi - 2}

procedure VgaTextStore16($ptr:int, $val:int);
  requires vgaAddr2($ptr);
  requires word($val);
  modifies $Eip;
  modifies $VgaNextEvent, $VgaEvents;
  ensures  $VgaNextEvent == old($VgaNextEvent) + 1;
  ensures  $VgaEvents == old($VgaEvents)[old($VgaNextEvent) := VgaTextStore($ptr, $val)];

// For diagnostics, allow arbitrary writes to the first line of the screen.
// (If no diagnostics are needed, this can be disabled.)
procedure VgaDebugStore16($ptr:int, $val:int);
  requires ?VgaTextLo <= $ptr && $ptr <= ?VgaTextLo + 158;
  requires word($val);
  modifies $Eip;

// $KeyboardEvents is an unbounded stream of events from the keyboard.
// $KeyboardEvents[0..$KeyboardDone-1] have already been read from the keyboard.
// $KeyboardEvents[$KeyboardDone..$KeyboardAvailable-1] are ready to read but
// have not yet been read.
var $KeyboardEvents:[int]int;
var $KeyboardAvailable:int;
var $KeyboardDone:int;

procedure KeyboardStatusIn8();
  modifies $Eip, eax, $KeyboardAvailable;
  ensures  and(eax, 1) == 0 ==> $KeyboardAvailable == $KeyboardDone;
  ensures  and(eax, 1) != 0 ==> $KeyboardAvailable > $KeyboardDone;

procedure KeyboardDataIn8();
  requires $KeyboardAvailable > $KeyboardDone;
  modifies $Eip, eax;
  modifies $KeyboardDone;
  ensures  $KeyboardDone == old($KeyboardDone) + 1;
  ensures  and(eax, 255) == $KeyboardEvents[old($KeyboardDone)];

