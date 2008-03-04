///////////////////////////////////////////////////////////////////////////////
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

namespace ACPI
{
#if 0
    class Parser
    {
        enum Prefix {
            NullName                = 0x00,
        };

        enum Op {
            ZeroOp                  = 0x00,
            OneOp                   = 0x01,
            AliasOp                 = 0x06,
            NameOp                  = 0x08,
            BytePrefix              = 0x0a,
            WordPrefix              = 0x0b,
            DWordPrefix             = 0x0c,
            QWordPrefix             = 0x0e,
            StringPrefix            = 0x0d,
            ScopeOp                 = 0x10,
            BufferOp                = 0x11,
            PackageOp               = 0x12,
            VarPackageOp            = 0x13,
            MethodOp                = 0x14,
            DualNamePrefix          = 0x2e,
            MultiNamePrefix         = 0x2f,
            // 0x30-0x39 ('0'-'9')  : DigitChar
            // 0x41-0x5a ('A'-'Z')  : LeadNameChars
            ExtOpPrefix             = 0x5b,
            RootChar                = '\\', // 0x5c
            ParentPrefixChar        = '^',  // 0x5e
            // 0x5f ('_')           : LeadNameChars
            Local0Op                = 0x60,
            Local1Op                = 0x61,
            Local2Op                = 0x62,
            Local3Op                = 0x63,
            Local4Op                = 0x64,
            Local5Op                = 0x65,
            Local6Op                = 0x66,
            Local7Op                = 0x67,
            Arg0Op                  = 0x68,
            Arg1Op                  = 0x69,
            Arg2Op                  = 0x6a,
            Arg3Op                  = 0x6b,
            Arg4Op                  = 0x6c,
            Arg5Op                  = 0x6d,
            Arg6Op                  = 0x6e,
            StoreOp                 = 0x70,
            RefOfOp                 = 0x71,
            AddOp                   = 0x72,
            ConcatOp                = 0x73,
            SubtractOp              = 0x74,
            IncrementOp             = 0x75,
            DecrementOp             = 0x76,
            MultiplyOp              = 0x77,
            DivideOp                = 0x78,
            ShiftLeftOp             = 0x79,
            ShiftRightOp            = 0x7a,
            AndOp                   = 0x7b,
            NandOp                  = 0x7c,
            OrOp                    = 0x7d,
            NorOp                   = 0x7e,
            XorOp                   = 0x7f,
            NotOp                   = 0x80,
            FindSetLeftBitOp        = 0x81,
            FindSetRightBitOp       = 0x82,
            DerefOfOp               = 0x83,
            ConcatResOp             = 0x84,
            ModOp                   = 0x85,
            NotifyOp                = 0x86,
            SizeOfOp                = 0x87,
            IndexOp                 = 0x88,
            MatchOp                 = 0x89,
            CreateDWordFieldOp      = 0x8a,
            CreateWordFieldOp       = 0x8b,
            CreateByteFieldOp       = 0x8c,
            CreateBitFieldOp        = 0x8d,
            ObjectTypeOp            = 0x8e,
            CreateQWordFieldOp      = 0x8f,
            LandOp                  = 0x90,
            LorOp                   = 0x91,
            LnotOp                  = 0x92,
            LequalOp                = 0x93,
            LgreaterOp              = 0x94,
            LlessOp                 = 0x95,
            ToBufferOp              = 0x96,
            ToDecimalStringOp       = 0x97,
            ToHexStringOp           = 0x98,
            ToIntegerOp             = 0x99,
            ToStringOp              = 0x9c,
            CopyObjectOp            = 0x9d,
            MidOp                   = 0x9E,
            ContinueOp              = 0x9f,
            IfOp                    = 0xa0,
            ElseOp                  = 0xa1,
            WhileOp                 = 0xa2,
            NoopOp                  = 0xa3,
            ReturnOp                = 0xa4,
            BreakOp                 = 0xa5,
            BreakPointOp            = 0xcc,
            OnesOp                  = 0xff,
        };

        enum ExtOp {    // NB: little-endian (thus 0x305b is 0x5b,0x30)
            MutexOp                 = 0x015b,   /* ExtOpPrefix 0x01 */
            EventOp                 = 0x025b,   /* ExtOpPrefix 0x02 */
            CondRefOfOp             = 0x125b,   /* ExtOpPrefix 0x12 */
            CreateFieldOp           = 0x135b,   /* ExtOpPrefix 0x13 */
            LoadTableOp             = 0x1F5b,   /* ExtOpPrefix 0x1F */
            LoadOp                  = 0x205b,   /* ExtOpPrefix 0x20 */
            StallOp                 = 0x215b,   /* ExtOpPrefix 0x21 */
            SleepOp                 = 0x225b,   /* ExtOpPrefix 0x22 */
            AcquireOp               = 0x235b,   /* ExtOpPrefix 0x23 */
            SignalOp                = 0x245b,   /* ExtOpPrefix 0x24 */
            WaitOp                  = 0x255b,   /* ExtOpPrefix 0x25 */
            ResetOp                 = 0x265b,   /* ExtOpPrefix 0x26 */
            ReleaseOp               = 0x275b,   /* ExtOpPrefix 0x27 */
            FromBCDOp               = 0x285b,   /* ExtOpPrefix 0x28 */
            ToBCDOp                 = 0x295b,   /* ExtOpPrefix 0x29 */
            UnloadOp                = 0x2a5b,   /* ExtOpPrefix 0x2a */
            RevisionOp              = 0x305b,   /* ExtOpPrefix 0x30 */
            DebugOp                 = 0x315b,   /* ExtOpPrefix 0x31 */
            FatalOp                 = 0x325b,   /* ExtOpPrefix 0x32 */
            OpRegionOp              = 0x805b,   /* ExtOpPrefix 0x80 */
            FieldOp                 = 0x815b,   /* ExtOpPrefix 0x81 */
            DeviceOp                = 0x825b,   /* ExtOpPrefix 0x82 */
            ProcessorOp             = 0x835b,   /* ExtOpPrefix 0x83 */
            PowerResOp              = 0x845b,   /* ExtOpPrefix 0x84 */
            ThermalZoneOp           = 0x855b,   /* ExtOpPrefix 0x85 */
            IndexFieldOp            = 0x865b,   /* ExtOpPrefix 0x86 */
            BankFieldOp             = 0x875b,   /* ExtOpPrefix 0x87 */
            DataRegionOp            = 0x885b,   /* ExtOpPrefix 0x88 */
        };

        static Parser *s_pCurrent;

        class ParseOp
        {
            struct ParseOpValues
            {
                const char *    func;
                int             line;
                int             depth;
                int             offset;
                int             retval;

                ParseOpValues()
                {
                }

                ParseOpValues(const char * _func, int _line, int _depth, int _offset)
                {
                    for (const char *psz = _func; *psz; psz++) {
                        if (*psz == ':') {
                            _func = psz + 1;
                        }
                    }
                    if (_func[0] == 'P' &&
                        _func[1] == 'a' &&
                        _func[2] == 'r' &&
                        _func[3] == 's' &&
                        _func[4] == 'e' &&
                        _func[5] != '\0') {
                        _func += 5;
                    }


                    func = _func;
                    line = _line;
                    depth = _depth;
                    offset = _offset;
                    retval = -1;
                }

