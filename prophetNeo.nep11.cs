using System;
using System.ComponentModel;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;
using Neo.SmartContract.Framework;
using System.Numerics;
using Neo;

namespace prophetneo
{
    [DisplayName("Prophet")]
    [ManifestExtra("Author", "NEO")]
    [ManifestExtra("Email", "developer@neo.org")]
    [ManifestExtra("Description", "Powered by divisible NFT")]
    [SupportedStandards("NEP-11")]
    [ContractPermission("*", "*")]
    public partial class prophetneo : TokenContract
    {
        [DisplayName("Transfer")]
        public static event Action<UInt160, UInt160, BigInteger, ByteString> OnTransfer;
        protected const byte Prefix_TokenId = 0x02;       // largest tokenId
        protected const byte Prefix_Token = 0x03;         // tokenId -> TokenState
        protected const byte Prefix_AccountTokenIdBalance = 0x04;  // owner + tokenId -> amount
        protected const byte Prefix_TokenIdAccountBalance = 0x05;    // (ByteString)(BigInteger)tokenId.Length + tokenId + owner -> amount
        protected const byte Prefix_TokenIdTotalSupply = 0x06;    // tokenId -> totalSupply of this token Id

        protected static StorageMap tokenMap = new(context, Prefix_Token);

        [Safe]
        public override string Symbol() => "FLK::NFTD";
        [Safe]
        public override byte Decimals() => 6;
        [Safe]
        public static new BigInteger TotalSupply() => (BigInteger)Storage.Get(Storage.CurrentContext, new byte[] { Prefix_TotalSupply });
        [Safe]
        public static BigInteger TotalSupply(ByteString tokenId) => (BigInteger)new StorageMap(Storage.CurrentContext, Prefix_TokenIdTotalSupply)[tokenId];
        [Safe]
        public static BigInteger TotalSupply(BigInteger tokenId) => (BigInteger)new StorageMap(Storage.CurrentContext, Prefix_TokenIdTotalSupply)[(ByteString)tokenId];
        [Safe]
        public static BigInteger LastTokenId() => (BigInteger)Storage.Get(context, new byte[] { Prefix_TokenId });

        [Safe]
        public static Iterator OwnerOf(ByteString tokenId)
        {
            if (tokenId.Length > 64) throw new Exception("tokenId.Length > 64");
            return new StorageMap(Storage.CurrentContext, Prefix_TokenIdAccountBalance).Find(
                (ByteString)(BigInteger)tokenId.Length + tokenId,
                FindOptions.RemovePrefix | FindOptions.KeysOnly
                );
        }

        [Safe]
        public static new BigInteger BalanceOf(UInt160 owner)
        {
            if (owner is null || !owner.IsValid)
                throw new Exception("The argument \"owner\" is invalid.");
            StorageMap balanceMap = new(Storage.CurrentContext, Prefix_Balance);
            return (BigInteger)balanceMap[owner];
        }

        [Safe]
        public static BigInteger BalanceOf(UInt160 owner, ByteString tokenId)
        {
            if (!owner.IsValid) throw new Exception("The argument \"owner\" is invalid");
            if (tokenId.Length > 64) throw new Exception("tokenId.Length > 64");
            return (BigInteger)new StorageMap(Storage.CurrentContext, Prefix_AccountTokenIdBalance).Get(owner + tokenId);
        }

        [Safe]
        public static Iterator Tokens()
        {
            StorageMap tokenMap = new(Storage.CurrentContext, Prefix_Token);
            return tokenMap.Find(FindOptions.KeysOnly | FindOptions.RemovePrefix);
        }

        [Safe]
        public static Iterator TokensOf(UInt160 owner)
        {
            if (owner is null || !owner.IsValid)
                throw new Exception("The argument \"owner\" is invalid");
            StorageMap accountMap = new(Storage.CurrentContext, Prefix_AccountTokenIdBalance);
            return accountMap.Find(owner, FindOptions.KeysOnly | FindOptions.RemovePrefix);
        }

        public static bool Transfer(UInt160 from, UInt160 to, BigInteger amount, ByteString tokenId, object data)
        {
            if (!Runtime.CheckWitness(from)) return false;
            if (to is null || !to.IsValid)
                throw new Exception("The argument \"to\" is invalid.");
            if (amount < 0) throw new Exception("amount < 0");
            if (from != to
                  && UpdateBalance(from, tokenId, -amount)
                  && UpdateBalance(to, tokenId, +amount))
                PostTransfer(from, to, tokenId, amount, data);
            else
                return false;
            return true;
        }

        protected static ByteString NewTokenId()
        {
            byte[] key = new byte[] { Prefix_TokenId };
            ByteString id = Storage.Get(context, key);
            Storage.Put(context, key, (BigInteger)id + 1);
            return id;
        }

        protected static void SetTokenState(ByteString tokenId, TokenState token)
        {
            StorageMap tokenMap = new(Storage.CurrentContext, Prefix_Token);
            tokenMap[tokenId] = StdLib.Serialize(token);
        }

