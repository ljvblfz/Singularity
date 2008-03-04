##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\mktests\Makefile
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CSCFLAGS = /nologo $(CSFLAGS) /warn:2 

##############################################################################

all: $(OBJDIR) $(OBJDIR)\mktests.exe

$(OBJDIR):
    -mkdir $(OBJDIR)

install: $(OBJDIR) $(OBJDIR)\mktests.exe
    $(SDEDIT) ..\..\build\mktests.*
    $(COPY) $(OBJDIR)\mktests.exe ..\..\build
    $(COPY) $(OBJDIR)\mktests.pdb ..\..\build


##############################################################################

clean:
    @-del /q $(OBJDIR)\mktests.* 2>nul
    @-del /q *~ 2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul

##############################################################################

REFERENCES = \
    /reference:System.Xml.dll \
    /reference:System.dll \
    /reference:$(BUILDIR)\MsilReader.dll \

SOURCE_FILES = \
    mktests.cs \

##############################################################################

$(OBJDIR)\mktests.exe: $(SOURCE_FILES)
    $(CSC) $(CSCFLAGS) $(REFERENCES) /out:$(OBJDIR)\mktests.exe $(SOURCE_FILES)

##############################################################################