                void Indent(int n)
                {
                    if (n > 60) {
                        n = 60;
                    }
                    if (n > 0) {
                        printf("%.*s", n, " . . . . . . . . . . . . . . . . . . . . . . .");
                    }
                }

                void PrintDeep(const char *msg = "")
                {
                    Indent(depth);
                    printf(" %s %d %s\n", func, offset, msg);
                }

                void Print()
                {
                    printf(" > %s %d\n", func, offset);
                }

                void PrintValue(const char *label, char *value)
                {
                    Indent(depth);
                    printf(" > %s=[%s]\n", label, value);
                }

                void PrintValue(const char *label, UINT32 value)
                {
                    Indent(depth);
                    printf(" > %s=0x%x\n", label, value);
                }

                void PrintValue(const char *label, UINT64 value)
                {
                    Indent(depth);
                    printf(" > %s=0x%lx\n", label, value);
                }

                void Return(int value)
                {
                    retval = value;
                }
            };

            static ParseOpValues    s_rStack[128];
            static int              s_nStack;
            static ParseOpValues    s_rTrace[512];
            static int              s_nTrace;

          public:
            ParseOp(int offset, const char *func, int line)
            {
                if (s_nStack >= arrayof(s_rStack) - 1) {
                    printf("Stack Overflow (%d of %d):\n",
                           s_nStack, arrayof(s_rStack));
                    Parser::s_pCurrent->Error();
                    Halt();
                }
                s_rStack[s_nStack] = ParseOpValues(func, line, s_nStack, offset);
                s_rStack[s_nStack].PrintDeep();

                s_rTrace[s_nTrace] = s_rStack[s_nStack];

                s_nStack++;
                s_nTrace = (s_nTrace + 1) % arrayof(s_rTrace);
            }

            ~ParseOp()
            {
                s_nStack--;
                if (s_rStack[s_nStack].retval != 0) {
                    s_rStack[s_nStack].PrintDeep(" <<GOOD>>");
                }
                //s_rStack[s_nStack].PrintDeep(" <Return>");
            }

            static bool Good(int offset)
            {
                s_rStack[s_nStack-1].offset = offset;
                s_rStack[s_nStack-1].retval = 1;
                // s_rStack[s_nStack-1].PrintDeep(" <Good>");
                return true;
            }

            static bool Fail(int offset)
            {
                s_rStack[s_nStack-1].offset = offset;
                s_rStack[s_nStack-1].retval = 0;
                // s_rStack[s_nStack-1].PrintDeep(" <Fail>");
                return false;
            }

            static bool Pass(int offset, bool value)
            {
                s_rStack[s_nStack-1].offset = offset;
                s_rStack[s_nStack-1].retval = value;
                // s_rStack[s_nStack-1].PrintDeep(" <Fail>");
                return value;
            }

            static void Error(int offset)
            {
                s_rStack[s_nStack-1].offset = offset;
                s_rStack[s_nStack-1].retval = -2;
                // s_rStack[s_nStack-1].PrintDeep(" <Error>");
                if (s_rStack[s_nStack-1].retval != 0) {
                    s_rStack[s_nStack-1].PrintDeep(" <<Error>>");
                }
            }

            static void PrintValue(const char *label, char * value)
            {
                s_rStack[s_nStack-1].PrintValue(label, value);
            }

            static void PrintValue(const char *label, int value)
            {
                s_rStack[s_nStack-1].PrintValue(label, (UINT32)value);
            }

            static void PrintValue(const char *label, UINT value)
            {
                s_rStack[s_nStack-1].PrintValue(label, (UINT32)value);
            }

            static void PrintValue(const char *label, UINT32 value)
            {
                s_rStack[s_nStack-1].PrintValue(label, value);
            }

            static void PrintValue(const char *label, UINT64 value)
            {
                s_rStack[s_nStack-1].PrintValue(label, value);
            }

            static void Dump()
            {
#if 0
                printf("Trace:\n");
                for (int n = 0; n < arrayof(s_rTrace); n++) {
                    int t = (s_nTrace + n) % arrayof(s_rTrace);

                    if (s_rTrace[t].func != NULL) {
                        s_rTrace[t].PrintDeep();
                    }
                }
#endif
                printf("Stack:\n");
                for (int n = 0; n < s_nStack; n++) {
                    s_rStack[n].Print();
                }
            }

        };

#define PARSEOP() ParseOp _self(m_pbStream - m_pbBegin, __FUNCTION__, __LINE__)
#define PARSEVAL(label,value) _self.PrintValue(label,value)
#define PARSEVALX(label) _self.PrintValue(#label,label)
#define PARSEGOOD() _self.Good(m_pbStream - m_pbBegin)
#define PARSEFAIL() _self.Fail(m_pbStream - m_pbBegin)
#define PARSEPASS(x) _self.Pass(m_pbStream - m_pbBegin,x)

      public:
        Parser(UINT8 *_pbStream, UINT32 _cbStream)
        {
            m_pbBegin = _pbStream;
            m_pbLimit = _pbStream + _cbStream;
            m_pbStream = _pbStream;
            s_pCurrent = this;
        }

      private:
        UINT8 Peek() const
        {
            return Peek8();
        }

        UINT8 Peek8() const
        {
            return (m_pbStream < m_pbLimit) ? *m_pbStream : 0;
        }

        UINT16 Peek16() const
        {
            return (m_pbStream + 1 <= m_pbLimit) ? *((UINT16*&)m_pbStream) : 0;
        }

        UINT8 Pop()
        {
            return Pop8();
        }

        UINT8 Pop8()
        {
            return (m_pbStream + sizeof(UINT8) <= m_pbLimit) ? *((UINT8*&)m_pbStream)++ : 0;
        }

        UINT16 Pop16()
        {
            return (m_pbStream + sizeof(UINT16) <= m_pbLimit) ? *((UINT16*&)m_pbStream)++ : 0;
        }

        UINT32 Pop32()
        {
            return (m_pbStream + sizeof(UINT32) <= m_pbLimit) ? *((UINT32*&)m_pbStream)++ : 0;
        }

        UINT64 Pop64()
        {
            return (m_pbStream + sizeof(UINT64) <= m_pbLimit) ? *((UINT64*&)m_pbStream)++ : 0;
        }

        bool Error()
        {
            printf("Parsing Error.\n");
            Dump(m_pbStream, m_pbLimit - m_pbStream < 16 ? m_pbLimit - m_pbStream : 16);
            ParseOp::Dump();
            ParseOp::Error(m_pbStream - m_pbBegin);
            Halt();
            return false;
        }

      private:
        UINT8 * m_pbBegin;
        UINT8 * m_pbLimit;
        UINT8 * m_pbStream;
        CHAR    m_szName[256];
        CHAR *  m_pszName;
        UINT32  m_cbLength;
        UINT8 * m_pbPkgLimit;

