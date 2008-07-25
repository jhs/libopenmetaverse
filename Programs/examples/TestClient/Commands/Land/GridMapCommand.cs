using System;
using System.Collections.Generic;
using System.Text;
using OpenMetaverse;
using OpenMetaverse.Packets;

namespace OpenMetaverse.TestClient
{
    public class GridMapCommand : Command
    {
        public GridMapCommand(TestClient testClient)
        {
            Name = "gridmap";
            Description = "Downloads all visible information about the grid map";
        }

        public override string Execute(string[] args, UUID fromAgentID)
        {
            //if (args.Length < 1)
            //    return "";

            Client.Grid.RequestMainlandSims(GridLayerType.Objects);
            
            return "Sent.";
        }
    }
}
