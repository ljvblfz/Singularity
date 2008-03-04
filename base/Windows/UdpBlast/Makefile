##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\UdpBlast\Makefile
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CSCFLAGS = /nologo $(CSFLAGS) /debug- /optimize /warn:4 /warnaserror+ \

CSCREFS = /r:System.dll

INSTALLDIR=..\..\build

##############################################################################

all: $(OBJDIR) $(OBJDIR)\udpblast.exe

$(OBJDIR):
    -mkdir $(OBJDIR)

install: $(OBJDIR) $(OBJDIR)\udpblast.exe
    $(SDEDIT) $(INSTALLDIR)\udpblast.exe
    $(COPY) $(OBJDIR)\udpblast.exe $(INSTALLDIR)

##############################################################################

clean:
    @-del /q $(OBJDIR)\udpblast.* *~ 2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul

##############################################################################

SOURCE_FILES = \
    UdpBlast.cs \

$(OBJDIR)\udpblast.exe : $(SOURCE_FILES)
    $(CSC) $(CSCFLAGS) $(CSCREFS) /out:$(OBJDIR)\udpblast.exe $**

################################################################# End of File.
