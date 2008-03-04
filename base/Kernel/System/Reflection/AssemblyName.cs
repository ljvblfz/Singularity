// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
/*============================================================
**
** File:    AssemblyName
**
**
** Purpose: Used for binding and retrieving info about an assembly
**
** Date:    June 4, 1999
**
===========================================================*/
namespace System.Reflection {
    using System.Runtime.CompilerServices;

    [RequiredByBartok]
    public class AssemblyName {
        // ---------- Bartok code ----------

        [RequiredByBartok]
        private String _Culture;

        public String Culture {
            get { return _Culture; }
        }

        // ---------- mscorlib code ----------
        // (some modifications to pull in less code)

        [RequiredByBartok]
        private String          _Name;                  // Name
        [RequiredByBartok]
        private byte[]          _PublicKeyToken;
        [RequiredByBartok]
        private Version         _Version;

        // Set and get the name of the assembly. If this is a weak Name
        // then it optionally contains a site. For strong assembly names,
        // the name partitions up the strong name's namespace
        //| <include path='docs/doc[@for="AssemblyName.Name"]/*' />
        public String Name
        {
            get { return _Name; }
            /* <markples> not needed for now
            set { _Name = value; }
            */
        }

        //| <include path='docs/doc[@for="AssemblyName.Version"]/*' />
        public Version Version
        {
            get {
                return _Version;
            }
            /* <markples> not needed for now
            set {
                _Version = value;
            }
            */
        }

        // The compressed version of the public key formed from a truncated hash.
        //| <include path='docs/doc[@for="AssemblyName.GetPublicKeyToken"]/*' />
        public byte[] GetPublicKeyToken()
        {
            /* <markples> not needed for now
            if ((_PublicKeyToken == null) &&
                (_Flags & AssemblyNameFlags.PublicKey) != 0)
                    _PublicKeyToken = nGetPublicKeyToken();
            */
            return _PublicKeyToken;
        }
    }
}
