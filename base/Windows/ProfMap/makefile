##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\ProfMap\Makefile
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CSCFLAGS = /nologo $(CSFLAGS) /warn:2 /define:SINGULARITY
REFERENCES = /reference:System.dll

all: $(OBJDIR)\ProfMap.exe

clean:
    @-del /q $(OBJDIR)\ProfMap.* *~ 2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul

install: all
    $(COPY) $(OBJDIR)\ProfMap.exe ..\..\build\ProfMap.exe

$(OBJDIR)\ProfMap.exe: ProfMap.cs
    $(CSC) $(CSCFLAGS) $(REFERENCES) /out:$(OBJDIR)\ProfMap.exe ProfMap.cs

