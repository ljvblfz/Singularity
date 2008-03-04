///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

using System;

namespace RunParallel
{
    sealed class Util
    {
        public static bool StringIsNullOrEmpty(string s)
        {
            return s == null || s.Length == 0;
        }

#if CLR2
        public static int StringCompareCaseInsensitive(string a, string b)
        {
            return String.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

#else
        public static int StringCompareCaseInsensitive(string a, string b)
        {
            return String.Compare(a, b, true);
        }

#endif

        public static bool IsBlank(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (!Char.IsWhiteSpace(s[i]))
                    return false;
            }
            return true;
        }
    }
}