        //////////////////////////////////////////////////////////////////////
        //

        //////////////////////////////////////////////////////////////////////////
        //
        bool ParseNameSeg()
        {
            PARSEOP();

            //NameSeg         := <LeadNameChar NameChar NameChar NameChar>
            //                    // Notice that NameSegs shorter than 4 characters are
            //                    // filled with trailing '_'s.

            UINT8 c = Peek();

            if (!(c >= 'A' && c <= 'Z') && !(c == '_')) {
                return PARSEFAIL();
            }

            m_pszName[0] = Pop();
            for (int i = 1; i <= 3; i++) {
                c = Peek();
                if (!(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') &&
                    !(c == '_')) {
                    return PARSEFAIL();
                }
                m_pszName[i] = Pop();
            }
            m_pszName[4] = '\0';
            // Remove trailing underscores.
            for (int i = 3; i > 1 && m_pszName[i] == '_'; i--) {
                m_pszName[i] = '\0';
            }
            while (*m_pszName != '\0') {
                m_pszName++;
            }
            return PARSEGOOD();
        }

        bool CanBeNameString(UINT8 c)
        {
            return ((c == RootChar) ||
                    (c == ParentPrefixChar) ||
                    (c >= 'A' && c <= 'Z') ||
                    (c == '_') ||
                    (c == DualNamePrefix) ||
                    (c == MultiNamePrefix) ||
                    (c == '\0'));
        }

        bool ParseNameString()
        {
            PARSEOP();

            //NameString      := <RootChar NamePath> | <PrefixPath NamePath>
            //PrefixPath      := Nothing | <'^' PrefixPath>
            //NamePath        := NameSeg | DualNamePath | MultiNamePath | NullName
            //DualNamePath    := DualNamePrefix NameSeg NameSeg
            //DualNamePrefix  := 0x2e
            //MultiNamePath   := MultiNamePrefix SegCount NameSeg(SegCount)
            //MultiNamePrefix := 0x2f
            //SegCount        := ByteData

            m_pszName = m_szName;
            m_szName[0] = '0';

            UINT8 c = Peek();

            if (c == RootChar || c == ParentPrefixChar) {
                *m_pszName++ = Pop();
            }

            if ((c >= 'A' && c <= 'Z') || (c == '_')) {
                if (!ParseNameSeg()) {
                    return Error();
                }
                PARSEVAL("name", m_szName);
                return PARSEGOOD();
            }
            else if (c == DualNamePrefix) {
                Pop();
                if (!ParseNameSeg()) {
                    return Error();
                }
                *m_pszName++ = '.';
                if (!ParseNameSeg()) {
                    return Error();
                }
                PARSEVAL("name", m_szName);
                return PARSEGOOD();
            }
            else if (c == MultiNamePrefix) {
                Pop();
                for (c = Pop(); c > 0; c--) {
                    if (!ParseNameSeg()) {
                        return Error();
                    }
                    if (c > 0) {
                        *m_pszName++ = '.';
                    }
                }
                PARSEVAL("name", m_szName);
                return PARSEGOOD();
            }
            else if (c == '\0') {
                Pop();
                *m_pszName = '\0';
                PARSEVAL("name", m_szName);
                return PARSEGOOD();
            }
            return PARSEFAIL();
        }

        bool ParsePkgLength()
        {
            PARSEOP();

            //PkgLength       := PkgLeadByte | <PkgLeadByte ByteData> | <PkgLeadByte ByteData ByteData> | <PkgLeadByte ByteData ByteData ByteData>
            //PkgLeadByte     := <bit 7-6: follow ByteData count> <bit 5-4: reserved> <bit 3-0: least significant package length byte>
            // Note: The high 2 bits of the first byte reveal how
            //      many follow bytes are in the PkgLength. If the
            //      PkgLength has only one byte, bit 0 through 5 are
            //      used to encode the package length (in other words,
            //  values 0-63).
            //      If the package length value is more than
            //      63, more than one byte must be used for the
            //      encoding in which case bit 5 and 4 of the
            //      PkgLeadByte are reserved and must be zero. If
            //      multiple bytes encoding is used, bits 3-0 of the
            //      PkgLeadByte become the least significant 4 bits
            //      of the resulting package length value. The next
            //      ByteData will become the next least significant
            //      8 bits of the resulting value and so on.
            m_pbPkgLimit = m_pbStream;
            UINT8 c = Pop();

            m_cbLength = 0;

            if (c >= 0x00 && c <= 0x3f) {
                m_cbLength = c;
                m_pbPkgLimit += m_cbLength;
                PARSEVAL("PkgLength",m_cbLength);
                ::Dump(m_pbStream, m_pbPkgLimit, 128);
                return PARSEGOOD();
            }
            else if (c >= 0x40 && c <= 0x4f) {
                m_cbLength = (c & 0xf);
                m_cbLength |= (Pop() << 4);
                m_pbPkgLimit += m_cbLength;
                PARSEVAL("PkgLength",m_cbLength);
                ::Dump(m_pbStream, m_pbPkgLimit, 128);
                return PARSEGOOD();
            }
            else if (c >= 0x80 && c <= 0x8f) {
                m_cbLength = (c & 0xf);
                m_cbLength |= (Pop() << 4);
                m_cbLength |= (Pop() << 12);
                m_pbPkgLimit += m_cbLength;
                PARSEVAL("PkgLength",m_cbLength);
                ::Dump(m_pbStream, m_pbPkgLimit, 128);
                return PARSEGOOD();
            }
            else if (c >= 0xc0 && c <= 0xcf) {
                m_cbLength = (c & 0xf);
                m_cbLength |= (Pop() << 4);
                m_cbLength |= (Pop() << 12);
                m_cbLength |= (Pop() << 20);
                m_pbPkgLimit += m_cbLength;
                PARSEVAL("PkgLength",m_cbLength);
                ::Dump(m_pbStream, m_pbPkgLimit, 128);
                return PARSEGOOD();
            }
            return Error();
        }

        bool ParseSimpleName()
        {
            PARSEOP();

            UINT8 c = Peek();

            if (CanBeNameString(c)) {
                return PARSEPASS(ParseNameString());
            }

            switch (c) {
              case Arg0Op:
              case Arg1Op:
              case Arg2Op:
              case Arg3Op:
              case Arg4Op:
              case Arg5Op:
              case Arg6Op:
                return PARSEPASS(ParseArgObj());

              case Local0Op:
              case Local1Op:
              case Local2Op:
              case Local3Op:
              case Local4Op:
              case Local5Op:
              case Local6Op:
              case Local7Op:
                return PARSEPASS(ParseLocalObj());
            }
            return PARSEGOOD();
        }

        bool ParseSuperName()
        {
            PARSEOP();

            // SimpleName
            if (CanBeNameString(Peek())) {                   // Ambiguous
                return PARSEPASS(ParseNameString());
            }

            switch (Peek()) {
              case Arg0Op:
              case Arg1Op:
              case Arg2Op:
              case Arg3Op:
              case Arg4Op:
              case Arg5Op:
              case Arg6Op:
                return PARSEPASS(ParseArgObj());

              case Local0Op:
              case Local1Op:
              case Local2Op:
              case Local3Op:
              case Local4Op:
              case Local5Op:
              case Local6Op:
              case Local7Op:
                return PARSEPASS(ParseLocalObj());

                // Type6Opcode
              case RefOfOp: return PARSEPASS(ParseDefRefOf());
              case DerefOfOp: return PARSEPASS(ParseDefDerefOf());
              case IndexOp: return PARSEPASS(ParseDefIndex());

                // DebugObj
              case ExtOpPrefix:
                switch (Peek16()) {
                  case DebugOp: Pop16(); return PARSEGOOD();
                }
                break;
            }

            // Last option of Type6Opcode
            if (CanBeNameString(Peek())) {                   // Ambiguous
                if (!ParseUserTermObj()) {
                    return Error();
                }
                return PARSEGOOD();
            }

            return PARSEFAIL();
        }

        bool ParseArgObj()
        {
            PARSEOP();

            switch (Peek()) {
              case Arg0Op:
              case Arg1Op:
              case Arg2Op:
              case Arg3Op:
              case Arg4Op:
              case Arg5Op:
              case Arg6Op:
                Pop();
                return PARSEGOOD();
            }
            return PARSEFAIL();
        }

        bool ParseLocalObj()
        {
            PARSEOP();

            switch (Peek()) {
              case Local0Op:
              case Local1Op:
              case Local2Op:
              case Local3Op:
              case Local4Op:
              case Local5Op:
              case Local6Op:
              case Local7Op:
                Pop();
                return PARSEGOOD();
            }
            return PARSEFAIL();
        }

        bool ParseDDBHandleObject()
        {
            PARSEOP();

            return ParseSuperName();
        }

        //////////////////////////////////////////////////////////////// Ugly!
        //
        bool ParseDataRefObject()   //
        {
            PARSEOP();

            if (!ParseDataObject() &&
                !ParseTermArg() /*ObjectReference*/ &&
                !ParseDDBHandleObject()) {
                return PARSEFAIL();
            }
            return PARSEGOOD();
        }
        bool ParseDataObject()   //
        {
            PARSEOP();

            if (ParseComputationalData() ||
                ParseDefPackage() ||
                ParseDefVarPackage()) {

                return PARSEGOOD();
            }
            return PARSEFAIL();
        }

        bool ParseTermArg()   //
        {
            PARSEOP();

            if (!ParseType2Opcode() &&
                !ParseDataObject() &&
                !ParseArgObj() &&
                !ParseLocalObj()) {
                return PARSEFAIL();
            }
            return PARSEGOOD();
        }

        bool ParseUserTermObj()   //
        {
            PARSEOP();

            // UserTermObj     := NameString TermArgList
            if (ParseNameString()) {
                while (ParseTermArg()) {
                }
                return PARSEGOOD();
            }
            return PARSEFAIL();
        }

        bool ParsePackageElementList(UINT8 *pbLimit)   //
        {
            PARSEOP();

            while (m_pbStream < pbLimit) {
                if (!ParseDataRefObject() &&
                    !ParseNameString()) {
                    return Error();
                }
            }
            if (m_pbStream != pbLimit) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseTermList(UINT8 *pbLimit)   //
        {
            PARSEOP();

            while (m_pbStream < pbLimit) {
                if (!ParseNameSpaceModifierObj() &&
                    !ParseNamedObj() &&
                    !ParseType1Opcode() &&
                    !ParseType2Opcode()) {
                    return Error();
                }
            }
            if (m_pbStream != pbLimit) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseObjectList(UINT8 *pbLimit)   //
        {
            PARSEOP();

            while (m_pbStream < pbLimit) {
                if (!ParseNameSpaceModifierObj() &&
                    !ParseNamedObj()) {
                    return Error();
                }
            }
            if (m_pbStream != pbLimit) {
                return Error();
            }
            return PARSEGOOD();
        }
        //
        //////////////////////////////////////////////////////////////////////

        bool ParseNameSpaceModifierObj()
        {
            PARSEOP();

            switch (Peek()) {
              case AliasOp: return ParseDefAlias();
              case NameOp: return ParseDefName();
              case ScopeOp: return ParseDefScope();
            }
            return PARSEFAIL();
        }

        bool ParseNamedObj()
        {
            PARSEOP();

            switch (Peek()) {
              case CreateBitFieldOp: return ParseDefCreateBitField();
              case CreateByteFieldOp: return ParseDefCreateByteField();
              case CreateDWordFieldOp: return ParseDefCreateDWordField();
              case CreateQWordFieldOp: return ParseDefCreateQWordField();
              case CreateWordFieldOp: return ParseDefCreateWordField();
              case MethodOp: return ParseDefMethod();

              case ExtOpPrefix:
                switch (Peek16()) {
                  case BankFieldOp: return ParseDefBankField();
                  case MutexOp: return ParseDefMutex();
                  case CreateFieldOp: return ParseDefCreateField();
                  case EventOp: return ParseDefEvent();
                  case FieldOp: return ParseDefField();
                  case OpRegionOp: return ParseDefOpRegion();
                  case DeviceOp: return ParseDefDevice();
                  case ProcessorOp: return ParseDefProcessor();
                  case PowerResOp: return ParseDefPowerRes();
                  case ThermalZoneOp: return ParseDefThermalZone();
                  case IndexFieldOp: return ParseDefIndexField();
                  case DataRegionOp: return ParseDefDataRegion();
                }
                break;
            }
            return PARSEFAIL();
        }

        bool ParseType1Opcode()
        {
            PARSEOP();

            switch (Peek()) {
              case BreakOp: return ParseDefBreak();
              case BreakPointOp: return ParseDefBreakPoint();
              case ContinueOp: return ParseDefContinue();
              case IfOp: return ParseDefIfElse();
              case NoopOp: return ParseDefNoop();
              case NotifyOp: return ParseDefNotify();
              case ReturnOp: return ParseDefReturn();
              case WhileOp: return ParseDefWhile();

              case ExtOpPrefix:
                switch (Peek16()) {
                  case LoadOp: return ParseDefLoad();
                  case StallOp: return ParseDefStall();
                  case SleepOp: return ParseDefSleep();
                  case SignalOp: return ParseDefSignal();
                  case ResetOp: return ParseDefReset();
                  case ReleaseOp: return ParseDefRelease();
                  case UnloadOp: return ParseDefUnload();
                  case FatalOp: return ParseDefFatal();
                }
                break;
            }
            return PARSEFAIL();
        }

        bool ParseString()
        {
            PARSEOP();

            if (Peek() != StringPrefix) {
                return PARSEFAIL();
            }
            Pop();
            m_pszName = m_szName;
            m_szName[0] = '\0';
            while (Peek() != 0) {
                if (Peek() >= 0x7f) {
                    return Error();
                }
                *m_pszName = Pop();
            }
            if (Peek() != 0) {
                return Error();
            }
            Pop();
            *m_pszName = '\0';
            PARSEVAL("string", m_szName);
            return PARSEGOOD();
        }

        bool ParseComputationalData()
        {
            PARSEOP();

            //ComputationalData       := ByteConst | WordConst | DwordConst | QwordConst | String | ConstObj | RevisionOp | DefBuffer

            switch (Peek()) {
              case BytePrefix: Pop(); PARSEVAL("byte", Pop8()); return PARSEGOOD();
              case WordPrefix: Pop(); PARSEVAL("word", Pop16()); return PARSEGOOD();
              case DWordPrefix: Pop(); PARSEVAL("dword", Pop32()); return PARSEGOOD();
              case QWordPrefix: Pop(); PARSEVAL("qword", Pop64()); return PARSEGOOD();
              case StringPrefix: return ParseString();

                // ConstObj
              case ZeroOp: Pop(); PARSEVAL("zero", 0); return PARSEGOOD();
              case OneOp: Pop(); PARSEVAL("one", 1); return PARSEGOOD();
              case OnesOp: Pop(); PARSEVAL("ones", 0xff); return PARSEGOOD();

              case ExtOpPrefix:
                switch (Peek16()) {
                  case RevisionOp: Pop16(); return PARSEGOOD();
                }
                break;

              case BufferOp: return ParseDefBuffer();
            }
            return PARSEFAIL();
        }

        bool ParseTermArgInteger()
        {
            PARSEOP();

            if (!ParseTermArg()) {
                return PARSEFAIL();
            }
            // TermArg=>Integer
            return PARSEGOOD();
        }

        bool ParseTermArgBuffer()
        {
            PARSEOP();

            if (!ParseTermArg()) {
                return PARSEFAIL();
            }
            // TermArg=>Buffer
            return PARSEGOOD();
        }

        bool ParseTermArgDataRefObject()
        {
            PARSEOP();

            if (!ParseTermArg()) {
                return PARSEFAIL();
            }
            // TermArg=>DataRefObject
            return PARSEGOOD();
        }

        bool ParseTermArgByteData()
        {
            PARSEOP();

            if (!ParseTermArg()) {
                return PARSEFAIL();
            }
            // TermArg=>ByteData
            return PARSEGOOD();
        }

        bool ParseTermArgComputationalData()
        {
            PARSEOP();

            if (!ParseTermArg()) {
                return PARSEFAIL();
            }
            // TermArg=>ComputationalData
            return PARSEGOOD();
        }

        bool ParseType2Opcode()
        {
            PARSEOP();

            UINT8 c = Peek();

            switch (c) {
              case AddOp: return ParseDefAdd();
              case AndOp: return ParseDefAnd();
              case BufferOp: return ParseDefBuffer();
              case ConcatOp: return ParseDefConcat();
              case ConcatResOp: return ParseDefConcatRes();
              case CopyObjectOp: return ParseDefCopyObject();
              case DecrementOp: return ParseDefDecrement();
              case DerefOfOp: return ParseDefDerefOf();
              case DivideOp: return ParseDefDivide();
              case FindSetLeftBitOp: return ParseDefFindSetLeftBit();
              case FindSetRightBitOp: return ParseDefFindSetRightBit();
              case IncrementOp: return ParseDefIncrement();
              case IndexOp: return ParseDefIndex();
              case LandOp: return ParseDefLAnd();
              case LequalOp: return ParseDefLEqual();
              case LgreaterOp: return ParseDefLGreater();
              case LlessOp: return ParseDefLLess();
              case MidOp: return ParseDefMid();
              case LnotOp: return ParseDefLNot();
              case LorOp: return ParseDefLOr();
              case MatchOp: return ParseDefMatch();
              case ModOp: return ParseDefMod();
              case MultiplyOp: return ParseDefMultiply();
              case NandOp: return ParseDefNAnd();
              case NorOp: return ParseDefNOr();
              case NotOp: return ParseDefNot();
              case ObjectTypeOp: return ParseDefObjectType();
              case OrOp: return ParseDefOr();
              case PackageOp: return ParseDefPackage();
              case VarPackageOp: return ParseDefVarPackage();
              case RefOfOp: return ParseDefRefOf();
              case ShiftLeftOp: return ParseDefShiftLeft();
              case ShiftRightOp: return ParseDefShiftRight();
              case SizeOfOp: return ParseDefSizeOf();
              case StoreOp: return ParseDefStore();
              case SubtractOp: return ParseDefSubtract();
              case ToBufferOp: return ParseDefToBuffer();
              case ToDecimalStringOp: return ParseDefToDecimalString();
              case ToHexStringOp: return ParseDefToHexString();
              case ToIntegerOp: return ParseDefToInteger();
              case ToStringOp: return ParseDefToString();
              case XorOp: return ParseDefXOr();
              case ExtOpPrefix:
                switch (Peek16()) {
                  case AcquireOp: return ParseDefAcquire();
                  case CondRefOfOp: return ParseDefCondRefOf();
                  case LoadTableOp: return ParseDefLoadTable();
                  case WaitOp: return ParseDefWait();
                  case FromBCDOp: return ParseDefFromBCD();
                  case ToBCDOp: return ParseDefToBCD();
                }
                break;
            }

            if (CanBeNameString(c)) {
                if (!ParseUserTermObj()) {
                    return Error();
                }
                return PARSEGOOD();
            }
            return PARSEFAIL();
        }

        bool ParseFieldList(UINT8 *pbLimit)
        {
            PARSEOP();

            while (m_pbStream < pbLimit) {
                UINT8 c = Peek();
                switch (c) {
                  case 0x00:
                    if (ParseReservedField()) {
                        continue;
                    }
                    break;
                  case 0x01:
                    if (ParseAccessField()) {
                        continue;
                    }
                    break;
                }

                if ((c >= 'A' && c <= 'Z') || (c == '_')) {
                    if (ParseNamedField()) {
                        continue;
                    }
                }
                break;
            }

            if (m_pbStream != pbLimit) {
                return Error();
            }
            return PARSEGOOD();
        }

        //////////////////////////////////////////////////////////////////////////////
        //
        bool ParseNamedField()
        {
            PARSEOP();

            m_pszName = m_szName;
            m_szName[0] = '0';

            if (!ParseNameSeg()) {
                return PARSEFAIL();
            }
            PARSEVAL("field", m_szName);

            if (!ParsePkgLength()) {
                return PARSEFAIL();
            }
            // Skip over Pkg.
            m_pbStream = m_pbPkgLimit;
            return PARSEGOOD();
        }

        bool ParseReservedField()
        {
            PARSEOP();

            if (Peek() != 0x00) {
                return PARSEFAIL();
            }
            Pop();
            if (!ParsePkgLength()) {
                return PARSEFAIL();
            }
            // Skip over Pkg.
            m_pbStream = m_pbPkgLimit;
            return PARSEGOOD();
        }

        bool ParseAccessField()
        {
            PARSEOP();

            if (Peek() != 0x01) {
                return PARSEFAIL();
            }
            Pop();
            UINT8 AccessType = Pop();
            PARSEVALX(AccessType);
            UINT8 AccessAttrib = Pop();
            PARSEVALX(AccessAttrib);
            return PARSEGOOD();
        }

        ////////////////////////////////////////////////// Guarded and Simple.
        //
        //  These functions are only called from functions that begin with
        //  a specific Opcode (i.e. by a ParseDef* function.  They are also
        //  deterministic and don't call conditionally any subfunctions.
        //
        bool ParsePredicate()
        {
            PARSEOP();

            if (!ParseTermArgInteger()) {
                return PARSEFAIL();
            }
            // Predicate := TermArg=>Integer
            return PARSEGOOD();
        }

        bool ParseTarget()
        {
            PARSEOP();

            if (Peek() == NullName) {
                Pop();
                return PARSEGOOD();
            }
            if (!ParseSuperName()) {
                return PARSEFAIL();
            }
            return PARSEGOOD();
        }

        bool ParseOperand()
        {
            PARSEOP();

            if (!ParseTermArgInteger()) {
                return PARSEFAIL();
            }
            // TermArg=>Integer
            return PARSEGOOD();
        }

        bool ParseMutexObject() //
        {
            PARSEOP();

            return ParseSuperName();
        }

        bool ParseEventObject() //
        {
            PARSEOP();

            return ParseSuperName();
        }

        ///////////////////////////////////////////////////// bool ParseDef*()
        //
        //  These function all begin with a specific OpCode.  They are also
        //  deterministic and don't call conditionally any subfunctions.
        //
        bool ParseDefAlias()
        {
            PARSEOP();

            if (Peek() != AliasOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseNameString()) {
                return Error();
            }
            if (!ParseNameString()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefName()
        {
            PARSEOP();

            if (Peek() != NameOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseNameString()) {
                return Error();
            }
            if (!ParseDataRefObject()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefCreateDWordField()
        {
            PARSEOP();

            if (Peek() != CreateDWordFieldOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArgBuffer()) {
                return Error();
            }
            if (!ParseTermArgInteger()) {
                return PARSEFAIL();
            }
            // ByteIndex       := TermArg=>Integer
            if (!ParseNameString()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefCreateQWordField()
        {
            PARSEOP();

            if (Peek() != CreateQWordFieldOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArgBuffer()) {
                return Error();
            }
            if (!ParseTermArgInteger()) {
                return PARSEFAIL();
            }
            // ByteIndex       := TermArg=>Integer
            if (!ParseNameString()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefCreateWordField()
        {
            PARSEOP();

            if (Peek() != CreateWordFieldOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArgBuffer()) {
                return Error();
            }
            if (!ParseTermArgInteger()) {
                return PARSEFAIL();
            }
            // ByteIndex       := TermArg=>Integer
            if (!ParseNameString()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefBreak()
        {
            PARSEOP();

            if (Peek() != BreakOp) {
                return PARSEFAIL();
            }
            Pop();

            return PARSEGOOD();
        }

        bool ParseDefBreakPoint()
        {
            PARSEOP();

            if (Peek() != BreakPointOp) {
                return PARSEFAIL();
            }
            Pop();

            return PARSEGOOD();
        }

        bool ParseDefContinue()
        {
            PARSEOP();

            if (Peek() != ContinueOp) {
                return PARSEFAIL();
            }
            Pop();

            return PARSEGOOD();
        }

        bool ParseDefNoop()
        {
            PARSEOP();

            if (Peek() != NoopOp) {
                return PARSEFAIL();
            }
            Pop();

            return PARSEGOOD();
        }

        bool ParseDefWhile()
        {
            PARSEOP();

            if (Peek() != WhileOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            if (!ParsePredicate()) {
                return Error();
            }
            if (!ParseTermList(pbLimit)) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefAnd()
        {
            PARSEOP();

            if (Peek() != AndOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefCopyObject()
        {
            PARSEOP();

            if (Peek() != CopyObjectOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArg()) {
                return Error();
            }
            if (!ParseSimpleName()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefDecrement()
        {
            PARSEOP();

            if (Peek() != DecrementOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseSuperName()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefFindSetLeftBit()
        {
            PARSEOP();

            if (Peek() != FindSetLeftBitOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefFindSetRightBit()
        {
            PARSEOP();

            if (Peek() != FindSetRightBitOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefIncrement()
        {
            PARSEOP();

            if (Peek() != IncrementOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseSuperName()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefLAnd()
        {
            PARSEOP();

            if (Peek() != LandOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseOperand()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefLEqual()
        {
            PARSEOP();

            if (Peek() != LequalOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseOperand()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefLGreater()
        {
            PARSEOP();

            if (Peek() != LgreaterOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseOperand()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefLLess()
        {
            PARSEOP();

            if (Peek() != LlessOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseOperand()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefLOr()
        {
            PARSEOP();

            if (Peek() != LorOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseOperand()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefMod()
        {
            PARSEOP();

            if (Peek() != ModOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArgInteger()) {
                return Error();
            }
            //Dividend := TermArg=>Integer
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //Dividend := TermArg=>Integer
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefMultiply()
        {
            PARSEOP();

            if (Peek() != MultiplyOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefNAnd()
        {
            PARSEOP();

            if (Peek() != NandOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefNOr()
        {
            PARSEOP();

            if (Peek() != NorOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefNot()
        {
            PARSEOP();

            if (Peek() != NotOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefObjectType()
        {
            PARSEOP();

            if (Peek() != ObjectTypeOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseSuperName()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefOr()
        {
            PARSEOP();

            if (Peek() != OrOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefRefOf()
        {
            PARSEOP();

            if (Peek() != RefOfOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseSuperName()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefShiftRight()
        {
            PARSEOP();

            if (Peek() != ShiftRightOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //ShiftCount := TermArg=>Integer
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefSizeOf()
        {
            PARSEOP();

            if (Peek() != SizeOfOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseSuperName()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefStore()
        {
            PARSEOP();

            if (Peek() != StoreOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArg()) {
                return Error();
            }
            if (!ParseSuperName()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefSubtract()
        {
            PARSEOP();

            if (Peek() != SubtractOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefToBuffer()
        {
            PARSEOP();

            if (Peek() != ToBufferOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefToDecimalString()
        {
            PARSEOP();

            if (Peek() != ToDecimalStringOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefToHexString()
        {
            PARSEOP();

            if (Peek() != ToHexStringOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefToInteger()
        {
            PARSEOP();

            if (Peek() != ToIntegerOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefXOr()
        {
            PARSEOP();

            if (Peek() != XorOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefDataRegion()
        {
            PARSEOP();

            if (Peek16() != DataRegionOp) {
                return PARSEFAIL();
            }
            Pop16();

            if (!ParseNameString()) {
                return Error();
            }
            if (!ParseTermArg()) {
                return Error();
            }
            if (!ParseTermArg()) {
                return Error();
            }
            if (!ParseTermArg()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefDevice()
        {
            PARSEOP();

            if (Peek16() != DeviceOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            if (!ParseNameString()) {
                return Error();
            }
            if (!ParseObjectList(pbLimit)) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefEvent()
        {
            PARSEOP();

            if (Peek16() != EventOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParseNameString()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefField()
        {
            PARSEOP();

            if (Peek16() != FieldOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            if (!ParseNameString()) {
                return Error();
            }
            UINT8 FieldFlags = Pop();
            PARSEVALX(FieldFlags);
            if (!ParseFieldList(pbLimit)) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefIndexField()
        {
            PARSEOP();

            if (Peek16() != IndexFieldOp) {
                return PARSEFAIL();
            }
            Pop16();

            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            if (!ParseNameString()) {
                return Error();
            }
            if (!ParseNameString()) {
                return Error();
            }
            UINT8 FieldFlags = Pop();
            PARSEVALX(FieldFlags);

            if (!ParseFieldList(pbLimit)) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefThermalZone()
        {
            PARSEOP();

            if (Peek16() != ThermalZoneOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            if (!ParseNameString()) {
                return Error();
            }
            if (!ParseObjectList(pbLimit)) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefLoad()
        {
            PARSEOP();

            if (Peek16() != LoadOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParseNameString()) {
                return Error();
            }
            if (!ParseDDBHandleObject()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefRelease()
        {
            PARSEOP();

            if (Peek16() != ReleaseOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParseMutexObject()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefReset()
        {
            PARSEOP();

            if (Peek16() != ResetOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParseEventObject()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefSignal()
        {
            PARSEOP();

            if (Peek16() != SignalOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParseEventObject()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefUnload()
        {
            PARSEOP();

            if (Peek16() != UnloadOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParseDDBHandleObject()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefCondRefOf()
        {
            PARSEOP();

            if (Peek16() != CondRefOfOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParseSuperName()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefLoadTable()
        {
            PARSEOP();

            if (Peek16() != LoadTableOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParseTermArg()) {
                return Error();
            }
            if (!ParseTermArg()) {
                return Error();
            }
            if (!ParseTermArg()) {
                return Error();
            }
            if (!ParseTermArg()) {
                return Error();
            }
            if (!ParseTermArg()) {
                return Error();
            }
            if (!ParseTermArg()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefToBCD()
        {
            PARSEOP();

            if (Peek16() != ToBCDOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefWait()
        {
            PARSEOP();

            if (Peek16() != WaitOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParseEventObject()) {
                return Error();
            }
            if (!ParseOperand()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefMutex()
        {
            PARSEOP();

            if (Peek16() != MutexOp) {
                return PARSEFAIL();
            }
            Pop16();

            if (!ParseNameString()) {
                return PARSEFAIL();
            }
            UINT8 SyncFlags = Pop();
            PARSEVALX(SyncFlags);

            return PARSEGOOD();
        }

        bool ParseDefPowerRes()
        {
            PARSEOP();

            if (Peek16() != PowerResOp) {
                return PARSEFAIL();
            }
            Pop16();

            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            if (!ParseNameString()) {
                return Error();
            }
            UINT8 SystemLevel = Pop();
            PARSEVALX(SystemLevel);

            UINT16 ResourceOrder = Pop16();
            PARSEVALX(ResourceOrder);

            if (!ParseObjectList(pbLimit)) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefProcessor()
        {
            PARSEOP();

            if (Peek16() != ProcessorOp) {
                return PARSEFAIL();
            }
            Pop16();

            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            if (!ParseNameString()) {
                return Error();
            }
            UINT8 ProcId = Pop();
            PARSEVALX(ProcId);

            UINT32 PblkAddr = Pop32();
            PARSEVALX(PblkAddr);

            UINT32 PblkLen = Pop();
            PARSEVALX(PblkLen);


            if (!ParseObjectList(pbLimit)) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefMethod()
        {
            PARSEOP();

            if (Peek() != MethodOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            if (!ParseNameString()) {
                return Error();
            }
            UINT8 MethodFlags = Pop();
            PARSEVALX(MethodFlags);


            if (!ParseTermList(pbLimit)) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefAcquire()
        {
            PARSEOP();

            if (Peek16() != AcquireOp) {
                return PARSEFAIL();
            }
            Pop16();

            if (!ParseMutexObject()) {
                return PARSEFAIL();
            }
            UINT16 Timeout = Pop16();
            PARSEVALX(Timeout);

            return PARSEGOOD();
        }

        bool ParseDefPackage()
        {
            PARSEOP();

            if (Peek() != PackageOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            UINT8 NumElements = Pop();
            PARSEVALX(NumElements);


            if (!ParsePackageElementList(pbLimit)) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefVarPackage()
        {
            PARSEOP();

            if (Peek() != VarPackageOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            if (!ParseTermArgInteger()) {
                return Error();
            }
            // TermArg=>Integer;

            if (!ParsePackageElementList(pbLimit)) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefCreateField()
        {
            PARSEOP();

            if (Peek16() != CreateFieldOp) {
                return PARSEFAIL();
            }
            Pop16();

            if (!ParseTermArgBuffer()) {
                return Error();
            }
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //BitIndex := TermArg=>Integer
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //NumBits := TermArg=>Integer
            if (!ParseNameString()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefSleep()
        {
            PARSEOP();

            if (Peek16() != SleepOp) {
                return PARSEFAIL();
            }
            Pop16();

            if (!ParseTermArgInteger()) {
                return Error();
            }
            //MsecTime := TermArg=>Integer
            return PARSEGOOD();
        }

        bool ParseDefStall()
        {
            PARSEOP();

            if (Peek16() != StallOp) {
                return PARSEFAIL();
            }
            Pop16();

            if (!ParseTermArgByteData()) {
                return Error();
            }
            //UsecTime := TermArg=>ByteData
            return PARSEGOOD();
        }

        bool ParseDefFromBCD()
        {
            PARSEOP();

            if (Peek16() != FromBCDOp) {
                return PARSEFAIL();
            }
            Pop16();

            if (!ParseTermArgInteger()) {
                return Error();
            }
            //BCDValue := TermArg=>Integer
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefShiftLeft()
        {
            PARSEOP();

            if (Peek() != ShiftLeftOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //ShiftCount := TermArg=>Integer
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefToString()
        {
            PARSEOP();

            if (Peek() != ToStringOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArg()) {
                return Error();
            }
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //LengthArg := TermArg=>Integer
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefMid()
        {
            PARSEOP();

            if (Peek() != MidOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArg()) {
                return Error();
            }
            //MidObj := TermArg=>Buffer|String
            if (!ParseTermArg()) {
                return Error();
            }
            if (!ParseTermArg()) {
                return Error();
            }
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefCreateBitField()
        {
            PARSEOP();

            if (Peek() != CreateBitFieldOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArgBuffer()) {
                return Error();
            }
            //SourceBuff := TermArg=>Buffer
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //BitIndex := TermArg=>Integer
            if (!ParseNameString()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefCreateByteField()
        {
            PARSEOP();

            if (Peek() != CreateByteFieldOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArgBuffer()) {
                return Error();
            }
            //SourceBuff := TermArg=>Buffer
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //ByteIndex := TermArg=>Integer
            if (!ParseNameString()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefReturn()
        {
            PARSEOP();

            if (Peek() != ReturnOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArg()) {
                return Error();
            }
            //ArgObject := TermArg=>DataRefObject
            return PARSEGOOD();
        }

        bool ParseDefAdd()
        {
            PARSEOP();

            if (Peek() != AddOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //Operand := TermArg=>Integer
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefBuffer()
        {
            PARSEOP();

            if (Peek() != BufferOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //BufferSize := TermArg=>Integer
            // Skip over data.
            m_pbStream = pbLimit;
            return PARSEGOOD();
        }

        bool ParseDefConcat()
        {
            PARSEOP();

            if (Peek() != ConcatOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArgComputationalData()) {
                return Error();
            }
            //Data := TermArg=>ComputationalData
            if (!ParseTermArgComputationalData()) {
                return Error();
            }
            //Data := TermArg=>ComputationalData
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefConcatRes()
        {
            PARSEOP();

            if (Peek() != ConcatResOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArgBuffer()) {
                return Error();
            }
            //BufData := TermArg=>Buffer
            if (!ParseTermArgBuffer()) {
                return Error();
            }
            //BufData := TermArg=>Buffer
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefDivide()
        {
            PARSEOP();

            if (Peek() != DivideOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArgInteger()) {
                return Error();
            }
            //Dividend := TermArg=>Integer
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //Divisor := TermArg=>Integer
            if (!ParseTarget()) {
                return Error();
            }
            // Remainder
            if (!ParseTarget()) {
                return Error();
            }
            // Quotient
            return PARSEGOOD();
        }

        bool ParseDefIndex()
        {
            PARSEOP();

            if (Peek() != IndexOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArg()) {
                return Error();
            }
            //BuffPkgStrObj := TermArg=>Buffer, Package or String
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //IndexValue := TermArg=>Integer
            if (!ParseTarget()) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefDerefOf()
        {
            PARSEOP();

            if (Peek() != DerefOfOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParseTermArg()) {
                return Error();
            }
            //ObjReference := TermArg=>ObjectReference|String
            return PARSEGOOD();
        }

        bool ParseDefOpRegion()
        {
            PARSEOP();

            if (Peek16() != OpRegionOp) {
                return PARSEFAIL();
            }
            Pop16();
            if (!ParseNameString()) {
                return Error();
            }
            UINT RegionSpace = Pop();
            PARSEVALX(RegionSpace);

            if (!ParseTermArgInteger()) {
                return Error();
            }
            //RegionOffset    := TermArg=>Integer
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //RegionLen       := TermArg=>Integer
            return PARSEGOOD();
        }

        bool ParseDefFatal()
        {
            PARSEOP();

            if (Peek16() != FatalOp) {
                return PARSEFAIL();
            }
            Pop16();
            UINT8 FatalType = Pop();
            PARSEVALX(FatalType);
            UINT32 FatalCode = Pop32();
            PARSEVALX(FatalCode);
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //FatalArg        := TermArg=>Integer
            return PARSEGOOD();
        }

        bool ParseDefNotify()
        {
            PARSEOP();

            if (Peek() != NotifyOp) {
                return PARSEFAIL();
            }
            Pop();
            if (!ParseSuperName()) {
                return Error();
            }
            //NotifyObject    := SuperName=>ThermalZone|Processor|Device
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //NotifyValue     := TermArg=>Integer
            return PARSEGOOD();
        }

        bool ParseDefMatch()
        {
            PARSEOP();

            if (Peek() != MatchOp) {
                return PARSEFAIL();
            }
            Pop();
            if (!ParseTermArg()) {
                return Error();
            }
            //SearchPkg       := TermArg=>Package
            UINT8 MatchOpcode1 = Pop();
            PARSEVALX(MatchOpcode1);
            if (!ParseOperand()) {
                return Error();
            }
            UINT8 MatchOpcode2 = Pop();
            PARSEVALX(MatchOpcode2);
            if (!ParseOperand()) {
                return Error();
            }
            if (!ParseTermArgInteger()) {
                return Error();
            }
            //StartIndex      := TermArg=>Integer
            return PARSEGOOD();
        }

        bool ParseDefScope()
        {
            PARSEOP();

            if (Peek() != ScopeOp) {
                return PARSEFAIL();
            }
            Pop();

            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8* pbLimit = m_pbPkgLimit;
            if (!ParseNameString()) {
                return Error();
            }
            if (!ParseTermList(pbLimit)) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefBankField()
        {
            PARSEOP();

            if (Peek16() != BankFieldOp) {
                return PARSEFAIL();
            }
            Pop16();

            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            if (!ParseNameString()) {
                return Error();
            }
            if (!ParseNameString()) {
                return Error();
            }
            if (!ParseTermArgInteger()) {
                return Error();
            }
            // BankValue := TermArg=>Integer
            UINT8 FieldFlags = Pop();
            PARSEVALX(FieldFlags);

            if (!ParseFieldList(pbLimit)) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefIfElse()
        {
            PARSEOP();

            if (Peek() != IfOp) {
                return PARSEFAIL();
            }
            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            if (!ParsePredicate()) {
                return Error();
            }
            if (!ParseTermList(pbLimit)) {
                return Error();
            }
            ParseDefElse();
            return PARSEGOOD();

        }

        bool ParseDefElse()
        {
            PARSEOP();

            if (Peek() != ElseOp) {
                return PARSEFAIL();
            }
            Pop();
            if (!ParsePkgLength()) {
                return Error();
            }
            UINT8 *pbLimit = m_pbPkgLimit;
            if (!ParseTermList(pbLimit)) {
                return Error();
            }
            return PARSEGOOD();
        }

        bool ParseDefLNot()
        {
            PARSEOP();

            if (Peek() != LnotOp) {
                return PARSEFAIL();
            }
            Pop();

            switch (Peek()) {
              case LlessOp:
              case LgreaterOp:
              case LequalOp:
                Pop();
                if (!ParseOperand()) {
                    return Error();
                }
                if (!ParseOperand()) {
                    return Error();
                }
                return PARSEGOOD();

              default:
                if (!ParseOperand()) {
                    return Error();
                }
                return PARSEGOOD();
            }
            return PARSEFAIL();
        }

      public:
        bool Parse()
        {
            PARSEOP();

            return ParseTermList(m_pbLimit);
        }
    };

    Parser::ParseOp::ParseOpValues Parser::ParseOp::s_rStack[128];
    int Parser::ParseOp::s_nStack;
    Parser::ParseOp::ParseOpValues Parser::ParseOp::s_rTrace[512];
    int Parser::ParseOp::s_nTrace;
    Parser *Parser::s_pCurrent = NULL;
#endif
}
