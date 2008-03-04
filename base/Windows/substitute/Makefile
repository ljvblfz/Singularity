##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\Substitute\Makefile
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CSCFLAGS = /nologo $(CSFLAGS) /warn:4 /warnaserror+ /r:System.dll

INSTALLDIR=..\..\build

##############################################################################

all: $(OBJDIR) $(OBJDIR)\substitute.exe

$(OBJDIR):
    -mkdir $(OBJDIR)

install: $(OBJDIR) $(OBJDIR)\substitute.exe
    $(SDEDIT) $(INSTALLDIR)\substitute.exe
    $(COPY) $(OBJDIR)\substitute.exe $(INSTALLDIR)
    $(SDEDIT) $(INSTALLDIR)\substitute.pdb
    $(COPY) $(OBJDIR)\substitute.pdb $(INSTALLDIR)

##############################################################################

clean:
    @-del /q $(OBJDIR)\substitute.* *~ 2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul

##############################################################################

test: $(OBJDIR) $(OBJDIR)\substitute.exe
    $(OBJDIR)\substitute Tools Fools <Substitute.cs >Test.cs

test2: $(OBJDIR) $(OBJDIR)\substitute.exe Makefile
    $(OBJDIR)\substitute "(?<x>//)" "$${x}$${x}" <Substitute.cs >Test.cs

##############################################################################

SOURCE_FILES = \
    Substitute.cs \
    AssemblyInfo.cs \

$(OBJDIR)\substitute.exe : $(SOURCE_FILES)
    $(CSC) $(CSCFLAGS) /out:$(OBJDIR)\substitute.exe $**

################################################################# End of File.
