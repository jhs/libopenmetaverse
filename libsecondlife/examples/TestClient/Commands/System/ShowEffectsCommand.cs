using System;
using OpenMetaverse;

namespace OpenMetaverse.TestClient
{
    public class ShowEffectsCommand : Command
    {
        bool ShowEffects = false;

        public ShowEffectsCommand(TestClient testClient)
        {
            Name = "showeffects";
            Description = "Prints out information for every viewer effect that is received. Usage: showeffects [on/off]";

            testClient.Avatars.OnEffect += new AvatarManager.EffectCallback(Avatars_OnEffect);
            testClient.Avatars.OnLookAt += new AvatarManager.LookAtCallback(Avatars_OnLookAt);
            testClient.Avatars.OnPointAt += new AvatarManager.PointAtCallback(Avatars_OnPointAt);
        }

        public override string Execute(string[] args, LLUUID fromAgentID)
        {
            if (args.Length == 0)
            {
                ShowEffects = true;
                return "Viewer effects will be shown on the console";
            }
            else if (args.Length == 1)
            {
                if (args[0] == "on")
                {
                    ShowEffects = true;
                    return "Viewer effects will be shown on the console";
                }
                else
                {
                    ShowEffects = false;
                    return "Viewer effects will not be shown";
                }
            }
            else
            {
                return "Usage: showeffects [on/off]";
            }
        }

        private void Avatars_OnPointAt(LLUUID sourceID, LLUUID targetID, LLVector3d targetPos, 
            PointAtType pointType, float duration, LLUUID id)
        {
            if (ShowEffects)
                Console.WriteLine(
                "ViewerEffect [PointAt]: SourceID: {0} TargetID: {1} TargetPos: {2} Type: {3} Duration: {4} ID: {5}",
                sourceID.ToString(), targetID.ToString(), targetPos, pointType, duration, 
                id.ToString());
        }

        private void Avatars_OnLookAt(LLUUID sourceID, LLUUID targetID, LLVector3d targetPos, 
            LookAtType lookType, float duration, LLUUID id)
        {
            if (ShowEffects)
                Console.WriteLine(
                "ViewerEffect [LookAt]: SourceID: {0} TargetID: {1} TargetPos: {2} Type: {3} Duration: {4} ID: {5}",
                sourceID.ToString(), targetID.ToString(), targetPos, lookType, duration,
                id.ToString());
        }

        private void Avatars_OnEffect(EffectType type, LLUUID sourceID, LLUUID targetID, 
            LLVector3d targetPos, float duration, LLUUID id)
        {
            if (ShowEffects)
                Console.WriteLine(
                "ViewerEffect [{0}]: SourceID: {1} TargetID: {2} TargetPos: {3} Duration: {4} ID: {5}",
                type, sourceID.ToString(), targetID.ToString(), targetPos, duration,
                id.ToString());
        }
    }
}
