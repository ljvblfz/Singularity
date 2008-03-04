##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\mkpxecom\Makefile
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CSCFLAGS = /nologo $(CSFLAGS) /warn:2 /define:SINGULARITY
REFERENCES = /reference:System.dll

all: $(OBJDIR) $(OBJDIR)\mkpxecom.exe

$(OBJDIR):
    -mkdir $(OBJDIR)

clean:
    @-del /q $(OBJDIR)\mkpxecom.* *~ 2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul

install: all
    $(SDEDIT) ..\..\build\mkpxecom.exe
    $(COPY) $(OBJDIR)\mkpxecom.exe ..\..\build\mkpxecom.exe

$(OBJDIR)\mkpxecom.exe: mkpxecom.cs
    $(CSC) $(CSCFLAGS) $(REFERENCES) /out:$(OBJDIR)\mkpxecom.exe mkpxecom.cs

