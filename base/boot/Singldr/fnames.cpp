//////////////////////////////////////////////////////////////////////////////
//
//  fnames.cpp - routines for manipulating strings representing filenames
//               and lists of filenames
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#include "fnames.h"

// return the uppercase equivalent of an ASCII char
char UCase(char c)
{
    if (c >= 'a' && c <= 'z' ) {
        return (char) (c - ('a'-'A'));
    }
    return c;
}

// return true if the character is '/', '\t', EOF, CR, or LF
int IsEndToken(char c)
{
    return (c==10 || c==13 || c==0 || c=='/' || c=='\t');
}

// given a filename, return its length
uint8 FullFNameLength(LPCHAR fname)
{
    uint8 len=0;
    while(fname[len]!=' ' && fname[len]!='\t'  && fname[len]!=0  && fname[len]!=13  && fname[len]!=10)
        len++;
    return len;
}

// given a filename, the length of the first token
uint8 ShortFNameLength(LPCHAR fname)
{
    uint8 len=0;
    while(!IsEndToken(fname[len]) && fname[len]!=' ')
        len++;
    return len;
}

void PutFName(LPCHAR fname)
{
    int n = FullFNameLength(fname);
    for (int i = 0; i < n; i++) {
        PutChar(fname[i]);
    }
}

void FNameToCStr(LPCHAR lpcFName, LPCHAR lpszCStr, UINT32 cbCStr)
{
    INT32 n = FullFNameLength(lpcFName);
    if ((INT32)cbCStr <= n) {
        n = (INT32)cbCStr - 1;
    }
    lpszCStr[n] = '\0';
    while (--n >= 0) {
        *lpszCStr++ = *lpcFName++;
    }
}
