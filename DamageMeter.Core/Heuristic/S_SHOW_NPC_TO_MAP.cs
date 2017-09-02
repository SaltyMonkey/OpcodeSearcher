﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tera.Game.Messages;

namespace DamageMeter.Heuristic
{
    class S_SHOW_NPC_TO_MAP : AbstractPacketHeuristic
    {

        public new void Process(ParsedMessage message)
        {
            base.Process(message);
            if (IsKnown || OpcodeFinder.Instance.IsKnown(message.OpCode)) { return; }
            if(message.Payload.Count != 4) { return; }
            if (!OpcodeFinder.Instance.IsKnown(OpcodeEnum.S_LOGIN)) { return; }
            OpcodeFinder.Instance.SetOpcode(message.OpCode, OPCODE);
        }
    }
}