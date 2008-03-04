The Singularity Debugger Extensions use the Debugger Interface for
the Microsoft(R) Debugging Tools for Windows(R).  The Debugger Interface
is also known as dbgeng.

The latest documentation on dbgeng is at http://dbg/docs/dbgeng.doc.
The dbgeng files, dbgeng.h and dbgeng.lig, come straight from the
Debugger distribution (http://dbg).

The objbase.h file was hand crafted to minimize the cruft
imported by the standard Windows objbase.h (12KB vs. 1MB).

To add a new command, you need to:
1) Create a the new function.
2) Add descriptive text to help.cpp
3) Add an export entry to singx86.def.

Windbg supports dynamic load and unloading of extensions.
Type ".unload singx86" at the windbg command prompt to unload it.
Type ".load singx86" at the windbg command prompt to load it.

The code for the debuggers can be found in the Windows source tree in
the \nt\sdktools\debuggers directory.  The most important sources are
in \nt\sdktools\debuggers\ntsd64.  The sources for the Windows debugger
stub are in \nt\base\ntos\kd64.