        protected static void Mint(UInt160 owner, BigInteger amount, ByteString tokenId, object data = null)
        {
            if (amount <= 0) throw new Exception("mint amount <= 0");
            UpdateBalance(owner, tokenId, amount);
            UpdateTotalSupply(amount);
            UpdateTokenIdTotalSupply(tokenId, amount);
            PostTransfer(null, owner, tokenId, amount, data);
        }

        protected static void Burn(UInt160 owner, BigInteger amount, ByteString tokenId)
        {
            if (amount <= 0) throw new Exception("burn amount <= 0");
            UpdateBalance(owner, tokenId, -amount);
            UpdateTotalSupply(-amount);
            UpdateTokenIdTotalSupply(tokenId, -amount);
            //if (OwnerOf(tokenId) has no element){
            //    StorageMap tokenMap = new(Storage.CurrentContext, Prefix_Token);
            //    TokenState token = (TokenState)StdLib.Deserialize(tokenMap[tokenId]);
            //    tokenMap.Delete(tokenId);
            //}
            PostTransfer(owner, null, tokenId, amount, null);
        }

        protected static bool UpdateBalance(UInt160 owner, ByteString tokenId, BigInteger increment)
        {
            StorageMap allTokenBalanceOfAccountMap = new(Storage.CurrentContext, Prefix_Balance);
            BigInteger allTokenBalance = (BigInteger)allTokenBalanceOfAccountMap[owner];
            allTokenBalance += increment;
            Assert(allTokenBalance >= 0, "allTokenBalance < 0");
            StorageMap accountMap = new(Storage.CurrentContext, Prefix_AccountTokenIdBalance);
            StorageMap tokenOwnerMap = new(Storage.CurrentContext, Prefix_TokenIdAccountBalance);
            ByteString key = owner + tokenId;
            ByteString tokenOwnerKey = (ByteString)(BigInteger)tokenId.Length + tokenId + owner;
            BigInteger currentBalance = (BigInteger)accountMap[key] + increment;
            Assert(currentBalance >= 0, "currentBalance < 0");
            if (allTokenBalance.IsZero)
                allTokenBalanceOfAccountMap.Delete(owner);
            else
                allTokenBalanceOfAccountMap.Put(owner, allTokenBalance);
            if (currentBalance > 0)
            {
                accountMap.Put(key, currentBalance);
                tokenOwnerMap.Put(tokenOwnerKey, currentBalance);
            }
            else
            {
                accountMap.Delete(key);
                tokenOwnerMap.Delete(tokenOwnerKey);
            }
            return true;
        }

        private new protected static void UpdateTotalSupply(BigInteger increment) => PUT(Prefix_TotalSupply, ((BigInteger)GET(Prefix_TotalSupply)) + increment);

        private protected static void UpdateTokenIdTotalSupply(ByteString tokenId, BigInteger increment)
        {
            StorageMap tokenIdTotalSupplyMap = new(Storage.CurrentContext, Prefix_TokenIdTotalSupply);
            tokenIdTotalSupplyMap.Put(tokenId, (BigInteger)tokenIdTotalSupplyMap.Get(tokenId) + increment);
        }

        protected static void PostTransfer(UInt160 from, UInt160 to, ByteString tokenId, BigInteger amount, object data)
        {
            OnTransfer(from, to, amount, tokenId);
            if (to is not null && ContractManagement.GetContract(to) is not null)
                Contract.Call(to, "onNEP11Payment", CallFlags.All, from, amount, tokenId, data);
        }


        [Safe]
        public Map<string, object> Properties(ByteString tokenId)
        {
            StorageMap tokenMap = new(Storage.CurrentContext, Prefix_Token);
            TokenState token = (TokenState)StdLib.Deserialize(tokenMap[tokenId]);
            TokenState liquidityToken = (TokenState)StdLib.Deserialize(tokenMap[token.liquidityTokenId]);
            Map<string, object> map = new();
            if (token.tokenType == TokenType.Liquidity)
            {
                map["proposition"] = propositionMap[tokenId];
                map["tokenType"] = "Liquidity";
            }
            if (token.tokenType == TokenType.True)
            {
                map["proposition"] = propositionMap[token.liquidityTokenId];
                map["tokenType"] = "True";
            }
            if (token.tokenType == TokenType.False)
            {
                map["proposition"] = propositionMap[token.liquidityTokenId];
                map["tokenType"] = "False";
            }

            map["collateralToken"] = liquidityToken.collateralToken;
            map["dueTimeStampMilliseconds"] = liquidityToken.dueTimeStampMilliseconds;
            map["liquidityTokenId"] = liquidityToken.liquidityTokenId;
            map["trueTokenId"] = liquidityToken.trueTokenId;
            map["falseTokenId"] = liquidityToken.falseTokenId;
            
            if (liquidityToken.winnerTokenType == TokenType.Liquidity) map["winnerTokenType"] = "Unknown";
            if (liquidityToken.winnerTokenType == TokenType.True) map["winnerTokenType"] = "True";
            if (liquidityToken.winnerTokenType == TokenType.False) map["winnerTokenType"] = "False";
            return map;
        }
    }
}
