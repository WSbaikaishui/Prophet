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

        public const byte PREFIX_PROPOSITION = 0x40;  // liuquidityTokenId -> string proposition
        private static StorageMap propositionMap = new(context, PREFIX_PROPOSITION);

        public const byte PREFIX_UNJUDGED_TOKEN_IDS = 0x46;  // tokenId -> liquidityTokenId; for lookup only
        private static StorageMap unjudgedTokenIdMap => new(context, PREFIX_UNJUDGED_TOKEN_IDS);
        public const byte PREFIX_WINNING_TOKEN_IDS = 0x47;  // winnning tokenId -> liquidityTokenId; only winning tokenIds are saved as keys; for lookup only

        [DisplayName("Created")]
        public static event Action<ByteString, ByteString, ByteString> OnCreated;
        [DisplayName("Judged")]
        public static event Action<ByteString, ByteString> OnJudged;

        [Safe]
        public static string GetProposition(ByteString tokenId) => new StorageMap(Storage.CurrentContext, PREFIX_PROPOSITION)[tokenId];
        [Safe]

        public static Iterator FindUnjudgedTokens(ByteString prefix) => new StorageMap(Storage.CurrentContext, PREFIX_UNJUDGED_TOKEN_IDS).Find(prefix, FindOptions.RemovePrefix);
        [Safe]
        public static Iterator FindWinningTokens(ByteString prefix) => new StorageMap(Storage.CurrentContext, PREFIX_WINNING_TOKEN_IDS).Find(prefix, FindOptions.RemovePrefix);


        public static ByteString[] CreatePropositionPair(string proposition, BigInteger dueTimeStampMilliseconds, UInt160 collateralToken)
        {
            VerifyAdmin();
            Assert(dueTimeStampMilliseconds > Runtime.Time, "invalid due time");
            ByteString liquidityTokenId = NewTokenId();
            ByteString trueTokenId = NewTokenId();
            ByteString falseTokenId = NewTokenId();
            propositionMap[liquidityTokenId] = proposition;
            SetTokenState(liquidityTokenId, new TokenState { tokenType = TokenType.Liquidity, collateralToken = collateralToken, dueTimeStampMilliseconds = dueTimeStampMilliseconds, winnerTokenType = TokenType.Liquidity, liquidityTokenId = liquidityTokenId, trueTokenId = trueTokenId, falseTokenId = falseTokenId });
            SetTokenState(trueTokenId, new TokenState { tokenType = TokenType.True, liquidityTokenId = liquidityTokenId });
            SetTokenState(falseTokenId, new TokenState { tokenType = TokenType.False, liquidityTokenId = liquidityTokenId });
            unjudgedTokenIdMap.Put(liquidityTokenId, liquidityTokenId);
            OnCreated(liquidityTokenId, trueTokenId, falseTokenId);
            return new ByteString[] { trueTokenId, falseTokenId };
        }

        public static void Judge(ByteString tokenId)  // the winning token id
        {
            VerifyJudge();
            TokenState token = (TokenState)StdLib.Deserialize(tokenMap[tokenId]);
            TokenState liquidityToken = (TokenState)StdLib.Deserialize(tokenMap[token.liquidityTokenId]);
            Assert(liquidityToken.dueTimeStampMilliseconds < Runtime.Time, "Due time not reached.");
            Assert(liquidityToken.winnerTokenType == TokenType.Liquidity, "Already judged.");
            liquidityToken.winnerTokenType = token.tokenType;
            SetTokenState(token.liquidityTokenId, liquidityToken);
            unjudgedTokenIdMap.Delete(token.liquidityTokenId);
            new StorageMap(PREFIX_WINNING_TOKEN_IDS).Put(tokenId, 1);
            OnJudged(liquidityToken.liquidityTokenId, tokenId);
        }

        private static ByteString counterpartTokenId(TokenState token) {
            TokenState liquidityToken = (TokenState)StdLib.Deserialize(tokenMap[token.liquidityTokenId]);
            if (token.tokenType == TokenType.True) return liquidityToken.falseTokenId;
            else if (token.tokenType == TokenType.False) return liquidityToken.trueTokenId;
            Assert(false, "invalid token type, no counterpartToken");
            return "";
        }
    }

    
    public enum TokenType
    {
        Liquidity = 0,
        True = 1,
        False = 2,
    }
    public class TokenState
    {
        public TokenType tokenType;
        public ByteString liquidityTokenId;
        public TokenType winnerTokenType; // only stored in liquidity token
        public UInt160 collateralToken; // only stored in liquidity token
        public BigInteger dueTimeStampMilliseconds; // only stored in liquidity token
        public ByteString trueTokenId; // only stored in liquidity token
        public ByteString falseTokenId; // only stored in liquidity token
    }
}