using System;
using System.Collections.Generic;
using System.Text;
using libsecondlife;
using libsecondlife.Packets;

namespace libsecondlife.TestTool
{
    public class PrimCountCommand: Command
    {
		public PrimCountCommand()
		{
			Name = "primCount";
			Description = "Shows the number of prims that have been received.";
		}

		public override string Execute(string[] args, LLUUID fromAgentID)
		{
			return TestTool.Prims.Count.ToString();
		}
    }
}