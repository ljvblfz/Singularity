##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\nib\Makefile
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CSCFLAGS = /nologo $(CSFLAGS) /warn:2 /unsafe

##############################################################################

all: $(OBJDIR) $(OBJDIR)\nib.exe

$(OBJDIR):
    -mkdir $(OBJDIR)

install: $(OBJDIR) $(OBJDIR)\nib.exe
    $(SDEDIT) ..\..\build\nib.*
    $(COPY) $(OBJDIR)\nib.exe ..\..\build
    $(COPY) $(OBJDIR)\nib.pdb ..\..\build


copy: $(OBJDIR) $(OBJDIR)\nib.exe
    $(COPY) $(OBJDIR)\nib.exe ..\..\build
    $(COPY) $(OBJDIR)\nib.pdb ..\..\build


##############################################################################

clean:
    @-del /q $(OBJDIR)\nib.* Shell.* Sb16.* *~ 2>nul
    @-rmdir /q /s x86  2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul

##############################################################################

REFERENCES = \
    /reference:System.Xml.dll \
    /reference:System.dll \

SOURCE_FILES = \
    nib.cs \
    ..\WindowsInteropLib\Kernel32.cs \
    

##############################################################################

$(OBJDIR)\nib.exe: $(SOURCE_FILES)
    $(CSC) $(CSCFLAGS) $(REFERENCES) /out:$(OBJDIR)\nib.exe $**

##############################################################################

SHELL_ASSEMBLIES=\
    $(APPSDIR)\Shell.exe \
    $(APPSDIR)\Directory.Contracts.dll \
    $(APPSDIR)\Io.Contracts.dll \
    $(APPSDIR)\FileSystem.Contracts.dll \
    $(LIBSDIR)\FileSystem.Utils.dll \
    $(APPSDIR)\Corlibsg.dll \
    $(ILLSDIR)\Singularity.V1.ill \
    $(APPSDIR)\Corlib.dll \
    $(APPSDIR)\System.Compiler.Runtime.dll \
    $(APPSDIR)\Microsoft.SingSharp.Runtime.dll \
    $(APPSDIR)\ILHelpers.dll \

SB16_ASSEMBLIES=\
    $(DRIVDIR)\Sb16.exe \
    $(APPSDIR)\Directory.Contracts.dll \
    $(APPSDIR)\Io.Contracts.dll \
    $(APPSDIR)\Corlib.dll \
    $(APPSDIR)\Corlibsg.dll \
    $(ILLSDIR)\singularity.V1.ill \
    $(APPSDIR)\System.Compiler.Runtime.dll \
    $(APPSDIR)\Microsoft.SingSharp.Runtime.dll \
    $(APPSDIR)\ILHelpers.dll \

test: $(OBJDIR)\nib.exe $(SHELL_ASSEMBLIES) $(SB16_ASSEMBLIES)
    @-mkdir x86 2> nul
    $(OBJDIR)\mkmani.exe \
        /out:Shell.manifest \
        /app:Shell \
        /x86:Shell.x86 \
        /cache:$(BASEDIR) \
        $(SHELL_ASSEMBLIES)
    $(OBJDIR)\nib.exe \
        /v \
        /cache:$(BASEDIR) \
        /options:$(OBJRELDIR).options \
        /temp:x86 \
        /native:x86 \
        Shell.manifest
    $(OBJDIR)\mkmani.exe \
        /out:Sb16.manifest \
        /app:Sb16 \
        /x86:Sb16.x86 \
        /cache:$(BASEDIR) \
        $(SB16_ASSEMBLIES)
    $(OBJDIR)\nib.exe \
        /v \
        /cache:$(BASEDIR) \
        /options:$(OBJRELDIR).options \
        /temp:x86 \
        /native:x86 \
        Sb16.manifest

##############################################################################
