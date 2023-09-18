using System;
using System.ComponentModel;
using System.Numerics;
using Neo;
using Neo.SmartContract;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace prophetneo
{
    public partial class prophetneo
    {
        public const byte PREFIX_REENTER_FLAG = 0x50;
        private static Transaction tx => (Transaction)Runtime.ScriptContainer;
        private static UInt160 caller => Runtime.CallingScriptHash;
        private static UInt160 self => Runtime.ExecutingScriptHash;
        private static StorageContext context => Storage.CurrentContext;
        private static void PUT(byte k, ByteString v) => Storage.Put(Storage.CurrentContext, new byte[] { k }, v);
        private static void PUT(byte k, BigInteger v) => Storage.Put(Storage.CurrentContext, new byte[] { k }, v);
        private static ByteString GET(byte k) => Storage.Get(Storage.CurrentContext, new byte[] { k });
        private static void Assert(bool b, string v) => ExecutionEngine.Assert(b, v);
        private static void NoReEnter() => Assert(GET(PREFIX_REENTER_FLAG) is null, "!reenter");
        private static void FlagEnter() => PUT(PREFIX_REENTER_FLAG, "o");
        private static void ClearEnter() => Storage.Delete(context, new byte[] { PREFIX_REENTER_FLAG });
    }
}
