﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tera.Game.Messages;

namespace DamageMeter.Heuristic
{
    class S_TRADE_BROKER_DEAL_SUGGESTED : AbstractPacketHeuristic
    {
        public static S_TRADE_BROKER_DEAL_SUGGESTED Instance => _instance ?? (_instance = new S_TRADE_BROKER_DEAL_SUGGESTED());
        private static S_TRADE_BROKER_DEAL_SUGGESTED _instance;

        public S_TRADE_BROKER_DEAL_SUGGESTED() : base(OpcodeEnum.S_TRADE_BROKER_DEAL_SUGGESTED) { }

        public new void Process(ParsedMessage message)
        {
            base.Process(message);
            if (IsKnown || OpcodeFinder.Instance.IsKnown(message.OpCode)) return;

            if (message.Payload.Count < 2 + 4 + 4 + 4 + 8 + 8 + 8 + 4) return;

            //could also check on specific item/player/amount/money
            var nameOffset = Reader.ReadUInt16();
            if(nameOffset != 0x2A) return;
            
            var playerId = Reader.ReadUInt32();
            var listing = Reader.ReadUInt32();
            var item = Reader.ReadUInt32();
            var amount = Reader.ReadUInt64();
            var sellerPrice = Reader.ReadUInt64();
            var offeredPrice = Reader.ReadUInt64();
            try
            {
                //Reader.BaseStream.Position = nameOffset - 4;
                var name = Reader.ReadTeraString();
                if (name.Length * 2 + 2 + 4 + 4 + 4 + 8 + 8 + 8 + 2 != message.Payload.Count) return;
            }
            catch (Exception e){return;}

            OpcodeFinder.Instance.SetOpcode(message.OpCode, OPCODE);
        }
    }
}
