//////////////////////////////////////////////////////////////////////////////
//
//  fnames.h - routines for manipulating strings representing filenames
//             and lists of filenames
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#ifndef __FILEMANIP_H__
#define __FILEMANIP_H__

#include "singldr.h"

//////////////////////////////////////////////////////////////////////////////
//
// These are general-purpose functions for manipulating filenames

// return the uppercase equivalent of an ASCII char
char UCase(char c);

// return true if the character is '/', '\t', EOF, CR, or LF
int IsEndToken(char c);

// given a filename, return its length (i.e. foo/boo/waloo
// returns 13)
uint8 FullFNameLength(LPCHAR fname);

// given a filename, the length of the first token
// (i.e. foo/boo/waloo returns 3)
uint8 ShortFNameLength(LPCHAR fname);

// write filename to screen and debugger
void PutFName(LPCHAR fname);

// copy filename to zero terminated string
void FNameToCStr(LPCHAR lpcFName, LPCHAR lpszCStr, UINT32 cbCStr);

#endif
