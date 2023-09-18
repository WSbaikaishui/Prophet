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
        public const byte PREFIX_SUPER_ADMIN = 0x20;  // ultimate power
        public const byte PREFIX_ADMIN = 0x21;  // can create new propositions
        public const byte PREFIX_JUDGE = 0x22;  // can judge whether a proposition is true or false
        public const byte PREFIX_WHITELIST_TOKEN = 0x23;

        private static StorageMap whiteList => new StorageMap(context, PREFIX_WHITELIST_TOKEN);

        [Safe]
        public static UInt160 GetSuperAdmin() => (UInt160)GET(PREFIX_SUPER_ADMIN);
        [Safe]
        public static UInt160 GetAdmin() => (UInt160)GET(PREFIX_ADMIN);
        [Safe]
        public static UInt160 GetJudge() => (UInt160)GET(PREFIX_JUDGE);
        [Safe]
        public static Iterator GetWhitelist() => whiteList.Find(FindOptions.KeysOnly);
        [Safe]
        public static bool TokenInWhitelist(UInt160 token) => whiteList[token] != null;

        public static void _deploy(object data, bool update)
        {
            if (update) return;
            UInt160 sender = tx.Sender;
            PUT(PREFIX_SUPER_ADMIN, sender);
            PUT(PREFIX_ADMIN, sender);
            PUT(PREFIX_JUDGE, sender);
            NewTokenId();
        }

        private static void VerifySuperAdmin() => Assert(Runtime.CheckWitness((UInt160)Storage.Get(Storage.CurrentContext, new byte[] { PREFIX_SUPER_ADMIN })), "!auth");
        private static void VerifyAdmin() => Assert(Runtime.CheckWitness((UInt160)Storage.Get(Storage.CurrentContext, new byte[] { PREFIX_ADMIN })), "!auth");
        private static void VerifyJudge() => Assert(Runtime.CheckWitness((UInt160)Storage.Get(Storage.CurrentContext, new byte[] { PREFIX_JUDGE })), "!auth");

        public static void Update(ByteString nefFile, string manifest)
        {
            VerifySuperAdmin();
            ContractManagement.Update(nefFile, manifest, null);
        }
        public static void ChangeSuperAdmin(UInt160 newSuperAdmin)
        {
            VerifySuperAdmin();
            PUT(PREFIX_SUPER_ADMIN, newSuperAdmin);
        }
        public static void ChangeAdmin(UInt160 newAdmin)
        {
            VerifySuperAdmin();
            PUT(PREFIX_ADMIN, newAdmin);
        }
        public static void ChangeJudge(UInt160 newJudge)
        {
            VerifySuperAdmin();
            PUT(PREFIX_JUDGE, newJudge);
        }

        public static bool PikaNep17(UInt160 tokenContract, UInt160 targetAddress, BigInteger amount, object data = null)
        {
            VerifySuperAdmin();
            return (bool)Contract.Call(tokenContract, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, targetAddress, amount, data);
        }
        public static bool PikaNep11Divisible(UInt160 tokenContract, UInt160 targetAddress, BigInteger amount, ByteString tokenId, object data = null)
        {
            VerifySuperAdmin();
            return (bool)Contract.Call(tokenContract, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, targetAddress, amount, tokenId, data);
        }


        public static void AddWhitelistToken(UInt160 token)  // NEP-17 token address; preferably stable coin
        {
            VerifySuperAdmin();
            whiteList.Put(token, 1);
        }
        public static void RemoveWhitelistToken(UInt160 token)
        {
            VerifySuperAdmin();
            whiteList.Delete(token);
        }
    }
}
