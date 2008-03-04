##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\DistroBuilder\Makefile
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CSCFLAGS = /nologo $(CSFLAGS) /warn:2 


##############################################################################

all: $(OBJDIR) $(OBJDIR)\DistroBuilder.exe

$(OBJDIR):
    -mkdir $(OBJDIR)

install: $(OBJDIR) $(OBJDIR)\DistroBuilder.exe
    $(SDEDIT) ..\..\build\DistroBuilder.*
    $(COPY) $(OBJDIR)\DistroBuilder.exe ..\..\build
    $(COPY) $(OBJDIR)\DistroBuilder.pdb ..\..\build


##############################################################################

clean:
    @-del /q $(OBJDIR)\DistroBuilder.* *~ 2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul

##############################################################################

REFERENCES = /reference:System.Xml.dll /reference:System.dll

SOURCE_FILES = DistroBuilder.cs

##############################################################################

$(OBJDIR)\DistroBuilder.exe: $(SOURCE_FILES)
    $(CSC) $(CSCFLAGS) $(REFERENCES) /out:$(OBJDIR)\DistroBuilder.exe $**
