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
        public const byte PREFIX_TRADING_LIQUIDITY = 0x30;  // tokenId -> liquidity available for trading. Can be provided by providers, and traders who bought the counterpart token. Reduced by traders buying the corresponding token.

        public const byte PREFIX_TMP_TOKEN_RECEIVED = 0x38; // tokenId => amount
        private static StorageMap tmpTokenReceivedMap = new StorageMap(context, PREFIX_TMP_TOKEN_RECEIVED);
        private static StorageMap tradingLiquidity = new StorageMap(context, PREFIX_TRADING_LIQUIDITY);

        public static void OnNEP17Payment(UInt160 from, BigInteger amount, object[] data)
        {
            Assert(amount > 0, "Invalid amount desired");
            switch ((ByteString)data[0])
            {
                case "deposit":
                    Deposit(from, (ByteString)data[1], amount);
                    break;
                default:
                    throw new Exception("Wrong data");
            }
        }
        public static void OnNEP11Payment(UInt160 from, BigInteger amount, ByteString tokenId, object[] data)
        {
            Assert(amount > 0, "Invalid amount desired");
            Assert(caller == self, "invalid token");
            switch ((ByteString)data[0])
            {
                case "redeem":
                    Redeem(from, tokenId, amount);
                    break;
                case "winnerRedeem":
                    WinnerRedeem(from, tokenId, amount);
                    break;
                case "buy":
                    Buy(from, tokenId, amount, (BigInteger)data[1], (BigInteger)data[2]);
                    break;
                case "removeLiquidity":
                    RemoveLiquidity(from, tokenId, amount, (BigInteger)data[1], (BigInteger)data[2], (BigInteger)data[3]);
                    break;
                case "addLiquidity":
                    AddLiquidity(from, tokenId, amount, (BigInteger)data[1], (BigInteger)data[2], (BigInteger)data[3]);
                    break;
                default:
                    throw new Exception("Wrong data");
            }
        }


        [DisplayName("Deposit")]
        public static event Action<UInt160, ByteString, BigInteger> OnDeposit;
        /// <summary>
        /// Give me 100 fUSDT. I will give you 100 True tokens and 100 False tokens
        /// </summary>
        private static void Deposit(UInt160 sender, ByteString liquidityTokenId, BigInteger amount)
        {
            TokenState liquidityToken = (TokenState)StdLib.Deserialize(tokenMap[liquidityTokenId]);
            Assert(caller == liquidityToken.collateralToken, "Wrong collateral");
            Assert(Runtime.Time <= liquidityToken.dueTimeStampMilliseconds, "Token due timestamp exceeded");

            Mint(sender, amount, liquidityToken.trueTokenId, null);
            Mint(sender, amount, liquidityToken.falseTokenId, null);
            OnDeposit(sender, liquidityTokenId, amount);
        }

        [DisplayName("Redeem")]
        private static event Action<UInt160, ByteString, BigInteger> OnRedeem;
        /// <summary>
        /// Give me 100 True tokens and then 100 False tokens. I will burn these True & False tokens and give 100 fUSDT back to you
        /// </summary>
        private static void Redeem(UInt160 sender, ByteString tokenId, BigInteger amount)
        {
            TokenState token = (TokenState)StdLib.Deserialize(tokenMap[tokenId]);

            // deposit this true first and wait for the false token
            if (token.tokenType == TokenType.True)
            {
                Burn(self, amount, tokenId);
                tmpTokenReceivedMap.Put(token.liquidityTokenId, amount);
                return;
            }
            Assert(token.tokenType == TokenType.False, "invalid token type");
            Assert((BigInteger)tmpTokenReceivedMap.Get(token.liquidityTokenId) == amount, "invalid amount");
            tmpTokenReceivedMap.Delete(token.liquidityTokenId);
            Burn(self, amount, tokenId);
            TokenState liquidityToken = (TokenState)StdLib.Deserialize(tokenMap[token.liquidityTokenId]);
            Assert((bool)Contract.Call(liquidityToken.collateralToken, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, sender, amount, null), "transfer failed");
            OnRedeem(sender, liquidityToken.liquidityTokenId, amount);
        }
        private static void WinnerRedeem(UInt160 sender, ByteString tokenId, BigInteger amount)
        {
            TokenState token = (TokenState)StdLib.Deserialize(tokenMap[tokenId]);
            Assert(token.tokenType != TokenType.Liquidity, "liquidity token cannot win");
            TokenState liquidityToken = (TokenState)StdLib.Deserialize(tokenMap[token.liquidityTokenId]);
            Assert(liquidityToken.winnerTokenType == token.tokenType, "not winner");

            Burn(self, amount, tokenId);
            Assert((bool)Contract.Call(liquidityToken.collateralToken, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, sender, amount, null), "transfer failed");
            OnRedeem(sender, liquidityToken.liquidityTokenId, amount);
        }

        public static BigInteger GetTradingLiquidity(ByteString tokenId) => (BigInteger)new StorageMap(Storage.CurrentContext, PREFIX_TRADING_LIQUIDITY)[tokenId];

        public const ulong FEE_NOMINATOR = 99_4000_0000;
        public const ulong FEE_DENOMINATOR = 100_0000_0000;

        public static BigInteger GetFeeNominator() => FEE_NOMINATOR;
        public static BigInteger GetFeeDenominator() => FEE_DENOMINATOR;

        public static BigInteger GetAmountOut(ByteString tokenIdOut, BigInteger amountIn)
        {
            return GetAmountOut((TokenState)StdLib.Deserialize(tokenMap[tokenIdOut]), tokenIdOut, amountIn);
        }

        private static BigInteger GetAmountOut(TokenState tokenOut, ByteString tokenIdOut, BigInteger amountIn)
        {
            BigInteger tokenOutLiquiditySupply = GetTradingLiquidity(tokenIdOut);
            BigInteger tokenInLiquiditySupply = GetTradingLiquidity(counterpartTokenId(tokenOut));
            BigInteger originalProduct = tokenInLiquiditySupply * tokenOutLiquiditySupply;
            BigInteger amountOut = tokenOutLiquiditySupply - (originalProduct / (tokenInLiquiditySupply + amountIn * FEE_NOMINATOR / FEE_DENOMINATOR) + 1);
            Assert((tokenOutLiquiditySupply - amountOut) * (tokenInLiquiditySupply + amountIn) >= originalProduct, "Product");
            return amountOut;
        }

        public static BigInteger GetAmountIn(ByteString tokenIdOut, BigInteger amountOut)
        {
            return GetAmountIn((TokenState)StdLib.Deserialize(new StorageMap(Storage.CurrentContext, Prefix_Token)[tokenIdOut]), tokenIdOut, amountOut);
        }

        private static BigInteger GetAmountIn(TokenState tokenOut, ByteString tokenIdOut, BigInteger amountOut)
        {
            BigInteger tokenOutLiquiditySupply = GetTradingLiquidity(tokenIdOut);
            BigInteger tokenInLiquiditySupply = GetTradingLiquidity(counterpartTokenId(tokenOut));
            BigInteger originalProduct = tokenInLiquiditySupply * tokenOutLiquiditySupply;
            BigInteger amountIn = ((tokenInLiquiditySupply * tokenOutLiquiditySupply / (tokenOutLiquiditySupply - amountOut)) - tokenInLiquiditySupply) * FEE_DENOMINATOR / FEE_NOMINATOR + 1;
            Assert((tokenOutLiquiditySupply - amountOut) * (tokenInLiquiditySupply + amountIn) >= originalProduct, "Product");
            return amountIn;
        }


        [DisplayName("Buy")]
        public static event Action<UInt160, ByteString, ByteString, BigInteger, BigInteger> OnBuy;  // sender, tokenIdIn, tokenIdOut, amountIn, amountOut
        private static BigInteger Buy(UInt160 from, ByteString tokenIdIn, BigInteger amountIn, BigInteger minAmountOut, BigInteger deadlineTimeStampMilliseconds)
        {
            NoReEnter();
            FlagEnter();
            TokenState tokenIn = (TokenState)StdLib.Deserialize(tokenMap[tokenIdIn]);
            TokenState liquidityToken = (TokenState)StdLib.Deserialize(tokenMap[tokenIn.liquidityTokenId]);
            ByteString tokenIdOut = counterpartTokenId(tokenIn);
            TokenState tokenOut = (TokenState)StdLib.Deserialize(tokenMap[tokenIdOut]);

            Assert(Runtime.Time <= deadlineTimeStampMilliseconds, "Transaction deadline exceeded");
            Assert(Runtime.Time <= liquidityToken.dueTimeStampMilliseconds, "Token due timestamp exceeded");

            BigInteger amountOut = GetAmountOut(tokenOut, tokenIdOut, amountIn);
            Assert(amountOut >= minAmountOut, "No enough amount out");

            tradingLiquidity.Put(tokenIdIn, (BigInteger)tradingLiquidity[tokenIdIn] + amountIn);
            tradingLiquidity.Put(tokenIdOut, (BigInteger)tradingLiquidity[tokenIdOut] - amountOut);

            Assert(Transfer(self, from, amountOut, tokenIdOut, null), "transferOut failed");
            OnBuy(from, tokenIdIn, tokenIdOut, amountIn, amountOut);
            ClearEnter();
            return amountOut;
        }

        [DisplayName("AddLiquidity")]
        public static event Action<UInt160, ByteString, ByteString, BigInteger, BigInteger> OnAddLiquidity;  // from, tokenIdA, tokenIdB, amountA, amountB
        /// <summary>
        /// Call this method twice in a single transaction. In the first call, transfer some false token In. In the second call, transfer some true token In, and specify amountAMin, amountBMin carefully
        /// </summary>
        /// <param name="from"></param>
        /// <param name="tokenIdA"></param>
        /// <param name="amountAMax">Actual amount of token transferred into this contract, representing the max amount you can accept</param>
        /// <param name="amountAMin"></param>
        /// <param name="amountBMin"></param>
        /// <param name="deadlineTimeStampMilliseconds"></param>
        /// <returns></returns>
        private static BigInteger[] AddLiquidity(UInt160 from, ByteString tokenIdA, BigInteger amountAMax, BigInteger amountAMin, BigInteger amountBMin, BigInteger deadlineTimeStampMilliseconds)
        {
            NoReEnter();
            FlagEnter();

            Assert(Runtime.Time <= deadlineTimeStampMilliseconds, "Transaction deadline exceeded");
            StorageMap tokenMap = new StorageMap(Storage.CurrentContext, Prefix_Token);
            TokenState tokenA = (TokenState)StdLib.Deserialize(tokenMap[tokenIdA]);
            TokenState liquidityToken = (TokenState)StdLib.Deserialize(tokenMap[tokenA.liquidityTokenId]);
            Assert(Runtime.Time <= liquidityToken.dueTimeStampMilliseconds, "Token due timestamp exceeded");
            ByteString tokenIdB = counterpartTokenId(tokenA);

            // deposit this token first and wait for the other token
            BigInteger amountBMax = (BigInteger)tmpTokenReceivedMap.Get(tokenIdB);
            if (amountBMax == 0)  // first call
            {
                tmpTokenReceivedMap.Put(tokenIdA, amountAMax);
                ClearEnter();
                return null;
            }

            tmpTokenReceivedMap.Delete(tokenIdB);

            BigInteger tradingLiquidityA = (BigInteger)tradingLiquidity[tokenIdA];
            BigInteger tradingLiquidityB = (BigInteger)tradingLiquidity[tokenIdB];

            BigInteger amountA, amountB;
            if (tradingLiquidityA == tradingLiquidityB && tradingLiquidityB == 0)  // initial offering of liquidity
            {
                amountA = amountAMax < amountBMax ? amountAMax : amountBMax;
                amountB = amountA;
            }
            else
            {
                amountB = amountAMax * tradingLiquidityB / tradingLiquidityA;
                if (amountB <= amountBMax)
                {
                    ExecutionEngine.Assert(amountB >= amountBMin, "No enough B");
                    amountA = amountAMax;
                }
                else
                {
                    amountB = amountBMax;
                    amountA = amountBMax * tradingLiquidityA / tradingLiquidityB;
                    ExecutionEngine.Assert(amountA <= amountAMax, "Excess A");
                    ExecutionEngine.Assert(amountA >= amountAMin, "No enough A");
                }
            }

            BigInteger liquidityAmount = amountA * amountB;

            Assert(liquidityAmount > 0, "Must addLiquidity to both tokens");

            tradingLiquidity.Put(tokenIdA, tradingLiquidityA + amountA);
            tradingLiquidity.Put(tokenIdB, tradingLiquidityB + amountB);

            Mint(from, liquidityAmount, liquidityToken.liquidityTokenId, null);

            liquidityAmount = amountAMax - amountA;  // This is actually returned amount. I am reusing the variable
            if (liquidityAmount > 0)
                Assert(Transfer(self, from, liquidityAmount, tokenIdA, null), "return A failed");
            liquidityAmount = amountBMax - amountB;  // This is actually returned amount. I am reusing the variable
            if (liquidityAmount > 0)
                Assert(Transfer(self, from, liquidityAmount, tokenIdB, null), "return B failed");

            OnAddLiquidity(from, tokenIdA, tokenIdB, amountA, amountB);
            ClearEnter();
            return new BigInteger[] { amountA, amountB };
        }
        [DisplayName("RemoveLiquidity")]
        public static event Action<UInt160, ByteString, ByteString, BigInteger, BigInteger> OnRemoveLiquidity;  // sender, tokenIdA, tokenIdB, reduceLiquidityA, reduceLiquidityB, redeemedAmount
        private static BigInteger[] RemoveLiquidity(UInt160 from, ByteString tokenId, BigInteger amount, BigInteger amountTrueMin, BigInteger amountFalseMin, BigInteger deadlineTimeStampMilliseconds)
        {
            NoReEnter();
            FlagEnter();

            Assert(Runtime.Time <= deadlineTimeStampMilliseconds, "transaction deadline exceeded");
            TokenState liquidityToken = (TokenState)StdLib.Deserialize(tokenMap[tokenId]);
            Assert(liquidityToken.tokenType == TokenType.Liquidity, "invalid token type");
            Assert(amountTrueMin > 0 && amountFalseMin > 0, "invalid amount desired");

            ByteString tokenIdT = liquidityToken.trueTokenId;
            ByteString tokenIdF = liquidityToken.falseTokenId;

            BigInteger tradingLiquidityT = (BigInteger)tradingLiquidity[tokenIdT];
            BigInteger tradingLiquidityF = (BigInteger)tradingLiquidity[tokenIdF];
            Assert(tradingLiquidityT > 0 && tradingLiquidityF > 0, "No liquidity in pool");

            BigInteger redeemAmountT, redeemAmountF;
            redeemAmountT = amount * tradingLiquidityT / TotalSupply(tokenId);
            redeemAmountF = amount * tradingLiquidityF / TotalSupply(tokenId);

            Assert(redeemAmountT >= amountTrueMin, "amountT >= minT");
            Assert(redeemAmountF >= amountFalseMin, "amountF >= minF");

            tradingLiquidity.Put(tokenIdT, tradingLiquidityT - redeemAmountT);
            tradingLiquidity.Put(tokenIdF, tradingLiquidityF - redeemAmountF);

            Burn(self, amount, liquidityToken.liquidityTokenId);

            Assert(Transfer(self, from, redeemAmountT, tokenIdT, null), "transfer T failed");
            Assert(Transfer(self, from, redeemAmountF, tokenIdF, null), "transfer F failed");

            OnRemoveLiquidity(from, tokenIdT, tokenIdF, redeemAmountT, redeemAmountF);

            ClearEnter();
            return new BigInteger[] { redeemAmountT, redeemAmountF };
        }
    }
}
